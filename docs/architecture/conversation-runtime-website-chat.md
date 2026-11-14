# Conversation runtime and hosted website chat

How a stranger on the internet talks to a workspace's AI, and what the platform
does with what they say.

Every phase before this one built the administrative product: workspaces, agents,
roles, members. None of it produced a single real conversation. This document
records the runtime that does — the first place where an unauthenticated request
reaches a paid provider, and therefore the first place where correctness is
measured in money as well as in data.

```
Visitor opens /chat/{publicKey}          public key: published, unguessable, not a secret
        |
        v
GET  /api/public/webchat/{publicKey}     agent display name + availability, nothing else
        |
        v
POST .../conversations                   Conversation + WebChatSession, one transaction
        |                                256-bit secret -> HttpOnly cookie; only its SHA-256 stored
        v
POST .../conversations/{id}/messages      one turn:
        |                                  1. authorise by credential, on this conversation only
        |                                  2. endpoint Active AND agent Active, re-checked
        |                                  3. idempotency: replay a settled turn, never re-spend
        |                                  4. take the per-conversation lock
        |                                  5. commit the customer message + turn
        |                                  6. call IAiTextGenerator  <-- no transaction held
        |                                  7. commit the reply, or record the failure
        v
Admin reads it                            membership + conversations.view, tenant-scoped
```

---

## C1. The runtime is a use case, not a service layer

**Decision.** One handler owns a turn end to end:
`SendWebChatMessageHandler`. There is no message bus, no background worker, no
job queue and no orchestration framework.

**Why.** A turn is a short synchronous exchange: somebody is waiting for an
answer with a browser open. Handing it to a queue would add durability nobody
asked for and take away the one thing the visitor needs — the reply, in the
response. The durable state that *is* required (§C10) is one row, not a workflow.

**Forbids.** A general workflow engine. Steps, retries, compensation and
scheduling, none of which any requirement asks for.

**Consequence.** The whole runtime is readable in one file, and the order of its
steps — which is the security and cost design — is visible in one place.

---

## C2. Website chat is its own concept, not fields on the agent

**Decision.** `WebChatEndpoint` is a separate aggregate: id, tenant, agent,
public key, status, timestamps. `AiAgent` gained nothing.

**Why.** An agent is what the business taught its AI to do; where that agent is
reachable from is a different question with a different lifecycle. Putting a
public key and a channel status on `AiAgent` would have meant every future
channel adding its own fields to the same aggregate, and an agent's version
changing because a channel was switched off.

**Forbids.** Channel-specific properties on `AiAgent`. Branding, colours, logos,
greeting builders, launcher positions, custom CSS and domain allowlists — every
one of them is presentation, and this phase is about whether the runtime behind
the page is correct. A settings bag now would fix a shape before anybody knows
what belongs in it.

**Consequence.** The endpoint is almost empty, deliberately. It answers one
question: which agent, and may the public reach it.

---

## C3. The public key is published, and nothing is built as though it were secret

**Decision.** `WebChatPublicKey` — 128 bits from the CSPRNG, 22 base64url
characters, stored in the clear, globally unique.

**Why it is not hashed.** The invitation token in
[team-members-invitations.md](team-members-invitations.md) is hashed because it
is a bearer credential and the database must not hold working copies. This value
is the opposite: it is *meant* to be published — printed in URLs, pasted into
chat clients, and eventually visible to every visitor of a customer's website.
Hashing it would buy nothing and would only turn the public lookup into an index
on a digest of something already public.

**Why it is random anyway.** Being public is not the same as being enumerable. A
sequential key would let anybody walk every workspace's chat endpoint, harvesting
agent names and spending every workspace's provider budget. Randomness stops
enumeration without making the value a credential.

**Forbids.** Treating possession as authorisation. No code path anywhere accepts
this value as evidence of membership, permission or identity.

**Consequence.** Holding a public key gets somebody exactly the public chat
product surface: an agent's display name, whether the chat is open, and the
ability to talk to that agent. Proven by test — a visitor holding a real key is
`401` on every admin route.

---

## C4. An endpoint binds one agent, and a conversation records that agent itself

**Decision.** An endpoint names exactly one agent, and cannot be re-pointed in
this phase. A conversation's `WebChatSession` records the `AiAgentId` it started
with, and the runtime reads the agent from *there*, not through the endpoint.

