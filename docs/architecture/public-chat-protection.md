# Public chat protection, session lifecycle and cost safety

Decisions recorded here are binding until explicitly revisited. Each entry states
the decision, why it was taken, and what it forbids.

Phase 15 delivered a correct conversational runtime and deliberately deferred
internet-abuse protection. It was accepted with six named limitations: anonymous
visitors could start conversations and consume AI generation without a distributed
rate limit; this was the first surface where a stranger could spend a workspace's
provider budget; every hosted-chat page load created a Conversation, so empty
Conversations accumulated; and one broad conversation cookie meant starting a
second conversation in a browser took away access to the first.

Phase 16 closes all six. It adds a Redis-backed distributed limiter in front of
the two public writes, a pseudonymous network identity derived from a genuinely
trusted client address, a workspace generation-attempt safety budget, lazy
Conversation creation, per-Conversation credential cookies, and an absolute
server-side session lifetime.

**What this phase does not make the platform.** It is not billing, not usage
metering, not bot detection, and not a WAF. It exists so that the absence of all
of those is bounded rather than unbounded.

---

## P1. Protection comes before knowledge retrieval

**Decision.** Distributed rate limiting, cost safety and session lifecycle are
built before RAG, embeddings or any knowledge base.

**Why.** Phase 15 put a paid, data-creating, anonymous surface on the public
internet. Every feature added on top of it multiplies what an attacker gets per
request: retrieval makes each generation more expensive, not less. Protecting the
surface first means every later capability inherits the bound instead of widening
an unbounded hole.

**Forbids.** Adding public-facing capability to the hosted chat before its
protection holds. Treating rate limiting as an operational concern to be solved by
a proxy later — the limits here are per workspace, per conversation and per
endpoint, and no reverse proxy knows what those are.

---

## P2. Redis holds the counters, and only the counters

**Decision.** `StackExchange.Redis` 3.1.31, referenced by Infrastructure and no
other project. It stores protection counters and nothing else.

**Why.** The platform will run more than one API instance. A per-process limiter
is correct until the second instance starts and then fails silently — no error, no
failing test, just twice the traffic allowed, then three times. A limit that stops
being a limit as the deployment grows is worse than no limit, because everybody
believes it is still there.

PostgreSQL was considered and rejected for this specific job. These counters are
written on every public request, are worthless within minutes, and must never be
the reason a request waits. Putting them in the transactional store would mean row
contention and vacuum pressure on the system of record in exchange for durability
nobody wants: if the counters were lost, the correct behaviour is to start counting
again, which is exactly what an in-memory store with TTLs does natively.

**Forbids.** Redis becoming a cache layer, a session store, a queue, or a second
copy of business data. Any Redis reference outside `OmniAI.Infrastructure` — an
executable guard asserts the Application does not reference it, and a second guard
asserts Infrastructure does, so the negative guards cannot pass merely because the
package was dropped.

---

## P3. Unavailable protection means the write does not happen

**Decision.** Public writes fail closed. If the counter store cannot be reached,
if no store is configured, or if no trustworthy client address can be established,
starting a Conversation and sending a message answer `503`. There is no in-memory
fallback.

**Why.** The platform can still perform the work perfectly well; it declines
because it cannot tell whether it should. Performing an unbounded number of paid,
data-creating operations for anonymous strangers because a counter was unavailable
is the one outcome that must not be possible.

An in-memory fallback is wrong twice over. It would be incorrect the moment there
is a second instance — and being incorrect silently is worse than being unavailable
loudly. And a fallback that engages exactly when the shared store is struggling
engages exactly when the platform is most likely to be under attack.

**Enforced by construction.** `IPublicChatProtection` has no return value meaning
"could not tell". An implementation that cannot answer raises
`PublicChatProtectionUnavailableException`, so an unreachable store cannot produce
an allowed decision however the calling code is written.

**Forbids.** A configuration switch that disables protection. A development
bypass. Catching the unavailable exception at a call site and proceeding.

---

