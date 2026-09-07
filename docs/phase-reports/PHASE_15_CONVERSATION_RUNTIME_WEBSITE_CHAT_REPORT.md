# Phase 15 — Conversation runtime and hosted website chat

## 1. Phase objective

Make OmniAI a real conversational product rather than only an admin platform: a
workspace Owner configures a website chat endpoint for an AI agent, gets a public
URL, and an anonymous visitor opens it, starts a conversation and sends messages
that the runtime answers through `IAiTextGenerator` — with both sides persisted
and readable from the admin. No fake conversation data.

---

## 2. Final status

**IMPLEMENTED, DOCUMENTED AND VERIFIED.**

| Gate | Result |
| --- | --- |
| Backend tests | **1687 passed, 0 failed, 0 skipped** |
| Frontend tests | **348 passed, 0 failed** (30 files) |
| Release build, 8 projects | 0 warnings, 0 errors |
| Frontend production build | succeeded — 385.75 kB initial, 94.43 kB transfer |
| `prettier --check .` | clean |
| `npm audit` | 0 vulnerabilities |
| `dotnet list package --vulnerable --include-transitive` | none, all 8 projects |
| Migration | applied to a clean database, schema read back |
| Chrome workflow | performed — Owner, visitor, admin, two-visitor isolation, disable |

One real defect was found by driving the browser and is fixed (§17). No live
OpenAI call was made and no API key was configured at any point.

---

## 3. Baseline commit

`ac2db14d49339cde659d5ecb69fda5ecfd13d16f` — *feat: implement team members and
secure invitations*, the accepted Phase 14 checkpoint.

Nothing was reset, cleaned, restored, stashed or discarded at any point.

---

## 4. Interruption and recovery

**The previous session was interrupted by Claude's usage/spend limit, not by a
code failure.** This report records the recovery honestly.

**Recovery state detected: STATE D** — implementation complete and automated
verification complete; Chrome/browser verification and documentation outstanding.

Determined by inspection, not by trusting the interrupted session's visible
numbers:

- `git log` — HEAD still `ac2db14`, nothing staged, nothing committed.
- `git status --short` — 17 modified tracked files, 80 new files, all Phase 15
  (the two Phase 15 documents did not yet exist).
- `git diff --check` — no whitespace errors.
- Neither `docs/architecture/conversation-runtime-website-chat.md` nor this
  report existed.
- Docker Desktop was **not running** (the engine had stopped), so no verification
  environment survived; containers existed but were stopped.

**Interruption point recovered:** the previous session had just finished the
frontend gates (30 files / 346 tests, `format:check` clean) and was reading the
production-build bundle line. Everything before that was already on disk.

**Re-proven rather than assumed.** Every gate was executed again in this session:
the full backend suite, the vulnerability scan, the frontend suite, formatting,
`npm audit` and the production build. The frontend total is now **348** rather
than 346 because this session added two tests for the browser-discovered defect.

**Work completed after recovery:** Docker restarted and the environment rebuilt;
all automated gates re-run; the complete Chrome verification performed; one
defect found and fixed with two regression tests; responsive and theme
verification; both documents written; final diff and secret review.

---

## 5. New permissions

Three, and none implies another:

| Code | Grants |
| --- | --- |
| `conversations.view` | Read the workspace's conversations |
| `website-chat.view` | See how website chat is configured |
| `website-chat.manage` | Create chat links and decide whether the public can use them |

`conversations.reply`, `.assign`, `.delete`, `.export` and `.handoff` were **not**
added, because none of those operations exists. The Owner receives all three
intrinsically, so **the migration writes no permission rows** — verified by
reading it (no `InsertData`).

Two existing guard tests correctly fired and were updated deliberately:
`The_catalogue_contains_no_capability_for_an_unimplemented_area` (conversations
and channels are now genuinely implemented) and
`Each_current_capability_is_present_and_named_as_documented`.

---

## 6. Existing Conversation domain reused

`Conversation`, `Message`, `ConversationId`, `MessageId`, `ChannelKind`,
`ConversationStatus`, `MessageSender` and `MessageContentType` were reused
**unchanged**. Messages are appended through `Conversation.AddTextMessage`, so
every Phase 3 invariant still applies, and `Message.MaxTextLength` (32,000) was
already a suitable platform bound.

