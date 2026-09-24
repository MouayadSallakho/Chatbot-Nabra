# Phase 31 — Reports, exports and scheduled reporting

Baseline: `e81bb9b` (Phase 30, accepted). Working tree left unstaged and
uncommitted for review. Phases 26B–29 remain deferred pending the authorised
Meta business account; Phase 32 not started.

Design reference: [reports-exports-scheduling.md](../architecture/reports-exports-scheduling.md).

## 1. Outcome

A workspace administrator can generate eight deterministic reports as CSV, XLSX or
PDF, download them through the API, and schedule weekly or monthly generation
whose output appears in Reports. Generation is queued, multi-instance safe,
bounded, retained for 30 days, and makes no provider call.

## 2. What was built

| Layer | Additions |
| --- | --- |
| Domain | `ReportExecution`, `ReportArtifact`, `ReportSchedule`, `ReportPeriod` (bounded half-open UTC days), `ReportScheduleCalendar` (Monday / 1st boundaries), typed ids, `ReportFailures` codes, permissions `reports.view` / `reports.generate` / `reports.schedule` |
| Application | `ReportCatalog` (types, formats, permission matrix, file names), `ReportDocument` (format-neutral model), `ReportDocumentBuilder`, `ReportHandlers`, `ReportScheduleHandlers`, `ReportGenerator`, `ReportScheduleRunner`, `ReportRetention`, `ReportAuthority` (named-member permission read for workers), ports `IReportArtifactStore`, `IReportExecutionStore`, `IReportScheduleStore`, `ICustomerActivityReader`, `IReportRenderer` |
| Infrastructure | EF configurations, `ReportExecutionStore` (SKIP LOCKED claim, token-conditional settlement), `ReportScheduleStore` (locked due batch, `ON CONFLICT` occurrence insert), `CustomerActivityReader`, `FileSystemReportArtifactStore`, CSV / XLSX / PDF renderers, generation / schedule / retention workers, DI |
| API | `/api/tenants/{t}/reports` — catalog, request (202), executions list/get, download, schedules list/get/create/update/enabled |
| Frontend | Lazy `/reports` (generate + generated list) and `/reports/schedules`, `ReportsApi`, sidebar entry gated on `reports.view` |

**Analytics reuse (narrow refactor).** `AnalyticsWindow.ForDays` for explicit
periods; `AnalyticsBreakdown` limits on `IWorkspaceAnalyticsReader`; base queries
moved to `AnalyticsQueries`; stable tie-breakers for agent/model ordering;
`CustomerLabels` shared with the directory. Dashboard behaviour and response are
unchanged; the existing Analytics suites pass unmodified apart from the new
parameter.

## 3. Report catalog and permissions

| Report | Data permission | Primary table (CSV) |
| --- | --- | --- |
| Conversation activity | analytics.view | daily activity |
| Channel usage | analytics.view | activity by channel |
| AI and human handling | analytics.view | replies by day (+ current-handling snapshot sheet) |
| Customer activity | customers.view | active customers (id, safe label, channels, counts, first/last activity) |
| AI agent activity | analytics.view | activity by agent |
| AI provider usage | analytics.view | calls/tokens by provider, model, operation (+ outcomes) |
| Estimated provider cost | analytics.view | cost by model with currency (+ per-currency totals) |
| WhatsApp delivery status | analytics.view | all seven delivery states |

Generate = `reports.generate` + data; download = `reports.view` + data (re-checked
at download); schedule = `reports.schedule` + `reports.generate` + data
(re-checked at every occurrence). No report needs `conversations.view` because
none contains conversation detail.

## 4. Privacy and security

- No message, knowledge, instruction or grounding text; no phone number, WhatsApp
  id, public chat key, session credential, email or provider key. Asserted by leak
  canaries across **all 8 types × 3 formats** against real PostgreSQL, a second
  workspace seeded beside the first to prove isolation.
- CSV formula injection neutralised (`= + - @ \t \r` → `'` prefix); XLSX writes text
  only as inline strings and no formula element exists in the package; PDF renders
  `<script>` as glyphs, with no JavaScript, actions, URIs, annotations, embedded
  files or images; the only font is the bundled one, embedded in the file.