## P4. What an outage does not take down

**Decision.** Only the two public writes are gated on the protection store.

Unaffected: reading a chat endpoint's public face, reading a transcript, the
entire admin surface, authentication, and the liveness probe.

**Why.** A read creates nothing and costs no provider call, so an unreachable
counter must not take a visitor's conversation away from them mid-sentence. And a
member of a workspace is not an anonymous stranger: nothing behind the admin
boundary is rate-limited by a policy written for one, so an outage in it must not
cost a business its administration or its conversation history.

**The documented boundary is exactly:**

| Surface | Protected | Behaviour when the store is away |
| --- | --- | --- |
| `POST /api/public/webchat/{key}/conversations` | yes | `503`, nothing created |
| `POST /api/public/webchat/conversations/{id}/messages` | yes | `503`, nothing stored, no provider call |
| `GET /api/public/webchat/{key}` | no | works |
| `GET /api/public/webchat/conversations/{id}/messages` | no | works |
| everything behind the admin boundary | no | works |
| `/health` | no | works |

**Accepted consequence.** An unprotected public read can be flooded. It is a
single indexed lookup that creates nothing and spends nothing, and flood
protection for reads belongs to a CDN or WAF, which is deferred.

---

## P5. Four layered limits, because the threats are different shapes

**Decision.** Public chat is bounded along four dimensions, not one.

| Limit | Keyed by | Answers |
| --- | --- | --- |
| Conversation starts per client | network + endpoint | one machine manufacturing conversations |
| Conversation starts per endpoint | endpoint | a botnet, each node politely under the per-network limit |
| Public write requests per client | network | a flood of requests that were going to be refused anyway |
| Messages per conversation | conversation | a script driving one legitimate credential |
| AI generations per workspace | workspace, three windows | the invoice |

**Why one limit is not enough.** Every per-network limit is bypassed by using more
networks, which is cheap — so an aggregate that does not care where traffic comes
from is required. An aggregate alone would let one machine consume a whole
endpoint's allowance — so a per-network limit is required. A valid session is
subject to neither, since it is one network and one endpoint — so a per-conversation
limit is required. And none of those bounds total spend across a workspace — so a
workspace budget is required.

**Forbids.** Collapsing these into one "rate limit". Adding a fifth dimension
without stating which threat it answers.

---

## P6. The policy is configuration, validated at startup

**Decision.** `PublicChatProtectionOptions`, bound from the non-secret
`PublicChatProtection` section, validated before the application serves anything.

The shipped defaults, chosen for a support chat where a real person asks a handful
of questions over a few minutes:

| Setting | Default |
| --- | --- |
| `ConversationStartsPerClient` | 5 per 5 minutes |
| `ConversationStartsPerEndpoint` | 60 per 5 minutes |
| `RequestsPerClient` | 30 per minute |
| `MessagesPerConversation` | 10 per minute |
| `GenerationsPerWorkspaceBurst` | 30 per minute |
| `GenerationsPerWorkspaceSustained` | 600 per hour |
| `GenerationsPerWorkspaceDaily` | 5,000 per 24 hours |
| `SessionLifetime` | 24 hours |
| `KeyPrefix` | `omniai:pcp:v1` |

Every one is several times more generous than a real conversation needs and far
below what an automated abuser needs to be worth the effort. Each is overridable
through ordinary configuration — `PublicChatProtection__RequestsPerClient__Limit`
as an environment variable — so tuning a deployment does not require a release.

**Why validated at startup.** These numbers are the difference between a chat page
and a billing incident. A mistyped window must be a deployment failure an operator
sees, not a rate limit a customer discovers. Validation also checks the
relationships: a daily ceiling below the hourly allowance is not a stricter policy,
it is two settings disagreeing where only one can be doing anything.

**Accepted trade-off, named explicitly.** A genuinely popular site could reach
`ConversationStartsPerEndpoint`, and its visitors would be told to try again
shortly. That is the correct failure while the alternative is an unbounded one, and
the number is configuration precisely so a busy deployment can raise it
deliberately.

