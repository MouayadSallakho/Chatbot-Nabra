# Phase 37.6: Insights — usage by purpose, comparisons, report codes, and the workspace time zone

What the redesigned overview, analytics and reports screens asked the backend for (plan rows 25 and 30–34), built as the approved plan designed them, with its two migrations M7 and M8. Evidence is in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-6\`.

## 1. Summary

- **A workspace counts its days in its own time zone.** The owner (or anyone holding `workspace.manage`) sets an IANA zone such as `Asia/Riyadh`. Analytics days, report periods and the weekly and monthly schedules then follow local days, so a Gulf conversation at 01:30 local time is no longer counted on yesterday. Every workspace stays in UTC until somebody changes it, and every response says which zone its days are in.
- **AI usage is split by purpose:** customer replies, finding knowledge for those replies, document indexing, agent previews, and the knowledge retrieval tester, which is now counted on its own. The parts always add up to the totals, and currencies are never added together.
- **The dashboard can show the previous period beside the current one** (`compare=previous`). It also says when the workspace's first conversation happened, so screens can write "since 18 October" and honest empty states.
- **Reports and their notifications carry stable codes,** so screens can name each report, and its options, in English or Arabic instead of reading the English text.
- Today's screens work exactly as before. In the process, the dashboard's two heaviest statements got faster (p95 52–57 ms against 59–71 ms before).

## 2. Starting point

- Branch `master`, HEAD `027591b` ("feat: say who holds each role, filter invitations, and answer "am I signed in" without a 401"), worktree clean (`git status --short` printed nothing). That is the 37.5b checkpoint, as "Starting point" requires.
- The dev stack was running the 37.5b build. Before any change I measured the p95 of every read this step touches (`latency\latency-before*.json`) and saved the owner's view of each (`api\snapshot-before.json`).
- **Interruption.** The connection dropped on 4 November at 08:05, just after backend gate run 2, and the computer was off until 7 November. Nothing in the worktree changed in between: same HEAD, the same 83 entries, and no file newer than `gates\backend-gates-run2.log`. On 7 November I started Docker Desktop and brought the dev stack back with the `stack.md` commands (`up`, `mailpit`, `api`). The provider canary ran before any traffic (`live\canary-resume.txt`), then the dev server started on :4200. The 36.12 live stack restarted on its own restart policy and was not touched.
- Free memory was 6.65–8.85 GB before every memory-gated command, so no page-cache drop was needed.

## 3. Changes by area

`git status --short`: 83 entries before this report, 66 modified and 17 new (`live\git-status-final.txt`), plus this report. `git diff --stat`: 66 files, +1,725 / −280.

| Folder | Files | What changed |
| --- | --- | --- |
| `backend/src/OmniAI.Domain` | 7 | `WorkspaceTimeZone` (new); `Tenant.TimeZone` and `ChangeTimeZone`; `ReportPeriod`, `ReportScheduleCalendar`, `ReportSchedule` (with `Rezone`) and `ReportExecution.TimeZone` in a zone; `AiUsagePurpose.AgentRetrievalTest` |
| `backend/src/OmniAI.Application` | 19 | `ChangeTenantTimeZoneHandler` and `IWorkspaceTimeZones` (new); zone-aware analytics window with `Previous()`; `byPurpose`, `firstActivityOn` and `AnalyticsComparison`; `ReadComparisonAsync`; the retriever takes its purpose; report and schedule handlers, the schedule runner and the document builder in the workspace zone; notification report types per page |
| `backend/src/OmniAI.Infrastructure` | 14 + 4 migration files | the reader (local days in SQL, purpose grouping, first activity, comparison, and two statements as SQL, §4); `TenantRepository.ChangeTimeZoneAsync`; `WorkspaceTimeZones` (new); the zone on the occurrence insert and the claim; renderer labels; notification type lookup; M7, M8 and the snapshot |
| `backend/src/OmniAI.Api` | 9 | `PUT /api/tenants/{id}/time-zone`; `timeZone` on the tenant; dashboard `compare`, `firstActivityOn`, `previous`, `byPurpose`; catalogue `parameters` and `timeZone`; execution `timeZone`; notification `parameters`; `Produces`/`WithSummary` for the generated document |
| `backend/tests` | 23 | 9 new test files and 14 updated (below) |
| `docs/architecture` | 7 | §3.3 |
| `frontend` | 0 | none |

New test files: `WorkspaceTimeZoneTests`, `ReportTimeZoneTests` (Domain); `AnalyticsTimeZoneTests`, `ChangeTenantTimeZoneHandlerTests`, `RetrievalTesterPurposeTests`, `FixedWorkspaceTimeZones` (Application); `WorkspaceAnalyticsInsightsTests`, `WorkspaceTimeZoneStoreTests` (Infrastructure); `InsightsApiTests` (API). The updated ones are test doubles for the new port members, the renderer and builder tests (zone labels), the retriever's purpose, and three deliberate changes to existing assertions:
- the 37.5a rename test pinned the tenant body to four fields; it now expects `timeZone` too;
- the analytics statement guard allows exactly one `min(` (plan §8, row 33) and one more statement (21 → 22);
- `MigrationSchemaTests` lists M7 and M8.

### 3.1 Items

| Row | What was built |
| --- | --- |
| 34, time zone | `WorkspaceTimeZone` accepts only an IANA name the runtime's zone database knows, spelt exactly as it spells it (`asia/riyadh`, `+03:00`, Windows names, `posix/` and `right/` aliases are refused). The PUT also requires the name in PostgreSQL's `pg_timezone_names`. Order, as for the rename: name (the same `400 workspace.time_zone.invalid` for anybody), membership (404), `workspace.manage` (403), PostgreSQL's list (400, same code), version (428/412). Choosing the current zone writes nothing. The change and the schedules' new next runs commit in one transaction, with the schedules locked `FOR UPDATE` |
| 34, what follows the zone | Dashboard windows and daily buckets (local midnights to instants in .NET; each row's local date `CAST(… AT TIME ZONE @zone AS date)` in PostgreSQL); report presets, custom dates and "the future"; schedule boundaries (counted in dates, so a week with a clock change is 167 or 169 hours); the occurrence and claim paths; renderer labels (`Date (Asia/Riyadh)`, "in Asia/Riyadh whole days", PDF and Excel period lines). Instants stay in UTC and say so. The plan quota's months stay UTC |
| 34, a change of zone | Analytics stores nothing per zone, so the next read simply counts in the new zone. Executions keep the zone they were made in (M8). Schedules still to come move to the new zone's next boundary; one already due keeps its time so no period is skipped. Documented in `analytics-usage-foundation.md` §4 and `reports-exports-scheduling.md` §9 and §11 |
| 30, by purpose | `aiUsage.byPurpose`: all five purposes in a fixed order, zeroes included, each with calls, outcomes, tokens, unrated calls and per-currency cost. Purpose is one more grouping key on the existing usage statements, so the parts add up by construction. `KnowledgeRetriever` takes its purpose from the caller: replies record `ConversationRetrievalEmbedding`, the tester `AgentRetrievalTest`. Tester rows from before 37.6 stay counted as `ConversationRetrievalEmbedding` (they cannot be told apart; no backfill), as `provider-usage-cost-accounting.md` §17 states |
| 31, comparison | `compare=previous` adds `previous: { period, summary, aiOperations }`: as many whole local days, ending where the current window starts, same zone, half-open. Absent (not `null`) without the parameter. Any other value: `400 analytics.compare.invalid`, before membership. Three more counting statements, only when asked. The realtime figures are not compared (plan) |
| 33, first activity | `firstActivityOn`: the local date of the first conversation ever, or `null`. One `min(started_at)` answered from `ix_conversations_tenant_started` |
| 32, catalogue codes | The wire `type` is declared the stable translation key. Each entry gains `parameters: [{ code: "range", values: ["7d","30d","90d","custom"] }, { code: "format", values: [its formats] }]`; the catalogue's `timeZone` is the workspace's |
| 25, report notifications | `parameters: { reportType }` on the four report kinds, with the catalogue codes, derived through occurrence → execution in two bounded reads per page (none on a page without report notifications); `null` for every other kind |

### 3.2 Contract table

Every change is additive. "Backward compatible" means the current frontend's requests get the same statuses and every field it reads keeps its name, type and value in a UTC workspace. That was checked against the saved responses (§5).

| Endpoint | Change | New fields or parameters | Permission | Backward compatible |
| --- | --- | --- | --- | --- |
| `GET /api/tenants/{id}` (and the `POST /api/tenants` and `PUT …/name` responses) | new field | `timeZone` (IANA, `UTC` by default) | `workspace.view` (unchanged) | Yes: added member; the body's other four fields unchanged |
| `PUT /api/tenants/{id}/time-zone` | new endpoint | body `{ timeZone }`; `If-Match`; answers the tenant and a new `ETag` | `workspace.manage` | Yes: new route; nothing calls it |
| `GET …/analytics/dashboard` | new fields; new opt-in parameter | `period.timeZone` (was always `UTC`, now the workspace's), `firstActivityOn`, `aiUsage.byPurpose[]`; `?compare=previous` → `previous { period, summary, aiOperations }` | `analytics.view` (unchanged) | Yes: in a UTC workspace every old field is identical; `previous` appears only on request; `aiUsage` still `null` without calls |
| `GET …/reports/catalog` | new fields | `reports[].parameters[] { code, values[] }`; `timeZone` now the workspace's | `reports.view` (unchanged) | Yes: added member; `UTC` unless the zone is changed |
| `POST …/reports`, `GET …/reports/executions[/{id}]` | new field; zone-aware period | `timeZone`; periods are local dates of the workspace's zone | unchanged | Yes: UTC workspaces get the same periods; older executions read `UTC` |
| `GET …/reports/schedules[/{id}]` | behaviour in a non-UTC workspace | `nextRunAt` is the local boundary | unchanged | Yes: unchanged in UTC |
| `GET …/notifications` | new field | `items[].parameters` (`{ reportType }` or `null`) | membership (unchanged) | Yes: added member |
| Report files (CSV, Excel, PDF) | labels | the zone in the day column, the period note and the period lines | unchanged | Yes: byte-for-byte labels in UTC |
| Problem codes | new | `workspace.time_zone.invalid`, `analytics.compare.invalid` (`problem-codes.md`) | — | Yes: new refusals of new inputs only |

**OpenAPI.** The new route, records and fields are in the generated document (`live\openapi-v1.json`, checked). The report and notification routes never declared their response types, so their schemas had been missing from the document. I added `Produces<…>()` and summaries so that this step's fields are described. That is metadata only.

### 3.3 Architecture documents

- `analytics-usage-foundation.md` §4 (the workspace zone, how days are cut, daylight saving, a change of the setting, the two zone databases, the previous period), §8 (the new members), §14 (deferred list).
- `provider-usage-cost-accounting.md` §3 (call sites) and new §17 (usage by purpose, rows before 37.6).
- `reports-exports-scheduling.md` §2 (codes), §9 (zone, M8, labels), §11 (schedules and a change of zone).
- `operational-notifications.md` §8 (parameters).
- `tenant-onboarding-vertical-slice.md` (the time-zone setting).
- `workspace-plans-entitlements-quotas.md` §7 (quota months stay UTC).
- `problem-codes.md` (two codes).

### 3.4 Migrations

Both were generated with `dotnet ef migrations add` from the model, not edited, and are exactly as approved (plan §3.3).

| Migration | Model change | Rendered SQL | Tested |
| --- | --- | --- | --- |
| M7 `20261104032917_AddWorkspaceTimeZone` | `Tenant.TimeZone` → `tenants.time_zone character varying(64) NOT NULL DEFAULT 'UTC'` (model default, no data step) | `sql\AddWorkspaceTimeZone.sql` | Fresh database (whole chain) and a copy of `omniai_p36`: column as designed; on the copy all 7 tenants read `UTC`. Then applied to `omniai_p36` itself with `migrate`, after `pg_dump` (`sql\omniai_p36-before-p37-6.dump`) |
| M8 `20261104033145_AddReportExecutionTimeZone` | `ReportExecution.TimeZone` → `report_executions.time_zone character varying(64) NOT NULL DEFAULT 'UTC'` | `sql\AddReportExecutionTimeZone.sql` | Same; all 8 existing executions read `UTC` on the copy |

Both: `sql\p37-6-all-idempotent.sql`; `has-pending-model-changes`: none; `DatabaseModelAuthorityTests` green (`gates\migration-checks.log`, the backend gates).

## 4. Decisions

- **The comparison window is the *n* whole days before** the current one (adjacent, as the plan requires), not "the same elapsed time". The current window ends at the request instant, so a client should say "so far" for today. Rejected: a previous window ending at the same wall-clock time *n* days ago, which would leave a gap between the two.
- **`previous` is absent, not `null`, without `compare`** (`JsonIgnore` when null, as 37.4 did for `events`). The existing request's response gains nothing it did not ask for.
- **`byPurpose` lists every purpose with zeroes; `aiUsage` stays `null` without calls.** The prompt's "with no calls the result is an honest zero" meets the existing contract this way. A workspace with no calls still has no usage section (unchanged for today's screen), and inside a present section an unused purpose is a row of zeroes, so a chart's legend does not come and go.
- **The PUT checks the name against both zone databases.** The runtime cuts window edges and PostgreSQL cuts row dates, so a zone only one of them knows cannot be used. The two currently differ by one release (runtime tzdata 2026c, PostgreSQL 2026b, `live\tzdata.txt`); see §8.
- **A stored zone is read without its rules,** so loading a workspace can never fail because of its zone. A zone the runtime no longer knew would fail loudly when a day is computed, never silently fall back to UTC.
- **Not a security event.** Changing the zone changes no access or identity, so it writes no `security_events` row. The rename writes one because visitors see the name (D-3).
- **Schedules on a zone change:** still-to-come runs move to the new zone's next boundary; already-due runs keep their time. Accepted consequence: the first report after a change can overlap the previous one by the offset difference, and each says its zone.
- **The usage buckets and the model breakdown are now SQL.** This was found by measuring. After the purpose key, the dashboard's p50 rose by about 14 ms. EF translated every token sum over the owned `Usage` columns as a correlated subquery per sum per group, and the extra key doubled the groups; projecting first did not change the translation. Both statements are now one parameterised `GROUP BY` each, over the same tenant-and-window predicate and index. A test holds every usage statement to one read of the table. The dashboard is now faster than before the step (§5). Rejected: leaving the regression, or adding purposes as a separate statement with the same subqueries.
- **Rule 19 differences from the prompt:** none of substance. The plan's statement guard is raised as it said (one `min(`; no `count(` exception for `compare`, which needed none).
- **Deferred, as planned:** a comparison on the realtime "open now" figures (no stored history), report files in the requester's language (37.7).

## 5. Verification

The backend gates ran in Docker as in the Phase 33.5 report §11:
- `mcr.microsoft.com/dotnet/sdk:10.0` with the repository mounted;
- a throwaway `pgvector/pgvector:pg17` and `redis:7-alpine` through `OMNIAI_TEST_POSTGRES` / `OMNIAI_TEST_REDIS`;
- the projects one at a time (`-m:1`);
- all removed afterwards.

Every gate ran alone. Free memory before each memory-gated command: backend run 1 7.76 GB, run 2 6.65 GB, `test:ci` 7.17 GB, `build` 7.18 GB (`gates\memory-*.txt`).

| Check | Result |
| --- | --- |
| `dotnet restore` (NuGet audit on) | PASS |
| `dotnet build OmniAI.slnx -c Release` | PASS, **0 warnings, 0 errors** |
| `dotnet list package --vulnerable --include-transitive` | PASS, no vulnerable packages in any of the 8 projects |
| Tests, `OmniAI.Api.IntegrationTests` | PASS, 1,092 passed, 2 skipped |
| Tests, `OmniAI.Application.Tests` | PASS, 883 passed |
| Tests, `OmniAI.Domain.Tests` | PASS, 908 passed |
| Tests, `OmniAI.Infrastructure.IntegrationTests` | PASS, 1,185 passed, 1 skipped |
| Total | **4,068 passed, 3 skipped, 0 failed** (37.5b: 3,944; 124 new). Exit 0, 0 leftover test databases (`gates\backend-gates-run2.log`). Run 1 had one failure, `MigrationSchemaTests` still listing nine migrations. I added M7 and M8 to the list and ran everything again |
| `DatabaseModelAuthorityTests` | PASS (in the run above) |
| M7 and M8 on a fresh database | PASS (`gates\migration-checks.log`) |
| M7 and M8 on a copy of `omniai_p36` | PASS: 7 tenants and 8 executions read `UTC`, no pending model change |
| `omniai_p36` migrated | Done after `pg_dump`; 11 migrations applied |
| Time-zone tests | PASS: UTC unchanged; `Asia/Riyadh`, where 22:30 UTC counts on the next local day (Domain, the reader on PostgreSQL, the API); `Europe/Berlin` on 25 Oct 2026, a 25-hour day holding all its hours, a 169-hour week, monthly due at local midnight; for every day of 2026 in ten zones, including skipped and repeated midnights (Santiago, Beirut, Havana), a day's start falls on it and the instant before on the day before; a change of zone moves schedules in the same transaction, keeps due ones, and a stale tag writes nothing; a Gulf schedule queues the Gulf week and its claim carries the zone |
| Usage-by-purpose tests | PASS: parts equal the totals (calls, outcomes, unrated, tokens, per-currency cost); currencies kept apart; with no calls every purpose is zero and `aiUsage` stays null; the tester records `AgentRetrievalTest`; a non-retrieval purpose is refused before any call; each usage statement reads the table once |
| Frontend `npm run format:check` | PASS |
| Frontend `npm run test:ci` | PASS, 96 files, 1,169 tests (no frontend change) |
| Frontend `npm run build` | PASS (initial total 313.56 kB raw, 73.66 kB transfer, as in 37.5b) |
| Frontend `npm run i18n:check` | PASS, 2,077 messages, 0 errors, 0 warnings |
| API checks against the dev stack | PASS, **60/61 in run 1** on the 4 November build (`api\api-checks-run1.json`); the one failure was my check's wrong expectation, see below |
| API checks on the final build | PASS for every new behaviour (`api\api-checks-run3-final.json`, 57/61); the 4 differences are the earlier runs' own synthetic data, see below |
| OpenAPI (`live\openapi-v1.json`) | PASS: the time-zone route, `compare`, and every new record and field are present |
| Chrome, English and Arabic | PASS, 17/17 on 4 November and again 17/17 after the restart on 7 November (`live\chrome\`) |
| p95 latency, before and after | See the table below |
| Provider canary | PASS at every API (re)start and at the end (`live\canary*.txt`, including `canary-resume.txt` and `canary-closing.txt`): every provider secret, `OMNIAI_LIVE_*` and the SMTP credentials absent; SMTP host loopback |
| Secret scan of the changed files and the evidence folder | PASS: the demo password appears nowhere. The only hits are EF's `Microsoft.AspNetCore.Identity.*` entity names in generated files, the throwaway `Password=$PW` / `$PGPW` templates, and the scan's own pattern list (`gates\secret-scan.txt`) |

**The API checks** used the demo owner, support and analyst accounts, and a stranger account with its own workspace created through the API.
- **Unchanged contracts:** in run 1, every field the saved 37.5b responses had (tenant, `/api/me/tenants`, the three dashboards except the request instant, catalogue, executions, schedules, notifications) was present with the same value: 9/9. Without `compare` there is no `previous`.
- **New fields in UTC:** `timeZone` UTC; `firstActivityOn` 2026-10-18; `byPurpose` sums equal the totals (3,625 calls); catalogue parameters on all 8 reports; the 8 old executions read `UTC`; 8 report notifications carry a catalogue `reportType`, and 3 others carry `null`.
- **Comparison:** adjacent, same length and zone for 7, 30 and 90 days; `compare=next` → 400 `analytics.compare.invalid`; another workspace → 404.
- **Refusals of the PUT:** no `If-Match` → 428; five invalid names → 400 `workspace.time_zone.invalid`; support and analyst → 403; stranger → 404; their own workspace with Northwind's tag → 412; nothing changed.
- **In Riyadh:** 200 with a new tag, and the old one now 412. The dashboard starts at 21:00 UTC and its previous period is adjacent in the same zone. The catalogue says `Asia/Riyadh`. The weekly schedule moved from Mon 00:00 UTC to Sun 21:00 UTC; the disabled monthly one, already past due, kept its time. A report asked then carries `Asia/Riyadh`, completes, its notification names `ConversationActivity`, and support does not see it.
- **The stand-in:** a preview counted under `AgentPreviewGeneration` and a retrieval test under `AgentRetrievalTest`, not as a reply; the parts still add up.
- **Back to UTC:** schedules returned to the UTC calendar. The Riyadh execution kept its zone. **Northwind is in UTC now,** because today's screens still say "UTC" on their own.
- **Run 1's one failure** expected the period note in the CSV. A CSV carries only the table, by design; its day column correctly reads `Date (Asia/Riyadh)` (`api\riyadh-csv-headers.txt`). I corrected the check, not the product. The PDF and Excel labels are covered by renderer tests.
- **Runs 2 and 3** compare against the same 37.5b snapshot, so they also see the earlier runs' own data. Each run adds two provider calls (a preview and a retrieval test) and one Riyadh report. The dashboards therefore differ only in `aiUsage`/`models` (+2 and +4 calls), and those Riyadh executions are not UTC (`api\dashboard-diff-run*.txt`). Everything else passed again.

**Chrome.** The Chrome DevTools MCP was not connected on 7 November, and signing in through it would in any case put the demo password into tool arguments. The checks therefore used **Playwright in Google Chrome (channel `chrome`, a temporary profile)**, which keeps the password in process memory. In English (`ltr`) and Arabic (`rtl`):
- overview, analytics, reports, schedules and the notification centre render as before; their reads carry the new fields, and the overview's read still has no `previous`;
- from the signed-in app, `compare=previous` adds the adjacent period, an unknown comparison is 400 with its code, and the workspace read says `UTC`.

The only failed requests were the sign-in page's known 401 from `/api/auth/me` and my deliberate `compare=yesterday` probe. Screenshots: `live\chrome\{en,ar}-0{1..5}-*.png`.

**p50 / p95 latency (ms)**, Northwind, owner session, 200 sequential reads after 20 warm-up (`latency\`). "Before" is two runs on the 37.5b build. "After" is two runs on the final build, after the SQL change of §4. The last column is the first after-build, before that fix, kept as the finding.

| Read | Before p50 (runs 1 / 2) | Before p95 | After p50 (final runs) | After p95 | Before the §4 fix, p50 / p95 |
| --- | --- | --- | --- | --- | --- |
| GET /analytics/dashboard (7d) | 62.6 / 48.2 | 110.2 / 64.3 | 54.9 / 46.5 | 70.3 / 57.3 | 62.3 / 75.8 |
| GET /analytics/dashboard (30d) | 47.1 / 49.9 | 61.0 / 70.8 | 46.1 / 46.3 | 52.3 / 52.0 | 62.2 / 71.5 |
| GET /analytics/dashboard (90d) | 47.4 / 48.4 | 59.2 / 71.6 | 46.6 / 46.1 | 70.9 / 54.5 | 61.1 / 71.1 |
| GET /analytics/dashboard (30d, compare=previous) | — | — | 47.1 / 46.7 | 58.7 / 54.6 | 62.3 / 74.5 |
| GET /analytics/dashboard (90d, compare=previous) | — | — | 46.9 / 47.3 | 60.4 / 74.7 | 62.4 / 76.4 |
| GET /tenants/{id} | 15.2 / 15.2 | 17.4 / 16.9 | 15.2 / 15.2 | 16.5 / 16.5 | 15.2 / 16.7 |
| GET /notifications (page 1, 20) | 15.1 / 15.2 | 16.3 / 17.5 | 15.2 / 15.2 | 18.3 / 19.7 | 15.4 / 19.8 |
| GET /reports/catalog | 15.3 / 15.3 | 16.8 / 16.8 | 15.1 / 15.1 | 16.7 / 17.3 | 15.1 / 17.1 |
| GET /reports/executions (page 1, 20) | 15.2 / 15.2 | 23.4 / 21.2 | 15.3 / 15.2 | 21.3 / 22.8 | 15.2 / 22.1 |
| GET /reports/schedules | 15.3 / 15.1 | 16.8 / 17.2 | 15.3 / 15.3 | 23.5 / 22.6 | 15.2 / 21.7 |
| GET /plan-usage (unchanged reference) | 15.3 / 15.3 | 23.3 / 24.2 | 15.2 / 15.2 | 21.1 / 21.3 | 15.2 / 21.4 |

Every read other than the dashboard sits on the ~15 ms loopback floor before and after. The comparison costs about 0.5 ms at p50. The first variant of each run (the 7d dashboard) carries warm-up noise in both columns. Per-statement execution times on the dev data are in `latency\statement-times.txt`. Latency was not re-measured after the 7 November restart: it was complete for this same code.

## 6. See it live

Dev server http://localhost:4200, signed in as the demo owner (`C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`), workspace Northwind Operations. Northwind is in UTC, so today's screens look and behave exactly as before. The new contracts are API-only until Phase 38:

- **Analytics** (http://localhost:4200/analytics): the same screen. In DevTools → Network, the `analytics/dashboard` response now has `period.timeZone`, `firstActivityOn` (2026-10-18) and `aiUsage.byPurpose`. Open `/api/tenants/{id}/analytics/dashboard?range=30d&compare=previous` in the same tab to see `previous`, whose `period.to` equals `period.from`.
- **Usage by purpose with the stand-in:** run a preview and a knowledge retrieval test on an AI agent, then reload the dashboard read. `AgentPreviewGeneration` and `AgentRetrievalTest` each grow by one, and the five rows still add up to `calls`.
- **Reports** (http://localhost:4200/reports): the catalogue read has `parameters` and `timeZone` on each report. The two executions requested in Riyadh show `timeZone: "Asia/Riyadh"`. Download the Riyadh CSV: its day column is `Date (Asia/Riyadh)`.
- **Notifications** (http://localhost:4200/notifications): each report item has `parameters.reportType`; others have `null`.
- **The time zone:** `GET /api/tenants/{id}` (ETag) then `PUT /api/tenants/{id}/time-zone` `{"timeZone":"Asia/Riyadh"}` with `If-Match`. The dashboard then starts at 21:00 UTC, and the weekly schedule moves to Sunday 21:00 UTC. Put it back to `UTC` afterwards: today's analytics and reports screens still print "UTC" on their own until Phase 38 reads `period.timeZone`.
- The OpenAPI document is at http://127.0.0.1:5027/openapi/v1.json. The API runs on 127.0.0.1:5027, Mailpit on http://127.0.0.1:8026, and both stay running.

## 7. Frontend follow-ups (what Phase 38 adopts)

| Adopt | Contract |
| --- | --- |
| A "Time zone" setting for `workspace.manage` holders, sending `If-Match`; word `workspace.time_zone.invalid` | `GET /api/tenants/{id}` → `timeZone` + `ETag`; `PUT /api/tenants/{id}/time-zone { timeZone }` → 200 tenant + `ETag`; 400/403/404/412/428 |
| Every date and day label in `period.timeZone` instead of a hard-coded "UTC" (analytics, overview strip, report period hints, schedule next run) | `period.timeZone`; catalogue `timeZone`; execution `timeZone` |
| Previous-period deltas, with "so far" for today | `?compare=previous` → `previous { period, summary, aiOperations }`; 400 `analytics.compare.invalid` |
| "Data since …" and honest empty states | `firstActivityOn` (`YYYY-MM-DD` or `null`) |
| A purpose breakdown of AI usage (never adding currencies) | `aiUsage.byPurpose[] { purpose, calls, succeeded, failed, unresolved, inputTokens, outputTokens, unratedCalls, ratedCost[] }` |
| Report names, descriptions and options worded by code | catalogue `reports[].type` (stable key), `parameters[] { code: "range"\|"format", values[] }` |
| Notification report names by code (drop the regex on the body) | `items[].parameters.reportType` (catalogue code) or `null` |

## 8. Risks and open issues

- **Two zone databases.** Window edges use the runtime's tzdata (2026c in the API image) and row dates use PostgreSQL's (2026b). They agree for every zone today. A rule change shipped to one before the other could misplace events within an hour of a changed boundary. Keep the two images' tzdata releases close; the PUT already refuses a name either side lacks.
- **A zone change can produce one overlapping scheduled report** (by the offset difference). This is accepted and documented, and each report states its zone.
- **Today's screens print "UTC" themselves.** A workspace switched to another zone before Phase 38 would show correct local days under a UTC label. Northwind was put back to UTC for that reason. No current screen can change the zone.
- **The two raw-SQL statements** in the analytics reader are a deliberate exception to "everything is LINQ". Their columns and predicates are pinned by the existing usage tests and the new one-read test, but a future column rename must update them by hand.
- **Synthetic dev data:** two Riyadh executions and their notifications, and a few stand-in provider calls (previews and retrieval tests) from the API checks, created through the real API.
- **Arabic wording** is not touched in this step: no new user-facing sentence was added. The two new problem codes need Arabic wording when Phase 38 adopts them.

## 9. Proposed checkpoint commit message

```
feat: count days in the workspace's time zone, split AI usage by purpose, and compare with the previous period

Phase 37.6: what the redesigned overview, analytics and reports screens asked
the backend for (plan rows 25, 30-34), with migrations M7 and M8.

- Workspace time zone: an IANA name on the tenant (tenants.time_zone,
  default UTC), set with PUT /api/tenants/{id}/time-zone under
  workspace.manage and If-Match; checked against the runtime's zone database
  and PostgreSQL's pg_timezone_names (400 workspace.time_zone.invalid).
  Analytics days, report periods and schedule boundaries are local days
  (23/25-hour days and skipped or repeated midnights handled); executions
  keep their zone (report_executions.time_zone); schedules still to come move
  in the same transaction. The plan quota keeps UTC months.
- Dashboard: period.timeZone, firstActivityOn, aiUsage.byPurpose (five
  purposes, parts add up, currencies apart), and opt-in compare=previous
  (adjacent whole days; 400 analytics.compare.invalid).
- The retrieval tester records AgentRetrievalTest; earlier rows stay counted
  as ConversationRetrievalEmbedding (documented, no backfill).
- Reports: stable type codes with range/format parameters by code, the
  workspace zone in the catalogue, executions and file labels; report
  notifications carry parameters.reportType.
- The usage buckets and model breakdown are one GROUP BY each instead of
  correlated subqueries per group; the dashboard is faster than before.

Every change is additive; no package, deploy/ or frontend change.

Gates: backend 0 warnings, 4,068 passed, 3 skipped, no vulnerable packages;
migrations on a fresh database and a copy of the dev database; frontend
format, 1,169 tests, build and i18n green; Chrome en and ar 17/17.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
