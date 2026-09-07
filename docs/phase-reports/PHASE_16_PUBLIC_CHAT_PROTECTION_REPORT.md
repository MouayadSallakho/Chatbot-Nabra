# Phase 16 — Public chat protection, session lifecycle and cost safety

## 1. Phase objective

Protect the public AI runtime Phase 15 delivered. Close the six limitations that
release was accepted with: no distributed rate limit on anonymous conversation
starts, no distributed rate limit on anonymous AI turns, the first surface where an
internet stranger can consume a workspace's provider budget, a Conversation created
by every hosted-chat page load, empty Conversations accumulating as a result, and
one broad conversation cookie so that starting a second conversation in a browser
took away access to the first.

## 2. Final status

Complete. Backend 1,845 tests passing, frontend 397 passing, Release build clean at
0 warnings, and every mandated browser workflow executed in Chrome against a real
containerised PostgreSQL 17 and Redis. Nothing committed.

## 3. Baseline commit

`24ac222` — `feat: implement conversation runtime and hosted website chat`, the
accepted Phase 15 checkpoint, read from Git rather than memory.

## 4. Initial Git status

At recovery: `HEAD` = `24ac222` on `master`, nothing staged, 25 modified tracked
files and 9 untracked paths from the interrupted session. `git diff --check` clean
apart from Git's routine CRLF advisories. No reset, clean, restore, checkout or
stash was run, and no untracked file was deleted.

## 5. Recovery after the usage-limit interruption

The previous session stopped mid-way through the first new Phase 16 test file, not
on a code failure.

**State detected: STATE B** — implementation complete and previously green
(485 API / 476 Domain / 414 Application / 317 Infrastructure all passing before the
interruption), with the new test suites just begun.

**Exact interruption point.** `PublicChatProtectionTests.cs` had been written and
did not compile: it referenced a `Harness` type that was still a private nested
class inside `WebChatRuntimeTests`, and a `FakeWebChatWorld.Conversations`
accessor that did not exist. Confirmed by building the Application test project,
which reported exactly those errors and nothing else.

**Work already present and preserved unchanged.** The entire implementation:
`ClientNetworkKey`, `IClientNetworkKeys`, `IPublicChatProtection`,
`PublicChatDecision`, `PublicChatProtectionOptions`, `RateLimitWindow`, the two
protection exceptions, `RedisPublicChatProtection`, `UnprotectedPublicChat`,
`HmacClientNetworkKeys`, `PublicChatProtectionSetup`, `ClientNetworkIdentity`, the
`WebChatSession.ExpiresAt` domain change, the handler ordering in
`SendWebChatMessageHandler` and `StartWebChatConversationHandler`, the expiry check
in `PublicWebChatAccess`, the per-conversation cookie path, the 429/503 Problem
Details arms, the `AddWebChatSessionExpiry` migration, the frontend lazy-start
component, `HostedChatSessionStore`, the `rateLimited` error kind, and the
`RedisTestServer` and `SimulatedPeerStartupFilter` harnesses.

**Work completed after recovery.** The `Harness` extraction; the
`FakeWebChatWorld.Conversations` and `.Sessions` accessors; all remaining test
suites (Application protection, options validation, session lifetime, the
Application Redis layer guard, the Infrastructure Redis/HMAC/setup suites, the
Infrastructure Redis positive guard, five API suites, the forwarded-scheme
regression suite); thread-safety in `RecordingAiTextGenerator`; the frontend
session-store and 429 suites; two real defects found and fixed (§96); the full
verification run; the Chrome workflows; and this documentation.

## 6. Phase 15 limitations addressed

| Limitation | Closed by |
| --- | --- |
| No distributed limit on conversation starts | per-network + per-endpoint start counters (P5) |
| No distributed limit on AI turns | per-conversation + three workspace generation windows (P5, P14) |
| First surface where a stranger spends provider budget | workspace generation-attempt safety budget (P14) |
| Every page load created a Conversation | lazy creation on first send (P18) |
| Empty Conversations accumulated | same; residual case documented (P19) |
| One cookie, so a second conversation lost the first | per-conversation cookie path (P21, P22) |

## 7. Redis decision

