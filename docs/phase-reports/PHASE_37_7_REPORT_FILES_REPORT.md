# Phase 37.7: Report files in the reader's language (PDF, Excel and CSV in English and Arabic)

Decision D-6 (plan row 36), built with the two approved migrations M9 and M10. Evidence is in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-7\`.

## 1. Summary

- **Report files now speak the reader's language.** A PDF, Excel or CSV report is written in English or Arabic: the language the request names, else the requester's saved language, else English. A scheduled report uses its schedule's language, which is its creator's unless they chose another.
- **Arabic files read right to left.** PDF pages are mirrored, with columns running from the right and names, e-mail addresses, ids and dates kept in their own order. Excel sheets open right to left with Arabic sheet names and headings. CSV files have Arabic headings and stay UTF-8 with a byte-order mark. Digits stay Western and dates Gregorian.
- **English files are byte-for-byte what they were**, proved against files captured before anything changed. Today's screens work unchanged: a member who switches the app to Arabic now downloads Arabic reports with no frontend change.
- **New, optional API fields:** `language` on a report request and on a schedule's create and edit, and a read-only `language` on every execution and schedule.

## 2. Starting point

- Branch `master`, HEAD `2b5824a` ("feat: count days in the workspace's time zone, split AI usage by purpose, and compare with the previous period"), the 37.6 checkpoint. The worktree was clean (`git status --short` printed nothing).
- The dev stack was running the 37.6 build. Before any change I ran the provider canary (`live\canary-start.txt`, PASS), measured the p95 of the reads this step touches (`latency\latency-before.json`) and saved the owner's view of the catalogue, executions and schedules (`api\snapshot-before.json`).
- **Before touching the source** I captured the English golden files: all 8 report types × 3 formats, built from a fixed fixture and written by the 37.6 code. A compare run on the unchanged source then passed 48/48 (24 comparisons and 24 determinism checks; `golden\capture.txt` has the hashes and the HEAD).
- Free memory was 7.72–9.05 GB before every memory-gated command, so no page-cache drop was needed.

## 3. Changes by area

`git status --short`: 39 entries, 26 modified and 13 new (`live\git-status.txt`), plus this report. `git diff --stat` of the modified files: 26 files, +852 / −290.

| Folder | Files | What changed |
| --- | --- | --- |
| `backend/src/OmniAI.Domain` | 2 | `ReportExecution.Language` (set at request, from the schedule for an occurrence; part of `SameRequestAs` when the repeat names one); `ReportSchedule.Language` (set at create, changed only by an `Update` that names one) |
| `backend/src/OmniAI.Application` | 7 + 1 new | `ReportWording` (new): every phrase a file says, keyed by its English, with Arabic; zone, period, page and date-isolation helpers. The builder words everything through it; `ReportDocument.Language`; handlers resolve the language (request, else `IAccountDirectory.FindLanguageAsync`, else `en`) and publish it; the generator passes the claimed language |
| `backend/src/OmniAI.Infrastructure` | 8 + 4 migration files | the `UserLanguage` converter; M9 and M10 mappings; the claim returns `language` and the occurrence insert writes it; `PdfText` gains an optional right-to-left paragraph; `PdfReportRenderer` mirrors the page for Arabic; `XlsxReportRenderer` marks sheets right to left and words its summary labels |
| `backend/src/OmniAI.Api` | 2 | optional `language` on the request and schedule bodies (400 `language.unsupported`, checked before membership); `language` on execution and schedule responses; response types and summaries declared on the schedule routes so the generated document describes them |
| `backend/tests` | 6 modified + 8 new + golden files | below |
| `docs/architecture` | 3 | §3.3 |
| `frontend`, `deploy/` | 0 | none |

New test files: `ReportLanguageTests` (Domain); `ReportWordingTests` (Application); `ReportFixtures`, `ReportGoldenFileTests`, `ArabicReportFileTests`, `ReportFileDump` (Infrastructure, no database); `ReportLanguageApiTests` (API). Updated: `MigrationSchemaTests` (13 migrations, and the two columns' exact definition), `ReportGenerationTests` (an Arabic execution of every type and format against PostgreSQL), `ReportScheduleTests` (an occurrence in its schedule's language, claimed with it), `ReportWorld` (a language parameter), and the test project file (the golden files are copied to the output). No existing assertion was changed except the migration count.

### 3.1 What a file says, per format

| Format | Arabic | English |
| --- | --- | --- |
| CSV | Arabic headers; UTF-8 with BOM (as before); every number, date and instant written exactly as in English; the platform's own cell words (channel, customer label) in Arabic | byte for byte as before |
| Excel | every sheet `rightToLeft="1"`; sheet names `الملخص` and each table's; Arabic headers and summary labels; same stylesheet, cell types, styles and numbers as the English workbook | part for part as before (no direction attribute) |
| PDF | mirrored page: title, headings, notes and labels from the right margin; values to the left of their labels; columns from the right; text cells from a column's right edge, numbers to its left edge; footer title right, «صفحة n من m» left; values keep their own direction; dates in Arabic sentences isolated (§4) | byte for byte as before, apart from the clock-derived `/ID` |

Translated: titles (the Arabic names report e-mails already use), labels, headings, sheet names, notes, and the platform's own cell values (channels, operations, outcomes, delivery states, "handled by", labels for customers nobody named). Unchanged: every figure and date, and stored data such as names, providers, models, currency codes and ids. Zones read `بالتوقيت العالمي` for UTC and `بتوقيت Asia/Riyadh` otherwise. Words follow `GLOSSARY.md` and the Arabic the screens already show (e.g. `دردشة الموقع`, `واتساب`, `وكيل الذكاء الاصطناعي`, `المحادثات التي بدأت`, `زائر مجهول`).

### 3.2 Contract table

Every change is additive. "Backward compatible" means today's frontend sends the same requests, gets the same statuses, and reads every field it uses with the same name, type and value.

| Endpoint | Change | New fields or parameters | Permission | Backward compatible |
| --- | --- | --- | --- | --- |
| `POST /api/tenants/{id}/reports` | optional request field; new response field | `language?: "en"\|"ar"\|null`; response `language` | `reports.generate` + the report's data permission (unchanged) | Yes: absent means the requester's saved language, else `en`, which is English for every account that never chose one. A bad value is a new 400 on a new input only. A repeat that names a different language is the existing 409 |
| `GET …/reports/executions`, `GET …/reports/executions/{id}` | new response field | `language` | `reports.view` (unchanged) | Yes: added member; every earlier execution reads `en` |
| `GET …/reports/executions/{id}/download` | the file's words | — | `reports.view` + data permission (unchanged) | Yes: English files byte-identical; file name unchanged |
| `POST …/reports/schedules` | optional request field; new response field | `language?`; response `language` | `reports.schedule` + `reports.generate` + data permission (unchanged) | Yes: absent means the creator's language, else `en` |
| `PUT …/reports/schedules/{id}` | optional request field | `language?` (absent keeps the schedule's) | as before, `If-Match` (428/412 unchanged) | Yes: today's edit sends no language and keeps it |
| `GET …/reports/schedules[/{id}]`, `PUT …/{id}/enabled` | new response field | `language`; `lastExecution.language` | unchanged | Yes: added member |
| Problem codes | reused | `language.unsupported` on the three writes above, before membership | — | Yes: new refusal of a new input only |

**OpenAPI.** `live\openapi-v1.json`: `RequestReportRequest.language` and `SaveReportScheduleRequest.language` are optional nullable strings; `ReportExecutionResponse.language` and `ReportScheduleResponse.language` are present. The schedule routes never declared their response types, so their schemas were missing from the document; I added `Produces<…>()` and summaries (metadata only).

### 3.3 Architecture documents

- `reports-exports-scheduling.md`: §7, a new "Report files in the reader's language" (which language, what changes, per format, dates in Arabic sentences, tests); §9 (the execution response's `language`); §11 (the schedule's language); §13 (M9, M10).
- `problem-codes.md`: `language.unsupported` also on the report and schedule writes.
- `localisation-arabic-rtl.md`: a pointer to the server-written files.

### 3.4 Migrations

Both were generated with `dotnet ef migrations add` from the model, not edited, and are exactly as approved (plan §3.3).

| Migration | Model change | Rendered SQL | Tested |
| --- | --- | --- | --- |
| M9 `20261107123602_AddReportExecutionLanguage` | `ReportExecution.Language` → `report_executions.language character varying(8) NOT NULL DEFAULT 'en'` (model default, no data step) | `sql\AddReportExecutionLanguage.sql` | Fresh database (whole chain) and a copy of `omniai_p36`: column as designed; on the copy all 11 executions read `en`. Then applied to `omniai_p36` with `migrate`, after `pg_dump` (`sql\omniai_p36-before-p37-7.dump`) |
| M10 `20261107123659_AddReportScheduleLanguage` | `ReportSchedule.Language` → `report_schedules.language character varying(8) NOT NULL DEFAULT 'en'`; a new schedule takes its creator's language in the application, not by column default | `sql\AddReportScheduleLanguage.sql` | Same; both existing schedules read `en` |

Both: `sql\p37-7-all-idempotent.sql`; `has-pending-model-changes`: none; `MigrationSchemaTests` pins the 13 migrations and both columns' definition; `DatabaseModelAuthorityTests` green (`gates\migration-checks.log`, the backend gates).

**One generation was discarded.** The first `migrations add AddReportExecutionLanguage` also picked up the schedule's new property by EF convention, which would have put M10's column into M9. I deleted that output (the two files it had just written and its snapshot change), ignored the schedule's property for the M9 generation only, generated M9 (one column), then mapped the property and generated M10. No generated file was edited.

## 4. Decisions

- **The prompt's request-level `language` was built, though the plan said "no new request parameter"** (rule 19). The prompt asks for an optional `language` on the run request and on the schedule edit, and for a test that it overrides the requester's. Both are additive and validated, and without them the plan's behaviour is exactly the default, so this is a superset and at least as safe. Recorded as the one difference from the plan.
- **No check constraint on the two columns.** The plan describes them as `varchar(8) NOT NULL DEFAULT 'en'`, without the check M1 and M2a have. Every write goes through the converter, which writes only `en` or `ar`. Kept exactly as approved; a check can come with a later approved migration if wanted.
- **`language.unsupported` reused** for the report and schedule writes, checked before membership like `analytics.compare.invalid`, so a bad value says nothing about a workspace. Values are exact (`en`, `ar`); `AR`, `ar-SA` and `""` are refused, as for every other `language` field.
- **Idempotency.** A repeated `clientRequestId` that names the other language is a different report (the existing 409). One that names none returns the report already settled, whatever the requester's language is now.
- **The language is settled when the report is asked for.** A person changing their language later does not change a queued report, and a schedule keeps its language when its creator changes theirs or another member saves it (saving makes the editor the owner, but not the language). Only an edit that names a language changes it.
- **English is the key.** `ReportWording` returns the English phrase untouched, so English cannot drift, and the golden files prove it. A missing Arabic phrase would fall back to English rather than fail a report somebody is waiting for; tests build every type in Arabic and fail on any heading, label or note without Arabic.
- **"Values unchanged" in CSV.** Numbers, dates, instants and stored data are byte-identical to English, and a test compares them. The platform's own words that sit in cells (a channel's name, a customer label for somebody nobody named, an outcome) are translated, because they are the platform's labels rather than data, and the three formats of one report must say the same thing. If you want CSV cells to stay English for machine consumers, that is one switch in the CSV path.
- **Dates inside Arabic sentences are wrapped in LEFT-TO-RIGHT MARKs.** Found by the PDF read-back test: under UAX #9 rule W2, digits after Arabic letters become Arabic numbers, the hyphens split them, and `2026-10-01` showed as `01-10-2026` in the period line. I confirmed it on a 300 dpi crop of the page before the fix, and on another after it. Excel and browsers do the same, so the marks sit in the wording (period note and range), not in the PDF code. Dates in their own cell need nothing.
- **Value cells keep their own direction (first strong character), and the report's own words are drawn as right-to-left paragraphs.** That is what isolation means on a PDF page: a Latin name, an e-mail address or an id never takes its order from the Arabic around it. The Clean step still strips explicit bidi controls from user text (unchanged).
- **The font.** The bundled DejaVu Sans shapes Arabic correctly (joining, lam-alef, marks) and reads clearly at the report sizes (the 16 rendered pages and the real Northwind reports in `pdf-images`). I did not add a font. **Proposal, for your decision:** IBM Plex Sans Arabic (already licensed for the frontend) would match the app's type and is more refined at small sizes. It would add font files to the backend, so I am asking rather than doing.
- **Numbers align to the end of their column**, which is the left edge in Arabic (the mirror of English). Headings align to the start. Same rule as the screens.
- **Not done, deliberately:** no Arabic-Indic digits, no Hijri dates (decision 3); no translated file name (the download name stays the server-built ASCII name); no frontend change (Phase 38 may show the language, §7).

## 5. Verification

The backend gates ran in Docker as in the Phase 33.5 report §11:
- `mcr.microsoft.com/dotnet/sdk:10.0` with the repository mounted;
- a throwaway `pgvector/pgvector:pg17` and `redis:7-alpine` through `OMNIAI_TEST_POSTGRES` / `OMNIAI_TEST_REDIS`;
- the projects one at a time (`-m:1`);
- all removed afterwards.

Every gate ran alone. Free memory before each memory-gated command: backend 7.72 GB, `test:ci` 6.74 GB, `build` 6.61 GB (`gates\free-memory-*.txt`).

| Check | Result |
| --- | --- |
| `dotnet restore` (NuGet audit on) | PASS; the two 37.1 pins resolve (`MessagePack` 2.5.303, `System.Security.Cryptography.Pkcs` 10.0.13) |
| `dotnet build OmniAI.slnx -c Release` | PASS, **0 warnings, 0 errors** |
| `dotnet list package --vulnerable --include-transitive` | PASS, no vulnerable packages in any of the 8 projects |
| Tests, `OmniAI.Api.IntegrationTests` | PASS, 1,105 passed, 2 skipped |
| Tests, `OmniAI.Application.Tests` | PASS, 890 passed |
| Tests, `OmniAI.Domain.Tests` | PASS, 915 passed |
| Tests, `OmniAI.Infrastructure.IntegrationTests` | PASS, 1,304 passed, 1 skipped |
| Total | **4,214 passed, 3 skipped, 0 failed** (37.6: 4,068; 146 new). Exit 0, 0 leftover test databases, throwaway containers removed (`gates\backend-gates-run1.log`) |
| `DatabaseModelAuthorityTests` | PASS (in the run above) |
| M9 and M10 on a fresh database | PASS: the whole chain of 13 applies; both columns `character varying(8) NOT NULL DEFAULT 'en'` (`gates\migration-checks.log`) |
| M9 and M10 on a copy of `omniai_p36` | PASS: 11 executions and 2 schedules read `en`; no pending model change |
| `omniai_p36` migrated | Done after `pg_dump`; 13 migrations applied (`live\migrate.log`) |
| English golden files | PASS: 24/24 identical to the files captured from the 37.6 code before any change, and 24/24 deterministic |
| Arabic files, read back (every type) | PASS: CSV header and strict UTF-8 with BOM, numbers and dates identical to English; Excel `rightToLeft` on every sheet, Arabic sheet names and headers, the same stylesheet, cell types, styles and numbers as English; PDF title against the right margin, footer mirrored, Latin and mixed values in order, numbers ending at a column's left edge, no Arabic-Indic digit, Arabic shaped in visual order |
| Language rules | PASS: Domain (default, explicit, refusal, idempotency, schedule kept across others' edits, occurrence takes the schedule's); PostgreSQL (an Arabic execution of every type and format generated in Arabic with no secret; a scheduled occurrence queued and claimed in its schedule's language); API (§ below) |
| Frontend `npm run format:check` | PASS |
| Frontend `npm run test:ci` | PASS, 96 files, 1,169 tests (no frontend change) |
| Frontend `npm run build` | PASS (initial total 313.56 kB raw, 73.66 kB transfer, as in 37.6) |
| Frontend `npm run i18n:check` | PASS, 2,077 messages, 0 errors, 0 warnings |
| API checks against the dev stack | PASS, **48/48** (`api\api-checks.json`) |
| OpenAPI (`live\openapi-v1.json`) | PASS: both request fields optional; both response fields present; schedule routes typed |
| Chrome, English and Arabic | PASS, **13/13** (`live\chrome\`), plus the Arabic PDF in Chrome's own viewer through the Chrome DevTools MCP |
| PDFs rendered to images | PASS: first page of all 8 types in both languages from the fixture (`pdf-images\{en,ar}-*.png`), and the live Northwind reports (`pdf-images\live-*.png`); all looked at |
| p95 latency, before and after | See the table below |
| Provider canary | PASS before any traffic, after the API restart, and at the end (`live\canary-*.txt`) |
| Secret scan of the changed files and the evidence folder | PASS: the demo password appears nowhere. The only hits are EF's `Microsoft.AspNetCore.Identity.*` entity names in generated files, the throwaway `Password=$PW` templates in the scripts, and the scan's own pattern list (`gates\secret-scan.txt`). `tmp\` deleted |

**The API checks** (dev stack, the demo owner, support and analyst, and a synthetic account with its own workspace created through the API, so no demo account's saved language changed):
- **Unchanged contracts:** every field the saved 37.6 catalogue, executions and schedules had is present with the same value (3/3); the 11 earlier executions and 2 schedules read `en`.
- **Both languages, three types, three formats:** 18 reports asked for, completed and downloaded with the right language; Arabic CSV headers are Arabic and English ones are not; the file names are the same ASCII names (`api\files\`).
- **Refusals:** `fr`, `AR`, `ar-SA` and `""` → 400 `language.unsupported`; another workspace with a bad value → 400 (before membership) and with a good one → 404; support (no `reports.generate`) → 403 in any language; analyst → 202; another workspace cannot read the Arabic execution or the schedule → 404; support cannot create a schedule → 403.
- **Idempotency:** the same key without a language → the same Arabic execution; with `en` → 409.
- **Requester and override:** an account with no language gets `en`; after choosing Arabic it gets `ar`; `language: "en"` in the request overrides it.
- **Schedules:** created by an Arabic-speaking member → `ar`; still `ar` after they switch to English; today's edit (no language) keeps it; `de` → 400; naming `en` changes it with a new ETag; stale `If-Match` → 412, missing → 428; a create naming `ar` → `ar`.
- **Not run live: a scheduled occurrence firing on the dev stack.** Schedules fire at the next Monday or the 1st; the occurrence path (queued in the schedule's language, claimed with it, generated in Arabic) is proved against PostgreSQL in `ReportScheduleTests` and `ReportGenerationTests`.

**Chrome.** Signing in through the Chrome DevTools MCP would put the demo password into tool arguments, so the signed-in checks used **Playwright in Google Chrome** (channel `chrome`, a temporary profile), which keeps it in process memory. In English (`ltr`) and Arabic (`rtl`):
- the Reports and Report schedules screens render as before, and their reads carry `language`;
- choosing the language in the unchanged profile menu saves it (`PUT /api/me/language` → 204);
- a report asked for from the unchanged form, which sends no language, is accepted in the member's language and downloads in it: English headers in English, Arabic headers after choosing Arabic;
- switching back to English restored the demo owner's saved language (`en`, as before).

The only failed requests were the sign-in page's known 401 from `/api/auth/me` (Phase 38 adopts the 37.5b session check). Screenshots: `live\chrome\{en,ar}-0{1..3}-*.png`. The Chrome DevTools MCP, signed out, opened the live Arabic agent-activity PDF in Chrome's own viewer (`live\chrome\mcp-ar-agent-activity-in-chrome-pdf-viewer.png`): Arabic title in the tab, mirrored page, dates in order.

**p50 / p95 latency (ms)**, Northwind, owner session, sequential reads after 20 warm-ups (`latency\`). Before: the 37.6 build, 200 reads. After: the final build, 200 and 300 reads.

| Read | Before p50 / p95 | After p50 / p95 (run 1) | After p50 / p95 (run 2) |
| --- | --- | --- | --- |
| GET /reports/executions (page 1, 20) | 15.4 / 23.2 | 15.4 / 27.6 | 15.4 / 27.2 |
| GET /reports/executions (page 1, 100) | 15.2 / 20.8 | 15.6 / 30.1 | 15.6 / 30.0 |
| GET /reports/executions/{id} | 15.1 / 17.2 | 15.6 / 33.4 | 15.3 / 19.3 |
| GET /reports/schedules | 15.2 / 22.8 | 15.3 / 21.2 | 15.1 / 23.2 |
| GET /reports/catalog (unchanged reference) | 15.3 / 16.7 | 15.2 / 22.0 | 15.1 / 20.7 |

p50 is unchanged everywhere. The list's p95 is higher after, but: the list now returns 20 and 33 rows where it returned 11 (the checks added 22 Northwind reports); the untouched catalogue moved by as much (16.7 → 20.7–22.0); a run at the old row count (page size 11) gave p50 15.9 with a noisy tail (p95 32.7, max 170 ms, `latency-after-same-rows.json`). The read path itself gained no statement and no join: the list, one execution and the schedules read one more column of the rows they already read. Nothing was indexed because nothing new is filtered or sorted on. I record the tail as session noise on this machine, not as a regression, with the numbers above for you to judge.

## 6. See it live

Dev server http://localhost:4200, signed in as the demo owner (`C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`), workspace Northwind Operations. The API runs on 127.0.0.1:5027 and Mailpit on http://127.0.0.1:8026; both stay running.

- **Arabic reports from today's screens:** open the profile menu (top corner), choose **العربية**, go to **التقارير** (http://localhost:4200/reports), choose a report and a format (PDF shows the layout best), and press **إنشاء التقرير**. When it is ready, download it: an Arabic, right-to-left file. Choose **English** in the profile menu again to get English files (and to leave the demo owner as it was).
- **Excel:** download an Arabic `.xlsx` and open it in Excel. Sheets open right to left with Arabic names; numbers and dates are real numbers and dates in the same formats.
- **CSV:** an Arabic `.csv` opens in Excel with readable Arabic headers (UTF-8 with BOM).
- **Already made, to open now:** `nabra-backend-evidence\p37-7\api\files\northwind-{ar,en}-{channel-usage,agent-activity,customer-activity}.{pdf,xlsx,csv}` (real Northwind data), and `files\{ar,en}\*` (all 8 types from the fixture). Page images: `pdf-images\`.
- **The API:** `POST /api/tenants/{id}/reports` with `"language":"ar"` (or `"en"`) overrides the member's language; every execution and schedule now shows `language`. The OpenAPI document is at http://127.0.0.1:5027/openapi/v1.json.

## 7. Frontend follow-ups: the whole of Phase 37, ready for Phase 38

Nothing in Phase 37 is required for today's screens to work. Everything below is opt-in, additive, and documented in `docs/architecture/*.md` and `problem-codes.md`.

**From 37.7 (this step)**

| Adopt | Contract |
| --- | --- |
| Optionally, a language choice on the report form ("Arabic file" / "English file"), defaulting to the member's language | `POST …/reports { …, language?: "en"\|"ar" }`; 400 `language.unsupported` |
| Optionally, a language on the schedule form; show it in the schedule list | `POST …/reports/schedules { …, language? }` (absent: creator's), `PUT …/schedules/{id} { …, language? }` (absent: kept); `language` on schedules |
| Optionally, a small language tag on each generated report | `language` on executions (also `lastExecution.language`) |

**From 37.6 (insights)**

| Adopt | Contract |
| --- | --- |
| A "Time zone" setting for `workspace.manage` holders, sending `If-Match`; word `workspace.time_zone.invalid` | `GET /api/tenants/{id}` → `timeZone` + `ETag`; `PUT /api/tenants/{id}/time-zone { timeZone }` → 200 + `ETag`; 400/403/404/412/428 |
| Every date and day label in `period.timeZone` instead of a hard-coded "UTC" | `period.timeZone`; catalogue `timeZone`; execution `timeZone` |
| Previous-period deltas, with "so far" for today | `?compare=previous` → `previous { period, summary, aiOperations }`; 400 `analytics.compare.invalid` |
| "Data since …" and honest empty states | `firstActivityOn` (`YYYY-MM-DD` or `null`) |
| A purpose breakdown of AI usage (never adding currencies) | `aiUsage.byPurpose[]` |
| Report names, descriptions and options worded by code | catalogue `reports[].type`, `parameters[] { code: "range"\|"format", values[] }` |
| Notification report names by code | `items[].parameters.reportType` or `null` |

**From 37.5b (screens)**

| Adopt | Contract |
| --- | --- |
| "Saving changes access for N members"; a count column on the roles list | `memberCount` on `GET …/roles[/{id}]` |
| Invitation tabs or filters, paged in the URL | `GET …/invitations?status=Pending\|Accepted\|Revoked\|Expired&page&pageSize` → `{ items, page, pageSize, total }`; 400 `invitations.status.invalid` |
| Areas and permissions named by key, in both languages | `groupKey` on `GET …/permissions` entries; labels by `code` |
| Plan limits and the fallback plan on the billing screen | `currentPlan { code, name, limits }`, `fallbackPlan` on `GET …/billing` |
| Word team, role, invitation and billing refusals by code | `problem-codes.md` |
| The session check on signed-out pages (ends the console 401 seen in every Chrome run) | `GET /api/auth/session` → `200 {authenticated:false}` or `{authenticated:true,user:{id,email,language}}`; 429 / 503 |
| Tell a person on the reset page they can sign in now | no contract change |

**From 37.5a (screens)**

| Adopt | Contract |
| --- | --- |
| "Failed" (and why) after a reload; "indexing now" for `Started` | `lastIndexAttempt { status, failureCode, startedAt, completedAt } \| null` on knowledge reads |
| File size in the knowledge list | `sourceByteLength` |
| Word file refusals by code, with the bound | `knowledge.file.*` codes and `limit` on 400/413/415/422 |
| Agent channels from the agent read | `channels [{ kind, id, status }]` on AI-agent reads |
| A "Workspace name" setting | `PUT /api/tenants/{id}/name { name }` with `If-Match`; 400 `workspace.name.invalid`, 403, 404, 412, 428 |
| Hosted page and widget: the workspace's name, greeting, accent and theme | `workspaceName`, `greeting`, `accentColor`, `theme` on the public reads |
| `Start`/`End` launcher corners | `launcherPosition: Left\|Right\|Start\|End` |
| Word knowledge, agent, channel and workspace refusals by code | `problem-codes.md` |

**From 37.4 (inbox)**

| Adopt | Contract |
| --- | --- |
| Lead rows with the customer; drop the client copy of the label rule | `customer { id, displayName, labelCode, reference, shortCode }` |
| Open long transcripts at the newest page; load older on scroll | `GET …/messages?tail=true&limit=n`, then `before={previousCursor}` |
| Failed-turn and handoff dividers from the server | `include=events` → `events[…]` |
| A "Needs a person" view with its own count | `?filter=needs-person` |
| Unread dots, and mark read | `unread`, `lastReadSequence`; `POST …/conversations/{id}/read { sequence }` → 204 |
| Word the four new refusals by code | `conversations.filter.invalid`, `transcript.cursor.conflict`, `transcript.include.invalid`, `conversations.read.sequence.invalid` |

**From 37.3 (accounts and email)**

| Adopt | Contract |
| --- | --- |
| Word sign-in throttling and outages | `POST /api/auth/login` → 429 `auth.throttled` (`Retry-After`), 503 `auth.throttle.unavailable` |
| Word sign-in and registration refusals by code | `code` / `codes` (`problem-codes.md`) |
| A language choice on the invite form | `POST /api/tenants/{id}/invitations { email, roleIds, language? }` |
| Optionally, "emails in …" in the account menu | `language` on `GET /api/auth/me` |
| (Already wired in 37.3: the language on sign-up, invitation acceptance, and the switch) | — |

**From 37.2 (local AI) and 37.1 (release blockers)**

| Adopt | Contract |
| --- | --- |
| Optionally, label the `DevelopmentStandIn` provider as "Local test stand-in" / «بديل محلي للاختبار» | provider name in usage reads |
| End-to-end tests against the local stand-in: replies, `[[fail]]`, `[[slow]]`, indexing, preview | no contract change |
| Optionally, note in widget settings that the snippet attribute keeps its historical name | `data-omniai-key` (unchanged) |

## 8. Risks and open issues

- **Arabic wording needs a native reading** (as the plan says for row 36): 109 phrases in `ReportWording`, plus its templated period, zone and page labels and three customer labels (the 8 report names are the ones 37.3's e-mails already use). They follow the glossary and the screens' Arabic; a reviewer can change any phrase in one place without touching English.
- **The font is DejaVu Sans.** Correct and legible, but plainer than the app's IBM Plex Sans Arabic. Adding Plex is your call (§4).
- **Narrow PDF columns truncate long Arabic headings** with an ellipsis, as English ones already do (Arabic headings are a little longer). Excel and CSV carry the full headings.
- **Left-to-right marks inside two Arabic sentences** (the period note and range) are invisible but present in the text: a person copying the note from Excel or the PDF copies them too. Harmless, and the alternative was reversed dates.
- **The p95 tail** of the executions list in this session (§5). p50 unchanged, no new statement; worth a glance when Phase 38 measures the reports screen.
- **Synthetic dev data:** 22 Northwind reports (Arabic and English) from the API and Chrome checks, and one synthetic account with its own workspace, 3 reports and 2 schedules (a weekly enabled one, English after the checks, and a monthly disabled Arabic one), all created through the real API. The demo owner's saved language is `en`, as before.

## 9. Proposed checkpoint commit message

```
feat: write report files in the reader's language, English or Arabic, right to left in Arabic

Phase 37.7 (D-6, plan row 36), with migrations M9 and M10.

- Each execution records its language (report_executions.language,
  default en): the request's optional language, else the requester's
  saved language, else English. Each schedule stores one
  (report_schedules.language), its creator's unless chosen, changed only by
  an edit that names one; scheduled occurrences use it.
- Optional language ("en"|"ar") on POST /reports and on schedule create and
  edit (400 language.unsupported, before membership); language on every
  execution and schedule response. Schedule routes declare their response
  types in the OpenAPI document.
- ReportWording holds every phrase a file says, keyed by its English, with
  Arabic to the glossary; English files are byte-for-byte as before (golden
  files captured from the 37.6 code).
- Arabic CSV: Arabic headers, UTF-8 with BOM, values unchanged. Arabic
  Excel: right-to-left sheets, Arabic sheet names and headers, formats
  unchanged. Arabic PDF: mirrored page, columns from the right, values
  isolated in their own direction, dates in Arabic sentences kept in order
  with left-to-right marks; same bundled font and shaping pipeline.

Every change is additive; no package, deploy/ or frontend change.

Gates: backend 0 warnings, 4,214 passed, 3 skipped, no vulnerable packages;
English files identical to golden files; migrations on a fresh database and
a copy of the dev database; frontend format, 1,169 tests, build and i18n
green; API checks 48/48; Chrome en and ar 13/13.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
