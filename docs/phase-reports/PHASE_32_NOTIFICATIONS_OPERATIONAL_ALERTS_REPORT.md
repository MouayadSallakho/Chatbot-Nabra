# Phase 32 — Notifications and operational alerts

Baseline: `e6266d3` (Phase 31, accepted and checkpointed). Working tree left
unstaged and uncommitted for review. Phases 26B–29 remain deferred pending the
authorised Meta business account; Phase 33 not started.

Design reference: [operational-notifications.md](../architecture/operational-notifications.md).

## 1. Outcome

When an authoritative operational transition happens, the members entitled to
know are told:
- **In the app:** a durable, per-member notification. The bell updates live
  through the existing workspace hub.
- **By email, if they opted in:** a transactional operational email, sent from a
  durable outbox that re-checks authority at send time.

Exactly one notification per recipient per occurrence. Business state is never
derived from notifications.

## 2. What was built

| Layer | Additions |
| --- | --- |
| Domain | `Notification`, `NotificationOccurrence`, `NotificationPreferences`, `NotificationEmail`, typed ids, closed `NotificationKind` / `NotificationCategory` / `NotificationSeverity` / `NotificationEmailStatus`, `NotificationKinds` (category + severity), `NotificationActionPath` (internal-route validation), `NotificationEmailFailures` codes |
| Application | `NotificationCatalog` (templates, recipient permission per category, constant subjects), `NotificationDispatcher`, `OperationalEmailDispatcher` (send-time re-checks, retry/ambiguity policy), `NotificationRetention`, `NotificationHandlers` (API use cases), `OperationalEmailRenderer`, ports (`INotificationStore`, `INotificationOccurrenceStore`, `INotificationOccurrenceRecorder`, `INotificationSources`, `INotificationRecipientDirectory`, `INotificationEmailOutbox`, `INotificationRetentionStore`, `INotificationRealtime`, `IOperationalEmailSender`), `OperationalEmailLinks` |
| Infrastructure | EF configurations, `NotificationOccurrenceSql` (in-transaction recording), `NotificationOccurrenceStore`, `NotificationStore`, `NotificationEmailOutbox`, `NotificationDirectory`, `OperationalEmailOptions`, `SmtpOperationalEmailSender` (MailKit), `WithheldOperationalEmailSender`, `UnconfiguredOperationalEmailSender`, dispatch / email / retention workers, DI |
| Integrations | `ReportExecutionStore` (complete, fail, exhausted sweep — CTEs), `WorkspaceEntitlementService.ReserveAsync` (threshold crossing), `BillingSubscriptionService` (apply, suspend), `WhatsAppOutbox` (settle Unknown, abandon stale — CTEs), `IndexKnowledgeDocumentHandler` (failed attempt) |
| API | `/api/tenants/{t}/notifications` — list, unread-count, `{id}/read`, read-all, preferences GET/PUT; `WorkspaceInboxHub.SubscribeNotifications`; `SignalRNotificationRealtime` |
| Frontend | `NotificationApi`, `NotificationRealtime` (lazy SignalR import), `NotificationCenter`, `NotificationItem`, top-bar `NotificationBell` with panel, lazy `/notifications` and `/notifications/preferences`, icons `bell` / `info` / `critical` / `check` |

No generic event bus, JSON event table, reflection routing, message broker,
MediatR expansion, workflow engine, SMS/push/Slack/Teams/webhook tables,
campaigns, templates editable by users, analytics, tracking pixels or
MaxNotifications/MaxEmails entitlement was introduced. No permission was added.

## 3. Catalogue, severity and recipients

See the architecture document, §3–§4. In summary:

| Category | Recipients |
| --- | --- |
| Reports | the requester, or the schedule owner, while they hold `reports.view` |
| Usage | holders of `analytics.view` |
| Billing | holders of `billing.view` |
| Knowledge | holders of `knowledge.manage` |
| ChannelHealth | holders of `whatsapp.manage` |

Severity is Information, Warning or Critical, and is presentation only.

## 4. Durability, deduplication and atomicity

- **Occurrences:** written in the same statement or transaction as the transition,
  unique on `(tenant, kind, key)`. The one exception is Knowledge failure, which
  is recorded best-effort because the failed attempt writes nothing to join.
- **Notifications:** unique on `(tenant, recipient, deduplication key)`.
- **Emails:** unique per notification, and queued in the same transaction as it.
- **Dispatch:** `FOR UPDATE SKIP LOCKED`, with a claim token and a lease.
- **Replays and recurrences:** a replayed transition or dispatch is a no-op. A
  genuine recurrence — PastDue → Active → PastDue, a new month, or a new failed
  attempt — is a new occurrence.

## 5. Email

