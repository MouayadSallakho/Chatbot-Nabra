# WhatsApp Cloud API

Written for: engineers working on this platform's channels.

A workspace connects a WhatsApp number to one AI agent. Customer messages arrive
through Meta's webhook, enter the **same** conversation runtime website chat
uses, and replies — from the AI or from a person in the shared inbox — go back
out through a durable outbox.

## Graph API version

Pinned to **v26.0** (released 29 July 2026, current at implementation time), in
`WhatsApp:GraphApiVersion`. Meta's unversioned paths follow whatever is current,
so an unpinned deployment changes behaviour on a date somebody else picked.
Moving to a later version is a configuration change with its own verification.

The version is validated at startup (`v` followed by two numbers). A malformed
one stops the deployment rather than surfacing the first time a customer waits.

## The shape of it

```
Meta ──POST──▶ /api/whatsapp/webhook
                 │  1. verify X-Hub-Signature-256 over the RAW body
                 │  2. parse (Infrastructure)
                 │  3. write whatsapp_inbound_messages
                 └─ 200, immediately

InboundWhatsAppWorker ──claim──▶ whatsapp_inbound_messages
                 │  resolve contact → conversation
                 └─ CustomerTurnRunner  ← website chat enters here too
                        │ advisory lock, replay check, handling (AI/Human)
                        │ RAG + generation when the AI is answering
                        └─ reply stored → whatsapp_deliveries queued

WhatsAppDeliveryWorker ──claim──▶ whatsapp_deliveries
                 │  re-check the 24-hour window
                 │  decrypt credential (AES-256-GCM)
                 └─ POST /v26.0/{phone_number_id}/messages

Meta ──POST──▶ /api/whatsapp/webhook  (delivery statuses)
                 └─ matched by provider message id, ranked, never regressed
```

## The runtime is shared, not copied

`CustomerTurnRunner` (Application/Conversations) is the one place a customer's
message is locked, replayed, answered or left for a person. Phase 20 extracted it
out of `SendWebChatMessageHandler`; both channels now hand it a `CustomerTurn`
and get back the same outcome.

What WhatsApp adds is transport: routing a phone number id to a workspace,
mapping a customer to a conversation, and queueing a delivery. Everything else —
the advisory lock, ingestion ordering, idempotency, retrieval, generation, the
realtime announcement, the last-activity high-water mark — is the shared runner.

There is deliberately no generation budget on the WhatsApp path. Phase 16's
budget meters anonymous browser traffic; a WhatsApp message arrives through a
signed webhook on a number the workspace deliberately connected, and applying the
public limiter would let unrelated web traffic silence a workspace's customers.

## Webhook security

**GET** (subscription): `hub.mode` must be `subscribe`, `hub.verify_token` must
match this deployment's token in constant time, `hub.challenge` must be present.
The challenge is echoed as `text/plain` and nothing else — Meta compares it byte
for byte. Any failure is a bodyless 403.

**POST**: the body is read once into one array. The signature is HMAC-SHA256 over
**those exact bytes**, compared in constant time. A route that parsed first and
re-serialised would verify a different document from the one it acts on, which is
the classic way a signed-webhook route is defeated. Failure is 401 and nothing
else happens: no lookup, no parse, no row, no AI, no send.

Bodies over 1 MB are refused with 413 without being read to the end.

The route is anonymous and **not** antiforgery-checked, correctly: antiforgery
defends a browser session against a cross-site request, and there is no session
here. The signature is the whole authentication and is stronger than a token a
browser would carry.

## Tenant routing

A verified payload is still not trusted to say whose it is. The workspace comes
from the **`metadata.phone_number_id`** in Meta's own envelope, looked up against
`whatsapp_connections`, and from nothing else in the body. `phone_number_id` is
unique **deployment-wide**, not per workspace — ambiguity there would mean one
company's customers landing in another company's inbox, and the unique index makes
it unrepresentable.

Anything unroutable — an unknown number, a connection that is not serving, a
message type this phase does not accept — is acknowledged with 200 and creates
nothing. Answering with a failure only makes Meta resend for hours.

## Credential storage

The access token is a workspace secret. It is:

- encrypted with **AES-256-GCM**: random 12-byte nonce, 16-byte authentication
  tag, stored as `version ‖ nonce ‖ tag ‖ ciphertext`;