## 7. Conversation modifications

**None to `Conversation` or `Message`.** Phase 15 added new types beside them:
`ConversationTurn`, `ConversationTurnId`, `ConversationTurnStatus` and
`ClientMessageId`. `LastActivityAt` high-water behaviour is untouched and still
advances through `RecordActivity`.

## 8. `WebChatEndpoint` domain

Aggregate with `Id`, `TenantId`, `AiAgentId`, `PublicKey`, `Status`, `CreatedAt`,
`UpdatedAt`; `Create` (as a draft), `FromStored`, `Activate`, `Disable`, and
`IsServingPublicTraffic`. Lifecycle changes are idempotent and `UpdatedAt` is a
high-water mark. No branding, colour, greeting or allowlist field exists.

## 9. `WebChatEndpointId`

`readonly record struct` over a UUIDv7, matching every other identifier. It is
the administrative identity only — the public URL carries `WebChatPublicKey`
instead, so publishing a chat page never publishes the id the admin API is
addressed by.

## 10. Public-key design

`WebChatPublicKey` — 128 bits from `RandomNumberGenerator`, 22 base64url
characters, fixed length, validated alphabet, **stored in the clear**, globally
unique (`ux_web_chat_endpoints_public_key`).

Deliberately not hashed: it is published in every hosted URL, so a digest would
protect nothing. Random anyway, because being public is not being enumerable.
`TryCreate` answers a malformed key without an exception, so a typo in an address
is indistinguishable from an unused key and never reaches a query.

## 11. Endpoint lifecycle

`Draft → Active → Disabled`, reversible. Created as a draft so a mistyped agent
selection is never live the moment it is saved. Only `Active` serves the public.

## 12. Agent binding

One endpoint names exactly one agent, and cannot be re-pointed in this phase. One
chat link per agent per workspace (`ux_web_chat_endpoints_tenant_agent`). A
composite foreign key onto the agent's `(id, tenant_id)` alternate key makes a
cross-workspace pairing unrepresentable rather than merely validated against.

## 13. Live agent-status rule

Live chat requires **both** an `Active` endpoint and an `Active` agent, re-read
before every reply rather than once at conversation start — an agent can be
disabled at any moment, and a start-time check is a snapshot that stops being
true. Preview semantics are unchanged: status is not consulted there.

## 14. Existing conversation agent binding

`WebChatSession` records the `AiAgentId` the conversation started with, and the
runtime reads the agent from the session — never through the endpoint. Adding
"re-point this endpoint" later therefore cannot move a conversation already under
way onto a different agent.

## 15. Public hosted-chat architecture

An Angular route `/chat/:publicKey`, outside the admin shell and behind no guard,
lazily loaded. It calls only `/api/public/webchat/**`, which is anonymous and
workspace-less. Its API client is a separate service from every admin client, so
no component can hold both kinds of authority.

## 16. Anonymous conversation credential

Minted per conversation. Its authority is exactly one conversation:
`WebChatSession` holds no user, membership or permission, and
`PublicWebChatAccess` returns a session and nothing an authorizer would accept.
Every public refusal is not-found, so a wrong credential cannot confirm that a
conversation id is real.

## 17. Token generation

`RandomNumberGenerator.GetBytes(32)` — 256 bits — rendered unpadded Base64Url (43
characters), zeroed from the working buffer after use. Recognition rejects
anything of the wrong length or alphabet before a query runs.

## 18. Token hashing

SHA-256, compared with `CryptographicOperations.FixedTimeEquals` through
`WebChatAccessTokenHash`, whose `ToString()` returns its own type name so no
digest reaches a log line. A plain digest rather than a KDF: the input is 256 bits
of CSPRNG output, so there is nothing to guess.

## 19. Raw-token storage proof

Proven against the live database, not asserted:

```
SHA-256 of the raw cookie token: bb9a37f4db42735e2d7818f3013432f42d879b0e8eeaba0448ff75c35c761c04
Stored digest:                   bb9a37f4db42735e2d7818f3013432f42d879b0e8eeaba0448ff75c35c761c04
```