- **Transport:** MailKit SMTP behind `IOperationalEmailSender`. Explicit TLS
  policy (StartTls by default); plain text is allowed to loopback only. Timeout,
  cancellation, and no protocol logging.
- **Configuration:** host, port, security, credentials, sender and the trusted
  `AppBaseUrl` from configuration; the password from the environment only.
  Unsafe settings refuse to start.
- **Development guard:** `OMNIAI_LIVE_EMAIL` must be exactly `1` before
  Development may use a non-loopback host.
- **Send-time re-checks:** membership, permission, preference and a usable
  account address.
- **Retries and ambiguity:** NotSent is retried at most three times with backoff.
  A stated rejection becomes Failed. An ambiguous send becomes Unknown and is
  never retried; a stale Sending row also becomes Unknown.
- **Content:** a constant subject, text/plain plus escaped HTML, one trusted link,
  no attachment, image, token or tracking.
- **Preferences:** email is off by default for every category.

## 6. Tests

Totals, as reported by the runners:

| Suite | Passed | Failed |
| --- | --- | --- |
| OmniAI.Domain.Tests | 797 | 0 |
| OmniAI.Application.Tests | 708 | 0 |
| OmniAI.Api.IntegrationTests | 843 | 0 |
| OmniAI.Infrastructure.IntegrationTests | 950 | 0 |
| **Backend total** | **3,298** | **0** |
| Frontend (Vitest, 57 files) | 726 | 0 |

The backend total was 3,183 at `e6266d3`; Phase 32 adds 115 backend tests. The
frontend total was 710; Phase 32 adds 16.

**New tests.** All PostgreSQL tests run against PostgreSQL 17 + pgvector in
Docker; none uses EF InMemory.

- **Domain** (`NotificationDomainTests`):
  - internal-route acceptance, and refusal of `//host`, `https:`, `javascript:`,
    queries, fragments, dot segments, backslashes, upper case, encoded slashes and
    doubled slashes;
  - the kind catalogue is total;
  - severity semantics;
  - monotonic read;
  - retention expiry;
  - bounded text;
  - email off by default;
  - bounded occurrence keys.
- **Application** (`NotificationTemplateTests`):
  - every kind has bounded text, an internal action and a constant single-line
    subject;
  - the recipient permission per category;
  - report text names the catalogue title only;
  - the HTML escapes `<script>` and `<img onerror>` and loads nothing (one link,
    no `<link>` or `url()`);
  - a CRLF in a label stays in the body;
  - links come from the trusted base.
- **Infrastructure, dispatch** (`NotificationDispatchTests`):
  - a completion notifies its requester only;
  - a failure is a Warning;
  - a scheduled occurrence notifies its owner once, even when replayed, and never
    carries the schedule name;
  - replaying the completion and dispatch yields one notification;
  - two workers past a lapsed lease write once, and one is Superseded;
  - a requester who lost `reports.view` is not told, and the occurrence still
    closes;
  - WhatsApp Unknown alerts `whatsapp.manage` holders only, with no number,
    message or reason text;
  - Sent records nothing;
  - an abandoned send alerts;
  - each Knowledge failure alerts `knowledge.manage` holders, with no content or
    title;
  - email is queued only for opted-in members;
  - an unconfigured deployment queues none.
- **Infrastructure, store** (`NotificationStoreTests`):
  - own rows only, newest first, stable bounded pages;
  - idempotent read, with the first `read_at` preserved;
  - another member's id is absent;
  - mark-all is scoped to the caller;
  - cross-workspace list, count, mark and find are refused;
  - the composite FK refuses an email row pointing at another workspace;
  - two concurrent first saves of preferences give one winner and one stale;
  - stale saves are refused, and a colleague's preferences are untouched;
  - retention deletes only expired rows, their emails and old processed
    occurrences, and a second pass removes nothing.
- **Infrastructure, email** (`OperationalEmailTests`, with an in-process loopback
  SMTP sink):
  - send once, and never resend;
  - permission revoked, left the workspace, and preference off each skip with the
    right code;
  - ambiguous → Unknown, not retried;
  - NotSent → backed-off retry, then Failed/attempts_exhausted;
  - concurrent claims are disjoint;
  - a stale-token settle is refused;
  - a lapsed Sending row becomes Unknown;
  - a real MailKit send to the sink: sender and recipient correct, constant
    subject, `Auto-Submitted`, multipart with text and HTML, no attachment, the
    workspace name escaped in HTML, one trusted link, no token;
  - a CRLF/Bcc label never becomes a header;
  - 550 → Rejected, and a dropped answer after DATA → Ambiguous;
  - nothing listening → NotSent;
  - the guard: A (opt-in absent) and B (`""`, `0`, `true`, `yes`) are withheld
    with zero network code; C (loopback in Development) sends without an opt-in;
    D (Production) needs no opt-in; exact `1` enables the transport; no host
    means no email;
  - eight unsafe configurations refuse to start.
