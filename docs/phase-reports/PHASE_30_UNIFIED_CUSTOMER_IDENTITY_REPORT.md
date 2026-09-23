# Phase 30 — Unified customer identity and contact foundation

**No live provider traffic of any kind.** `OMNIAI_LIVE_META` and
`OMNIAI_LIVE_OPENAI` were never set, no OpenAI key and no Stripe key were
configured, and WhatsApp went to a deterministic local stub on `127.0.0.1`. Zero
requests to `graph.facebook.com`, `api.openai.com` or `api.stripe.com` —
confirmed from the browser's own network log and from the API's logs.

**Baseline.** `42ffb41`, clean tree at the start of the phase.

**Phases 26B–29 remain deferred.** The Meta account a real WhatsApp validation
needs is still to be prepared by the authorized business representative, and
nothing in this phase depends on it.

---

## 1. Recovery after a session-limit interruption

The first session was cut off mid-Chrome-verification by the individual spend
limit. Nothing was lost: no commit, no staging, and every Phase 30 file was on
disk. Recovery confirmed `HEAD = 42ffb41`, a clean `git diff --check`, nothing
staged, and the complete implementation plus tests present.

### The exact interruption point, and what it turned out to be

The last action was the hosted-chat lazy-creation check. A visitor page was
opened in an isolated browser context, the counts were read as `0 | 0 | 0`, the
message "Do you deliver on Sundays?" was sent, and the counts were still
`0 | 0 | 0` with a console `503`.

That was left deliberately uninterpreted. On recovery it was diagnosed from
evidence rather than assumed, in this order: page state → network request →
response body → API log.

- The network panel showed **one** API call for the send:
  `POST /api/public/webchat/{key}/conversations` → **503**.
- The response body was `"Chat unavailable"`.
- The API log named the cause exactly:
  *"No public chat protection store is configured, so public web chat writes are
  refused. Set `ConnectionStrings:OmniAiProtectionRedis`."*

**It was not a Phase 30 defect.** It was a wrong environment-variable name in my
own verification stack: the API container had been started with
`WebChat__Protection__Redis__ConnectionString`, while the application reads
`ConnectionStrings:OmniAiProtectionRedis` and `PUBLIC_CHAT_NETWORK_KEY_SECRET`.
Phase 16 refuses public web-chat writes when no protection store is configured,
which is accepted behaviour — so `0 | 0 | 0` was the *correct* answer for a
misconfigured stack, and the invariant was never in question.

The stack was restarted with the real names and the verification continued. No
production code was changed to make the test pass, and no sleep or poll was added
in place of a fix.

---

## 2. What was built

### Customer domain

`Customer` (`OmniAI.Domain.Customers`) holds a workspace, an optional
`CustomerDisplayName`, a merge pointer (`MergedIntoCustomerId`, `MergedAt`,
`MergedBy`) and two timestamps. It holds **no** channel identifier, and no
column exists for a note, tag, stage, score or metadata bag.

`CustomerId` is a distinct type from `WhatsAppCustomerId` on purpose: one is the
platform's name for a person, the other is a phone number a provider gave us, and
two types is what stops the second being passed where the first is expected.

`CustomerDisplayName` is optional, bounded to 120 characters, single-line, free
of control characters, trimmed but never otherwise repaired. Nothing resolves,
matches or merges on it, and nothing ever may.

### Channel identity

| Channel | Where identity lives | Key |
| --- | --- | --- |
| WhatsApp | `whatsapp_customer_identities` (new) | `(tenant_id, whatsapp_customer_id)` |
| Web chat | the conversation itself | `web_chat_sessions.conversation_id` is already the session's identity |

The WhatsApp identity table is **separate from `whatsapp_contacts`** because the
two answer different questions: a contact row records one conversation and there
are N of them per person; identity is one row per person per workspace. Hanging
the canonical customer off the contact row would give "who is this?" N possible
answers that no constraint could reconcile.

