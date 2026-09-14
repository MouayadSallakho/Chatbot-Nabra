# Phase 20 — WhatsApp Cloud API

Written for: the reviewer of this phase.

Baseline: `f989b92` (Phase 19 checkpoint). Nothing is staged or committed.

## Recovery after a usage-limit interruption

The previous session stopped mid-phase when the individual usage limit was
reached. Nothing was restarted and nothing was recreated.

State recovered from Git before any edit: `HEAD` at `f989b92`, no staged changes,
8 modified files and 15 untracked paths, all of them Phase 20 work in progress.
The interruption landed while writing `backend/src/OmniAI.Api/WhatsApp/
WhatsAppEndpoints.cs`, which existed and was complete but was not yet wired into
`Program.cs`, had no problem-details mappings and no tests.

Already present and kept: the Domain aggregates, the Application handlers and
`CustomerTurnRunner`, the secret protector, the EF configurations and
repositories, the Meta transport, the workers, the DI registration and the single
migration. Written this session: the webhook endpoints, the problem-details
mappings, composition wiring, the human-reply dispatcher, the shared-inbox
WhatsApp read, the whole frontend, all Phase 20 tests, the browser verification
and these documents.

## Meta and provider scope

**No Meta or WhatsApp Business account was used, and none exists.** Every WABA
id, phone number id, access token, app secret and verify token in this phase is
invented. The webhook signature is HMAC-SHA256 over the request body, which a
test computes exactly as Meta does.

**No request reached `graph.facebook.com`.** Verified three ways: the browser
recorded zero external resource loads; the API container's Graph address pointed
at loopback throughout; the transport suite installs a canned handler as the
client's primary handler.

**No OpenAI traffic.** `OPENAI_API_KEY` was blank in the verification host and
`OMNIAI_LIVE_OPENAI` was never set. The AI-mode leg of the simulation therefore
exercised the provider-unconfigured path, which is what the phase asked for.

## Graph API version

**v26.0**, released 29 July 2026, read from Meta's published changelog at
implementation time. Pinned in validated Infrastructure configuration
(`WhatsApp:GraphApiVersion`) and asserted in tests; a malformed version stops the
deployment at startup. The local run put `/v26.0/100200300400500/messages` on the
wire, observed in the stub's log.

## What was built

Architecture and rationale: `docs/architecture/whatsapp-cloud-api.md`.

- **Domain** — `WhatsAppConnection` (Draft/Active/Disabled, `HasCredential` only),
  `WhatsAppContact`, `InboundWhatsAppMessage`, `WhatsAppDelivery`, and value
  objects for the WABA id, phone number id, customer identity (masking
  `ToString`) and provider message id.
- **Application** — `CustomerTurnRunner`, extracted from
  `SendWebChatMessageHandler` so both channels enter one orchestration;
  `ReceiveWhatsAppWebhookHandler`, `ProcessInboundWhatsAppHandler`,
  `DispatchWhatsAppDeliveryHandler`, `WhatsAppReplyDispatcher`,
  `CustomerServiceWindow`, `WhatsAppConnectionHandlers`,
  `ReadWhatsAppConversationHandler`, and the `ISecretProtector` contract.
- **Infrastructure** — `AesGcmSecretProtector`, `MetaWhatsAppMessageSender`,
  `MetaWebhookParser`, `MetaWebhookAuthenticator`, four repositories, two
  `BackgroundService` workers, one migration.
- **Api** — webhook GET/POST, connection admin with ETag/If-Match, the inbox's
  WhatsApp read, four problem-details mappings.
- **Frontend** — a lazy-loaded WhatsApp channel area (list and detail), the
  channels page and navigation, and the shared inbox showing channel, masked
  customer and per-message delivery state.

## Credential encryption

AES-256-GCM, random 12-byte nonce, 16-byte tag, stored `version ‖ nonce ‖ tag ‖
ciphertext` in its own table, bound by associated data to
`(purpose, tenant, connection)`. Master key from `OMNIAI_SECRET_MASTER_KEY`
(base64, exactly 32 bytes); outside Development a missing key yields a protector
that refuses to store rather than one that stores plaintext.

Proved by test: plaintext absent from the stored bytes and from the database
column; decryption round-trips; a tampered byte fails; a different key fails; a
ciphertext moved to another workspace, another connection or another purpose all
decrypt to nothing; two protections of the same secret differ; a short key stops
startup. Proved in the browser: the stored column contains no plaintext, no read
path returns a token, and the API log contains neither the token nor the app
secret nor the verify token.

## Webhook verification