**Why the redundancy is the point.** Re-pointing an endpoint changes what every
future visitor talks to. Reading the agent through the endpoint would mean an
administrator changing it mid-afternoon silently moved every conversation already
in progress onto a different agent — and made one visitor's history look as
though it came from an agent that never saw it. Recording the agent on the
session makes that impossible before the capability to re-point even exists.

**Consequence.** Adding "change this endpoint's agent" later cannot disturb a
single existing conversation, and needs no migration to stay safe.

---

## C5. Live chat requires an active endpoint *and* an active agent, checked every turn

**Decision.** Both halves must hold, and both are re-read before every reply —
not once when the conversation started.

**Why not at creation.** An agent can be disabled and an endpoint withdrawn at
any moment. A check at conversation start is a snapshot that stops being true,
and the consequence of trusting it is a disabled agent still answering the public
until every open conversation happens to end.

**Why preview is different.** `PreviewAiAgentHandler` deliberately ignores
status: a draft, active and disabled agent all preview, because previewing is an
administrator testing a configuration. Serving the public is not testing, and the
two rules are allowed to differ.

**Consequence.** Disabling either half stops new conversations and new replies
immediately. Verified in Chrome: after disabling the endpoint, starting a
conversation and sending a turn both answered `503` while the existing transcript
stayed readable.

---

## C6. The visitor's credential is a conversation-scoped secret, not the conversation id

**Decision.** Starting a conversation mints a 256-bit CSPRNG secret. The database
stores **only** its SHA-256 digest, in `web_chat_sessions.access_token_hash`
(`bytea`). Access to a public conversation is: present a secret whose digest
matches *that conversation's* session, or be told the conversation does not exist.

**Why a conversation id cannot be the credential.** It travels in URLs, appears
in every administrator's list, and is guessable to anybody who has seen one.
Without a separate secret, knowing or guessing an id would be enough to read a
stranger's transcript and to talk in their conversation on the workspace's
provider budget.

**Why the digest and not the secret.** A copy of the sessions table must not be a
set of working conversation credentials — the same reasoning as invitation
tokens. A plain digest rather than a password KDF, because the input is 256 bits
of CSPRNG output: there is nothing to guess, and a deliberately slow hash would
only make every message slower.

**Its authority stops at one conversation.** `WebChatSession` holds no user, no
membership and no permission. `PublicWebChatAccess` returns a session and nothing
else — no principal, no claim, no tenant decision — and `TenantAuthorizer` has no
parameter it could be passed as. There is no expression in the platform in which
holding a conversation credential contributes to an admin decision.

**Proven, not asserted.** In the live application the stored digest was compared
byte for byte against SHA-256 of the raw cookie value the browser received: they
matched exactly, and a search of the database for the raw value found nothing.

---

## C7. The credential lives in an HttpOnly cookie, scoped to the chat API

**Decision.** `omniai.webchat`, `HttpOnly`, `SameSite=Lax`,
`Path=/api/public/webchat`, session-scoped, `Secure` outside Development. The
response that issues it carries `Cache-Control: no-store`, and no response body
anywhere contains the secret.

**Why a cookie and not `localStorage`.** Script never needs this value — the
browser attaches it by itself — so script never gets it. A token in
`localStorage` is readable by every later script on the origin, including one
introduced by a future mistake, and survives the browser being closed. The
narrow path means the credential is not even sent to the admin API or the
sign-in routes, so it cannot be captured there.

**Why session-scoped.** A visitor's conversation lasts as long as their browser
session. Persisting the credential to disk would leave a stranger's conversation
reachable on a shared computer long after they walked away — a worse failure than
having to start a new chat.

**Why `Secure` relaxes only in Development.** Development is served over plain
HTTP on a loopback address, and a cookie the browser refuses to send there would
make the feature untestable locally. Nothing else relaxes, and the automated
tests assert `Secure` outside Development.

**Verified in Chrome:** the `Set-Cookie` line carried `httponly`,
`path=/api/public/webchat` and `samesite=lax` with no expiry;
`localStorage`, `sessionStorage` and IndexedDB were all empty; the only
script-visible cookie was `XSRF-TOKEN`; and no secret appeared in the URL or the
console.