**Forbids.** Literal limits in handlers. Exposing these settings through any API.
An administrative screen for them — plan-aware quotas are deferred, and a
customer-configurable limit is a limit an attacked customer can be talked into
raising.

---

## P7. One action, one atomic decision over every counter that applies

**Decision.** A single Lua script per decision. It inspects every applicable
counter, and then consumes every applicable counter — or none.

```
pass 1: for each counter, read it; if this action would exceed it, return
        (refused, remaining life of that counter)
pass 2: for each counter, INCR; if the result is 1, EXPIRE it
        return (allowed, 0)
```

**Why two passes.** A single interleaved pass would consume the earlier counters
before discovering that a later one refuses. That turns a rejected request into a
request that still cost the caller their allowance everywhere else — so a caller
already over their workspace budget could drain their conversation and network
allowances by retrying. It would also let two racing requests both read "under the
limit" before either incremented.

**Why in Redis and not in .NET.** Concurrency is settled by Redis, not by how the
tasks happened to interleave. A read-then-increment limiter lets most of a
simultaneous burst through, and the failure is invisible in every status code —
only a provider call count reveals it.

**Forbids.** Separate round trips for the counters of one action. A generic Lua
framework — this is one script with one shape.

**Known constraint.** The multi-key evaluation assumes a single Redis instance or
a primary/replica pair. On a cluster these keys would need hash tags to share a
slot. Recorded rather than pre-emptively engineered around.

---

## P8. An anchored window, and `Retry-After` is the truth

**Decision.** Fixed windows anchored at the first request. The expiry is set only
when a counter is created and never extended. On refusal, the remaining life of the
counter that refused becomes `Retry-After`, floored at one second.

**Why anchored rather than sliding.** A sliding expiry would let sustained traffic
keep a full counter alive forever, turning a one-minute limit into a permanent
lockout.

**The boundary behaviour, stated plainly.** A caller who spends their allowance at
the end of one window and again at the start of the next achieves up to twice the
limit across the boundary. For bounding cost and abuse — as opposed to metering —
that is a factor of two on numbers chosen with far more headroom than that, and it
buys an implementation with no per-request sorted-set writes and no clock
arithmetic to get wrong. A token bucket was considered and judged not worth the
extra state.

**Why a real retry hint.** A refusal that says "too many" without saying "for how
long" invites the client to retry immediately, which is the behaviour the refusal
exists to stop. The floor at one second means it can never read as "retry now".

**Forbids.** A constant or invented `Retry-After`. Extending a window on use.
Disclosing the limit or the current count — the hint says *when*, never *how much*.

---

## P9. The client address comes from the connection, never from a header

**Decision.** `ClientNetworkIdentity` reads `Connection.RemoteIpAddress` and
nothing else. By the time it runs, the forwarded-headers middleware has either
replaced that address with one a *trusted* proxy reported, or left it alone.

**Why.** Anything that can open a connection to this process can send
`X-Forwarded-For: 1.2.3.4`. If that were believed, every per-network limit in the
platform would be bypassable in one line: a fresh forged value per request is a
fresh bucket every time.

**Forbids.** Any code path that reads a forwarded header to decide a client's
identity. Returning a placeholder when no address is available — see P11.

---

## P10. Two forwarded-header passes, scheme first

**Decision.** `X-Forwarded-Proto` and `X-Forwarded-For` are processed by two
separate `UseForwardedHeaders` passes, in that order, both under the same
`KnownProxies`/`KnownIPNetworks` trust list with `ForwardLimit = 1`.
`X-Forwarded-Host` remains unprocessed.

**Why two passes — this was a defect found in this phase.** The obvious
implementation, one pass opted in to both headers, was written first and a test
immediately proved it unsafe: the middleware abandons *all* of its work at the
first `X-Forwarded-For` entry it cannot parse, so a proxy forwarding one malformed
address also lost the scheme, and every request was then refused as insecure
transport. A single misconfigured upstream would have taken the whole deployment
down. Two passes decouple them: a security decision must not depend on the
well-formedness of an unrelated header.