The digest column is `bytea`, 32 bytes. A search of the database for the raw
value returned **0 rows**. A schema test additionally asserts that the only
credential-shaped columns in the entire database are
`tenant_invitations.token_hash` and `web_chat_sessions.access_token_hash`, both
`bytea`, and that `web_chat_sessions` has exactly six columns — no visitor name,
IP address, user agent or email.

## 20. Cookie / access design

`omniai.webchat`, `HttpOnly`, `SameSite=Lax`, `Path=/api/public/webchat`,
session-scoped, `Secure` outside Development. Read only from the cookie — never
from a header, query string or body. Observed on the wire:

```
set-cookie: omniai.webchat=…; path=/api/public/webchat; samesite=lax; httponly
cache-control: no-store
```

No `expires`/`max-age`. The response body carries `conversationId` and
`agentName` only.

## 21. CSRF

Anonymous writes are antiforgery-checked through the existing
`AntiforgeryEndpointFilter`; the hosted page bootstraps the pair from the existing
anonymous `GET /api/auth/csrf`. A write without the header is `400`. Phase 12's
design is unchanged.

## 22. Public start endpoint

`POST /api/public/webchat/{publicKey}/conversations` — requires an Active
endpoint and Active agent, creates the conversation with channel `WebChat` and its
session in one transaction, sets the cookie, and returns id plus agent name. The
command carries a public key and nothing else: no tenant, agent or channel a
visitor could choose.

## 23. Public message endpoint

`GET`/`POST /api/public/webchat/conversations/{conversationId}/messages`. The
credential must match that exact conversation; knowing another id is insufficient.

## 24. Message request contract

`{ clientMessageId, text }` — two fields. No sender, tenant, agent, timestamp,
message id or ingestion sequence, asserted by a frontend test on the request
body's key set. The server owns all of those.

## 25. Message content limit

4,000 characters for the public surface — far below the Domain's 32,000, because
this is an unauthenticated browser, and below `AiTurn.MaxTextLength` so every
accepted message can be replayed as a turn. Oversized input is `400`, never
truncated.

## 26. Plain-text rendering

Plain text only; no Markdown, HTML or attachments. Every message is rendered
through Angular interpolation, and no `innerHTML` exists on any Phase 15 screen.

## 27. Idempotency design

`ConversationTurn` keyed by `(conversation_id, client_message_id)` with a unique
index. The handler consults the record twice — cheaply before taking the lock for
the common repeat, then again inside it — and relies on the index to settle a true
race.

## 28. Duplicate request result

A completed duplicate returns the **stored** customer message and reply, so a
repeat and the original are the same rows rather than two that read alike. A
failed one is terminal and answers `409`, because the platform will not re-spend
on what may be an automatic retry.

## 29. Concurrent duplicate result

Three concurrent sends with one `clientMessageId`: **exactly one turn, exactly one
provider invocation**, and every other attempt refused. Asserted against a
provider double that counts its own calls.

## 30. Same-conversation serialisation

`IConversationLock`, implemented as a PostgreSQL **session-level advisory lock**
on a key derived from the conversation id, held on a dedicated connection across
the provider call. A test holds one turn open with a gated provider and asserts
`MaxConcurrentHolders == 1` while both messages are answered, both stored exactly
once, and the transcript reads as two complete exchanges rather than two questions
followed by two answers.

## 31. Different-conversation concurrency

Two conversations reach the gated provider **simultaneously** — asserted by
waiting for the provider's call count to reach 2 before releasing the gate — with
two concurrent holders. Nothing globally serialises generation.

## 32. Crash / lock semantics

The lock lives on a connection that closes when the hold is disposed; if the
process dies, PostgreSQL releases every advisory lock that session held. There is
no lease to expire and no state that can be left stuck. The explicit unlock is a
courtesy so a pooled connection is not returned still holding one; a broken
connection is treated as already released.

## 33. Customer persistence before generation

The customer message and its turn are committed in one transaction **before** the
provider is called. No business transaction spans the provider call — holding one
open across a third party's network call would pin a connection and hold row
locks for however long that service took.

## 34. Provider success