Web chat deliberately gets **no** `customer_id` column of its own: a session is
one-to-one with its conversation, so a second column would be the same fact
twice. This is a documented departure from the phase brief's example, taken
because the schema makes the example redundant rather than because it was
inconvenient.

### Conversation attribution

`Conversation.CustomerId` is required in the Domain and enforced by a composite
foreign key `(tenant_id, customer_id)` → `customers(tenant_id, id)`. It is
supplied at the transport boundary and written in the same transaction as the
conversation. Its only mutation is `MoveToCustomer`, used by merge.

Nothing about message ingestion ordering, `SentAt`, the `LastActivityAt`
high-water mark, human/AI handling, retrieval, realtime or public credentials was
touched. `CustomerTurnRunner` is unchanged — it did not become an identity
resolver.

---

## 3. Browser verification

Windows Chrome, driven through the `chrome-devtools` CLI. Local stack throughout:
PostgreSQL 17 + pgvector and Redis in Docker, the API in Docker on
`127.0.0.1:5027`, the Angular dev server on `:4200`, a deterministic Meta stub on
loopback. No VPS, no deployment, no live provider.

### Web chat identity (the recovered workflow)

| Step | Customers | Conversations | Sessions | Messages |
| --- | --- | --- | --- | --- |
| Page load and widget open | **0** | **0** | 0 | 0 |
| First real customer send | **1** | **1** | 1 | 1 |
| Reload and resume the same conversation | 1 | 1 | 1 | 1 |
| Second message in the resumed conversation | **1** | **1** | 1 | 2 |
| A genuinely new anonymous session | **2** | **2** | 2 | 3 |

Nothing is created by a page view. Resuming keeps the same customer. Two
unrelated visitors stay two customers — no false merge. The customer created for
a visitor has **no name**: the platform does not invent one.

The `POST /messages` that follows returns 503 because no AI provider is
configured, which is required for this phase. The customer's own message is still
persisted, which is the accepted Phase 15/16 semantics and is why the message
count advances.

### WhatsApp identity

Through signed webhooks to the local stub:

| Step | Result |
| --- | --- |
| First inbound from a sender | 1 customer, 1 conversation, **1 identity row** |
| Second inbound, conversation still open | same conversation, **no new customer** |
| Conversation closed, third inbound | **new conversation**, **same customer**, still 1 identity |
| Customer detail | shows **both** conversations, one `Closed` and one `Open` |

### Directory and detail

- Customers appears in the navigation, and the directory is bounded and paged
  ("1–3 of 3 customers", Previous/Next disabled at the ends).
- Safe fallback labels: "Anonymous visitor", "WhatsApp customer" — never a name
  taken from a message.
- Channel badges carry the channel's **name**, so they are not colour-only.
- The WhatsApp identity renders as `••••0123` with a screen-reader prefix
  "Masked identifier,".
- Conversation counts and last activity render; clicking a conversation
  navigates to the existing conversation detail.
- **No directory or detail response carries message text or a raw provider
  identifier** — asserted in the browser and by API tests that scan the
  serialised bodies.

### Display name and ETag

- Saving sends `If-Match`; the ETag advances on success.
- Two tabs at the same version: tab B saved, tab A's stale save answered **412**.
- Exactly **one** PUT was made — no automatic retry.
- Tab A's typed value stayed in the box, and tab B's write stood.
- A display name of `<script>alert(1)</script>` rendered **literally** in the
  page heading and the field: zero `<script>` elements in `main`, nothing
  executed, no `innerHTML` anywhere on the page.

### Merge

Through the real UI, on synthetic customers:

- The target is chosen from a bounded list the reader pages through. There is no
  suggestion, no ranking and no similarity hint.
- The source is never offered as its own target.
- "Combine these customers" stays disabled until a target is chosen.
- The confirmation is a `role="alert"` region naming both records and saying
  "This combines their channel identities and conversation history. **Confirm
  that these records belong to the same customer.** It cannot be undone." It
  never claims the platform knows they are the same person.
- After merging: identities and conversations moved to the target, the source
  hidden from the directory ("1–2 of 2 customers"), and the source's page still
  resolves with "This record was combined with another customer" and a link to
  the canonical record.