**Why the order is not interchangeable.** The address pass rewrites
`Connection.RemoteIpAddress` to the client's, and the trust check both passes make
is against that address — so running them the other way round would have the scheme
pass compare the *client's* address to the trusted-proxy list, find it untrusted,
and silently discard the scheme.

**Why one hop.** With `ForwardLimit = 1` the middleware takes the rightmost
`X-Forwarded-For` entry — the address the trusted proxy itself observed — and
ignores everything to its left, which is whatever the caller chose to prepend.
Raising the limit would start believing entries written upstream of the proxy,
which is the classic spoofable configuration.

**Malformed input fails safe.** An unparseable address leaves the peer alone, so
the resolved client is the proxy and everybody behind it shares one allowance:
more restrictive than intended, never less. A degradation in fairness, never in
protection.

**Forbids.** `RequireHeaderSymmetry` (it would restore the coupling above).
Processing `X-Forwarded-Host` without a genuine requirement. Trusting a forwarded
address from an unlisted peer.

---

## P11. Unknown is not a bucket

**Decision.** A public write whose client address cannot be established is
refused with `503`.

**Why.** Putting every such request into one shared counter would make that
counter the cheapest lane through the whole policy: an attacker who can suppress
whatever the edge reads gets an allowance shared with nobody else's real traffic.
Refusing costs a handful of genuinely unusual requests and closes the lane.

**Forbids.** A default, constant or empty network key.

---

## P12. The address becomes a keyed pseudonym, and nothing keeps the address

**Decision.** The client address is normalised, then reduced to
`HMAC-SHA-256(secret, network bytes)` truncated to 128 bits and base64url-encoded.
Only that value crosses into the Application, and only that value appears in a
Redis key or a log line.

**Why keyed rather than merely hashed.** A plain SHA-256 of an IPv4 address is
reversible by brute force in seconds — there are only four billion of them — so
anybody who obtained the counter names could recover every visitor's address with a
laptop. An HMAC under a server-held secret makes that attack require the secret as
well.

**Why a dedicated secret.** `PUBLIC_CHAT_NETWORK_KEY_SECRET`, a flat environment
variable, minimum 32 characters, refused at startup if shorter. Not the data
protection key that signs the authentication cookie and not the AI provider key:
one leaked value must not compromise two unrelated things, and rotating either for
its own reasons must not silently reset every rate-limit bucket. It is kept out of
the `PublicChatProtection` section — which is all non-secret and belongs in
`appsettings` — so the distinction is impossible to miss. A test asserts no
settings file in the repository contains it.

**Rotation.** Rotating this secret resets the IP-keyed buckets: old keys stop being
produced and expire on their own TTLs. A momentary loss of counting, not of
protection. Accepted.

**Development.** With no secret configured and only in Development, a random
per-process key is generated and a warning is logged. Every limit is still
enforced in full; the only difference is that the buckets are renamed on restart.
Nothing is compiled in — there is no default secret in the source to be found and
used in production by accident. Outside Development a missing secret fails closed
at the point of use, naming the variable.

**Forbids.** Storing a raw address in PostgreSQL, in Redis, in a log, in a Domain
entity or in an Application model. Logging the address beside the key derived from
it, which would defeat the pseudonymisation for anybody reading that log.
Publishing the key.

---

## P13. Address normalisation, and why IPv6 is bucketed by /64

**Decision.** Before hashing: an IPv4-mapped IPv6 address is folded to the IPv4
address it denotes; an IPv6 scope id is dropped by hashing raw bytes rather than
text; and an IPv6 address is reduced to its /64 prefix. IPv4 addresses are keyed
whole.

**Why normalise at all.** A digest of an inconsistent input is an inconsistent
key. The same visitor must not land in two buckets because the socket reported
their address one way on one connection and another way on the next.