- **Infrastructure, usage** (`UsageThresholdNotificationTests`):
  - Warning, Critical and Exhausted are each recorded once; nothing is recorded
    inside a threshold or after refusal;
  - 20 concurrent operations crossing 70% record one occurrence and one
    notification per eligible recipient, and the ledger is unchanged at 80 rows;
  - a new month restarts the thresholds;
  - an unlimited plan never alerts.
- **Infrastructure, billing** (added to `BillingSubscriptionServiceTests`):
  - PastDue, replay, Recovered, PastDue again, Suspended and Canceled give exactly
    five occurrences in order;
  - a refused write records nothing.
- **API** (`NotificationApiTests`):
  - anonymous → 401; stranger → 404 on every route;
  - page bounds → 400;
  - end to end, a report reaches only its requester's bell;
  - the payload carries no transcript canary, recipient or storage key;
  - a colleague is refused another member's read (404);
  - idempotent read;
  - mark-all is scoped to the caller;
  - cross-workspace ids → 404;
  - preferences: 428, 200 with a new ETag, 412, a partial body → 400, and a
    colleague's preferences unchanged;
  - SignalR goes to the recipients' own groups only, never the workspace group or
    a broadcast, and carries the workspace id only;
  - the hub lets a member join only their own group, and a stranger is refused.
- **Frontend:**
  - bell: accessible name and badge; never opens on a realtime event and keeps
    focus; opening marks nothing; severity and unread state in words; hostile
    text rendered as text; only internal links, with meaningful names; explicit
    mark-read and mark-all; Escape returns focus;
  - preferences: defaults off, switches labelled and described; If-Match save;
    412 message and reload; the email-unavailable note;
  - history page: bounded page, marks nothing on visit, Previous/Next.

**Updated tests.** `MigrationSchemaTests` now expects 19 migrations.
`WebChatSchemaTests` lists the two new claim-token columns: server-only lease
tokens, not credentials.

## 7. Chrome verification (Windows Chrome, local only)

**Environment**

- PostgreSQL 17 + pgvector: the `omniai_pg25` container, with a new database
  `omniai_p32` migrated with all 19 migrations.
- Redis: `omniai_redis18`.
- The .NET API, running in Linux Docker (`mcr.microsoft.com/dotnet/sdk:10.0`).
- Angular dev server on port 4200.
- Mailpit (`axllent/mailpit:v1.21`) as the loopback SMTP sink. It shares the API's
  network namespace, so SMTP reaches `127.0.0.1:1025` with no live-email opt-in.
  Its UI is on port 8026.
- The Phase 25 local Stripe stub (verification scaffolding, outside the
  repository).

**Synthetic data**

- Members: `owner.p32@`, `analyst.p32@` and `billing.p32@example.test`.
- Workspace name: `Acme <script>alert(1)</script> <img src=x onerror=alert(1)>`.
- A seeded starter offer and price mapping.

**In-app flow.** Report generated → bell updated live over SignalR (no reload) →
the item's action opened Reports and marked it read (count 1 → 0). Later the
analyst's "Mark all as read" cleared their 3 unread; the owner still had 1.

**Recipients.** Created an Analyst role (`analytics.view`, `reports.view`,
`reports.generate`) and a Billing viewer role (`billing.view`). Both members were
invited and accepted through the real Team flow.
- The analyst's report notified only the analyst.
- The usage alert reached the owner and the analyst, not the billing viewer.
- The billing alert reached the owner and the billing viewer, not the analyst.

**Email preferences**
- The analyst turned on Reports email and generated a report: exactly one email
  in Mailpit.
- They turned it off and generated another: an in-app notification, and no email.
- Default preferences were all off.

**Email content** (inspected in Mailpit and as raw MIME)
- From `OmniAI <notifications@omniai.example>`, To the member only, no Cc or Bcc.
- Subject `OmniAI: Your report is ready`, `Auto-Submitted: auto-generated`.
- multipart/alternative with text/plain and HTML.
- The workspace name escaped (`&lt;script&gt;`) in HTML and rendered as text.
- The only link is `http://localhost:4200/reports`: no token, no attachment, no
  image, no remote resource.
- UTF-8 quoted-printable, with the curly quotes intact.

**Scheduled report**
- A real weekly schedule created in the UI, with `next_run_at` moved into the
  past, gave one execution → Completed → one `ScheduledReportCompleted`
  notification and one email, with no attachment.
- Replayed by clearing the occurrence's `processed_at` and making the schedule due
  again: reprocessed, still one notification, one execution and one email.

