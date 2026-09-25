# Operational notifications and alerts

Phase 32. How OmniAI tells the right workspace members that something important
happened: durable in-app notifications, a structural realtime signal, and optional
transactional operational email.

It is **operational notification infrastructure**. It is not marketing email,
campaigns, newsletters, SMS, push, Slack, Teams, webhooks, user-defined alert
rules, a workflow engine or a generic event bus.

## 1. Source of truth

A notification says "this member was told that an authoritative state transition
occurred". It is never the source of truth for that state.

| Area | Authoritative record | Notification is |
| --- | --- | --- |
| Reports | `report_executions` | the requester (or schedule owner) being told it settled |
| Usage | `ai_provider_usages` ledger + Phase 24 plan | holders of `analytics.view` being told a threshold was crossed |
| Billing | `workspace_subscriptions` (reconciled) | holders of `billing.view` being told the lifecycle moved |
| Knowledge | the failed indexing attempt (nothing written) | holders of `knowledge.manage` being told it failed |
| Channel health | `whatsapp_deliveries` | holders of `whatsapp.manage` being told a delivery is Unknown |

Nothing ever reads a notification back to decide business state.

## 2. Model

Four tables, created by the single Phase 32 migration
`20260924100009_AddOperationalNotifications`.

**`notification_occurrences`** — the durable record that a transition happened.
`id, tenant_id, kind, occurrence_key, source_id, occurred_at, processed_at,
attempts, claim_token, claimed_at`. Unique on `(tenant_id, kind, occurrence_key)`.
No payload column: who is told and what they read is worked out at dispatch.

**`notifications`** — one row per recipient. `id, tenant_id, recipient_user_id,
occurrence_id, kind, category, severity, title, body, action_path,
deduplication_key, created_at, read_at, expires_at`. Unique on
`(tenant_id, recipient_user_id, deduplication_key)`. A check constraint restricts
`action_path` to `^/([a-z0-9-]+(/[a-z0-9-]+)*)?$`.

**`notification_preferences`** — per member per workspace: five email booleans,
`updated_at`, and the `xmin` system column as the concurrency token (no physical
column). A row exists only once the member has saved.

**`notification_emails`** — the email outbox. `id, notification_id, tenant_id,
recipient_user_id, category, status, created_at, available_at, attempts, sent_at,
settled_at, failure_code, claim_token, claimed_at`. Composite FK to
`notifications (tenant_id, id)` with cascade; unique on `notification_id`. No
address, subject, body, transcript or credential is stored.

Recipient columns reference `users(id)` with cascade (hand-added, like
`fk_tenant_memberships_user`). Every table references `tenants` with cascade.

## 3. Catalogue and severity

A closed enum, `NotificationKind`, of transitions that already exist in the
product. Severity is presentation only and grants nothing.

| Kind | Category | Severity | Action |
| --- | --- | --- | --- |
| ReportCompleted | Reports | Information | `/reports` |
| ReportFailed | Reports | Warning | `/reports` |
| ScheduledReportCompleted | Reports | Information | `/reports` |
| ScheduledReportFailed | Reports | Warning | `/reports` |
| UsageWarning (≥70%) | Usage | Warning | `/plan-usage` |
| UsageCritical (≥90%) | Usage | Critical | `/plan-usage` |
| UsageExhausted (limit) | Usage | Critical | `/plan-usage` |
| BillingPastDue | Billing | Warning | `/billing` |
| BillingSuspended | Billing | Critical | `/billing` |
| BillingRecovered (PastDue/Suspended → Active) | Billing | Information | `/billing` |
| BillingCanceled | Billing | Warning | `/billing` |
| KnowledgeIndexingFailed | Knowledge | Warning | `/knowledge/{documentId}` |
| WhatsAppDeliveryUnknown | ChannelHealth | Warning | `/channels/whatsapp/{connectionId}` |

