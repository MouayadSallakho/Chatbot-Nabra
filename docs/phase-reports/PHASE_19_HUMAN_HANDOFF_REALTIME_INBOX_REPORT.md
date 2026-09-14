# Phase 19 — Human handoff, conversation assignment, realtime inbox

Baseline: **`3e6d6a1`** (`feat: implement secure embeddable website chat widget`),
the accepted Phase 18 checkpoint.

Architecture note: [`human-handoff-realtime-inbox.md`](../architecture/human-handoff-realtime-inbox.md).

---

## 1. Recovery after an interrupted session

This phase was implemented across two sessions. The first ended mid-work when the
machine lost DNS and the Claude API became unreachable — a connectivity failure,
not a repository one.

**Exact interruption point.** The backend, the realtime layer and the frontend
were already written, and the focused frontend conversation suite was running
with **2 of 24 tests failing**:

- `shows a conversation with no messages as empty rather than broken`
- `reports a refused precondition and retries nothing`

**Recovery.** `git status` / `git log` confirmed `HEAD` still at `3e6d6a1`, no
staged changes, and the whole Phase 19 diff present and consistent. Nothing was
reset, restored, stashed or recreated. Both failures were diagnosed on their
merits:

| Failure | Cause | Fix |
| --- | --- | --- |
| empty conversation | a stale fixture flushed the detail without an `ETag`, so the versioned read threw and the transcript was never requested | test fixture given a tag, like every other conditional read |
| refused precondition | the assertion named the *server's* sentence; the shared client mapper turns every `412` into one application-wide message | assertion corrected to the client's wording, plus a new assertion that the refused write changed nothing |

Neither was weakened to pass. Both suites then went green, and the phase
continued.

---

## 2. What was built

- `ConversationHandling` (`Ai` / `Human`) beside `ConversationStatus`.
- Take over / return to AI, on the **same PostgreSQL advisory lock** the AI
  runtime holds across a provider call.
- Optional assignment to one member, with eligibility and workspace isolation.
- Human replies: server-derived attribution, DB-enforced idempotency.
- Conversation `ETag` with `If-Match` / `428` / `412`.
- SignalR with a Redis backplane, two hubs, and short-lived public tickets.
- An operational admin inbox, and live updates in both public transports.

---

## 3. Defects found and fixed during the phase

Six real defects, all found by tests or by Chrome rather than by inspection.

**1. Best-effort realtime was a promise, not a guarantee.** The notifier
interface documented that implementations swallow their own failures, and the
shipped one did — but the Application layer awaited the call directly, so any
other implementation could fail a write that had already succeeded. A test with a
throwing notifier proved it. Every call site now goes through
`AnnounceQuietlyAsync`, so the guarantee holds whatever is behind the interface.

**2–4. Three untranslatable EF queries.** Reaching inside a value object in a
predicate or a join key (`AssignedTo.Value.Value`, `written.Contains(reply.MessageId.Value)`,
`membership.UserId.Value`) is not translatable: the value converter maps the whole
object to a column and EF has nothing to map a member of it to. Each surfaced as
a `500` on a real screen — the transcript, the conversation list, and the
assignment control — never as a compile error. All three are now either
value-object comparisons or a second small query, each commented where it occurs.

**5. A visitor whose first turn failed got no live updates.** The widget
subscribed only after a *successful* send, so a first turn that failed — a
provider outage, a rate limit, a lost response — left a conversation with no
subscription. A colleague taking it over and replying would reach a visitor who
never heard about it, which is precisely the case where live updates matter most.
Found in Chrome, because the provider is deliberately unconfigured locally and
the first send therefore fails. Both clients now subscribe as soon as a
conversation exists.

**6. A takeover alone could not reach the customer.** The visitor learned the
mode only from a turn result or from a human reply, so a takeover left them
watching an AI that had stopped answering. The public transcript response now
carries `handling` — the mode, never an identity — so the next read after a
notification tells them.

Two configuration gaps were also fixed: `/hubs` was missing from the Angular dev
proxy (websockets enabled), and the API integration project needed
`InternalsVisibleTo` to construct two instances for the backplane test.