- **The transcript was byte-identical before and after** — every message id,
  conversation id, ingestion sequence and text unchanged. Nothing duplicated,
  nothing reordered.

### Permissions, with real roles and a real invitation

Roles were created through the real endpoints and granted through an invitation a
second real account accepted. No testing bypass was added.

| Member | Customers nav | Directory page | Detail | Edit / Merge | Rename API | Merge API | Conversations API |
| --- | --- | --- | --- | --- | --- | --- | --- |
| No `customers.view` (holds `analytics.view`) | **absent** | honest refusal, no data | — | — | — | — | — |
| `customers.view` only | present | reads fully | reads, history withheld with a sentence | **absent** | **403** | **403** | **403** |
| Owner | present | full | full | present | 200 | 200 | 200 |

The third column of the middle row is the design's central claim: the directory
does not become a way around `conversations.view`. The count is still shown —
"This customer has 2 conversations" — because knowing there are two is not being
able to read them.

### Responsive, themes and accessibility

Customer list and detail, including a 77-character display name, multiple
channels, conversation history and the open merge confirmation:

- **390 / 768 / 1440 / 1920**: no page-level horizontal scroll and **no element
  past the viewport** in any combination.
- **Light and dark at 390 / 768 / 1440**: same, and both themes resolve to the
  design system's own tokens.
- Heading order `h1 → h2 Identity → h3 Channels → h2 Name → h2 Combine → h2
  Conversations`, no skips.
- Visible focus: 1.6px solid outline on every control.
- The merge confirmation and every failure are announced (`role="alert"`); "Saved."
  is a polite `role="status"`.
- Contrast measured: channel badge 5.16:1, fact values 14.81:1, page heading
  16.15:1.
- Zero console messages on the customer surfaces. No images without `alt`.
- The 412 conflict is understandable without a status code: "Somebody else
  changed this customer while you were editing… nothing you typed has been lost."

---

## 4. The defect Chrome found, and the fix

**Two unnamed customers were indistinguishable in the merge picker.** Both
website visitors render as "Anonymous visitor", so the picker offered a choice
between two identical rows — and the confirmation named both sides identically —
for the one operation on the page that cannot be undone. An administrator could
have combined the wrong pair with nothing on screen to prevent it.

**Fix.** Each candidate, and both sides of the confirmation, now carry the only
distinguishing facts the platform honestly holds: which channels the person used
and when the workspace first heard from them — "Website · first seen 1 Sept
2026". Nothing was invented, and nothing was taken from what anybody said.

A regression test asserts both candidates and both halves of the confirmation are
distinguishable, with the dates compared as the component renders them rather
than as a hard-coded English string.

Verified again in the browser after the fix.

---

## 5. Database safety

- **Tenant isolation is a constraint, not a convention.** Composite foreign keys
  `(tenant_id, customer_id)` on `conversations` and on
  `whatsapp_customer_identities` make a cross-workspace reference impossible in
  PostgreSQL. A real-database test writes one and asserts
  `fk_conversations_customer` refuses it.
- **Identity races settle in the database.** The primary key on
  `(tenant_id, whatsapp_customer_id)` is what makes two simultaneous first
  messages produce one customer. There is no process-local lock anywhere.
- **Merge locking** uses transaction-scoped advisory locks taken in an order
  derived from the ids, so `A → B` racing `B → A` queues rather than deadlocks,
  and the depth-one rule makes a cycle impossible rather than merely unlikely.

Real-PostgreSQL tests cover: same-workspace merge succeeds; cross-workspace
refused; source equal to target refused; identities and conversations move;
the canonical pointer is correct; a repeated merge is deterministic and moves
nothing twice; a source already merged elsewhere is refused with its canonical
id; merging into an already-merged target is refused; two merges of one source
leave exactly one winner; opposite-direction merges cannot deadlock or cycle; and
every message is untouched.

---

## 6. Migration and backfill