Redis is introduced as a justified runtime dependency for public-chat protection
only. PostgreSQL was considered and rejected for these counters: they are written
on every public request, worthless within minutes, and must never make a request
wait, so putting them in the system of record would buy contention and vacuum
pressure in exchange for durability nobody wants. See P2.

## 8. Redis package and version

`StackExchange.Redis` 3.1.31 — the latest stable release on nuget.org when this
phase ran, declared once in `Directory.Packages.props` under central package
management, referenced by `OmniAI.Infrastructure` only.

## 9. Application abstraction

`IPublicChatProtection` with three named questions —
`AuthoriseConversationStartAsync`, `AuthoriseRequestAsync`,
`AuthoriseGenerationAsync` — each returning a `PublicChatDecision` and each an
atomic decision over every counter that applies to that action.
`IClientNetworkKeys` turns an address into a `ClientNetworkKey`.

Deliberately not `IRedisService` or `IDistributedCache`: a generic store would have
let the counting policy leak into handlers one `Increment` at a time, and a handler
that composes its own limits is a handler that can compose them wrongly. An
executable guard asserts the Application references no Redis client; a second
asserts Infrastructure does, so the first cannot pass by the package being dropped.

## 10. Redis configuration

`ConnectionStrings:OmniAiProtectionRedis`, blank in the committed
`appsettings.json`. `AbortOnConnectFail = false` so the application starts behind a
Redis that is still booting and reconnects by itself after an outage; the
multiplexer is built lazily so an unreachable Redis still lets the admin and the
health probe work. `ClientName = omniai-public-chat-protection` so an operator can
tell which application's connections these are.

## 11. Redis unavailable behaviour

Fail closed for the two public writes: `503` with customer-safe wording, nothing
created, nothing stored, no provider call. Public reads, the whole admin surface,
authentication and `/health` are unaffected. No in-memory fallback exists, and the
contract has no return value meaning "could not tell". Full boundary table in P4.

With no Redis configured at all, `UnprotectedPublicChat` is registered and refuses
every public write while naming the missing setting in the log — the same shape the
platform already uses for a missing database connection string and a missing AI
provider key.

## 12. Rate-limit algorithm

A single Lua script per decision: read every applicable counter and refuse if any
would be exceeded, otherwise `INCR` all of them and `EXPIRE` each on creation.
Sent as `EVAL` rather than managed as a stored script — a few hundred bytes against
a local Redis, and it removes the `NOSCRIPT`-after-restart failure mode entirely.

## 13. Rate-limit windows

Fixed windows anchored at the first request in each. The boundary behaviour is
stated rather than hidden: up to twice the limit across a window boundary, which is
a factor of two on numbers chosen with far more headroom, in exchange for no
per-request sorted-set writes and no clock arithmetic. See P8.

## 14. Exact default limits

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

Pinned by a test, because these exact numbers are quoted in the architecture
document and a silent edit would make that documentation wrong with nothing
failing.

## 15. Configuration validation

`PublicChatProtectionOptions.Validate()` runs at startup. Every limit must be at
least one; every window must be longer than nothing and no longer than a day; the
sustained window must be wider *and* at least as permissive as the burst, and the
daily wider and at least as permissive as the sustained; the session lifetime must
be positive and no longer than thirty days; the key prefix must be non-blank. A
present-but-unparseable value is an error, never a silent fallback. Every message
names the setting an operator has to change — asserted by a test.

## 16. Conversation-start protection

Per network **and** per endpoint, consumed together atomically, after the endpoint
is resolved and its serving state checked, before anything is written. An unknown
public key and a withdrawn endpoint both refuse before any counter is consumed, so
typos and unavailability do not count against a limit meant to bound conversations.

## 17. IP / request protection

`RequestsPerClient`, consumed as the first thing a public message does — before the
credential is believed — so a flood of requests with a junk cookie costs a counter
rather than a database round trip each. It therefore counts requests that turn out
to be unauthorised, which is exactly what an anti-flood limit should do.

## 18. Session / conversation protection

`MessagesPerConversation`, consumed with the workspace counters in one atomic
decision. A script driving one perfectly valid credential is the cheapest abuse
there is, and this is what bounds it.

## 19. Workspace generation budget