With a deterministic double: the request carries the agent's **current**
instructions and the conversation history in ingestion order with
`Customer→User` / `AiAgent→Assistant`, and exactly one `AiAgent` message is
persisted. Changing the agent's instructions mid-conversation is observed on the
*next* turn's request and not retrospectively.

## 35. Provider failure

`503` unavailable, `504` timeout and `502` unusable answer each leave the customer
message stored, the turn `Failed`, no reply row, and **exactly one** provider
call — nothing retried automatically.

## 36. `IAiTextGenerator` mapping

Reused unchanged. No conversation handler references OpenAI, no provider type
leaves Infrastructure, and no second provider abstraction was created. The
existing architecture dependency tests still pass.

## 37. Agent instructions behaviour

Read at the moment of each turn, never snapshotted. A correction takes effect from
the next turn; earlier replies are left as they were said.

## 38. Context resource bound

`ConversationContextPolicy`: **40 messages** and **24,000 characters**, whichever
binds first, taken from the newest end, whole messages only, order never changed.
Provider-independent by design — token counting would require the Application to
know which provider it is talking to. `System` and `HumanAgent` messages are not
replayed. A message longer than `AiTurn.MaxTextLength` is dropped rather than
shortened.

## 39. Ingestion-order guarantee

Every message ordering in Phase 15 is `message_ingestion_sequence`. A repository
grep confirms **no `OrderBy` on `SentAt` anywhere** in the new code, admin
pagination included.

## 40. Out-of-order timestamp regression

Two tests: one builds a history whose `SentAt` values run backwards and asserts
the provider still receives ingestion order; another does the same through the
runtime with a seeded conversation. A window cut by a bound is also asserted to
stay in ingestion order.

## 41. `LastActivityAt`

Untouched Phase 3 behaviour — a high-water mark that never regresses. Observed in
the admin list, which orders by it with the conversation id as a tie-break.

## 42. Public access isolation

Verified in Chrome with two isolated browser contexts, both holding real
credentials:

| Attempt | Result |
| --- | --- |
| A reads A's own conversation | `200`, its own message |
| A reads B's conversation | `404` |
| A writes to B's conversation | `404` |
| A reads an invented conversation id | `404` |
| B's transcript after A's attempt | unchanged, no intrusion |
| Owner's admin session reads a visitor conversation | `404` |

## 43. Public-key security result

A visitor holding the real public key received `401` on every one of
`/conversations`, `/website-chat`, `/website-chat/{id}`, `/ai-agents`, `/members`
and `/roles`. The public config response was asserted to contain no tenant id, no
agent id, no instructions and no workspace detail — and the view type has exactly
two properties, so adding one would have to be deliberate.

## 44. Admin conversation API

`GET /api/tenants/{tenantId}/conversations`, `/{conversationId}` and
`/{conversationId}/messages`. Read-only: no reply, assignment, close or delete
endpoint exists.

## 45. List pagination

Bounded page size (default 20, max 50), ordered `LastActivityAt DESC` with the
conversation id as a stable tie-break. Rows carry a message count and a
160-character preview read as a correlated subquery, so a page of fifty
conversations is one round trip and no transcript is loaded to show one line.

## 46. Message pagination

Cursor pagination on the ingestion sequence — `?after=N&limit=…`, default 100,
max 200 — because a page number shifts under every new message while somebody is
reading. A test walks a conversation in pages of three and asserts all eight
messages appear exactly once in one strictly increasing run.

## 47. Tenant isolation

Every admin query carries the tenant in the predicate. A foreign conversation id
answers `404` for both detail and messages, and ownership is settled before a
single message is read. Non-member `404`, member without the permission `403`,
anonymous `401`.

## 48. Website Chat admin API

`GET`/`POST /api/tenants/{tenantId}/website-chat`,
`GET /…/{id}`, `POST /…/{id}/activate`, `POST /…/{id}/disable`. Reading needs
`website-chat.view`; creating and lifecycle need `website-chat.manage`.

## 49. Website Chat ETag

Strong `ETag` from the row's `xmin` shadow property; `If-Match` required;
`428` when absent, `412` when stale, new `ETag` on success. Observed in Chrome:

```
GET  …/website-chat/{id}          → 200, etag: "AAA1PA"
POST …/website-chat/{id}/activate → if-match: "AAA1PA", x-xsrf-token: …
                                  → 200, etag: "AAA1PQ"   (Draft → Active)
```

A stale tag is refused and changes nothing; the frontend offers a deliberate
reload and retries nothing.

## 50. Migration

`20260906233518_AddWebsiteChatAndConversationRuntime` — one migration.

## 51. Migration inspection

Read by hand. The scaffolder again emitted a physical `xmin` column for
`web_chat_endpoints`; it was **removed**, as in all four earlier migrations,
because a real column of that name would shadow PostgreSQL's system column and
silently disable every concurrency check built on it. An explicitly named
alternate key (`ak_web_chat_endpoints_id_tenant`) replaced EF's inferred one. The
migration contains no `InsertData` and no raw `Sql`, and `Down()` is symmetric.

## 52. PostgreSQL constraints

Verified against the live schema:

- `ux_web_chat_endpoints_public_key` — unique
- `ux_web_chat_endpoints_tenant_agent` — unique
- `ux_web_chat_sessions_access_token_hash` — unique
- `ux_conversation_turns_client_message` — unique on `(conversation_id, client_message_id)`
- `ux_conversation_turns_customer_message` — unique
- `fk_web_chat_endpoints_agent`, `fk_web_chat_sessions_endpoint`,
  `fk_web_chat_sessions_agent` — composite, all reading the same `tenant_id`
- `web_chat_sessions` primary key is `conversation_id` alone
- `fk_conversation_turns_conversation` — `ON DELETE CASCADE`
- `ix_messages_conversation_ingestion` — still unique, still the only ordering
- no physical `xmin` column on `web_chat_endpoints`

## 53. Raw-secret DB inspection

See §19. Only the digest exists; the raw value is nowhere in the database.

## 54–57. Test totals

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `OmniAI.Domain.Tests` | 471 | 0 | 0 |
| `OmniAI.Application.Tests` | 414 | 0 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 317 | 0 | 0 |
| `OmniAI.Api.IntegrationTests` | 485 | 0 | 0 |
| **Backend total** | **1687** | **0** | **0** |
| Frontend (Vitest, 30 files) | 348 | 0 | — |

Phase 15 contributed 131 backend tests and 41 frontend specs:

| File | Tests |
| --- | ---: |
| `WebChatEndpointTests` (incl. public key) | 13 |
| `WebChatSessionTests` (incl. digest) | 9 |
| `ConversationTurnTests` (incl. `ClientMessageId`) | 13 |
| `WebChatRuntimeTests` | 24 |
| `ConversationContextPolicyTests` | 14 |
| `WebChatSchemaTests` | 14 |
| `WebChatAdminApiTests` | 15 |
| `HostedChatApiTests` | 16 |
| `ConversationAdminApiTests` | 13 |
| `hosted-chat.spec` | 13 |
| `website-chat-page.spec` | 7 |
| `website-chat-detail.spec` | 6 |
| `conversation-list.spec` | 7 |
| `conversation-detail.spec` | 8 |

## 58. Idempotency tests

Repeat returns the same stored rows with one provider call and one turn; three
concurrent duplicates produce one turn and one provider call; a failed id is not
retried. Also proven live over HTTP (§29, §68).

## 59. Provider-call-count tests

Every runtime test asserts on a provider double that counts its own calls —
cross-tenant, unavailable-endpoint and refused-input cases assert **zero**.

## 60. Concurrency tests

Same-conversation serialisation (one holder, ordered transcript) and
different-conversation concurrency (two simultaneous provider calls, two holders).

## 61. Cross-visitor tests

At the Application layer, over HTTP, and in Chrome — all three.

## 62. Authorization matrix

| Caller | Website chat | Conversations |
| --- | --- | --- |
| Anonymous | `401` | `401` |
| Non-member | `404` | `404` |
| Member, no permission | `403` | `403` |
| `website-chat.view` | read `200`, create `403` | `403` |
| `website-chat.manage` | full | `403` |
| `conversations.view` | `403` | `200` |

## 63. Frontend API architecture