**This is a same-origin design, and that is a boundary rather than an oversight.**
A cookie scoped to this path works because the chat page is served from the same
origin as the API. A third-party embeddable widget runs on somebody else's site
and cannot use this at all; it will need its own cross-origin transport, decided
with the widget. Nothing here is weakened in advance to accommodate it.

---

## C8. Anonymous is not a CSRF exemption

**Decision.** Every unsafe hosted-chat request carries the platform's existing
antiforgery token, through the same `AntiforgeryEndpointFilter` the admin API
uses. The hosted page bootstraps the pair from the existing anonymous
`GET /api/auth/csrf`.

**Why.** These are still browser requests made from a page on this origin, and a
cross-site page can still cause a browser to send them. Exempting them because
"there is no session to steal" would be wrong twice over: there *is* a session —
the conversation cookie — and it is exactly what a cross-site request would ride
on. Verified: a request without the header is refused `400`.

**Forbids.** Weakening Phase 12's antiforgery design for the public surface.

---

## C9. The customer's message is committed before the provider is called

**Decision.** The order is: commit the customer message and its turn, then call
the provider, then commit the reply or record the failure. Each is its own short
transaction.

**Why.** What the customer said is a fact from the moment they said it, and it
must outlive the reply failing. A transcript that quietly loses what somebody
typed is worse than one that shows an unanswered message. Committing first is
also what makes the turn visible to a concurrent duplicate, which is how the
second request discovers it has nothing to do.

**No transaction spans the provider call.** Holding one open across a network
call to somebody else's service would pin a connection, hold row locks and keep a
snapshot alive for however many seconds that service felt like taking — which
under load is how a database runs out of connections and how vacuum stops keeping
up.

**Verified in Chrome** with the provider deliberately unconfigured: the customer
message persisted and was visible, the response was a genuine `503`, no reply was
invented, and the database showed the turn as `Failed` with no reply row.

---

## C10. One durable turn record, and it is the idempotency boundary

**Decision.** `ConversationTurn` — conversation, `ClientMessageId`, the customer
message it stored, the reply if there is one, and a status of
`Generating` / `Completed` / `Failed`. A unique index on
`(conversation_id, client_message_id)` is what settles concurrency.

**Why the client names the message.** A send that reaches the server and whose
response is lost looks, to the browser, exactly like a send that never arrived —
and to a proxy retry, a double click or a reconnect, exactly like a new message.
Because the client names the message *before* sending it, every repeat carries
the same name and can be recognised as the same turn rather than answered again
at a provider's price.

**Why the index and not a check.** Two concurrent requests carrying the same id
both pass any in-process test. The only place that can settle the race is the
database, so the handler consults the record twice — cheaply before the lock for
the common repeat, and again inside it for the racing one — and then relies on
the index refusing the loser's insert.

**A completed duplicate replays; a failed one does not retry.** Repeating a
completed turn returns the *stored* rows, so a repeat and the original are the
same answer rather than two that happen to read alike. Repeating a failed turn is
answered `409`: every reply costs the workspace money, and the platform will not
spend it again on what may simply be an automatic retry. Somebody who genuinely
wants to try again sends a new message, which carries a new id.

**Verified live**: three POSTs with one `clientMessageId` produced `503`, `409`,
`409`, one turn row, and exactly one stored customer message.

---

## C11. Generation is serialised per conversation, by the database

**Decision.** `IConversationLock`, implemented with a PostgreSQL **session-level
advisory lock** on a key derived from the conversation id, held on a dedicated
connection for the duration of the turn.

**Why the scope is one conversation.** Two messages answered at once both read
the same history, both answer as though the other had not happened, and the
transcript ends with two replies to the same state in an order nobody can
explain — each paid for against a context that was already stale. Two *different*
conversations have nothing to say to each other, and serialising them together
would make every workspace's chat as slow as the slowest reply anywhere on the
deployment.

**Why not a `SemaphoreSlim`.** An in-process lock is correct exactly until there
are two processes, and then it silently stops working — no error, no failing
test, just interleaved conversations under load. The platform will run more than
one instance, so the only honest place for this boundary is somewhere both
instances can see.

**Why advisory and not a row lock.** A row lock requires an open transaction, and
this hold has to span the provider call. An advisory lock holds no rows and
blocks no other work.

**Crash safety comes from the connection, not from careful code.** The lock is
taken on a connection that is closed when the hold is disposed. If the process
dies mid-generation the connection dies with it and PostgreSQL releases every
advisory lock that session held. There is no lease to expire, no heartbeat to
miss, and no state that can be left stuck — which is exactly the failure a
home-grown "lock row with an expiry" table would have.