**Usage alert.** The workspace was put on a local 10-operation plan. A one-off
local probe drove the real Phase 23 ledger entry point
(`IAiProviderUsageLedger.BeginAsync`, which takes the Phase 24 reservation). It
called no provider: each operation was settled as Failed.
- 6 operations: nothing.
- The 7th: one Warning each for the owner and the analyst, action
  `/plan-usage`, shown live.
- The 8th: nothing new.
- The billing flow later moved the workspace onto the subscribed Starter plan, so
  the 9th stayed below a threshold there. After restoring the local plan, the 10th
  recorded Exhausted (Critical), and the 11th was refused by the quota and
  recorded nothing.

**Billing alert** (local Stripe stub only)
- Checkout → the stub's local pay step → Active, which is not notified.
- A stub control call moved the subscription to past_due; the signed webhook was
  reconciled to PastDue. Warning notifications went to the owner and the billing
  viewer, with action `/billing`.
- The opted-in billing viewer received one email containing no payment data, card
  details, prices or provider ids.
- Active then gave Recovered. PastDue again gave a second PastDue occurrence.

**Permission loss and removal before send**
- With the email worker idle, the owner removed the Billing viewer role in Team
  while a Recovered email was Pending. Result: `Skipped / permission_revoked`.
- Re-granted through the UI, then a new PastDue email was queued and the
  membership rows deleted directly in the local database (the product has no
  member-removal flow). Result: `Skipped / not_a_member`.
- Mailpit stayed at 2 through both.

**XSS and headers**
- The hostile workspace name renders as text everywhere: sidebar, invitation
  acceptance, email.
- A CR/LF label cannot be entered, because the product rejects control characters
  in workspace names. Header injection is therefore tested at the renderer and
  transport level: a CRLF/Bcc label through a real MailKit send produced one
  envelope recipient and no extra header.

**Responsive and theme.** Checked at 390 (light, dark), 768 (light, dark),
1440 (dark) and 1920 (light). There was no page-level horizontal overflow with the
panel closed or open. On phones the panel is pinned to the 16 px gutters (16–374
at 390). Checked surfaces:
- bell and badge;
- panel;
- history page;
- preferences;
- long text;
- a Critical item (octagon icon plus the word "Critical").

**Accessibility**
- The bell's name includes the count in words.
- A polite live region announces changes.
- A visible focus ring. Enter opens the panel, Tab enters it, Escape closes it and
  returns focus to the bell.
- The panel never auto-opens.
- Severity and unread state are carried in words as well as colour.
- Action links carry the item title in their names.
- Switches have labels and descriptions.

**Network and console.** No console errors or warnings. Every request went to
`localhost:4200`: none to an external host, font service or image. The SignalR
client loaded as a separate lazy chunk.

**Defects found and fixed**
- Phase 31 Reports copy still said "Nothing is emailed" on both Reports pages. It
  now says the member is notified and that files are never emailed.
- The "Mark as read" button's optical alignment was fixed.
- SignalR was pulled into the initial bundle by the shell-level bell. It is now
  imported lazily; the initial bundle is 407.59 kB (previously 464.22 kB with the
  bell and a static SignalR import).

No authorisation, test or security bypass was added. The probe and all seeding
are local scratch outside the repository.

## 8. Builds and audits

| Gate | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx` | up to date |
| `dotnet build OmniAI.slnx -c Release` | succeeded, 0 warnings, 0 errors |
| `dotnet test OmniAI.slnx -c Release` | 3,298 passed, 0 failed |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in any of the 8 projects |
| `npm ci` | succeeded |
| `npm run build` | succeeded; only the pre-existing `embed-chat.scss` 72-byte budget warning |
| `npm run test:ci` | 726 passed, 57 files |
| `npm run format:check` | clean |
| `npm audit` | 0 vulnerabilities (`npm audit fix` not run) |
| lint | no lint script exists in this project |

## 9. Migration and packages

- **Migration:** exactly one new migration,
  `20260924100009_AddOperationalNotifications`, with four tables, their indexes
  and check constraints, and three hand-added user FKs. No historical migration
  was edited. No physical `xmin` column: the scaffolded line is omitted from the
  SQL by Npgsql, as in Phase 31, and the existing schema test asserts it.
- **Packages:** MailKit 4.18.0 and MimeKit 4.18.1, both MIT, centrally pinned and
  scanned. No frontend package.

## 10. Limitations

- Knowledge-failure occurrences are best-effort (no transaction to join).
- No product flow for removing a member exists yet; removal semantics are enforced
  by the re-checks.
- The notification realtime subscription opens its own connection to the existing
  hub.
- English only; SMTP only; no digest, quiet hours, SMS, push or chat channels.
- The deferred Phase 31 limitations are unchanged.

## 11. External traffic

- **Live email:** none (Mailpit and the in-test sink on loopback only).
- **Live Meta:** none.
- **Live OpenAI:** none.
- **Live Stripe:** none (local stub only).
