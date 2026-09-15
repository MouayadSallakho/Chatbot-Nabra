# Phase 22 — Analytics and usage foundation

Written for: the reviewer of this phase.

Baseline: `0d04be8` (Phase 21 checkpoint). Nothing is staged or committed.

## Provider scope

**No live OpenAI traffic.** `OMNIAI_LIVE_OPENAI` was never set and no key was
configured. Reading analytics cannot reach a provider at all: the handler takes
neither a text generator nor an embedding generator, which a reflection test
asserts, and an API test drives a real dashboard read against a provider double
that throws if it is ever called.

**No live Meta traffic.** The WhatsApp activity in the browser walkthrough came
from webhooks signed locally with a throwaway secret and delivered to
`127.0.0.1:5027`. The container's Graph address pointed at a dead loopback port
throughout, and the API log contains no reference to `graph.facebook.com`.

## What was built

| Layer | Added |
| --- | --- |
| Domain | `PlatformPermissions.AnalyticsView` — one code, no `analytics.manage` |
| Application | `IWorkspaceAnalyticsReader`, `GetWorkspaceAnalyticsHandler`, `AnalyticsRange` and its window, the view records |
| Infrastructure | `WorkspaceAnalyticsReader` — eight aggregate queries |
| Api | `AnalyticsEndpoints` (one `GET`), `AnalyticsDashboardResponse` |
| Frontend | `/analytics` dashboard replacing the placeholder, plus a four-number strip on Overview |

Full design rationale, metric definitions and forbidden directions:
[analytics-usage-foundation.md](../architecture/analytics-usage-foundation.md).

## Architecture

Analytics **queries the authoritative tables**: `conversations`, `messages`,
`conversation_turns`, `web_chat_sessions`, `whatsapp_contacts`,
`whatsapp_connections`, `whatsapp_deliveries`, `ai_agents`.

**No events were duplicated into a second system**, and Phase 22 created no table
and no column. No Kafka, no bus, no warehouse, no microservice, no rollup table.

`IWorkspaceAnalyticsReader` has one method, because a dashboard is one screen and
should be one request — verified in Chrome as a single `GET` per page load.

## Analytics is not billing

No cost, no tokens, no plans, no credits, no invoices and no estimates appear
anywhere — not as a field, not as a label, not as a zero. A frontend test scans
the whole rendered page for those words and fails if any appears.

The only near-matches in the rendered text are the denials themselves: *"there is
no sample data"* and *"Counted from what the runtime recorded, not estimated."*

## Permission

`analytics.view`. No `analytics.manage`, because Phase 22 has nothing to
configure — a second code would grant authority over nothing.

Owner picked it up through the intrinsic Owner behaviour with no migration: the
Roles screen now reads **"All 22 permissions"**, up from 21.

| Caller | Answer | Verified |
| --- | --- | --- |
| Anonymous | `401` | API test |
| Non-member | `404` | API test + Chrome (cross-workspace read) |
| Member without the code | `403` | API test + Chrome |
| Member with it | `200` | API test + Chrome |
| Owner | `200` | API test + Chrome |

## Time

Ranges `7d` / `30d` / `90d`, default `30d`; anything else is `400` rather than
clamped. Windows are whole UTC days, half-open `[from, to)` — both edges tested
against real rows.

UTC, because the platform has no workspace timezone yet, and the screen says so
rather than leaving it implied.

**Transcript order is untouched.** A regression test writes two messages out of
timestamp order and asserts both readings: the analytics buckets follow `SentAt`,
and the transcript still follows ingestion sequence.

## Tenant isolation

`messages` and `conversation_turns` carry no tenant column, so they are reached
only by joining through `conversations`. Proved by two tests that build
identically-shaped workspaces — one at the reader, one over HTTP — and in Chrome
with two real workspaces.

## Metrics

Conversations started; customer / AI / human / system messages; open
conversations by handling (right now, deliberately ignoring the period); daily
buckets with every day present; channel breakdown; top-10 agent breakdown; AI
attempts / completed / failed / in progress; WhatsApp delivery by state.