Three focused services, kept apart so the authorisation boundaries stay legible:
`WebChatAdminApi`, `ConversationsApi` (both authenticated, workspace-scoped) and
`HostedChatApi` (anonymous). No component calls `HttpClient` directly, and there
is no generated client. `HostedChatApi` has no parameter or field that could hold
a credential.

## 64. Website Chat admin UI

`/channels` lists channels with website chat as the only one offering set-up and
the rest honestly labelled "Not built yet". `/channels/website-chat` lists links
and creates one (agents that already have a link are not offered). The detail
screen shows status, agent status, timestamps, the hosted URL with a copy button,
and publish/disable behind the conditional write, with a stale-write banner and a
warning when the bound agent is not active.

## 65. Hosted chat UI

`/chat/:publicKey` — agent name, transcript, composer, Send, sending state,
failure state with retry guidance and no automatic retry, and one indistinct
unavailable state. Labelled composer, keyboard submit, `aria-live` transcript,
sender carried by name and alignment rather than colour alone.

## 66. Conversations admin UI

List with channel badge, agent, last activity and preview; detail with the
transcript in ingestion order, customer/AI distinction, timestamps, "Load older
messages" and manual Refresh. No reply box, no assignment, no handoff.

## 67. XSS test

`<script>alert(1)</script>` sent through the hosted chat is stored, returned and
displayed as visible text in the visitor's page, the admin transcript and the
admin list preview. Asserted at the API and in three frontend specs; no `script`
or `img` element is created.

## 68. Chrome Owner workflow

Registered `owner@example.test`, created *Acme Trading*, saw Conversations and
Channels appear in the navigation, created and activated *Support Concierge*,
opened Channels → Website chat, created a chat link (Draft), opened it, published
it, captured the ETag/If-Match/new-ETag exchange (§49), and read the hosted URL
`http://localhost:4400/chat/_DFF5dKc8fMbAcZrQEpqJA` — a public key from the
throwaway local database, which was dropped with its container. It is not a
credential (C3), and no raw conversation secret appears anywhere in this repository.

## 69. Chrome visitor workflow

Opened the hosted link in a clean isolated context: **no login required**, agent
display name rendered, conversation started. Sent "Do you deliver on Sundays?" —
the customer bubble stayed on screen, the response was a genuine `503`, no reply
was invented, and the words stayed in the composer. `localStorage`,
`sessionStorage` and IndexedDB were all empty; the only script-visible cookie was
`XSRF-TOKEN`. **Zero external requests** left the browser and no `OPENAI` string
appeared anywhere in the page.

Live idempotency: three POSTs with one `clientMessageId` returned `503`, `409`
("Message already attempted"), `409`, leaving exactly one turn row and one stored
customer message.

## 70. Chrome conversations workflow

Returned as Owner: the real conversations appeared with channel **Website chat**,
agent **Support Concierge**, correct last-activity ordering and customer previews.
The detail showed the real customer message, **no AI message** after the `503`,
and no reply box. Cross-checked against PostgreSQL: one message at
`ingestion_sequence = 2`, and three `conversation_turns` rows all `Failed` with
no reply. Manual Refresh re-read both the list and the transcript.

## 71. Chrome storage / cookie inspection

No raw secret in `localStorage`, `sessionStorage`, IndexedDB, the URL or the
console. `HttpOnly`, `SameSite=Lax` and the narrow path confirmed on the wire,
session-scoped with no expiry, `Cache-Control: no-store` on credential and
transcript responses, `X-XSRF-TOKEN` present on every anonymous write, and no
duplicate or looping requests.

## 72. Chrome two-visitor isolation

See §42 — performed with two isolated contexts and a real credential on each.

## 73. Chrome disable workflow

Disabled the endpoint as Owner, then as the visitor: public config reported
`available: false` (without saying why), starting a conversation `503`, a new turn
in the existing conversation `503`, and **the existing transcript still readable
at `200`**. The hosted page showed "This chat is not available right now." The
Owner still saw all six historical conversations. Re-published afterwards.

## 74. Responsive verification

No horizontal overflow anywhere; at 390 the widest element in `main` measured
exactly 390.