- Downloads only through the authorised API route; storage keys and paths never
  serialised; server-built `Content-Disposition`; `no-store`; `nosniff`.
- Storage root must be absolute and outside the application and any repository;
  keys are shape-validated and path-contained.
- Requester, colleague and schedule owners are shown as "You", "Another team
  member" or "Schedule: name" — never an email.

## 5. Tests

All suites run in Docker (`mcr.microsoft.com/dotnet/sdk:10.0`) against
PostgreSQL 17 + pgvector and Redis, because Smart App Control blocks locally built
integration assemblies on this machine.

| Suite | Passed | Failed |
| --- | --- | --- |
| OmniAI.Domain.Tests | 765 | 0 |
| OmniAI.Application.Tests | 686 | 0 |
| OmniAI.Api.IntegrationTests | 834 | 0 |
| OmniAI.Infrastructure.IntegrationTests | 898 | 0 |
| **Backend total** | **3,183** | **0** |
| Frontend (Vitest, 54 files) | 710 | 0 |

New Phase 31 coverage includes:

- **Real PostgreSQL:** execution round trip and tenant isolation; 10-way
  concurrent idempotency; two-worker disjoint claiming; lease takeover with a
  stale token that can settle nothing; exhausted attempts; release; artifact
  metadata; retention expiring only what is expired; schedule ETag and two-editor
  conflict; list versions; two-instance disjoint due claiming; previous-week
  occurrence; occurrence idempotency after a forced rewind; owner revocation
  switching a schedule off; missing data permission; disabled schedules never due;
  schema check that report tables have no blob/JSON/text columns and no physical
  `xmin`.
- **Real files:** CSV BOM and round-trip parse; every formula prefix; XLSX opened
  with the OpenXML SDK (sheets, frozen header, numeric cells, inline strings, no
  `<f>`, no VBA); PDF opened with PdfPig (title, period, "Page n of m", markup as
  text, Unicode preserved, no active or remote content); SHA-256 and length match
  stored metadata; no temporary file left.
- **Artifact store:** atomic write; size bound and failed render leave nothing;
  forged and traversal keys; delete while reading; abandoned write sweep; unsafe
  roots refused.
- **API:** anonymous 401; stranger 404; permission matrix; generate → worker →
  download with headers and hash; zero text-generation and embedding calls;
  idempotent repeat and 409 on reused key; 8 malformed/unbounded requests → 400;
  cross-tenant and guessed ids → 404; download re-check → 403; schedule
  create/428/412/toggle; viewer cannot edit.
- **Frontend:** permission-gated pages, idempotency key on submit, API download
  links with full accessible names, textual statuses, markup rendered as text,
  range validation, storage-unconfigured state, no recipient field, If-Match on
  edit, stale-save recovery, platform-disabled reason.

Two existing schema guards were updated deliberately: the migration count
(17 → 18), and the secret-column sweep, which now lists
`report_executions.claim_token` (a worker claim nonce, not a credential).
`AnalyticsApiFixture` was unsealed so the reports fixture can inherit it.

## 6. Builds and audits