**AI operations are included because they are exact** — the runtime settles each
turn as completed or failed, so this is a count, not an inference. **Provider
latency, token counts and cost are omitted** because the runtime does not persist
them.

**Hosted and embedded web chat are not split**: a conversation's channel is
`WebChat` either way, and inventing a split from a session-level field would be a
number that does not mean what it says.

## Browser verification

Local only: PostgreSQL 17 + pgvector, Redis, the .NET API in a container on
`127.0.0.1:5027`, the Angular dev server on Windows, Chrome. No VPS, no
deployment, no live provider.

Activity was created through real product flows — no test-only endpoint exists.

1. Owner signed in; agent **Support desk** created and activated; a website chat
   link created and published.
2. Two visitors held real conversations through the published chat link (three
   customer messages). With no provider configured the AI turns failed, which is
   real `Failed` runtime state.
3. A WhatsApp number was connected through the UI and put into service, then
   three locally signed webhooks were delivered, creating two WhatsApp
   conversations and three more customer messages.
4. Two human replies were sent from the inbox after taking over — one on web
   chat, one on WhatsApp. The WhatsApp one produced a real delivery row.
5. Analytics opened: **the placeholder is gone.**
6. Every figure matched a direct PostgreSQL count:

   | Metric | Dashboard | `psql` |
   | --- | --- | --- |
   | Conversations | 4 | 4 |
   | Customer messages | 6 | 6 |
   | AI replies | 0 | 0 |
   | Human replies | 2 | 2 |
   | Open / AI / human | 4 / 2 / 2 | 4 / 2 / 2 |
   | AI attempts / failed | 6 / 6 | 6 / 6 |
   | WhatsApp delivery | 1 unknown | 1 unknown |

7. Channels: Website chat 2 (50%), WhatsApp 2 (50%) — matching the rows.
8. Agents: Support desk with 4 conversations, 6 customer, 0 AI, 2 human — all
   four conversations attributed, two through the web chat session and two
   through the WhatsApp connection.
9. `7d` / `30d` / `90d` each re-read and produced 7 / 30 / 90 buckets and table
   rows; Refresh re-read the same period.
10. Workspace **Beta Supplies** (second local workspace) showed all zeroes and
    the honest empty state — none of workspace A's activity, and no sample chart.
11. A member holding only `conversations.view` got no Analytics nav entry, no
    Overview strip, the refusal page on direct navigation, and **`403` from the
    API** called directly from that session. Granting `analytics.view` through
    the Roles screen then gave them the nav entry and the real numbers.
12. Console clean; every request same-origin; one dashboard request per page
    load; the API log contains none of the message text and no phone numbers.

Responsive: no horizontal overflow at 390 / 768 / 1440 / 1920. At 390 the summary
cards stack one per row and the tables scroll inside their own container — the
page never does.

Light and dark at 390 / 768 / 1440. The chart colours change with the theme
because every one is a semantic token; nothing is hard-coded.

Accessibility: the period control is a real `fieldset` with legend "Period" and
three labelled radios, focus ring visible on the label; Refresh is a real
`button`; the chart is `aria-hidden` and every value is repeated in a table with
`<caption>`, five `scope="col"` headers and a `scope="row"` per row; headings run
`h1` → `h2` with no level skipped; a polite off-screen live region announces what
arrived without moving focus; a failed read is `role="alert"`.

## Tests and gates

**Backend — all green.**

| Project | Passed | Failed |
| --- | --- | --- |
| `OmniAI.Domain.Tests` | 644 | 0 |
| `OmniAI.Application.Tests` | 648 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 617 | 0 |
| `OmniAI.Api.IntegrationTests` | 722 | 0 |
| **Total** | **2,631** | **0** |

**69 are this phase's**: 27 Application, 18 Infrastructure, 24 API. The
Infrastructure and API suites run against real PostgreSQL 17 + pgvector with
Redis available; analytics itself does not depend on Redis.