**Why /64 for IPv6.** A single customer is routinely handed a whole /64 —
eighteen quintillion addresses — and can pick a fresh one for every request
without leaving their own network. Keying on the full address would make every
per-network limit free to bypass for exactly the visitors who have IPv6, which is
most of them on mobile. A /64 is the smallest unit an operator assigns, so it is
the smallest unit worth counting.

**Accepted cost.** Separate people behind one /64 share an allowance. That is
already true of everybody behind one IPv4 address, which is most corporate and
mobile traffic, and the per-network numbers are generous enough for it not to
matter.

**Why not bucket IPv4 by prefix.** IPv4 addresses are scarce and shared, so the
whole address is the smallest unit worth counting; a /24 would put unrelated
networks in one allowance.

---

## P14. The workspace generation budget is a count of attempts, not money

**Decision.** Three windows on new AI generation attempts per workspace: a burst
minute, a sustained hour, and a daily ceiling.

**Why three.** A burst limit alone permits burst-after-burst forever. A sustained
limit alone lets a single minute spend a large fraction of an hour's allowance. The
daily ceiling is the one an operator would recognise on an invoice.

**What it is not.** Not dollars, not tokens, not invoice usage, not a billing
plan. Provider usage accounting and billing do not exist yet; this exists only so
that their absence is not unbounded.

**Partitioned per workspace, deliberately.** A busy or attacked workspace must
never take another business's chat down with it.

**Forbids.** Treating this as a spend limit. Deriving it from provider pricing.

---

## P15. The budget is consumed once per new logical turn

**Decision.** `AuthoriseGenerationAsync` is called after the idempotency check,
inside the per-Conversation advisory lock, after the second idempotency check, and
before the customer's message is stored.

The full order for a public message:

1. parse and validate the request
2. establish the trusted client network identity — refuse if impossible
3. consume the cheap per-network request limit
4. authorise the exact Conversation credential
5. verify the session has not expired
6. verify the endpoint is Active
7. verify the Agent is Active
8. check for a completed or failed turn with this client message id
9. acquire the per-Conversation PostgreSQL advisory lock
10. check for that turn again
11. **consume the Conversation and workspace generation counters, atomically**
12. persist the Customer message and the turn, in one transaction
13. call `IAiTextGenerator`
14. persist the reply, or mark the turn Failed

**Why the two limits sit at different depths.** The request limit is at the top
because its job is to make a flood of doomed requests cheap. The generation budget
is at the bottom because its job is to bound money, and only the one request about
to call a provider is spending any. Put the budget any higher and a visitor whose
browser retried a completed message would be charged again; put the request limit
any lower and every refusal would cost a database round trip first.

**The consequence.** One message sent a hundred times — a retry, a duplicated
network request, an impatient double click, a racing copy — costs exactly one
generation attempt. The request counter sees all hundred, which is correct: they
were a hundred HTTP requests.

**Forbids.** Consuming the generation budget before the idempotency checks or
outside the lock. Counting requests as generations.

---

## P16. A failed provider attempt still costs a generation

**Decision.** The budget is consumed before the provider call and is not refunded
when the call fails.

**Why.** An attacker able to make attempts fail — with oversized context, or by
exhausting an upstream — would otherwise have found an unlimited free lane through
the safety budget. The workspace made an external attempt either way.

**Forbids.** Refunding on `502`, `503` or `504` from the provider. Retrying
automatically, which would spend the budget again on the platform's own initiative.

---

## P17. A 429 means nothing was spent

**Decision.** When a generation limit refuses: no Customer message is stored, no
turn row is written, nothing claims a generation occurred, and
`IAiTextGenerator` is not reached.

**Enforced by ordering, not by hope.** The budget is consulted before
`BeginTurnAsync`, and the provider is only reachable through it.

**The response.** `429 Too Many Requests`, `Retry-After` in seconds, RFC 9457
Problem Details with the title `Too many requests`. The detail is a fixed sentence
naming no quota, window, counter, workspace, address or policy — a caller learns
that they should wait and roughly how long, and nothing about the limit they hit.
Disclosing more would be a map: how many requests a minute, from how many
networks, before anybody notices.