**Known and accepted:** advisory keys are 64 bits and a conversation id is 128,
so two conversations could in principle share a key. The only consequence is that
those two take turns — a rare, harmless loss of concurrency. Nothing depends on
two particular conversations generating at the same instant.

---

## C12. History is replayed in ingestion order, and never sorted by time

**Decision.** The runtime reads the conversation's tail ordered by
`message_ingestion_sequence` and hands it to the provider in that order.
`SentAt` is display only.

**Why.** This is the Phase 3 invariant, and Phase 15 is the first code that could
have broken it. In a distributed omnichannel system webhook delay, retries,
provider redelivery and bulk imports all mean a message can arrive after one that
was sent later. Ingestion order is what this platform actually knows; a timestamp
is what a channel claimed.

**Enforced, not intended.** No `OrderBy` on `SentAt` exists anywhere in the
Phase 15 code, admin pagination included. A regression test builds a history whose
timestamps run backwards and asserts the provider still receives it in ingestion
order.

**Mapping.** `Customer → User`, `AiAgent → Assistant`. `System` notes are never
replayed — they are the platform talking *about* the conversation, not somebody
talking in it, and replaying one would let internal audit text be read as a
customer's words and as an instruction to the model. `HumanAgent` is not replayed
either: human handoff does not exist, and whether a colleague's words should be
presented to the AI as its own prior turns is a decision that belongs with the
handoff design.

---

## C13. The context window is a resource policy, not token counting

**Decision.** `ConversationContextPolicy`: at most **40 messages** and at most
**24,000 characters**, whichever binds first. The window is taken from the newest
end. Only whole messages are dropped. Order is never changed.

**Why not tokens.** Counting tokens means knowing the provider's tokeniser, which
means the Application knowing which provider it is talking to — exactly the
coupling `IAiTextGenerator` exists to prevent — and a number that changes when a
model changes. Whole messages and characters are provider-independent, stable,
and understandable to anybody reading a transcript. A provider needing a tighter
bound can enforce its own; it has the knowledge to.

**Why two bounds.** A message count alone lets forty long messages through and
blows the budget; a character count alone lets a thousand one-word messages
through, each of which still costs structure.

**Why newest-first and whole messages only.** The recent exchange is what the next
reply has to be consistent with. Nothing is ever cut in half: a half-message
changes what somebody said, and a model asked to continue from a sentence that
stops mid-word answers something nobody said either. The newest message always
survives, even if it alone exceeds the character bound — otherwise a long question
would be dropped and the provider asked to answer the conversation without it.

**Instructions are never traded against history.** They are sent separately. An
agent that loses its instructions to make room for chat stops being the agent the
workspace configured.

---

## C14. Each reply uses the agent's current instructions

**Decision.** Instructions are read at the moment of the turn, not snapshotted
when the conversation started.

**Why.** A workspace that corrects its agent expects the correction to take
effect, and it does — from the next turn onwards. Earlier replies are left as
they were said, because rewriting them would be rewriting history.

**Deferred.** Agent versioning and per-conversation configuration snapshots. When
an audit trail of "which configuration produced this reply" is required, it
arrives with the audit design rather than being guessed at now.

---

## C15. Provider failure is reported, never disguised

**Decision.** The existing provider translation is reused unchanged:
`503` unavailable, `504` timeout, `502` unusable answer. On any of them the
customer message stays, the turn is recorded `Failed`, and no message is written.

**Forbids.** A placeholder reply. An apology stored as though the agent had said
it. Any automatic retry of a paid request.

**What the visitor sees.** A sentence they can act on — that no answer came back,
that their message was received, and that trying again is worth it. Deliberately
*not* the shared administrator copy: "AI preview is currently unavailable because
no provider is configured" tells somebody who came to ask about a delivery about
this platform's deployment, and is internal operational detail the public has no
business being given. The distinction between an unconfigured provider, a timeout
and a bad answer is also withheld — to a visitor they are one thing, and telling
them which would be reporting on the business's infrastructure. The real status is
in the response and the logs, where operators look.

**Found in the browser.** The first Chrome run showed the shared copy verbatim on
the public page. It is fixed and pinned by a test that asserts the words
*provider*, *preview*, *configured*, *runtime* and *deployment* never reach a
visitor.