Three windows — burst minute, sustained hour, daily ceiling — evaluated together
with the conversation counter in one script. A count of new AI generation attempts,
not dollars, tokens, invoice usage or a plan. Partitioned per workspace so an
attacked business cannot take another one's chat down.

## 20. Generation-attempt accounting

Consumed once per new logical turn: after the pre-lock idempotency check, inside
the per-Conversation advisory lock, after the in-lock idempotency check, and before
the Customer message is stored. Full ordering in P15. A duplicate never reaches the
budget, because it returned a replay above it.

## 21. Failed-provider accounting

A genuine provider attempt that fails still costs one generation. The budget is
consumed before the call and is not refunded. Intentional: an attacker able to make
attempts fail would otherwise have found an unlimited free lane. Verified
end-to-end — with a provider that answers with nothing usable, exactly the
configured four attempts were made and the budget was then spent.

## 22. Duplicate accounting

One message sent twelve times consumes one generation attempt and reaches the
provider once; the request counter sees all twelve, which is correct. Three racing
copies of one message consume one between them. End to end, eight repeats of one
`clientMessageId` against an allowance of two new messages left the second genuine
message still allowed — which it would not have been had the repeats each been
charged.

## 23. Redis atomicity

Proven, not asserted. When the workspace burst limit refuses, the conversation
counter — which had room — is untouched. When the endpoint aggregate refuses, the
refused network's own allowance is untouched and remains fully available at another
endpoint. Twenty simultaneous new turns against a workspace with five attempts left
produced exactly five permissions and fifteen refusals.

## 24. Redis TTL policy

Every key carries a TTL no longer than its window; the expiry is set on creation
and never extended. Verified against the live stack: every key positive and ≤ 24
hours, and the short-window keys expired on their own during the Chrome session
leaving only the hourly and daily counters.

## 25. Redis key structure

```
omniai:pcp:v1:start:client:{networkKey}:{endpointId}
omniai:pcp:v1:start:endpoint:{endpointId}
omniai:pcp:v1:request:client:{networkKey}
omniai:pcp:v1:generate:conversation:{conversationId}
omniai:pcp:v1:generate:tenant:{tenantId}:{burst|sustained|daily}
```

Values are integers. The prefix carries a schema version and is configurable.

## 26. Redis privacy review

A sweep over every key and value produced by the live Chrome session found no
address in any form (the Docker gateway address, and the `172.`, `192.168.`, `10.`
and `127.0` families), no message text, no email, no credential, no token, no
secret and no agent name. The client identity appears only as a 22-character
base64url digest. No key had an unbounded TTL.

## 27. Forwarded-headers change

`X-Forwarded-Proto` and `X-Forwarded-For` are now both processed, by **two
separate passes**, scheme first, under the same trust list with
`ForwardLimit = 1`. `X-Forwarded-Host` is still not processed.
`RequireHeaderSymmetry` is left off, and P10 records why turning it on would look
like a tightening and be a regression.

## 28. Trusted proxy behaviour

Behind a proxy the deployment names, the forwarded client address is believed: two
different forwarded addresses get two independent allowances, the same forwarded
address shares one, and only the rightmost entry is believed — two requests whose
prepended values differ but whose rightmost entry matches share a bucket.

## 29. Untrusted spoof result

From a peer the deployment does not trust, a forged `X-Forwarded-For` buys nothing:
four requests carrying four different forged addresses all landed in one bucket, so
the second was refused. Forging somebody else's address does not spend their
allowance either. A direct connection is identified by its own address and is still
bounded; two direct connections from different addresses are two visitors.

Malformed forwarded values — `not-an-address`, blank, `,,,`, `203.0.113.999`, an
injected script tag, an injected SQL fragment — all fail safe: no fault, and no
fresh unmetered bucket. Rubbish shares a bucket with the peer, which is the safe
direction.

## 30. Client-address normalisation

IPv4-mapped IPv6 folded to IPv4; IPv6 scope id dropped by hashing raw bytes; IPv6
reduced to its /64 prefix; IPv4 keyed whole. Rationale and accepted cost in P13.

## 31. HMAC network identity