- bound by **associated data** to `(purpose, tenant id, connection id)`, so
  ciphertext copied into another row or another workspace decrypts to nothing;
- keyed from `OMNIAI_SECRET_MASTER_KEY` (base64, exactly 32 bytes), read from the
  environment and never bound into a settings object;
- kept in **its own table** (`whatsapp_credentials`), so no list, no detail read
  and no webhook routing lookup ever touches it.

The aggregate holds `HasCredential`, a boolean — it has no field a token could
live in, so no response built from it can leak one. There is no endpoint that
returns a token and no UI that reveals one after saving.

Outside Development a missing master key produces a protector that **refuses to
store** rather than one that stores plaintext. In Development a random
per-process key is allowed: usable on a laptop, and worthless anywhere else
because a restart makes every stored credential unreadable.

`OMNIAI_WHATSAPP_APP_SECRET` and `OMNIAI_WHATSAPP_VERIFY_TOKEN` are **platform**
configuration — one Meta app per deployment, the same for every workspace. They
are never tenant fields, never in a committed settings file, and never entered in
the admin. A deployment missing either refuses every webhook request.

## Durable inbox

`whatsapp_inbound_messages` is written by the request handler and read by a
worker. Two properties carry it:

- **Idempotency is the database's job.** A unique index on
  `(whatsapp_connection_id, provider_message_id)` settles concurrent redeliveries;
  the loser's insert is caught and reported as the ordinary duplicate it is.
  Meta's `wamid` is hashed to `"wa-" + 40 hex` to fit the platform-wide
  `ClientMessageId`, deterministically — so a redelivery replays the settled turn
  instead of generating a second reply.
- **Claiming is one statement.** `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP
  LOCKED LIMIT n) RETURNING …`. Two instances asking together get disjoint sets.

A claim is a timestamp, not a lease. Nothing renews it; a worker that dies leaves
a row that simply looks old, and the sweeper returns it to the queue. The attempt
count is raised **at the claim**, so a message that repeatedly kills its worker
runs out of attempts instead of looping for ever.

Text only. An image, a location or a reaction is stored (so a redelivery is
recognised) with **no text at all** and status `Ignored` — never a placeholder
the AI could answer as though the customer had typed it.

## Contact and conversation mapping

`whatsapp_contacts` maps `(connection, customer)` to a conversation. A partial
unique index — `UNIQUE (whatsapp_connection_id, customer_id) WHERE is_current` —
enforces one current mapping. Two messages a second apart can be processed by two
workers at once; without the index both would start a conversation and the
customer would be answered twice by two threads that cannot see each other.

When the mapped conversation is Closed, the old mapping is superseded and a new
Open conversation is created in one transaction. Earlier mappings are kept, so
the previous conversation stays reachable from the inbox.

The customer's number is personal data. `WhatsAppCustomerId.ToString()` returns
the **masked** form, so interpolating one into a log line discloses four digits
rather than a phone number. The admin API returns only the masked reference.

## Outbound delivery

`whatsapp_deliveries` is queued in the same transaction as the reply it carries,
and sent outside it — no database transaction spans the HTTP call.

A send has **three** outcomes, not two:

| Outcome | Meaning | What the platform does |
|---|---|---|
| Accepted | Meta named the message | `Sent`, with the provider message id |
| Rejected | a definitive 4xx | `Failed`, with a sentence a colleague can read |
| Ambiguous | 5xx, 408, timeout, dropped connection | `Unknown` — **never sent again** |

Ambiguity is the reason this table exists. A request that may or may not have
arrived is the one that decides whether a customer reads the same sentence twice.
Nothing retries it: the outbox claims with `maxAttempts: 1`, the HTTP client has
no resilience handler, and a delivery a dead worker left mid-send is swept to
`Unknown` rather than re-queued.

An accept without a message id is also `Unknown`: nothing could ever match a
receipt to it, so claiming it was sent would claim knowledge the platform will
never have.

## Delivery statuses

Matched by provider message id. Ranked `Sent < Delivered < Read`; a status that
would move backwards is ignored, and a repeat changes nothing. `Failed` applies
over any of them, because it is not a step in the sequence — it is Meta saying the
message is not arriving.

The row is locked (`SELECT … FOR UPDATE`) before it is read, so two receipts
seconds apart are applied sequentially rather than racing.