**Exactly one migration**, `20260923154731_AddUnifiedCustomerIdentity`. No
historical migration was edited. It adds `customers`,
`whatsapp_customer_identities`, `conversations.customer_id`, the composite keys
and three indexes — and no CRM table of any kind. No physical `xmin` column: the
customer's concurrency token is PostgreSQL's own system column, as everywhere
else.

The column arrives **nullable**, the backfill runs, and only then is it made
required and the foreign key added — a zero-GUID default would have made every
conversation on the deployment belong to one customer that does not exist.

A test migrates a database to the Phase 25 schema, fills it with records shaped
the way a live deployment's are, applies the Phase 30 migration and asserts:

- no conversation and no message lost (5 and 4 respectively, unchanged);
- every conversation has an owner;
- one WhatsApp sender's **closed and open** conversations are **one** customer,
  with one identity row dated from the earlier conversation;
- a **different** sender is a **different** person;
- web chat and an unmatched legacy conversation each become their own customer —
  three conversations, three customers — because the schema cannot prove
  otherwise and pooling strangers is not reversible;
- no customer's workspace differs from its conversations';
- every backfilled customer is canonical.

---

## 7. Quality gates

| Gate | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx` | succeeds |
| `dotnet build OmniAI.slnx -c Release` | succeeds, **0 warnings, 0 errors** |
| `dotnet test OmniAI.slnx -c Release` | **3,051 passed, 0 failed** |
| `dotnet list package --vulnerable --include-transitive` | **no vulnerable packages** in any of the eight projects |
| `npm ci` | succeeds |
| `npm run build` | succeeds; one pre-existing budget warning on `embed-chat.scss` (72 bytes over 4 kB), unchanged by this phase |
| `npm run test:ci` | **699 passed, 0 failed** (52 files) |
| `npm run format:check` | all files Prettier-clean |
| `npm audit` | **0 vulnerabilities**; `npm audit fix` not run |
| lint | **there is no lint script in this project.** `package.json` defines `ng`, `start`, `build`, `watch`, `test`, `test:ci`, `format` and `format:check`, and nothing else. There is no ESLint configuration in the repository. |

Backend suites: 742 Domain, 677 Application, 818 Infrastructure, 814 API.

**Phase 30 added 56 backend tests** (3,051 from 2,995) and **27 frontend tests**
(699 from 672).

**Packages added: none.** No CRM SDK, no identity-resolution library, no fuzzy
matching, no AI matching service, no frontend state library.

---

## 8. Limitations and open points

1. **Web chat identity does not survive a new session, and cannot.** The
   transport has no durable identity to key on. Two visits by the same person are
   two customers until an administrator combines them. That is the honest answer,
   not a gap to be filled with a fingerprint.
2. **There is no unmerge.** Merging is irreversible in this phase, which is why
   the interface makes somebody confirm it in words and why the defect above was
   worth fixing rather than noting.
3. **The preflight for merge candidates is a bounded first page, not a search.**
   A workspace with thousands of customers cannot yet find a specific one to
   merge into. A search would need to decide what is safe to search on — never
   hidden raw identities — and that is a design of its own.
4. **Analytics was deliberately not expanded.** Unique-customer counts, retention
   and cohorts are future Analytics work with their own privacy decisions.
5. **Customers consume no entitlement.** There is no `MaxCustomers`, and no plan,
   price or quota changed.
6. **Pre-existing, unrelated:** after the API restarts, the browser's session is
   gone and the first sign-in attempt can fail until the page is reloaded, because
   the SPA holds a stale antiforgery token. It predates this phase and affects
   every screen equally.

---

## 9. Documents

- Architecture: [`docs/architecture/unified-customer-identity.md`](../architecture/unified-customer-identity.md)
- This report: `docs/phase-reports/PHASE_30_UNIFIED_CUSTOMER_IDENTITY_REPORT.md`

## Next recommended step

Reviewer's call on whether to checkpoint Phase 30. Nothing here depends on the
deferred Meta work, so Phase 26B remains available whenever the authorized
representative has prepared the account.