---

## C16. Public and admin authorisation are separate systems that never substitute

**Decision.**

| | Admin | Public visitor |
| --- | --- | --- |
| Identity | authenticated user | none |
| Scope | `TenantMembership` | one conversation |
| Right | `conversations.view` | a matching credential |
| Resolved by | `TenantAuthorizer` | `PublicWebChatAccess` |

**Neither can stand in for the other**, and it is structural rather than
enforced: `PublicWebChatAccess` returns a `WebChatSession` and nothing an
authorizer accepts; `TenantAuthorizer` never reads a cookie. Verified in Chrome
both ways — an owner's session reading a visitor's conversation is `404`, and a
visitor holding a real credential is `401` on every admin route.

**Every refusal on the public side is not-found.** A distinct "wrong credential"
would confirm that a conversation id is real, which is the fact somebody walking
ids is trying to learn.

---

## C17. Reading conversations is a permission of its own

**Decision.** Three codes: `conversations.view`, `website-chat.view`,
`website-chat.manage`. None implies another.

**Why `conversations.view` is separate.** A conversation is the most sensitive
business data this platform holds — what a customer actually said, in their own
words, to a business they probably did not expect to be read by everybody who
works there.

**Why the channel codes are separate from each other and from conversations.**
Seeing that a chat endpoint exists and deciding whether the world can talk to it
are different responsibilities; activating one starts spending the workspace's
provider budget on whoever finds the link. And administering the channel is not
the right to read what customers said on it — proven by test in both directions.

**Not added:** `conversations.reply`, `.assign`, `.delete`, `.export`,
`.handoff`. None of those operations exists, and a permission that grants nothing
is a promise this platform has not made.

**Owner receives all three intrinsically**, through the existing Owner semantics,
so the migration writes no permission rows.

---

## C18. Admin transcripts page by ingestion sequence

**Decision.** Conversations are listed most-recently-active first with the
conversation id as a tie-break. Transcripts page on a cursor of "after ingestion
sequence N".

**Why a cursor and not a page number.** A conversation is still being added to
while somebody reads it. A page number shifts under every new message — a
customer replying between two page reads would move every subsequent offset, and
the reader would see a message twice or miss one. The ingestion sequence is
stable and strictly increasing in the order history is read, which is exactly
what a cursor needs.

**Why the tie-break.** Two conversations sharing a `LastActivityAt` would
otherwise swap places between pages and hide a row or show one twice.

**Tenant isolation is in the predicate**, not checked afterwards: a conversation
id from another workspace finds nothing rather than being found and rejected.
Ownership is settled before a single message is read, so a foreign id cannot
become an alternate route into another workspace's transcripts.

---

## C19. Plain text, and the client renders it as text

**Decision.** Messages are plain text. There is no Markdown rendering, no HTML,
no attachments. Angular renders every message through ordinary interpolation, and
nothing on the hosted page or in the admin uses `innerHTML`.

**Why.** Rendering customer-supplied text as markup is the classic way a chat
product acquires an XSS hole, and a rich-text pipeline would need a sanitiser
whose configuration becomes a security surface of its own. Nothing in this phase
needs it.

**Verified**: `<script>alert(1)</script>` sent through the hosted chat is stored,
returned and displayed as visible text, in the visitor's page, the admin
transcript and the admin list preview.

---

## C20. Public input is bounded more tightly than the Domain

**Decision.** The hosted chat accepts at most **4,000 characters** per message.
Oversized input is refused `400`, never trimmed.

**Why not the Domain's 32,000.** That bound exists to stop any message becoming a
storage and rendering problem, and applies to channels this platform trusts. This
one applies to an unauthenticated browser nobody has vouched for, and it is a
different question: how much text should a stranger be able to push into a
workspace's database and into a paid call, per request. It also sits under
`AiTurn.MaxTextLength`, so every message this surface accepts can actually be
replayed as a turn.

**Why refused and not truncated.** Answering half of somebody's message is worse
than telling them it was too long.

---

## C21. No polling

**Decision.** The admin conversation screens refresh on a button. There is no
timer, no polling interval and no SignalR.

**Why.** A timer firing every few seconds against every open admin tab is a load
pattern nobody asked for, and it still would not be realtime. Live updates
deserve a deliberate phase with a real transport; until then a Refresh button is
honest about what it does.