`429` is deliberately not `503` (the service is healthy and this caller
specifically is being asked to slow down) and not `403` (the request is permitted,
just not yet).

---

## P18. Opening the hosted page creates nothing

**Decision.** `GET /chat/{publicKey}` reads the endpoint's public face and, if the
browser was already talking, that one transcript. A Conversation is created by the
first message a visitor actually sends.

**Why.** Phase 15 started a Conversation as part of rendering, so every page view,
refresh, theme change and crawler left an empty Conversation in somebody's
workspace — and each of those also issued a credential and consumed nothing that
would have noticed.

**Verified in Chrome against the real database.** Four page loads, three reloads,
a light/dark switch and a navigate-away-and-back produced zero Conversations, zero
sessions and zero messages.

**Forbids.** Any write on a passive page load. Creating a Conversation to render
an empty chat shell.

---

## P19. The residual start-then-no-send case, honestly

**Decision.** The first send is two requests: start the Conversation, then send
the message. If the start succeeds and the message request is then lost, an empty
Conversation exists.

**Why this is accepted.** It is bounded on three sides. It requires a visitor to
have deliberately sent a first message and then lost connectivity in the window
between two requests, so it is rare rather than one-per-page-view. Direct or
abusive start calls are bounded by the per-network and per-endpoint start limits.
And an empty Conversation is a harmless row, not a cost.

Collapsing the two requests into one atomic first-message endpoint was considered.
It would remove this case at the price of a second write path with its own
credential-issuing, idempotency and error semantics — duplicating the most
security-sensitive code in the platform to remove a rare empty row. Not worth it.

**Observed in verification.** One empty Conversation exists in the Chrome
database. It was produced by a direct API probe that started a Conversation and
never sent a message — never by a page load.

**Forbids.** Presenting this as solved. A background cleanup worker in this phase —
legitimate Conversation records are not silently deleted.

---

## P20. The browser remembers a Conversation id, and nothing else

**Decision.** `sessionStorage`, one key per public chat key
(`omniai.webchat.conversation.{publicKey}`), holding a Conversation id.

**Why this is safe.** A Conversation id is not a credential: it appears in an
administrator's list, it is returned to the page that created it, and knowing one
grants nothing. The secret that opens the Conversation stays in an `HttpOnly`
cookie the page cannot read — so a reload works because the *browser* still holds
the credential, not because the page kept a copy.

**Why `sessionStorage` and not `localStorage`.** The credential cookie is
session-scoped, so remembering the Conversation for longer would leave a resume id
whose cookie is already gone. Matching the two lifetimes also means a stranger's
conversation is not offered up on a shared computer the next morning.

**Every access is guarded.** Storage throws outright in some contexts — a browser
configured to block site data, a sandboxed frame — and a chat page that crashed
because it could not write a convenience value would be a worse failure than one
that starts a new conversation.

**Forbids.** Storing the raw credential, its digest, a tenant id or agent
instructions in any browser storage. `localStorage` for this value. Putting the
Conversation id in the URL.

---

## P21. One credential cookie per Conversation, scoped by path

**Decision.** The cookie keeps its name, `omniai.webchat`, and is written at the
path of the Conversation it opens:
`/api/public/webchat/conversations/{conversationId}`. `HttpOnly`, `SameSite=Lax`,
`Secure` outside Development, session-scoped, no expiry.

**Why.** A single cookie scoped to the whole chat API could only ever hold one
Conversation's secret, so starting a second Conversation silently took away access
to the first — somebody with two tabs open lost one of them.

**Why not the cookie name.** A name derived from the resource is not a name a
server can look up without already knowing the answer, and putting the *secret* in
a name would be indefensible.

**The path is not authorisation.** Paths separate one Conversation from another
inside one browser. A credential is still only accepted for the Conversation whose
stored digest it matches, so presenting one Conversation's secret at another's path
is still refused — verified both in the integration suite and in Chrome.