GET validates `hub.mode`, the verify token in constant time and the presence of
`hub.challenge`, and echoes the challenge as `text/plain`; every failure is a
bodyless 403 that does not echo. POST verifies `X-Hub-Signature-256` over the
**exact raw body** in constant time before the payload is parsed; failure is 401
and nothing else happens.

Ten cases covered by test and re-driven live: correct GET, wrong verify token,
missing mode, missing challenge, valid signature, missing signature, malformed
signature (four shapes), a signature over a different body, a one-character body
mutation, an unknown phone number id, and a connection that is not serving.

## Durable inbox and idempotency

`whatsapp_inbound_messages`, unique on `(connection, provider_message_id)`;
claimed with one `FOR UPDATE SKIP LOCKED` statement. Proved against real
PostgreSQL: a duplicate record is refused, twenty rows claimed concurrently by two
contexts produce twenty distinct claims, an abandoned claim returns to the queue
with its attempt still counted, and a message that used its attempts is not
claimed again. Proved live: the same `wamid` posted twice produced exactly one
row and one customer message.

## Contact and conversation mapping

Partial unique index `UNIQUE (connection, customer) WHERE is_current`. Proved:
two concurrent resolutions of the same customer produce one conversation; a
closed conversation is superseded by a new open one with both mappings kept and
exactly one current; a conversation from another channel has no link. The
customer identity leaves masked (`••••0123`, observed in the browser) and is
masked by the value object's own `ToString`.

## Runtime reuse

WhatsApp enters `CustomerTurnRunner`. Proved by tests driving the **same runner
instance** the website-chat suite drives: an inbound message is answered once and
queued once; a redelivery replays and generates nothing; in Human mode the
customer message persists with zero generation, zero embedding and a realtime
announcement; a message claimed before a connection was disabled is not answered.

## Outbound delivery

Queued in the transaction that stores the reply, sent outside it. Accepted →
`Sent`; definitive 4xx → `Failed`; 5xx, 408, timeout or dropped connection →
`Unknown`, never resent. An accept without a message id is `Unknown`. A worker
that dies mid-send leaves a row the sweeper marks `Unknown` rather than
re-queueing. Exactly one attempt per delivery, asserted at both the transport and
the outbox.

Live: one human reply produced exactly one request to the local stub, carrying the
pinned version, the connection's own phone number id, the decrypted credential as
a bearer token, and the documented text envelope with `preview_url: false`.

## Delivery statuses

Ranked `Sent < Delivered < Read`, applied under a row lock. Duplicates change
nothing; a late `delivered` after `read` is ignored; `Failed` applies over any
state and is itself idempotent; an unmatched provider id changes nothing. No
receipt creates a transcript message. Live: `sent`, `delivered`, `read`, then a
repeated `read` and a late `delivered` left the delivery at `Read`, and the
transcript still held exactly two messages.

## Customer service window

24 hours from the customer's last message, through `TimeProvider`, re-checked in
the worker immediately before sending. Outside it the platform does not call Meta,
the delivery is `Failed` with a sentence explaining why, and the reply stays in
the transcript.

## Admin UI and inbox

WhatsApp replaces the "Not built yet" placeholder with a real lazy-loaded feature:
list, connect, detail, agent selection, WABA id, phone number id, write-only
token, a configured Yes/No indicator, Draft/Active/Disabled, and the webhook
address to point Meta at. No reveal exists after saving. A 412 keeps unsaved
edits.

The shared Phase 19 inbox shows `Channel: WhatsApp`, the masked customer, and the
delivery state beside each outbound message (as a word, not a colour). Handoff,
assignment, replying and realtime are unchanged.

## Browser verification

Local only: PostgreSQL 17 + pgvector, Redis, the .NET API in a container on
`127.0.0.1:5027`, the Angular dev server on Windows, Chrome. No VPS, no
deployment, no live provider.

1. Owner registered, workspace created, agent created and activated.
2. Channels page shows WhatsApp as implemented.
3. Connection created with throwaway fake ids — created `Draft`, "No access
   token", and "Put into service" disabled.
4. Fake token saved: request carried `If-Match: "AAEILw"`, response returned
   `ETag: "AAEIMQ"` and `hasCredential: true` with **no token field**; the input
   cleared and the page said the token cannot be displayed again.
5. Activated; the number went `Active`.
6. Signed webhook simulation: right verify token → 200 + challenge; wrong token →
   403; unsigned → 401; one-character body mutation → 401; signed message → 200;
   redelivery → 200 with one stored row.
7. The conversation appeared in the inbox as WhatsApp with the customer shown as
   `••••0123`, and the customer's message persisted although no AI provider was
   configured.