Covered: zero data, one conversation, multiple channels, every sender kind, open
versus closed, AI versus human handling, several agents with a bounded top-10,
both range boundaries, all three ranges, out-of-order timestamps, tenant
isolation, `401`/`404`/`403`/`200`, cross-tenant refusal, an unbounded range,
CSRF not required on the safe `GET`, no mutation verb, no provider call, no
content in the response body, server-side aggregation and no N+1.

- `dotnet restore` — succeeded
- `dotnet build -c Release` — succeeded, **0 warnings, 0 errors**
- `dotnet list package --vulnerable --include-transitive` — **no vulnerable
  packages** in any of the 8 projects

**Frontend — all green.**

- `npm ci` — succeeded
- `npm run build` — succeeded. One warning, pre-existing and unrelated:
  `embed-chat.scss` exceeds its 4.00 kB budget by 72 bytes. Untouched by this
  phase.
- `npm run test:ci` — **615 passed, 0 failed** across 47 files. **28 are this
  phase's**, all in the analytics dashboard spec.
- `npx prettier --check` — all matched files use Prettier code style
- `npm audit` — **0 vulnerabilities**. `npm audit fix` was not run.
- **There is no `lint` script in this project**, so no lint gate was run. Stated
  rather than glossed.

## Migration

One: `20260915045322_AddAnalyticsIndexes`. **Two indexes and nothing else** — no
table, no column, no rollup.

- `conversations (tenant_id, started_at)` — every dashboard query starts here,
  and the existing index pairs the workspace with `last_activity_at`.
- `whatsapp_deliveries (tenant_id, created_at)` — the existing index leads with
  `status`; this table had no index on its workspace column at all.

Deliberately not added: anything on `messages`, `conversation_turns`,
`web_chat_sessions` or `whatsapp_contacts` — each is reached by a join whose key
already leads an existing index, from a conversation set the two above have
narrowed.

No historical migration was edited. A schema test asserts both indexes on the
right columns, and a second asserts `messages` and `conversation_turns` still
carry exactly the indexes they had.

## Packages added

**None**, backend or frontend. The chart is CSS; the aggregation is EF Core over
PostgreSQL.

## Defects found and fixed during the phase

**Daily buckets did not translate.** Grouping by
`conversation.StartedAt.UtcDateTime.Date` produced an EF shaper failure —
`GetUserDefinedCoercionOrThrow` — because the `DateTime` the record expected did
not match what the provider returned. Fixed by grouping on
`DateOnly.FromDateTime(...)`, which PostgreSQL answers with a `date` directly.
Caught by the real-database suite on its first run; an in-memory provider would
have accepted the original.

**A test fixture violated a real constraint.** The analytics world built a web
chat endpoint per conversation, which the unique index on `(tenant, agent)`
rightly refused. The fixture now reuses one endpoint per agent, as production
does.

**Two test expectations were wrong, not the code.** The isolation test
under-counted AI messages because the WhatsApp delivery helper adds one, and the
range test placed a conversation exactly on the window's exclusive end instant.
Both corrected to what the data actually is.

## Known limitations

- **No provider usage or cost accounting.** Tokens, latency and price are not
  persisted, so they are not reported. This is the main thing a later phase
  should add — and it should be labelled accounting, not analytics.
- **No exports or reports.** CSV, Excel, PDF, AI-generated and scheduled reports
  are all out of scope and build on this foundation later.
- **UTC only.** Per-workspace analytics timezones wait for workspace timezone
  configuration.
- **Hosted and embedded web chat are not split**, pending a model that can
  distinguish them at the conversation level.
- **Ranges stop at 90 days.** Longer periods would need a different query
  strategy and probably the rollup tables this phase deliberately avoided.
- **No caching.** Correct by construction today; if profiling ever shows a need,
  a cache needs an invalidation story before it is added.
- **The agent breakdown is bounded to ten.** The eleventh agent by volume is not
  visible, by design.

## Not done

Nothing was staged, committed or pushed. No VPS was accessed. Nothing was
deployed. Phase 23 has not been started.