---

## DECIDED NOW

- The conversation runtime as one use case, with no workflow engine
- `WebChatEndpoint` as its own aggregate; `AiAgent` unchanged
- Draft / Active / Disabled lifecycle, created switched off
- 128-bit public key, published in the clear, unguessable, not a credential
- One endpoint per agent per workspace, enforced by a unique index
- Endpoint bound to one agent; the conversation records that agent itself
- Live chat requires an Active endpoint **and** an Active agent, re-checked every turn
- Preview semantics unchanged: status is not consulted there
- Hosted `/chat/{publicKey}` route, outside the shell, behind no guard
- Public config publishes an agent display name and availability, nothing else
- 256-bit CSPRNG conversation credential; only its SHA-256 digest persisted
- Credential in an `HttpOnly`, path-scoped, session-scoped cookie, `Secure` outside Development
- `Cache-Control: no-store` on every response that carries a credential or a transcript
- Antiforgery required on anonymous writes
- Customer message committed before the provider is called
- `IAiTextGenerator` reused; no provider type outside Infrastructure
- Current agent instructions on every turn
- History in ingestion order, `SentAt` never an ordering
- Provider-independent context bound: 40 messages, 24,000 characters, whole messages only
- `ConversationTurn` as the durable idempotency and outcome record
- `(conversation_id, client_message_id)` unique — the idempotency boundary
- Completed duplicates replay the stored rows; failed ones are terminal
- Per-conversation PostgreSQL session advisory lock; crash-released by the connection
- Different conversations generate concurrently
- Provider failure keeps the customer message, records `Failed`, invents nothing
- No automatic retry of a paid request
- Customer-facing failure copy that leaks no operational detail
- Read-only admin conversation list, detail and cursor-paged transcript
- Ingestion-sequence cursor pagination
- `conversations.view`, `website-chat.view`, `website-chat.manage`; none implies another
- Public and admin authorisation structurally unable to substitute
- Plain text only, rendered as escaped text; no `innerHTML`
- 4,000-character public message bound, refused rather than truncated
- Manual refresh; no polling

## DEFERRED

- Third-party embeddable widget, and its cross-origin transport and storage
- Origin allowlists for embedding
- Website chat branding, colours, logos, greeting builders, launcher customisation
- Human agent replies, and human handoff
- SignalR and any realtime inbox
- Read/unread state, and conversation assignment
- Conversation close and reopen from the admin
- RAG and knowledge retrieval
- Tools and function calling
- WhatsApp, Instagram, Messenger
- Rate limiting for public chat, bot abuse protection and CAPTCHA
- Empty-conversation cleanup (§ known limitation: every hosted page load starts one)
- More than one live conversation per browser (one cookie, so a new conversation
  replaces the previous credential)
- Agent configuration snapshots per conversation, and reply-level audit
- Transcript export, retention policies and PII controls
- Provider token accounting and per-workspace spend limits
- Production deployment

## Phase 37.5a: what the hosted page is told

`GET /api/public/webchat/{publicKey}` (anonymous, as before) gains four fields beside
`agentName` and `available`:

| Field | Value |
| --- | --- |
| `workspaceName` | The workspace's display name (owner decision D-3), so a visitor knows whose chat it is. The owner changes it with `PUT /api/tenants/{id}/name` |
| `greeting` | The widget's greeting, or `null` |
| `accentColor` | The widget's accent colour, as on the embedded read |
| `theme` | `System`, `Light` or `Dark`, as on the embedded read |

Each passes the billboard rule: a visitor sees all of them on the page anyway, and
the embedded widget already published the appearance. They come from the same single
statement as before (the public-key read now also joins `tenants` for the name only,
by the endpoint's own tenant). Nothing else about the workspace is read or published:
no slug, status, plan, member, other endpoint or id. An unknown key still answers the
same generic `404` as before (no `detail`, no `code`), and the public-chat rate limits
are unchanged.

## Phase 39.1: the greeting in the page's language

`GET /api/public/webchat/{publicKey}?lang=en|ar` answers `greeting` in that language
when the workspace set one, else the fallback greeting; no `lang`, or one this
platform does not know, answers exactly as before. No other member changes and none
is added. The admin side and the rules are in `embeddable-web-chat-widget.md` §13.