Nothing is notified for ordinary traffic: customer messages, AI replies,
successful or unchanged indexing, NeedsIndex, Sent/Delivered/Read receipts,
Pending/Running report transitions, or provider operations inside a threshold.
There is no provider-health or "Meta outage" kind, because no such state is
persisted; a WhatsApp connection-level failure state does not exist either, so it
is not fabricated.

Titles and bodies are fixed templates in `NotificationCatalog`. Report kinds name
the report from the fixed catalogue (never a workspace-authored schedule name).
No text carries message content, knowledge content, a document title, a phone
number, a provider id, payment data or exception text.

## 4. Recipient matrix

Existing permissions only; nothing new was added to `PlatformPermissions`.

| Category | Recipients (read fresh at dispatch) | Email re-check at send |
| --- | --- | --- |
| Reports | the execution's `requested_by` (requester, or schedule owner) — only while a member holding `reports.view` | member + `reports.view` + preference |
| Usage | current members holding `analytics.view` (the Plan & usage permission) | member + `analytics.view` + preference |
| Billing | current members holding `billing.view` | member + `billing.view` + preference |
| Knowledge | current members holding `knowledge.manage` | member + `knowledge.manage` + preference |
| ChannelHealth | current members holding `whatsapp.manage` | member + `whatsapp.manage` + preference |

"Holding" is the union of the member's roles' effective permissions, Owner
included, computed from the same role definitions the request path uses.
Assignments are joined to memberships, so a removed member is never a recipient.

## 5. Durability, atomicity and deduplication

**Occurrences ride on the transition.** Each integration writes its occurrence
in the same statement or transaction as the state change, with
`ON CONFLICT (tenant_id, kind, occurrence_key) DO NOTHING`:

| Integration | Where | Key |
| --- | --- | --- |
| Report completed | `ReportExecutionStore.CompleteAsync` — CTE with the Completed update, same transaction as the artifact row | `{executionId}:Completed` |
| Report failed | `FailAsync` and the exhausted-attempt sweep in `ClaimAsync` — CTE with the Failed update | `{executionId}:Failed` |
| Usage threshold | `WorkspaceEntitlementService.ReserveAsync`, after the ledger insert, under the per-workspace advisory lock, same transaction | `{yyyy-MM}` per kind |
| Billing | `BillingSubscriptionService.ApplyAsync` / `SuspendAsync`, same transaction and lock as the subscription write | `{subscriptionId}:{status}:{updatedAtMs}` |
| WhatsApp Unknown | `WhatsAppOutbox` settle (only when the new status is Unknown) and `AbandonStaleSendsAsync` — CTE with the update | `{deliveryId}` |
| Knowledge failure | Since Phase 33: the durable `knowledge_index_attempts` row. Settling an attempt Failed (explicit provider or unexpected failure) or, by the stale-attempt reconciler, Unknown/`interrupted` inserts the occurrence in the same SQL statement as the settlement. See [security-reliability-observability.md](security-reliability-observability.md) §4 | `{documentId}:{attemptId}` |

A replayed statement is one occurrence. A genuine recurrence has a new key:
PastDue → Active → PastDue is two PastDue occurrences (verified locally); a new
UTC month restarts the thresholds; each failed indexing attempt is its own.

**Usage thresholds are Phase 24's.** The state before and after the one operation
is computed by `EntitlementUsage.Create`; an occurrence is recorded only when the
state moves up. Operations at 92% record nothing. The advisory lock serialises
concurrent reservations, and the month key makes a repeat impossible anyway. An
unlimited plan never alerts. No second counter exists.

**Dispatch.** `NotificationDispatchWorker` claims occurrences with
`FOR UPDATE SKIP LOCKED`, a fresh claim token and a 5-minute lease.
`NotificationDispatcher` resolves recipients from permissions as they stand now,
builds one notification per recipient (deduplication key `{Kind}:{occurrenceKey}`),
decides which recipients' emails to queue, and `CompleteAsync` — in one
transaction — re-locks the occurrence by token, inserts notifications with
`ON CONFLICT DO NOTHING`, inserts emails only for newly written notifications, and
marks the occurrence processed. A lost claim writes nothing. An occurrence with no
eligible recipient is processed, not stuck. After 5 attempts an occurrence is
closed.