`HMAC-SHA-256` under a dedicated `PUBLIC_CHAT_NETWORK_KEY_SECRET`, truncated to 128
bits, base64url-encoded. Minimum 32 characters, refused at startup if shorter. Not
the authentication key and not the OpenAI key. Thread-safe under a lock, verified
by 200 parallel derivations agreeing. Rotation resets the IP-keyed buckets, which
is accepted. In Development with no secret configured a random per-process key is
used and a warning logged — not a protection bypass, since every limit is still
enforced; two such processes provably disagree, so nothing is compiled in.

## 32. Missing-client-address behaviour

Fail closed: the write is refused with `503` and no counter is consumed. Unknown is
not a bucket (P11).

## 33. Passive page-load behaviour

Zero Conversations. Verified in Chrome against the real database: four page loads,
three reloads, a light/dark switch and a navigate-away-and-back left conversations,
sessions and messages all at zero. The page issued exactly three requests — the
antiforgery bootstrap, the account probe, and the endpoint's public face — and no
credential cookie was set.

## 34. Lazy Conversation start

The first `Send` starts the Conversation and then submits the message. The
Conversation id is stored only after the server confirms it, so a failed start
leaves no resume id pointing at something that was never created.

## 35. First-send flow

Verified in Chrome: no Conversation before Send → `POST /conversations` → id stored
in `sessionStorage` → `HttpOnly` credential issued at the conversation's own path →
message submitted → exactly one Conversation, one session, one Customer message,
one turn. The provider is genuinely unconfigured, so the turn is `Failed`, no reply
is invented, and the visitor is told "no answer came back just now" with the
message kept in the box.

## 36. Empty Conversation result

Normal browsing creates none. One empty Conversation exists in the verification
database, produced by a direct API probe that started a Conversation and never sent
a message — the residual edge of P19, never by a page load.

## 37. `sessionStorage` design

One key per public chat key, `omniai.webchat.conversation.{publicKey}`, holding a
Conversation id. Verified in Chrome that it is the only key the page writes. Every
access is guarded against storage that throws.

## 38. Per-Conversation cookie design

`omniai.webchat` at `/api/public/webchat/conversations/{conversationId}`,
`HttpOnly`, `SameSite=Lax`, `Secure` outside Development, session-scoped. The
legacy broad-path cookie is expired on start, and the deletion must precede the
append — see P21 for the trap and why the ordering is written down.

## 39. Multiple browser Conversations result

Works, verified in one real Chrome cookie jar: two distinct ids, both transcripts
readable and each showing only its own messages, and a write into the older
conversation accepted after the newer one had started. Five coexisting conversations
exercised in the integration suite. A credential presented at another conversation's
path is still refused, so path scoping is separation and not authorisation.

## 40. Session lifetime

Absolute, persisted as `WebChatSession.ExpiresAt`, default 24 hours, read through
`TimeProvider`. Verified in the live database as `created_at + 24 hours`.

## 41. Session expiration behaviour

Past the expiry: public read and public send both answer not-found, zero provider
calls, no generation consumed. Admin history remains fully available. The visitor
can start a fresh conversation. Three hours of activity inside a four-hour lifetime
does not move the stored instant, and lengthening the configured lifetime does not
revive an expired session.

## 42. Migration

`20260907113003_AddWebChatSessionExpiry` — exactly one. No historical migration was
touched.

## 43. Migration inspection

Inspected and rewritten by hand. The scaffolder produced a single `AddColumn` with
a sentinel default of `0001-01-01`, which would have marked every existing session
as expired two thousand years ago and cut off every visitor mid-conversation on
deployment. Replaced with three statements: add nullable, backfill
`expires_at = created_at + INTERVAL '24 hours'`, set `NOT NULL`. The backfill reads
`created_at` and deliberately not `now()`, so a session that started twenty-three
hours ago has an hour left rather than a fresh day. The interval is a literal
because a migration must describe one fixed transformation. No physical `xmin`
column was emitted anywhere; a schema test asserts it, and another asserts the
`web_chat_sessions` column list exactly.

## 44. 429 API contract

`429 Too Many Requests`, RFC 9457 Problem Details, title `Too many requests`, a
fixed detail sentence naming no quota, window, counter, workspace, address or
policy name. Verified end to end that the body contains none of `redis`, `quota`,
`budget`, `counter`, `window`, `lua`, `workspace`, `tenant`, the network address, or
the tenant id.

## 45. Retry-After