| Gate | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx` | ok |
| `dotnet build OmniAI.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet test OmniAI.slnx -c Release` | 3,183 passed, 0 failed |
| `dotnet list package --vulnerable --include-transitive` | none, all 8 projects |
| `npm ci` | ok |
| `npm run build` | ok; one pre-existing budget warning (`embed-chat.scss`, 72 bytes), not touched by this phase |
| `npm run test:ci` | 710 passed |
| `npm run format:check` | clean |
| `npm audit` | 0 vulnerabilities |
| lint | no lint script exists in `package.json` |

## 7. Chrome verification

Windows Chrome via DevTools. Local stack: PostgreSQL 17 + pgvector, Redis, the
API in Docker on `127.0.0.1:5027`, Angular dev server on `:4200`. Database
`omniai_p31` migrated with all 18 migrations; synthetic data only (three
customers including `=HYPERLINK(...)` and `Añyā بدر <script>…` labels, five
messages carrying a transcript canary, three provider usage rows in USD, EUR and
unrated).

| Check | Result |
| --- | --- |
| Owner catalog | all 8 reports |
| CSV Conversation activity | POST 202 `Pending` + Location → poll → `Completed`; download 200 `text/csv; charset=utf-8`, `attachment; filename=conversation-activity-2026-08-26-to-2026-09-24.csv`, `no-store`, `nosniff`, BOM; browser SHA-256 = PostgreSQL SHA-256 (682 bytes); no canary |
| XLSX Customer activity | `PK` container; sheets Summary/Customers; frozen header; hostile labels as literal inline strings; 0 formula elements; no VBA; no canary |
| PDF Estimated provider cost | `%PDF-`; title, workspace label, UTC period, generated time, models, separate USD and EUR rows; no JavaScript/actions/URIs; no canary |
| Storage | opaque key under `/var/omniai-reports/artifacts` outside the repository; `incoming` empty |
| Failed state | `Reports__MaxPdfRows=5` (configuration, no test endpoint) → PDF of 31 rows `Failed`, "Report exceeds the supported export size", announced |
| Refresh | reloads the list; no polling except the one just-requested report |
| Schedules | weekly (next run Mon 28 Sep 00:00 UTC) and monthly (1 Oct) created; no recipient field; delivery note shown |
| Two-tab edit | tab one saves; tab two PUT → 412, message and Reload action; no If-Match → 428 |
| Enable/disable | off/on with announcement |
| Due occurrence | `next_run_at` moved into the past in local PostgreSQL → one execution for 14–20 Sep (completed period), next run advanced; forced again → still one |
| Permission re-evaluation | owner removed the scheduler's role → due schedule switched off with `owner_lost_permission`, nothing generated, reason shown in the UI |
| No reports.view | nav hidden, page refusal, catalog/list 403 |
| reports.view only | list and download 200; Customer activity shows "Needs more access"; generate 403; schedule 403 |
| reports.generate | 7 reports offered (no Customer activity without customers.view); generate 202; customer 403; schedule 403, no controls |
| reports.schedule | schedule Agent activity 201; Customer activity 403 |
| Network | every request to `localhost`; no external host; downloads only via `/api/.../download` |
| Provider calls | `ai_provider_usages` unchanged at the 3 seeded rows |
| Responsive / themes | no page-level horizontal overflow on either page at any width checked (widths and themes per page below); tables fit their container at 1440 and 1920 and scroll inside it on narrow screens |
| Accessibility | all controls labelled; captions and scoped headers; textual statuses; live-region announcements; `role="alert"` errors; visible focus; keyboard submit; download links named "Download ⟨report⟩, ⟨period⟩, ⟨format⟩" |

Responsive checks, by page:

| Page | 390 | 768 | 1440 | 1920 |
| --- | --- | --- | --- | --- |
| Reports | light, dark | light | light, dark | light |
| Schedules | dark | dark | light | — |

**Defects Chrome found and fixed:**

1. A download whose file was missing from storage said it was "removed by the
   retention policy". It now says the file is no longer in report storage (410).
2. Form controls used Bootstrap classes instead of the design system's
   `.select` / `.input` / `.field-label` primitives.
3. At 1440 the schedule and report tables pushed their action columns out of view.
   Format was folded under the report name, and widths were rebalanced.

## 8. Packages

XLSX uses `DocumentFormat.OpenXml` 3.5.1 and CSV uses CsvHelper 33.1.0, both
already pinned.

PDF, after the Unicode remediation (section 12), uses these new, centrally
pinned, MIT-licensed packages:
- `SkiaSharp` 4.152.1
- `SkiaSharp.HarfBuzz` 4.152.1
- `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.152.1
- `HarfBuzzSharp` 14.2.1.201
- `HarfBuzzSharp.NativeAssets.Linux` 14.2.1.201
- `Unicode.Bidi` 0.3.18

The vulnerability scan is clean. No Office, COM, LibreOffice, browser or external
process is involved. No frontend package was added.

## 9. Migration

Exactly one: `20260923233747_AddReportsExportsAndScheduling`. It creates
`report_executions`, `report_artifacts` and `report_schedules`, with indexes,
check constraints and composite tenant foreign keys. No historical migration was
edited. No blob or JSON columns. The `xmin` row version is the system column
(verified that no physical `xmin` column exists).

## 10. Limitations

- PDFs embed the bundled font whole (about 0.75 MB per file), because the Linux
  Skia build does not subset.