---

## 4. Test totals (actual)

Backend, on Docker's Linux engine against PostgreSQL 17 + pgvector and Redis —
Windows Smart App Control blocks the freshly built assemblies (`0x800711C7`), as
recorded in the Phase 18 report:

| Project | Passed | Failed |
| --- | --- | --- |
| OmniAI.Domain.Tests | 593 | 0 |
| OmniAI.Application.Tests | 584 | 0 |
| OmniAI.Infrastructure.IntegrationTests | 491 | 0 |
| OmniAI.Api.IntegrationTests | 637 | 0 |
| **Total** | **2,305** | **0** |

Frontend: **542 tests across 43 files, 0 failing**.

New Phase 19 test files: `ConversationHandoffTests`, `HumanModeRuntimeTests`,
`ConversationInboxTests` (+ doubles), `RealtimeTicketTests`, `HandoffSchemaTests`,
`RealtimeBackplaneTests`, `ConversationInboxApiTests`.

Three existing expectations were updated because Phase 19 changes what they
assert: the permission catalogue (17 → 20 codes), the migration count (9 → 10),
and the now-nullable AI reply on a turn result.

Coverage includes: handoff both ways and repeated; `428`/`412`; the takeover/lock
serialisation; human mode with **embedding calls = 0 and generation calls = 0**;
no retroactive replies on return; attribution; duplicate and *concurrent*
duplicate replies producing one message; assignment eligibility and cross-tenant
rejection; ticket scope, expiry, transport separation, conversation-id-alone, and
digest-only storage; structural-payload-only on the wire; and Redis propagation
between two instances.

---

## 5. Builds and audits

| Gate | Result |
| --- | --- |
| `dotnet restore` / `dotnet build -c Release` | Succeeded, **0 warnings** |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages in any of the 8 projects |
| `npm ci` / `npm run build` | Succeeded, **0 warnings** |
| `npm run test:ci` | 542 passed |
| `npm run format:check` | Clean |
| `npm audit` | 0 vulnerabilities |
| Lint | **There is no lint script in this frontend.** The gates are the build, the tests and the format check. |

Packages added: **`Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.11**
(backend) and **`@microsoft/signalr` 10.0.11** (frontend). SignalR itself is part
of the ASP.NET Core shared framework; the package is the Redis backplane.

---

## 6. Chrome verification

Real Windows Chrome against the local stack — PostgreSQL 17 + pgvector, Redis,
the API, Angular — with **`OPENAI_API_KEY` present in the API process** and
**`OMNIAI_LIVE_OPENAI` absent**, so the Phase 18 safeguard held throughout.

### Embedded widget, full handoff workflow

A workspace, agent and chat link were created through the UI, the widget
allowlisted to a customer site served from the session scratchpad on
`127.0.0.1:8081`, and the snippet pasted there.

1. Customer sent *"My order 9912 has not arrived yet."* → stored, **no AI reply**,
   the existing honest refusal.
2. Admin opened the conversation: **Answered by "The AI"**, no composer, and the
   line *"The AI is answering this conversation. Take it over to reply yourself."*
3. **Take over** → the widget showed *"A support team member will reply here."*
   **without a refresh**.
4. Admin replied → it appeared in the widget **without a refresh**, labelled
   **"Support"**.
5. Customer replied *"Thank you, that is good news."* → the admin transcript
   updated **without a refresh**.
6. **Return to AI** → the widget dropped the human-support notice.
7. Customer sent a **new** message → the AI resumed, hit the unconfigured
   provider, and returned the safe refusal. **No fake AI response.**

The stored transcript confirms **no retroactive answering**: the message sent
during human mode was never answered by the AI.

### Hosted chat

The same handoff and reply workflow on `/chat/{publicKey}`: take over, reply,
delivered live, labelled "Support". Hosted realtime works, not only embedded.

### Realtime security

- The websocket URL is `ws://…/hubs/public-chat?id=…&access_token=<43 chars>`.
  Hashing that token in the page and looking the digest up in Redis found
  `…:realtime:<sha256>` holding **`Hosted:<conversationId>`** with **23 s of a
  90 s TTL left** — so the value in the URL is a realtime ticket, bound to one
  transport and one conversation, stored only as a digest. The raw ticket appears
  in no key or value, and no API log line contains the embed credential.