| Screen | 390 | 768 | 1440 | 1920 |
| --- | --- | --- | --- | --- |
| Hosted chat | ✓ | ✓ | ✓ | ✓ |
| Conversations list | ✓ | ✓ | ✓ | ✓ |
| Conversation detail | ✓ | ✓ | ✓ | ✓ |
| Website chat list | ✓ | ✓ | ✓ | ✓ |
| Website chat detail | ✓ | ✓ | ✓ | ✓ |

The hosted chat fills the screen on a phone and becomes a 672px panel above
640px, capped so a conversation never stretches across 1920px. The conversation
list stacks below 900px and becomes a columned grid above it. The long chat URL
wraps inside its card at 390 rather than forcing the page sideways.

## 75. Light / dark

Verified at 390, 768 and 1440. The admin screens were switched with the shell's
own toggle (`data-theme` light and dark both measured). The hosted chat has no
toggle by design — it is outside the shell — and follows
`prefers-color-scheme`: reloaded under each, it reported `data-theme` `light` and
`dark` respectively with the correct palettes.

*An earlier reading suggested the hosted page ignored light mode. That was a
test-sequencing artifact — the attribute is stamped at load and I had switched the
emulated scheme afterwards. Re-measured after a reload, it is correct. No defect.*

## 76. Accessibility

Hosted chat: composer labelled (`label[for]` → "Your message"), keyboard submit
through a real `type="submit"` in a `FormGroup`-bound form, focus reachable and
verified programmatically, `aria-live="polite"` transcript, `role="status"` on
sending and `role="alert"` on failure, sender carried by name and alignment rather
than colour alone, transcript reading order identical to ingestion order. Admin
screens keep the established baseline (skip link, landmarks, `aria-busy`,
labelled controls).

## 77. Console / network

**Zero console messages** across all three browser contexts. Every response
carried the expected status; no request loops or unexpected duplicates.

## 78–81. Before / after

| | Before (Phase 14) | After (Phase 15) |
| --- | ---: | ---: |
| Backend tests | 1545 | **1687** |
| Frontend tests | 305 | **348** |
| Frontend test files | 25 | **30** |

## 82. Backend build

`dotnet build OmniAI.slnx -c Release` — succeeded, **0 warnings, 0 errors**.

## 83. Frontend build

`npm run build` — succeeded.

## 84. Vulnerability scan

`dotnet list package --vulnerable --include-transitive` — no vulnerable packages
in any of the 8 projects.

## 85. `npm audit`

0 vulnerabilities. `npm audit fix` was not run.

## 86. `npm ci`

Not re-run this session: `node_modules` was already installed from the previous
session and `package.json`/`package-lock.json` are **unmodified** by Phase 15
(no packages added). `npm audit` and the production build both ran against that
tree successfully. Stated plainly rather than claimed.

## 87. Bundle before / after

385.75 kB initial (94.43 kB transfer) versus Phase 14's 384.22 kB (93.69 kB) —
**+1.53 kB, +0.4%**. Fully explained: the three new permission codes, two
navigation entries and four route definitions live in `main`. Every Phase 15 area
is a lazy chunk and **none appears in the initial bundle** — `hosted-chat`
8.74 kB, `website-chat-detail` 8.55 kB, `website-chat-page` 7.68 kB,
`conversation-detail` 7.03 kB.

## 88. Packages added

**None**, backend or frontend. Cryptography from the BCL, advisory locks from
Npgsql, everything else from the existing stack.

## 89–93. Layers modified

| Layer | Modified |
| --- | --- |
| Domain | **YES** — new `WebChat` and conversation-runtime types; `Conversation`/`Message` untouched |
| Application | **YES** |
| Infrastructure | **YES** |
| Api | **YES** |
| Frontend | **YES** |

## 94. Security review

Explicitly proven:

- Knowing a `ConversationId` is not sufficient for public access — `404`
- The anonymous secret is SHA-256 hashed in PostgreSQL; the raw value is absent
- The raw secret is absent from `localStorage`, `sessionStorage`, IndexedDB, the
  URL and the console