- The PDF text layer names shaped Arabic glyphs by their presentation forms
  (see section 12).
- Filesystem storage is single-node. Multi-node deployments need shared storage or
  an object-store implementation of `IReportArtifactStore`.
- If files are lost outside retention (for example, ephemeral storage replaced),
  the list still offers the download until the attempt returns 410 with a truthful
  message.
- A crash between the file rename and the completion commit can leave an
  unreferenced file. It is never served, but no orphan sweep exists yet.
- Provider usage outcomes are workspace totals, not split per model.
- UTC only, until a workspace timezone exists.
- No email or in-app notification (Phase 32).

## 11. Live providers

Live OpenAI traffic: **NO**. Live Meta traffic: **NO**. Live Stripe traffic:
**NO**. No VPS access, no deployment.

## 12. PDF Unicode remediation (review follow-up)

**Root cause.** The first PDF renderer used PdfPig's writer with standard
Helvetica. That meant WinAnsi-only glyphs and no shaping engine, so any
non-ASCII text was replaced with `?`. Embedding a font in PdfPig would not have
been enough: it places one glyph per character, so Arabic would print
disconnected and in logical order.

**The fix is rendering only.**
- `PdfReportRenderer` now writes through Skia's PDF backend.
- A new `PdfText` class runs each line through the Unicode bidi algorithm
  (`Unicode.Bidi`, UAX #9), shapes each visual run with HarfBuzz in its own
  direction, and draws the glyph runs together with their source text.
- The bundled DejaVu Sans fonts are embedded from assembly resources.
- Explicit bidi control characters in user text are stripped.

Unchanged: the report catalog, metrics, permissions, date semantics, cost
wording, artifact lifecycle, workers and download flow. CSV and XLSX were not
touched.

**Regression tests.** New `PdfUnicodeTests`, plus updated `ReportRenderingTests`:
- Arabic words shape to contextual forms, with no missing glyph.
- لا becomes a single ligature glyph.
- Arabic runs are laid out right to left: the source clusters fall across the run.
- "Support — خدمة العملاء" splits into one left-to-right run and one right-to-left
  run.
- Digits and "USD" beside Arabic keep their left-to-right order.
- José García and François Müller use real glyphs.
- U+202E and control characters are removed.
- In the finished PDF, the text layer contains "José García", "François Müller"
  and `<script>alert(1)</script>` verbatim.
- The text layer contains the exact joined presentation forms, in visual order,
  for النور, شركة, أحمد محمد and العملاء (including the lam-alef ligature).
- The text layer contains no `?` and no U+FFFD.
- The bundled font is embedded, and there is no JavaScript, action, URI,
  annotation, embedded file, image or remote reference.
- The CSV formula-injection tests and the XLSX literal-text tests were re-run and
  pass unchanged.

**Linux.** All tests ran in `mcr.microsoft.com/dotnet/sdk:10.0`. The API
container loaded `libSkiaSharp.so` and `libHarfBuzzSharp.so` from the
NoDependencies native assets, with no host fonts and no fontconfig.

**Chrome.**
- **Flow:** in the real Reports UI, the Owner generated a Customer Activity PDF
  from synthetic data. The workspace was named شركة النور, and the customers
  included أحمد محمد, José García, François Müller and "Support — خدمة العملاء".
  It completed and downloaded through the authorised API (`200 application/pdf`
  with an attachment filename).
- **Arabic:** at 100% and 200% in Chrome's PDF viewer, Arabic is joined and reads
  right to left; أحمد محمد is right-aligned in its cell.
- **Mixed text:** mixed lines read correctly in both directions.
- **Accents:** exact.
- **Plain text:** `<script>` and `=HYPERLINK` show as literal text.
- **No corruption:** no `?` and no missing-glyph boxes.
- **Network and console:** only `localhost:4200`, no font requests, no console
  errors.

**Defect found and fixed.** The right-to-left workspace name was right-aligned
across its whole value column, far from its label. Label/value pairs now start an
RTL value at the value column; table cells keep right alignment for RTL text.

**Gates after the fix.**
- Backend: 3,183 passed, 0 failed; 0 build warnings; no vulnerable packages.
- Frontend (unchanged by this fix): build passes, 710 tests pass, format check is
  clean, audit reports 0 vulnerabilities.