- The hosted conversation credential is **absent from `document.cookie`**
  (`HttpOnly`, path-scoped); the page's storage holds only the conversation id.
- `iframe.contentDocument` is `null` and cross-frame access throws
  `SecurityError`; the host page's `localStorage` and `sessionStorage` are empty
  and no OmniAI cookie is set.
- The only postMessages the host received were
  `{"type":"omniai:v1:ready","position":"Right"}` and `{"type":"omniai:v1:open"}`.
- No duplicate messages were rendered (the client merges on ingestion sequence).
- **Zero outbound OpenAI requests**: the API log contains no OpenAI logger
  activity and no embedding activity across the whole session.

### Concurrency

Two admin tabs on one conversation at the same version. Tab B returned it to the
AI; Tab A's mutation with the version it was holding answered **412** with
*"This conversation was changed somewhere else…"*, and **nothing was retried**.

### Assignment

The assignment control offers only members holding `conversations.reply` — the
Owner, and nobody else. The customer never saw an employee address at any point.
Cross-tenant and ineligible-member rejection are covered by the automated suites.

### Responsive and accessibility

Admin conversation detail and the public human-mode chat at **390, 768, 1440 and
1920**, light and dark: **no horizontal overflow at any width**, and the handoff,
assignment and composer controls remain usable at 390.

- The composer is labelled; the assignment select is labelled.
- The transcript is an `aria-live="polite"` region.
- Handling is announced in words — "A person" / "The AI" — not by colour.
- A live takeover arriving while a visitor was mid-sentence **kept focus in the
  composer and preserved the half-typed draft**.

---

## 7. Security and privacy

- A conversation credential never appears in a realtime URL; a short-lived,
  narrowly-scoped ticket does.
- Tickets are stored as digests with a TTL, are never logged, and admit exactly
  one conversation on one transport.
- Realtime carries a conversation id and nothing else — no transcript, no tenant,
  no knowledge, no employee identity.
- Human reply senders are server-derived; the command has no field for one.
- Composite foreign keys make both cross-workspace assignment and cross-workspace
  attribution unrepresentable in the database.
- Employee emails reach the admin API only; no public view type has a field for
  one.
- Admin writes remain authenticated, permission-checked and antiforgery-protected.
  No human endpoint is exposed under a public group.
- Phase 16 protection, Phase 17 RAG and Phase 18 origin/session semantics are
  unchanged, and are covered by the regression suites.

---

## 8. Migration

One: `20260913195005_AddHumanHandoffAndAssignment`. Hand-edited to backfill
`handling` rather than default it, to add both composite foreign keys, and to drop
the scaffolder's redundant index. No historical migration was edited. A schema
test asserts each edit, and that no physical `xmin` column exists.

---

## 9. Known limitations

- A visitor learns the handling mode on their next read rather than from a push.
- The admin list re-reads a whole page on any conversation change in that
  workspace.
- Several reads are two queries rather than one join, because value-object ids
  cannot be unwrapped in EF predicates.
- No queue, unread counts, SLA, canned replies, attachments, or
  customer-initiated handover request — none of which exists.

---

## 10. Live OpenAI, VPS, deployment

- **No live OpenAI traffic. `OMNIAI_LIVE_OPENAI` stayed absent throughout, and
  the API log shows zero OpenAI and zero embedding activity.**
- No VPS was accessed. No deployment was performed. No deployment configuration
  was changed.

---

## 11. Git status

Nothing staged, nothing committed, no branch created, no remote touched.

- 54 tracked files modified
- 35 new files
- 1 new migration

The customer test site was served from the session scratchpad, outside the
repository, and is not part of the diff.

---

**PHASE 19 COMPLETE — WAITING FOR REVIEW**