8. Take over, reply → the local stub received **exactly one** send.
9. Statuses `sent`/`delivered`/`read` plus a duplicate and a late one → the inbox
   shows `Read`, transcript unchanged at two messages.
10. Two-tab 412: tab B saved, tab A's save returned 412 (one request, no retry),
    and tab A's unsaved edit was preserved with a message explaining what
    happened.
11. Security: no token, app secret, verify token or master key in any response,
    console, cookie, `localStorage` or `sessionStorage`; zero external resource
    loads; the API log contains none of them and no raw phone number.
12. Responsive at 390 / 768 / 1440 / 1920 with no horizontal overflow, light and
    dark at 390 / 768 / 1440, focus rings present, every field labelled, and
    status carried by words rather than colour alone.

## Tests and gates

Backend (Docker Linux — Smart App Control blocks locally built assemblies on this
machine):

| Suite | Result |
|---|---|
| `OmniAI.Domain.Tests` | 621 passed, 0 failed |
| `OmniAI.Application.Tests` | 607 passed, 0 failed |
| `OmniAI.Infrastructure.IntegrationTests` | 551 passed, 0 failed |
| `OmniAI.Api.IntegrationTests` | 676 passed, 0 failed |
| **Total** | **2,455 passed, 0 failed** (2,218 at Phase 19) |

`dotnet restore` clean · `dotnet build -c Release` succeeded, 0 warnings ·
`dotnet list package --vulnerable --include-transitive` — no vulnerable packages
in any of the eight projects.

Frontend: `npm ci` clean · `npm run build` succeeded (one pre-existing 72-byte
SCSS budget warning on the embed widget, unrelated) · `npm run test:ci` 555
passed in 45 files (542 at Phase 19) · `npm run format:check` clean ·
`npm audit` 0 vulnerabilities (`npm audit fix` not run). **There is no lint
script in this project** — the scripts are `ng`, `start`, `build`, `watch`,
`test`, `test:ci`, `format`, `format:check`.

## Migration

Exactly one: `20260914110120_AddWhatsAppCloudApiChannel`. No historical migration
was touched. Inspected by hand; three departures from the scaffolder, each
documented in the file:

1. the physical `xmin` column removed (PostgreSQL's own system column);
2. the current-contact index written by hand, because it is partial
   (`WHERE is_current`) and EF cannot express that;
3. the credential in its own table.

Constraints: `phone_number_id` unique deployment-wide; inbound unique on
`(connection, provider_message_id)`; one delivery per internal message; one
current contact per `(connection, customer)`; foreign keys to tenant (restrict),
agent (restrict), conversation, message and connection (cascade). Applied cleanly
to a real PostgreSQL 17 + pgvector database during the browser run, and a new
schema test asserts `whatsapp_connections` has no physical `xmin`.

## Packages added

One: **`Microsoft.Extensions.Http` 10.0.11** (Infrastructure only), for
`IHttpClientFactory`. No WhatsApp SDK: the surface is one POST and one webhook,
and an unofficial wrapper would be a dependency on Meta's documentation that
nobody here controls. Everything else is `HttpClient` and
`System.Security.Cryptography`.

## Defects found and fixed during the phase

- `WhatsAppConnectionHandlers` required `whatsapp.view` **and**
  `whatsapp.manage` for writes, which would have made one permission a silent
  prerequisite for another. Writes now require `whatsapp.manage` alone, matching
  every other feature and the platform's "no implication between permissions"
  rule.
- The scaffolder emitted a physical `xmin` column and unnamed foreign-key
  indexes; both corrected by hand, and the `xmin` one is now guarded by a test.
- The first WhatsApp persistence tests shared a database and asserted on global
  counts, so they passed or failed depending on order. Each test now starts from
  an empty channel schema, which is what makes "exactly one row" mean anything.

## Known limitations

- Text only. Media, templates, campaigns, Embedded Signup, Instagram and
  Messenger remain out of scope.
- An ambiguous send leaves a delivery `Unknown` and waits for a person. There is
  no admin action to resend or dismiss it yet.
- The delivery state in the inbox is read on load and refresh; it is not pushed.
  Phase 19's realtime invalidation covers the conversation, so a colleague sees it
  on the next read rather than the same instant.
- A connection whose agent is deleted shows "Agent removed" and stops answering.
  Nothing prevents deleting an agent a number depends on.
- Real Meta validation — a genuine WABA, a real number, live webhooks — is a
  separate, explicitly authorised future phase.

## Not done

No staging, no commit, no push, no VPS access, no deployment, no Phase 21.