Present on every 429, in seconds, floored at one so it can never read as "retry
now", and equal to the remaining life of the counter that refused. Observed in
Chrome as `retry-after: 67` against a 90-second window.

## 46. 429 frontend UX

Its own `rateLimited` error kind with its own copy — "You are sending messages too
quickly", plus an approximate wait when `Retry-After` is usable, rounded to whole
minutes past 90 seconds. Rendered as a warning rather than an error, because
nothing failed. The unsent message stays in the box, Send stays enabled, and
nothing retries automatically. A missing, blank, fractional, negative, absurd or
HTTP-date `Retry-After` leaves the wait undefined and the copy says the honest
vague thing.

## 47. Redis 503 frontend UX

The same generic "no answer came back just now" a failing provider produces, and
the same "Chat unavailable" title a withdrawn endpoint produces. A visitor cannot
tell the three apart. Verified in Chrome with Redis stopped.

## 48. Public read behaviour

Not gated on the protection store. With Redis stopped, the endpoint's public face
and a transcript both answered `200` while both writes answered `503`. A
rate-limited visitor can still read their own transcript.

## 49. Admin independence

The anonymous policy is never applied to the admin. With every public allowance for
a network exhausted, twenty-eight admin requests across seven routes all answered
`200`. With Redis stopped, the whole admin surface — `me`, tenants, agents, website
chat, conversations, roles, members — answered `200`, and the admin could still read
the real transcript of the interrupted conversation.

## 50. Phase 15 runtime regression

None. All Phase 15 suites remain green, including visitor isolation, the ordering
guarantees, provider-failure handling and the antiforgery requirement on anonymous
writes (verified again in Chrome: both public writes answer `400` without the
token).

## 51. Idempotency regression

Green. Repeat, concurrent-duplicate, completed-replay and failed-terminal cases all
preserved, now additionally asserting that the generation budget sees one attempt.

## 52. Same-Conversation lock regression

Green. `MaxConcurrentHolders == 1` per conversation is unchanged. The distributed
limiter does not replace the advisory lock; they solve different problems.

## 53. Different-Conversation concurrency regression

Green. Different conversations still enter the provider concurrently.

## 54. Provider-call-count result

Zero on every refusal path: a rate-limited turn, an expired session, a missing
client identity, an unreachable protection store, and a cross-visitor credential.
Exactly the permitted number under concurrency: four of twelve simultaneous new
turns against a budget of four.

## 55. Workspace burst test

Twelve visitors on twelve networks with twelve conversations, all arranged before
any of them sent, then released together: exactly four allowed, eight refused, and
the provider reached exactly four times. Spreading across conversations and
rotating networks each bought nothing.

## 56. Multi-instance Redis test

Two independently constructed `RedisPublicChatProtection` instances over two
independent multiplexers against one Redis. Instance A spent the whole allowance;
instance B, which had served nothing, saw it exhausted and refused with a positive
retry hint. A second test alternated instances through a six-attempt workspace
budget and both refused the seventh.

## 57. Real Redis integration tests

25 tests against a real Redis: first request, threshold, threshold+1, retry hint
bounds, TTL existence and bounds, a counter observed expiring and the limit
resetting, anchored-window non-extension, independent client / endpoint / session /
workspace keys, the endpoint aggregate defeating address rotation, the workspace
aggregate across conversations, no partial consumption on rejection for both
multi-counter decisions, concurrency, multi-instance sharing, fail-closed on an
unreachable store, operator-versus-visitor message separation, and three privacy
assertions.

## 58. PostgreSQL integration tests

All Infrastructure persistence suites green, including the updated
`web_chat_sessions` column list, the new `expires_at` index, the seven-migration
sequence and the `xmin` guards.

## 59. Forwarded-header tests

18 client-identity tests plus 6 forwarded-scheme regression tests, across two hosts
differing in exactly one setting — the configured trusted-proxy address.

## 60. Cookie tests

15 session/cookie API tests: the per-conversation path, the legacy expiry and its
ordering, two and five coexisting conversations, continuing the older one, and a
forged credential at the right path still refused.

## 61. Session-expiry tests

Application tests over a controllable clock (stored expiry, before/after, no
sliding, no retroactive revival, per-conversation independence) and API tests over
a two-hour host lifetime (before, after, admin intact, fresh start, and the stored
interval read back from the database as 7,200 seconds).