**One upgrade step.** Starting a Conversation also expires any cookie of this name
left at the old broad path, so a browser upgrading across this change never sends
two same-named cookies on one request. **The deletion must precede the append**:
deleting a cookie removes any pending `Set-Cookie` of the same name from the
response, matching the path as a substring, and every per-Conversation path begins
with the legacy one — so doing it second silently stripped the credential just
issued. Found by a failing test; recorded here because the ordering looks
arbitrary and is not.

**Forbids.** Reintroducing a single broad-path credential cookie. Treating path
scoping as an access check.

---

## P22. Several Conversations coexist in one browser

**Decision.** Supported, and verified in a real Chrome cookie jar rather than
inferred.

**What was verified.** In one browser profile: two Conversations with distinct
ids, both transcripts readable, each showing only its own messages, and a
*write* into the older Conversation accepted after the newer one had started —
which is the operation that was impossible in Phase 15. Five coexisting
Conversations were also exercised in the integration suite, each written to after
all five existed.

---

## P23. The public session has an absolute, persisted lifetime

**Decision.** `WebChatSession.ExpiresAt`, computed once when the session opens
from the configured `SessionLifetime` (24 hours by default) and stored. Compared
against `TimeProvider`.

**Why stored rather than derived.** Computing it from `CreatedAt` and the current
configuration would mean changing the platform's lifetime retroactively lengthened
or revoked credentials people were already relying on, and would leave no record of
what any given session was promised.

**Why absolute rather than sliding.** A sliding expiry means whoever holds a
stolen credential keeps it alive forever simply by using it, which is precisely the
case the expiry exists for. Verified: three hours of activity inside a four-hour
lifetime does not move the stored instant.

**Two independent bounds.** The cookie is session-scoped so the browser forgets
it; the server independently refuses it past `ExpiresAt`. A cookie's lifetime is
entirely under the holder's control — a copied value has no browser session to end —
so only the server-side bound is a guarantee.

**Forbids.** Extending the lifetime on use. Recomputing expiry from configuration
at check time.

---

## P24. An expired session is answered exactly like an unknown one

**Decision.** Past `ExpiresAt`: reading the transcript and sending a message both
answer not-found. No provider call. Admin history is untouched. The hosted page
clears its stale resume id and renders a fresh empty chat, creating nothing until
the next message.

**Why not-found.** "This conversation exists but your credential has expired"
would confirm the id and tell somebody holding a stale copy that they had once held
a real one. One answer covers a Conversation that never existed, a credential for a
different Conversation, a malformed cookie and a session that has run out.

**Why the check sits after the digest comparison.** Checking expiry first would
answer faster for an expired session than for a wrong credential, which is a
difference somebody probing can measure — and it would mean reading a session's
expiry before establishing that the caller had any right to it.

**Expiry is not retention.** The visitor's access ends; the workspace's record does
not.

---

## P25. Redis holds integers under bounded TTLs, and nothing else

**Decision.** Every protection key carries a TTL no longer than its window. Keys
are named
`{prefix}:{start|request|generate}:{client|endpoint|conversation|tenant}:{id}`
and their values are integers.

**What is deliberately absent.** No raw address, no credential, no message text,
no email, no agent instructions. The keys hold a pseudonymous network digest, a
tenant id, an endpoint id and a Conversation id.

**Verified in the live stack.** Every key in the Chrome run carried a positive TTL
no longer than 24 hours; a privacy sweep over every key and value found no address
in any form, no message text, no credential and no agent name; short-window keys
expired on their own during the session, leaving only the hourly and daily
counters.

**The prefix carries a schema version** (`omniai:pcp:v1`), so a future change to
what a counter means can start counting afresh rather than inheriting values with
the old meaning. It is configurable, which is also how parallel test classes share
one Redis without colliding.

**Forbids.** A key with no expiry. Storing business data in Redis. Reusing the
prefix across a semantic change.

---

## P26. What a rate-limited visitor is told