- A public key grants no admin data — `401` on six admin routes
- Visitor A cannot read or write Visitor B's conversation, and leaves no trace
- A duplicate message does not duplicate the provider call
- Concurrent same-conversation generation is serialised; different conversations
  are not
- The customer message survives provider failure; no fake reply is ever stored
- No unsafe HTML rendering anywhere
- The agent must be Active and the endpoint must be Active, re-checked per turn
- Cross-tenant admin reads fail
- No OpenAI key configured; no provider call from the browser; no paid call made
- No public admin bypass, and no test backdoor
- No VPS access, no deployment

## 95. Architecture decisions

Recorded in
[conversation-runtime-website-chat.md](../architecture/conversation-runtime-website-chat.md)
as C1–C21 with DECIDED NOW / DEFERRED lists.

## 96. Deviations

- `RoleApiFixture` was **unsealed** so the website chat fixture could inherit its
  workspace, membership and permission helpers rather than copying ~250 lines. It
  adds no members and overrides nothing there.
- `AiAgentConfiguration` gained an alternate key on `(id, tenant_id)`. Required by
  PostgreSQL for the composite foreign keys that force an endpoint's agent and its
  workspace to agree — the same construction `tenant_roles` already uses.
- A public message bound (4,000) was introduced *in addition to* the Domain's
  32,000, rather than reusing it. Reasoning in §25 and C20.

## 97. Known issues and limitations

- **Every hosted-chat page load starts a conversation.** A visitor who reloads
  leaves an empty conversation in the admin list — six conversations existed after
  the browser session, three of them empty. Not a security issue, but real
  clutter, and related to the deferred rate-limiting and abuse-protection work.
- **One credential cookie per browser.** Starting a new conversation replaces the
  previous one's secret, so the earlier conversation becomes unreachable from that
  browser. A documented consequence of the single-cookie design; encountered
  during verification.
- **An advisory-key collision would serialise two unrelated conversations.**
  64-bit keys against 128-bit ids. Harmless and rare; documented in C11.
- **Admin permission state can go stale on screen** if another administrator
  changes the reader's roles — Phase 13's D26, still open, still not a security
  issue because the server re-checks.
- **No `npm run lint` script exists** in this project. The frontend gates are
  `format:check`, `build` and `test:ci`; no ESLint was added for this phase.

## 98. Blockers

None.

## 99. `git status --short`

17 modified tracked files and 82 new files, all Phase 15. Nothing staged,
nothing committed.

## 100. `git diff --stat`

17 files changed, 566 insertions, 9 deletions (tracked modifications). The 82
new files add 14,809 lines.

`git diff --check` reports **no whitespace errors** — only Git's routine LF→CRLF
advisory notices on this Windows checkout.

## 101. Secret review

The complete diff and every new file were scanned for anonymous conversation
credentials, API keys, OpenAI secrets, passwords, authentication cookie values,
antiforgery values, database credentials, SSH credentials and production
connection strings. Every match is prose in documentation comments. No `.env`,
`node_modules`, `dist`, `bin`, `obj`, PostgreSQL data, Docker data or screenshot
appears among the new files. No test backdoor and no development-only endpoint
exists.

## 102. VPS not accessed

No SSH, no remote host, no production credential. All verification ran against
local containers on an isolated Docker network.

## 103. Deployment not performed

No deployment, no Nginx change, no published artefact.

## 104. Readiness recommendation for Phase 16

The runtime is correct but **unprotected**. Before this surface faces the real
internet, the next phase should address, in this order:

1. **Rate limiting and abuse protection for public chat.** This is the one
   endpoint where an anonymous stranger spends the workspace's money. Nothing
   currently bounds how many conversations one visitor starts or how fast they
   send. Everything else in this list is less urgent than this.
2. **Empty-conversation handling**, which falls out of the same work.
3. **Realtime**, deliberately: the read model and manual refresh were built so a
   transport can be added without reshaping the inbox.
4. **Human handoff**, which is where `MessageSender.HumanAgent` and the deferred
   context decision about replaying a colleague's words finally get answered.

Ownership transfer, member removal and role deletion remain outstanding from
Phases 13–14 and are unaffected by this phase.

---

**PHASE 15 COMPLETE — WAITING FOR REVIEW**