## 62. Visitor-isolation tests

Green, at Application, API and browser level.

## 63. Browser passive-load workflow

Executed. Zero Conversations. §33.

## 64. Browser first-send workflow

Executed. Exactly one Conversation. §35.

## 65. Browser reload-resume workflow

Executed. Reload restored the transcript from the stored id plus the `HttpOnly`
credential; conversations and sessions stayed at one; no credential became visible
to script.

## 66. Browser same-profile multi-conversation workflow

Executed in one cookie jar. §39.

## 67. Browser 429 workflow

Executed with a per-conversation limit of one supplied as an environment variable.
The second message answered `429` with `retry-after: 67`; the page showed "You are
sending messages too quickly. Please wait about 67 seconds and try again." as a
warning; the message stayed in the box; Send stayed enabled; no retry was issued;
no message row and no turn row were created for it, and the refused text appears
nowhere in the database. Normal defaults were restored afterwards.

## 68. Browser Redis-failure workflow

Executed by stopping the Redis container with the page live. A new message and a
new conversation both `503` with customer-safe copy; nothing stored; public reads
and the whole admin surface unaffected; the log named the protection store and
`RedisConnectionException`. Restarting Redis restored public writes **without
restarting the API**.

## 69. Chrome storage inspection

`sessionStorage`: the conversation id only, and empty before the first message.
`localStorage`: nothing written by this page. `document.cookie`: no
`omniai.webchat` at any point — the credential is `HttpOnly` and invisible to
script, while the browser still sends it, proven by the conversation's own
transcript answering `200` and another conversation's answering `404`. No raw
secret in the URL, and no client address in any public response body or header.

## 70. Chrome cookie inspection

The credential is issued once, at the conversation's own path, `HttpOnly`,
`SameSite=Lax`, session-scoped with no `Expires` or `Max-Age`, and absent from the
response body. Two conversations hold two separate credentials in one jar.

## 71. Chrome network result

Every request accounted for: three on a passive load, and on a first send exactly
the start, the message and the transcript re-read. No polling, no automatic retry
after the 429, and no request carrying a credential to a route that does not need
one.

## 72. Chrome console result

Clean. Three entries, all Chrome's own resource notices for the deliberate `429`
and `503` responses. No application errors, no stack traces, no secrets.

## 73. Responsive result

390, 768, 1440 and 1920: zero horizontal overflow at every width, the composer
within the viewport and focusable, and the "New chat" action within the viewport on
one line at every width including 390.

## 74. Light / Dark

390, 768 and 1440 in both themes. Light background `rgb(245, 247, 250)`, dark
`rgb(12, 15, 22)` with heading `rgb(231, 236, 243)`. The page also follows a live
system-scheme change without a reload.

## 75. Accessibility

The transcript is a labelled `aria-live="polite"` list; the composer is labelled and
keyboard-submittable; the rate limit and every failure are announced through
`role="alert"`; the rate limit is distinguished by copy as well as colour;
`:focus-visible` styling resolves; and starting or resuming a conversation does not
steal keyboard focus.

## 76. Backend tests before

1,687 passing, 0 failing, 0 skipped — Domain 471, Application 414, Infrastructure
317, API 485. Measured at the start of this phase, not projected.

## 77. Backend tests after

**1,845 passing, 0 failing, 0 skipped** — Domain 476, Application 451,
Infrastructure 372, API 546. Every project executed against real PostgreSQL 17 and
real Redis.

## 78. Frontend tests before

348 passing across 30 files.

## 79. Frontend tests after

**397 passing across 31 files.**

## 80. Backend build

`dotnet build OmniAI.slnx -c Release` — succeeded, **0 warnings, 0 errors**, with
`TreatWarningsAsErrors` on and no `NoWarn` anywhere.

## 81. Frontend build

`npm run build` succeeded. Initial bundle **386.46 kB** raw / 94.62 kB transfer,
against Phase 15's 385.75 kB — **+0.71 kB**, which is the route provider, the error
kind and the session store in `main`. The hosted chat itself is a 10.79 kB lazy
chunk, absent from the initial bundle.

## 82. Vulnerability scan