No process-local queue is ever authoritative.

## 6. Read state and retention

Unread count is a server-side `COUNT` over a partial index of unread rows. The
list is one bounded page (default 20, max 50), `created_at DESC, id DESC`. Mark
one read and mark all read are monotonic and idempotent; no ETag. Reading is
explicit: opening the panel or the page marks nothing; following an item's action
or pressing its "Mark as read" does.

Retention: `Notifications:RetentionDays`, default **90**, via `TimeProvider`.
`NotificationRetentionWorker` deletes, in bounded batches, notifications whose
`expires_at` has passed (their emails cascade) and processed occurrences older
than the retention period. No report, subscription, ledger row, document or
delivery is touched. Two instances deleting at once is one delete that matches
and one that matches nothing.

## 7. Realtime

The Phase 19 `WorkspaceInboxHub` gained `SubscribeNotifications(tenantId)`:
membership is the only requirement (not `conversations.view`), and the group is
`notifications:{tenant}:{userId}` from the signed-in session, never an argument.
`SignalRNotificationRealtime` sends `notificationsChanged` with the **workspace id
only** to each recipient's own group, through the Redis backplane when configured.
No title, body, id or count travels. The client re-reads the HTTP API. A failed
signal is logged and swallowed; the next read is correct.

## 8. API

All under `/api/tenants/{tenantId}/notifications`, authenticated, antiforgery
on unsafe methods, `Cache-Control: no-store` on reads. None takes a user id.

| Method | Route | Result |
| --- | --- | --- |
| GET | `/?page&pageSize` | own page + unread count |
| GET | `/unread-count` | `{ unreadCount }` |
| POST | `/{id}/read` | 204; 404 if not the caller's |
| POST | `/read-all` | `{ marked }` |
| GET | `/preferences` | own preferences + `emailConfigured`, ETag |
| PUT | `/preferences` | If-Match required (428), stale 412, all five categories required (400) |

Anonymous → 401. Non-member → 404. Another member's or workspace's notification →
404.

## 9. Email

**Scope.** Transactional operational email that mirrors selected in-app
notifications. No marketing, no user-written content, no arbitrary recipient.

**Abstraction.** `IOperationalEmailSender` in the Application (no MailKit type).
Infrastructure adapter `SmtpOperationalEmailSender` uses **MailKit 4.18.0**
(MimeKit 4.18.1), MIT. No SendGrid/Resend contract.

**Configuration** (`Email` section; secrets from the environment only):

| Key | Default |
| --- | --- |
| `Email:Smtp:Host` | empty → email off (`UnconfiguredOperationalEmailSender`) |
| `Email:Smtp:Port` | 587 |
| `Email:Smtp:Security` | `StartTls` (`SslOnConnect`, or `None` for loopback only) |
| `Email:Smtp:Username` / `Password` | both or neither; password e.g. `Email__Smtp__Password` |
| `Email:Smtp:FromAddress` / `FromName` | required / `OmniAI` |
| `Email:Smtp:Timeout` | 30 s (1 s – 2 min) |
| `Email:AppBaseUrl` | required when a host is set |

Startup refuses: plain text to a non-loopback host, a sender address with a line
break or that is not one plain address, a username without a password, and an
application URL that is not absolute, not https (http allowed for loopback only),
or carries credentials, a query or a fragment. No password default exists and none
is committed.

**Development live-email guard.** In Development, a non-loopback SMTP host without
`OMNIAI_LIVE_EMAIL` exactly `1` gets `WithheldOperationalEmailSender`, which has no
network code: every email settles `Failed / live_email_disabled`. A loopback host
(`localhost`, `127.0.0.1`, `::1`) needs no opt-in. Production never requires it.
Independent of the Meta and OpenAI guards.