A receipt never creates a transcript `Message`. It is a fact about a message that
already exists.

## Customer service window

Meta allows free-form text only within **24 hours** of the customer's last
message; outside it, only a pre-approved template, and templates are out of scope
for this phase. So the platform refuses locally rather than paying for a round
trip to be told the same thing in Meta's words.

Measured from the last **Customer** message in the transcript — replies from the
business do not extend it — read through `TimeProvider`. Checked when a reply is
queued and **again in the worker immediately before sending**, because a delivery
can sit in the outbox while the window closes. The conversation detail includes
`replyWindow.isOpen`, `closesAt` and `checkedAt` for WhatsApp. The inbox shows the
remaining time from this server snapshot and keeps the draft when sending closes.
A new customer message refreshes the deadline. The lookup filters by customer
before taking the latest transcript entry, regardless of later team messages.

A reply outside the window is still stored and still shown in the transcript; the
delivery says plainly that it did not reach the customer.

## Admin

Permissions: `whatsapp.view` and `whatsapp.manage`. Neither implies the other,
and neither says anything about conversations — administering a channel is not a
licence to read what customers said on it. The shared inbox's WhatsApp read is
guarded by `conversations.view`.

Every write is conditional: `If-Match` required (428 when missing, 412 when
stale), a new `ETag` returned. The admin screen keeps unsaved edits through a
412 — losing a race means somebody else changed the connection, not that this
person meant something else.

An Active connection cannot be reconfigured. Changing the number or the agent
while messages are arriving would reroute a conversation mid-sentence.

## Configuration

| Key | Kind | Notes |
|---|---|---|
| `OMNIAI_SECRET_MASTER_KEY` | secret, env | base64, exactly 32 bytes |
| `OMNIAI_WHATSAPP_APP_SECRET` | secret, env | Meta app secret; HMAC key |
| `OMNIAI_WHATSAPP_VERIFY_TOKEN` | secret, env | echoed at subscription |
| `WhatsApp:GraphBaseAddress` | setting | HTTPS, unless loopback |
| `WhatsApp:GraphApiVersion` | setting | default `v26.0` |
| `WhatsApp:Timeout` | setting | default 30s, finite |
| `WhatsApp:Workers:*` | setting | poll interval, batch size, attempts, claim timeout |
| `OMNIAI_LIVE_META` | Development opt-in, env | added in Phase 26A; see below |

There is no default for any of the three secrets. A deployment missing the app
secret or the verify token composes an authenticator that refuses every webhook
request, and one missing either half of the pair is treated as unconfigured
rather than as partly working.

## The Development live-Meta safeguard

Added in Phase 26A, after the channel was built and before it was ever pointed at
Meta.

**Decision.** In **Development**, a `WhatsApp:GraphBaseAddress` that is not on
loopback does not on its own compose the Meta transport. `OMNIAI_LIVE_META` must
also be exactly `1`. Without it the container composes a sender that refuses
every message in-process: no HTTP request, no credential on a socket, and a
delivery that settles as failed with a sentence naming the variable.

**Why.** The OpenAI guard exists because a key was inherited from a shell once
and spent money. The same accident on this channel puts words into a real
person's WhatsApp, which cannot be unsent.

**Loopback is the test, not the hostname.** Asking "is this Meta?" would leave
every typo, regional alias and proxy on the permitted side. Asking "is this on my
machine?" is the safe way round, at the cost of one deliberate inconvenience: a
non-loopback stub needs the opt-in too.

**Refused, not unknown.** A send that never left the process definitively did not
arrive. Calling it ambiguous would leave a durable row nobody can settle, and the
guard must not make stored state less truthful than a real refusal would.

**Forbids.** Requiring the opt-in outside Development — a deployment that has
deliberately configured the channel must not go silent because somebody forgot a
flag. Relaxing webhook signature verification for any reason: that is a security
control, and this is not.

**Related.** The operator procedure for a real Meta validation, including the
configuration contract, the callback URL, the tunnel rules and the credential
handling, is in
[operations/meta-whatsapp-real-validation-runbook.md](../operations/meta-whatsapp-real-validation-runbook.md).

## Out of scope for this phase

Media, voice, documents, location, reactions, templates, campaigns, marketing
messaging, Embedded Signup / OAuth onboarding, Instagram, Messenger, billing,
analytics.