`dotnet list package --vulnerable --include-transitive` — no vulnerable packages in
any of the 8 projects, `StackExchange.Redis` included.

## 83. npm audit

`found 0 vulnerabilities`. `npm audit fix` was not run.

## 84. npm ci

Clean install completed; the build and the full test suite were re-run afterwards
and both passed unchanged.

## 85. Bundle before / after

385.75 kB → 386.46 kB initial.

## 86. Packages added

Backend: `StackExchange.Redis` 3.1.31. Frontend: none.

## 87. Domain modified

**YES.** `WebChatSession` gained `ExpiresAt` and `HasExpired`, with validation that
a session cannot expire before it begins. No other Domain type changed.

## 88. Application modified

**YES.** New `WebChat/Protection` namespace; `PublicWebChatAccess` gained the
expiry check; both hosted-chat commands carry a client network key; both handlers
consult protection at the documented depths.

## 89. Infrastructure modified

**YES.** New `WebChat/Protection` namespace; `WebChatConfiguration` maps
`expires_at` and its index; one new migration.

## 90. API modified

**YES.** `ForwardedHeadersSetup` split into two passes; `Program.cs` applies them
in order; `ClientNetworkIdentity` added; `HostedChatEndpoints` scopes the credential
per conversation and resolves the client identity; `DomainProblemDetailsHandler`
gained the 429 and 503 arms plus the `Retry-After` header and operator-reason
logging; `CompositionRoot` registers protection; `appsettings.json` declares a blank
Redis connection string.

## 91. Schema changed

**YES.** `web_chat_sessions.expires_at` (`timestamptz NOT NULL`) and
`ix_web_chat_sessions_expires_at`.

## 92. Architectural decisions

28 recorded as P1–P28 in
[public-chat-protection.md](../architecture/public-chat-protection.md), each with
its rationale and what it forbids, plus a DEFERRED list.

## 93. Security review

All eighteen mandated properties proven:

1. public page load creates zero Conversations — §33
2. reload resumes rather than creates — §65
3. multiple Conversation credentials coexist in one browser — §39
4. the raw credential is absent from JS storage — §69
5. the raw credential is absent from Redis — §26
6. the raw client address is absent from Redis keys, values and logs — §26, P12
7. an untrusted `X-Forwarded-For` cannot spoof the rate-limit identity — §29
8. a trusted proxy's `X-Forwarded-For` is honoured — §28
9. limits are shared across instances — §56
10. a Redis outage fails closed for abusable public writes — §68
11. a 429 prevents provider invocation — §54
12. a duplicate `clientMessageId` does not spend the budget twice — §22
13. the workspace budget is atomic under concurrency — §23, §55
14. an expired session cannot read or send publicly — §41
15. admin historical access survives public-session expiry — §41, §49
16. endpoint and Agent Active checks remain — §50
17. Phase 15 visitor-isolation tests remain green — §62
18. no public path reaches the admin — §49, and the public access check still
    returns only a `WebChatSession`, which no admin decision takes

## 94. Privacy review

No raw client address is stored in PostgreSQL, in Redis, in a log, in a Domain
entity or in an Application model. The address exists only inside
`HmacClientNetworkKeys.Derive`, where it is normalised, hashed and dropped;
`ClientNetworkIdentity` deliberately contains no diagnostic that could log it beside
the derived key. The only credential-shaped columns in the database remain two
`bytea` digests. `sessionStorage` holds one non-secret identifier. Redis holds
integers under bounded TTLs.

## 95. Deviations

- **Two forwarded-header passes** rather than the single pass the brief's wording
  implies. Mandatory: one pass is unsafe, and the brief's own requirement that
  Phase 12's `X-Forwarded-Proto` protection not be weakened cannot be met any other
  way. §27, P10.
- **`SessionLifetime` lives in `PublicChatProtectionOptions`** rather than a
  separate section. It answers the same question as the limits — how much may an
  anonymous stranger consume, and for how long — and splitting them would let an
  operator tighten one and forget the other.
- **No atomic single-request first send.** Considered and rejected as the brief
  permits; the reasoning and the residual case are in P19 and §36.

## 96. Defects discovered and fixed

Two real defects, both found by tests written in this phase:

1. **A malformed `X-Forwarded-For` cost every request its scheme.** With both
   headers in one pass, the middleware abandons all of its work at the first
   unparsable address — so a proxy forwarding one malformed value made every
   request fail the insecure-transport fail-safe, taking the whole deployment down.
   Fixed by splitting into two passes, scheme first.
2. **Deleting the legacy cookie stripped the credential just issued.**
   `Response.Cookies.Delete` removes pending `Set-Cookie` entries of the same name,
   matching the path as a substring — and every per-conversation path begins with
   the legacy one. Fixed by deleting before appending; the ordering is documented
   because it looks arbitrary.

And one implementation gap:

3. **A blank `Retry-After` was read as "retry now".** `Number('')` is `0`, which
   passed the integer and non-negative checks. Fixed by rejecting a blank value
   before conversion.

Also corrected: `RecordingAiTextGenerator` counted calls non-atomically, which
would have made the concurrency assertions — the most important in the phase —
unreliable under genuinely parallel provider calls.

## 97. Known issues

- The start-then-no-send race can leave an empty Conversation (P19, §36).
- Fixed windows allow up to twice a limit across a boundary (P8).
- Multi-key Lua assumes a single Redis instance or primary/replica, not a cluster
  (P7).
- Public reads are unprotected by design; read flooding belongs to a CDN or WAF.
- Separate people behind one IPv4 address or one IPv6 /64 share an allowance (P13).
- Rotating the network-key secret resets the IP-keyed buckets (P12).
- There is no alerting when a workspace approaches its safety budget.
- No provider usage accounting, token metering or billing exists; the budget bounds
  attempts only.

## 98. Deferred work

CAPTCHA and proof-of-work; bot reputation and device fingerprinting; billing plans
and plan-aware quotas; provider token metering and monetary accounting;
customer-configurable limits and an administrative screen for them; RAG, knowledge
bases, embeddings and pgvector; the embeddable third-party widget and cross-origin
rate limiting; WAF/CDN and Nginx-level protection; read flood protection; Redis
Cluster support; a sweeper for expired sessions or empty Conversations; Human
handoff, SignalR and a realtime inbox; budget alerting.

## 99. Blockers

None.

## 100. `git status --short`

35 modified tracked files and 21 untracked paths (29 untracked files). Nothing
staged, nothing committed.

## 101. `git diff --stat`

35 files changed, 2,156 insertions, 421 deletions in tracked files, plus 8,471
lines across 29 new files. Extensions: `.cs` (26), `.ts` (2), `.md` (1) among the
new files; the tracked changes are `.cs`, `.ts`, `.html`, `.scss`, `.json`, `.props`
and `.csproj`.

## 102. Secret review

The complete diff was inspected. No Redis production credential (the committed
connection string is blank), no production HMAC secret (the variable name appears;
no value does — test secrets are generated at runtime, and a test asserts no
settings file contains the variable), no raw client address, no raw anonymous
credential, no API key, no OpenAI credential, no password, no authentication cookie
value, no antiforgery token value, no database credential, no SSH credential, no
production connection string, no test backdoor, no development authorization bypass,
no Redis dump or data, no local PostgreSQL data, no Docker-generated data, no
screenshots, and no `node_modules`, `dist`, `bin` or `obj`.

The two environment-conditional branches in production code are the accepted cookie
policy (`Secure = !IsDevelopment()`) and the Development-only ephemeral network-key
secret, which enforces every limit in full and is documented in P12. Neither
bypasses a check.

## 103. VPS

Not accessed. No SSH, no production Nginx, no production database, no production
credential. Every verification ran against local containers on a private Docker
network with PostgreSQL and Redis unpublished.

## 104. Deployment

Not performed.

## 105. Readiness recommendation for Phase 17

The public chat surface is now bounded: distributed, atomic, fail-closed, with a
trustworthy and pseudonymous client identity, a workspace cost ceiling and a finite
anonymous session. Knowledge retrieval can be built on it, because each retrieval
inherits the same generation bound.

Before production, and independent of Phase 17: provider usage accounting and
alerting behind the safety budget (a workspace hitting its daily ceiling should
page somebody, not just refuse), email verification and password recovery, and a
deliberate rate-limiting policy for the authenticated surfaces — this phase
protected `/api/public/webchat/**` only, and sign-in is still unmetered.