**Decision.** "You are sending messages too quickly." plus an approximate wait
when `Retry-After` is usable, rendered as a warning rather than an error. The
unsent message stays in the box. Nothing retries automatically.

**Why its own copy.** Nothing failed and nothing was lost, so it must not read
like an error, and the shared error copy would be wrong. It says nothing about
quotas, budgets, workspaces or how the limit is counted.

**Why no automatic retry.** A retry of a paid request is a decision, and being
told to wait is not licence to poll. A platform that retried through its own rate
limit would be arguing with itself.

**`Retry-After` handling.** Only the seconds form is read; an HTTP date is
deliberately not parsed, because that would mean trusting the reader's clock
against the server's. A missing, blank, fractional, negative or unparseable value
leaves the wait undefined and the copy says the honest vague thing rather than
rendering a guess. A value over an hour is capped — anything longer is a
misconfiguration or a server saying "come back tomorrow", and neither is worth a
countdown next to a message somebody is waiting to send.

---

## P27. A protection-store outage looks like any other unavailability

**Decision.** The hosted page shows the same generic "no answer came back just
now" it shows for an unconfigured or failing provider, and the same "chat
unavailable" title a withdrawn endpoint produces.

**Why identical.** A visitor must not be able to tell an unreachable counter store
from a withdrawn endpoint or an unconfigured provider: the difference is a fact
about this deployment's internals. The operator's copy of the difference is in the
log, where `PublicChatProtectionUnavailableException.Reason` names the unreachable
dependency or the missing setting — and carries no address, credential or customer
text.

**Verified in Chrome.** With Redis stopped: a new message and a new Conversation
both `503` with customer-safe copy naming no dependency; nothing stored; public
reads and the whole admin surface unaffected; the log naming the protection store.
Restarting Redis restored public writes without restarting the API, which is why
`AbortOnConnectFail` is off.

---

## P28. Every Phase 15 boundary still holds

Re-asserted rather than assumed, because this phase changed the transport under
them:

- a Conversation id alone grants no public access
- the credential is a 256-bit secret, stored only as a SHA-256 digest
- the raw secret is never stored, logged or returned in a body
- a credential authorises exactly one Conversation
- public and admin authorisation remain structurally separate and never substitute
- **public writes remain antiforgery protected** — being anonymous is not an
  exemption, because there *is* a session and it is what a cross-site request
  would ride on. Verified in Chrome: both public writes answer `400` without the
  token.
- the Customer message is committed before the provider is invoked
- no ordinary database transaction spans the provider call
- `IAiTextGenerator` remains the provider abstraction; no vendor SDK outside
  Infrastructure
- no automatic provider retry, and no fabricated AI message
- provider failure preserves the Customer message
- idempotency is `Conversation` + `ClientMessageId`, settled by a unique index
- message history is ordered by `message_ingestion_sequence`; `SentAt` is never an
  ordering authority
- generation within one Conversation is serialised by a PostgreSQL advisory lock;
  different Conversations generate concurrently
- plain text only, rendered by interpolation, no `innerHTML`
- the 4,000-character public message bound
- both the endpoint and the Agent must be Active, checked every turn

**The distributed limiter does not replace the advisory lock.** The lock serialises
generation inside one Conversation so two messages cannot be answered against the
same history at once. The limiter bounds how much may be generated at all. They
solve different problems and both are required.

---

## DEFERRED

Named so that their absence is a decision rather than an oversight:

- CAPTCHA and proof-of-work
- bot reputation services and device fingerprinting
- billing plans and plan-aware quotas
- provider token metering and monetary spend accounting
- customer-configurable limits and an administrative screen for them
- RAG, knowledge bases, embeddings and pgvector
- the embeddable third-party widget and cross-origin rate limiting
- WAF and CDN protection, and Nginx-level rate limits
- flood protection for public reads
- Redis Cluster support (hash-tagged keys)
- a background sweeper for expired sessions or empty Conversations
- Human handoff, SignalR and a realtime inbox
- alerting when a workspace approaches its safety budget