**Outbox.** Emails are rows queued with their notification. `OperationalEmailWorker`
first marks rows stuck in `Sending` for longer than the 10-minute lease as
`Unknown / abandoned_while_sending`, then claims `Pending` rows that are due
(`FOR UPDATE SKIP LOCKED`, claim token, attempts+1). Settlement is conditional on
the token.

**Send-time re-check** (`OperationalEmailDispatcher`), in order, each skipping with
a stable code: email not configured → notification gone → not a member →
permission revoked → preference disabled → account has no usable address. The
address is read from the Identity account at send time; there is no separate
email-verification concept in the product, so none was invented.

**Ambiguity.** Connect/TLS/sign-in failures are `NotSent` → back to `Pending`
with backoff (1 min × attempt), at most 3 attempts, then `Failed /
attempts_exhausted`. A refusal the server states (sender, recipient or message not
accepted) → `Failed / rejected`. Anything else after transmission began → `Unknown
/ outcome_unknown`, never retried. Statuses: Pending, Sending, Sent, Failed,
Unknown, Skipped.

**Content.** `OperationalEmailRenderer`: a constant subject (`OmniAI: {title}`), a
text/plain and an HTML part (multipart/alternative), fixed markup with every
inserted value HTML-encoded, the workspace name reduced to one line, one link built
from `AppBaseUrl` + the internal action path, no image, font, stylesheet or
tracking pixel, `Auto-Submitted: auto-generated`, no attachment — a report email
links to Reports, where the authenticated download remains the only way to the
file. No token, session, key or invitation ever appears in a link. SMTP
transcripts, bodies and credentials are never logged (no protocol logger).

**Failure isolation.** Email never rolls back anything: the notification and the
business state are committed before an email is even claimed.

## 10. Preferences

Per member per workspace, self-service only; no administrator override. Email is
**off by default** for all five categories (no recorded consent exists). In-app
notifications are not a preference. The first save is concurrency-checked against
a "nothing stored" version (`xmin` 0 is never live), so two first saves cannot
both win.

## 11. Isolation, privacy and authorization changes

Every store predicate names workspace and recipient. Tenant B's ids behave like
ids that never existed. Unread counts, mark-all and preferences are per member.
Membership removal: the member can no longer query the workspace (membership
check), and a pending email re-checks and skips (`not_a_member`); historical rows
are not rewritten and expire with retention. Permission loss: an existing
notification stays visible to its recipient (its text is non-sensitive), the
action's destination re-checks as always, and a pending email skips
(`permission_revoked`).

## 12. Frontend

A bell in the top bar (`NotificationBell`): accessible name with the count in
words, a decorative badge, a polite live region, a non-modal disclosure panel with
the eight most recent items, "Mark all as read", "View all notifications" and
"Email preferences". Escape, outside click and navigation close it; focus returns
to the bell on Escape. It never opens itself. Items show a severity icon of a
distinct shape plus the severity word, an unread dot plus bold title plus the word
"unread", relative time with an absolute tooltip, a meaningful action link (only
for internal routes, re-checked client-side) and "Mark as read". Lazy routes
`/notifications` (bounded pages) and `/notifications/preferences` (five labelled
switches). `NotificationRealtime` reuses the workspace hub and imports the SignalR
client lazily, so it stays out of the initial bundle.

## 13. Limitations

- ~~Knowledge failures are recorded best-effort outside a transaction.~~ Closed
  in Phase 33: the attempt is a durable row created before any embedding
  request, and the alert is written in the same statement that settles it; an
  abandoned attempt is settled and alerted exactly once by the reconciler.
- A PastDue that moves directly to Suspended in the same reconciliation is one
  Suspended occurrence; grace-expiry suspension is the Phase 25 sweeper's.
- No member-removal flow exists in the product yet; removal semantics are enforced
  by the re-checks and tested at the database level.
- Emails are English only; no digest, quiet hours, SMS, push or chat channels.
- Only the SMTP adapter exists; the abstraction admits others later.
- The notification SignalR subscription opens its own connection to the existing
  hub rather than sharing the inbox's.
