# Reports, exports and scheduled reporting

Phase 31. A workspace administrator generates deterministic reports from the
platform's own operational records, downloads them as CSV, XLSX or PDF, and
schedules weekly or monthly generation. Reports are the Analytics numbers
exported — not a second analytics system, not a report builder, and not
AI-written summaries.

## 1. Boundaries

| In scope | Out of scope (deliberately) |
| --- | --- |
| Eight fixed report types | Report builder, field picker, user SQL or query JSON |
| CSV, XLSX, PDF | Email, webhook or any external delivery (Phase 32) |
| Explicit, bounded UTC periods | "All time" exports |
| Weekly / Monthly schedules | Cron, hourly, natural-language schedules |
| Aggregates and safe labels | Message text, knowledge text, agent instructions, transcripts |
| Zero provider calls | AI summaries, explanations or recommendations |

## 2. Report catalog and permission matrix

**Codes (Phase 37.6, row 32).** Each entry's `type` (`ConversationActivity`,
`ChannelUsage`, `AiHumanHandling`, `CustomerActivity`, `AgentActivity`,
`ProviderUsage`, `ProviderCost`, `WhatsAppDelivery`) is the report's stable
translation key: a client words the name and description from it, and it never
changes. Each entry also lists its request `parameters` by code — `{ code: "range",
values: ["7d","30d","90d","custom"] }` and `{ code: "format", values: [its formats] }`
— and the catalogue's `timeZone` is the workspace's. The English `title` and
`description` stay for the current client. Report notifications carry the same
codes (`operational-notifications.md` §8).

The catalog is code (`OmniAI.Application.Reports.ReportCatalog`), not configuration.
Three new permissions, none implying another:

| Permission | Grants |
| --- | --- |
| `reports.view` | The catalog, the generated-report list and schedule list, downloads (with the data permission below) |
| `reports.generate` | Asking for a report |
| `reports.schedule` | Creating, editing, switching schedules on and off |

Each report also needs its data's own permission, checked at request time, at
generation time (for the requester), at download time (for the downloader) and at
every scheduled occurrence (for the schedule owner):

| Report | Data permission | Generate | Download | Schedule |
| --- | --- | --- | --- | --- |
| Conversation activity | `analytics.view` | generate + data | view + data | schedule + generate + data |
| Channel usage | `analytics.view` | 〃 | 〃 | 〃 |
| AI and human handling | `analytics.view` | 〃 | 〃 | 〃 |
| Customer activity | `customers.view` | 〃 | 〃 | 〃 |
| AI agent activity | `analytics.view` | 〃 | 〃 | 〃 |
| AI provider usage | `analytics.view` | 〃 | 〃 | 〃 |
| Estimated provider cost | `analytics.view` | 〃 | 〃 | 〃 |
| WhatsApp delivery status | `analytics.view` | 〃 | 〃 | 〃 |

No report carries conversation detail, so none needs `conversations.view`; a
future detail-style report would have to add it to the catalog explicitly.
The catalog endpoint lists only the reports the caller may generate. Owner holds
every code automatically, as with every other permission.

## 3. One meaning per metric

Seven reports read `IWorkspaceAnalyticsReader` — the same reader the dashboard
uses — through a narrow refactor:

- `AnalyticsWindow` accepts an explicit whole-day period (`ForDays`) as well as a
  dashboard preset. Presets are unchanged.
- The reader takes an `AnalyticsBreakdown` (agent/model row limits). The dashboard
  passes 10/10; a report passes its export bound + 1. Only the limit differs.
- Agent and model ordering gained stable tie-breakers (id; provider/model/operation).
- The two base sets — conversations started in a window, messages sent in a
  window — moved into `AnalyticsQueries`, which the dashboard reader and the
  Customer Activity reader both use.
- The customer fallback label moved into `CustomerLabels`, shared by the
  directory and reports.

`ReportDocumentBuilder` copies fields; it never counts.

## 4. Execution lifecycle

```
POST /reports ──► ReportExecution(Pending) ──► 202 + Location
                              │
     ReportGenerationWorker   ▼   FOR UPDATE SKIP LOCKED, claim token, lease
                       Running ──► permissions re-checked for the requester
                              │     build document (analytics / customer reader)
                              │     render → artifact store (temp → hash → rename)
                              ▼
          Completed (artifact row + status in one transaction, conditional on token)
          Failed    (stable code: report_too_large, permission_revoked,
                     storage_unavailable, generation_failed)
          Expired   (retention removed the file)
```

- **Idempotency.** `client_request_id` is required; unique on
  `(tenant_id, requested_by, client_request_id)`. A repeat returns the same
  execution; the same key for a different report is 409.
- **Multi-instance.** Claims use `FOR UPDATE SKIP LOCKED`. Each claim stamps a
  fresh `claim_token`; complete / fail / release are `WHERE claim_token = @token`.
  A lapsed lease (10 min) lets another instance take over; the late original can
  settle nothing and deletes its own file.
- **Attempts.** 3. A transient failure releases the execution; after the last
  attempt it fails with `generation_failed`. Exception text is logged by id only
  and never stored or shown.

## 5. Artifact storage

`IReportArtifactStore` (Application) knows keys, never paths. The V1
implementation is `FileSystemReportArtifactStore`:

- Root: `Reports:Storage:RootPath`. Absent → reports cannot be generated (catalog
  says so, POST is 503). Relative, inside the application directory, or inside a
  source repository → startup fails naming the setting. Nothing falls back to the
  working directory, `wwwroot` or the repository.
- Keys: 32 lower-case hex characters from `RandomNumberGenerator`; checked against
  that shape and re-checked to resolve inside the root before any file access.
- **Atomic writes:** `incoming/<key>.partial` → counted and SHA-256 hashed while
  written → size bound enforced → `fsync` → rename into `artifacts/<key>`. Any
  failure deletes the temp file.
- Files are opened with `FileShare.Delete`, so retention may remove a file while a
  download streams it; the open handle reads the complete bytes.
- Metadata (`report_artifacts`): content type, byte length, SHA-256, opaque key,
  created, expires. No bytes in PostgreSQL. The key never leaves the server.

An S3 / blob implementation replaces the one class; nothing else changes.

## 6. Downloads

`GET /api/tenants/{t}/reports/executions/{id}/download` — authenticated, member,
`reports.view` + the report's data permission, re-checked now. Streams through
the API with `Content-Disposition: attachment` built only from server parts
(`<slug>-<from>-to-<last-day>.<ext>`), `Cache-Control: no-store`,
`X-Content-Type-Options: nosniff`. Not completed → 409; expired or file missing
→ 410 with a truthful message. Another workspace's id → 404.

## 7. Formats and their security

| Format | Library | Notes |
| --- | --- | --- |
| CSV | CsvHelper (already pinned) | Primary table only. UTF-8 **with BOM** so Excel reads non-Latin names. Text beginning `= + - @ \t \r` is prefixed with `'` (OWASP). Numbers invariant; dates ISO. |
| XLSX | DocumentFormat.OpenXml (already pinned) | Summary sheet + one sheet per table, bold frozen header, content-sized columns, numeric counts/money (`0.000000`) beside a currency column, real dates. Text is only ever an inline string; the renderer never writes a formula element; no macros, external links or data connections. |
| PDF | SkiaSharp PDF backend + HarfBuzz + Unicode.Bidi | A4 landscape, title, workspace label (only if the requester holds `workspace.view`), UTC period, generated time, summary, tables, notes, "Page n of m". Bundled font embedded; no fetched font, image, link, annotation, JavaScript, form or open action. Document properties: Creator `Nabra reports`, Producer `Nabra` (Phase 37.1; were `OmniAI`). |

### Unicode PDF text

Every PDF line goes through `PdfText`:

1. **Clean.** Control characters become spaces. Explicit bidi embeddings,
   overrides and isolates (U+202A-U+202E, U+2066-U+2069) are removed, so user
   text cannot reorder its neighbours ("Trojan Source").
2. **Bidi.** `Unicode.Bidi` (a port of the Rust `unicode-bidi` crate: UAX #9,
   Unicode 16) gives the paragraph direction and the line's visual runs with
   their embedding levels.
3. **Pick the font (Phase 39.7b).** Each run is split by script: Arabic letters
   go to IBM Plex Sans Arabic, Latin letters and every digit to Geist, letters of
   any other script to DejaVu Sans. Spaces, punctuation, symbols and marks stay
   with the letters around them, so a word is never set in two fonts; a character
   a face lacks goes to the next bundled face that has it.
4. **Shape.** HarfBuzz (`HarfBuzzSharp`, called directly since 39.7b so the
   `tnum` feature can be asked for) shapes each piece in the direction its level
   dictates, never guessed from the script, so digits and punctuation inside
   Arabic go the right way. Joining, the lam-alef ligature, marks and kerning come
   from the font's OpenType tables; Geist's figures are set tabular. Nothing
   reverses a string, and no letter form is chosen in code.
5. **Draw.** Runs are placed in visual order as positioned glyph runs that also
   carry their UTF-8 source and cluster map, so the PDF text layer is real text.

Alignment:
- A table cell whose text is right-to-left aligns to the cell's right edge.
- Numbers align right, as before.
- A label's value starts at its own value column.

**Tables (Phase 39.7b, Phase 38 note 2).**
- **Columns sized by content.** A column wants its widest value (of the first 200
  rows) or its heading on one line, plus 5 pt of padding on each side. A table is
  as wide as its columns and starts at its reader's margin (left in English, right
  in Arabic); spare width is no longer spread over every column.
- **Headings wrap, never cut.** When the columns want more than the page, each
  keeps at least its widest value and its heading's longest word, the rest is
  shared by how much each heading still wants, and headings wrap at their spaces,
  bottom-aligned on a shared last line. Only when even that does not fit do the
  widest text columns give up width (a water level: narrow ones keep theirs, never
  below 60 pt), and their longest values end with an ellipsis. A number is never
  shortened.
- **A number column's heading aligns with its numbers:** at the column's right
  edge in English and its left edge in Arabic. A text column's heading starts
  where its values start.
- Rows are 16 pt; an Arabic report's lines get 1.6 × their size (Plex Arabic
  reaches higher and lower than Latin type).

**Fonts (Phase 39.7b: the brand's, replacing DejaVu Sans for Latin and Arabic).**

| Face | File | Used for | Licence (beside the files, copied to the build output) |
| --- | --- | --- | --- |
| Geist 1.900 Regular, SemiBold | `Reports/Rendering/Fonts/Geist/` (upstream TTF release v1.9.2) | Latin text and every digit, in both languages; SemiBold (600) for titles, headings and summary figures | SIL OFL 1.1, `Geist/OFL.txt` → `OFL-Geist.txt` |
| IBM Plex Sans Arabic 1.005 Regular, SemiBold | `Reports/Rendering/Fonts/IBMPlexSansArabic/` (IBM's TTF release `@ibm/plex-sans-arabic@1.1.0`) | Arabic text; SemiBold for titles and headings | SIL OFL 1.1 with the Reserved Font Name "Plex", `IBMPlexSansArabic/OFL.txt` → `OFL-IBMPlexSansArabic.txt` |
| DejaVu Sans 2.37 Regular, Bold | `Reports/Rendering/Fonts/` (Debian `fonts-dejavu-core` 2.37-8) | only scripts neither brand font draws: Greek, Cyrillic, Hebrew and the like | Bitstream Vera / Arev terms, `LICENSE-DejaVu.txt` |

- **Loading:** embedded as assembly resources; never read from the host and never
  fetched. A face no glyph on a page uses is not embedded in that file, so an
  English report carries only Geist and DejaVu appears only when a value needs it.
- **Why DejaVu stays:** Geist draws 5 of 144 Greek and 134 of 256 Cyrillic code
  points and no Hebrew; Plex Arabic draws none of them. Dropping DejaVu would turn
  such a customer's name into missing-glyph boxes, which the 37.7 files drew.
- **Arabic shaping:** checked letter by letter at 300 dpi against the DejaVu files
  (joining, lam-alef, shadda, damma and tanween, ISO dates and Latin inside
  Arabic). Plex Arabic also forms its own ligatures (sheen-reh, the stacked «في»
  and «تي»), as it does in the app.
- The fonts are the same families and weights the app self-hosts in
  `frontend/public/fonts/` (39.2a), as whole TTFs: the PDF needs full glyph sets,
  not the web's `unicode-range` subsets.
- No CJK or other large font families are bundled.

**Libraries** (all MIT):
- `SkiaSharp` and `SkiaSharp.HarfBuzz` 4.152.1 (since 39.7b only its
  `ToHarfBuzzBlob` is used, to build a HarfBuzz font over a typeface's bytes
  exactly as its shaper did)
- `HarfBuzzSharp` 14.2.1.201
- the Linux native assets: `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.152.1
  and `HarfBuzzSharp.NativeAssets.Linux` 14.2.1.201
- `Unicode.Bidi` 0.3.18

Rejected:
- **QuestPDF:** its Community licence is revenue-capped.
- **iText:** AGPL.
- **PDFsharp:** no complex-script shaping.
- **PdfPig's writer:** no shaping engine.

**Text layer.** Skia names shaped Arabic glyphs by their Unicode presentation
forms, in visual order: for example U+FEE3 (meem initial) and U+FEFC (lam-alef
final), and Plex Arabic's sheen-reh ligature by U+FD0D. Copy and search work, and
the forms are compatibility-equivalent to the letters.

**Size.** Since 39.7b an English PDF is about 125 KB and an Arabic one about
350 KB for the fixture reports (DejaVu made every PDF about 0.75 MB). That is
well inside the 25 MiB bound.

### Report files in the reader's language (Phase 37.7, D-6)

Every file is written in English or Arabic, chosen when the report is asked for
and kept on the execution (`report_executions.language`, M9).

**Which language.**

| Report | Language |
| --- | --- |
| Asked for in the app or the API | The request's optional `language` (`"en"`/`"ar"`); absent or `null`: the requester's stored `users.language` (37.3); unset: `en` |
| A scheduled occurrence | Its schedule's `language` (`report_schedules.language`, M10). A schedule takes its creator's language when it is created unless the request names one; only an edit that names a language changes it. Neither the creator changing their own language nor another member saving or switching the schedule changes it |
| Made before 37.7 | `en` (the column default), which is what it was |

A `language` other than exactly `en` or `ar` is `400 language.unsupported`,
checked before membership. A repeated `clientRequestId` that names a different
language is the existing `409` (it asks for a different report); one that names
none returns the report already settled. The download's file name is the same
server-built ASCII name in both languages.

**What changes, and what does not.** `ReportWording` (Application) holds every
phrase the platform writes into a file, keyed by its English, with its Arabic
written to `frontend/src/locale/GLOSSARY.md` and the words the Arabic screens
use. In English it returns the key unchanged, so **an English file is
byte-for-byte what it was before 37.7**: `ReportGoldenFileTests` compares all 24
type × format files with golden files captured from the 37.6 code before any
change (CSV byte for byte; workbooks part by part, with the SDK's random
relationship ids numbered; PDFs byte for byte except the clock-derived `/ID`, with
the two embedded fonts compared by SHA-256). In Phase 39.7b the eight PDF golden
files were captured again, through the test's own write mode, after the old and
new pages were compared as images (the brand's fonts and the table layout); the
CSV and workbook golden files came out of the same run unchanged.

| Translated | Unchanged |
| --- | --- |
| Title (the Arabic names the report e-mails already use, `NotificationCatalog.ReportName`), labels, column headings, sheet names, notes, the PDF footer | Every number, date and instant, written the same way (Western digits, Gregorian calendar, ISO dates: localisation decision 3) |
| The platform's own cell values: channel names (`دردشة الموقع`, `واتساب`), operations, outcomes, delivery states, "handled by", and the labels for customers nobody named (`عميل بلا اسم`, `عميل واتساب`, `زائر مجهول`) | Stored data: customer and agent names, providers, models, currency codes, ids |
| Zone labels: `بالتوقيت العالمي` for UTC, `بتوقيت Asia/Riyadh` otherwise (the IANA name as spelt) | Number formats and cell styles in the workbook; CSV quoting, escaping and the formula-prefix defence |

**Per format.**
- **CSV:** Arabic headers; UTF-8 with a byte-order mark, as every CSV already
  was; values written exactly as in English. Channel lists use the Arabic comma.
- **Excel:** every sheet's view carries `rightToLeft="1"`, so column A is on the
  right; Arabic sheet names (`الملخص`, then each table's) and headers; the same
  stylesheet and the same cell types and styles as the English workbook. An
  English workbook carries no direction attribute at all.
- **PDF:** the page is mirrored. Title, headings, notes and labels start at the
  right margin; each label's value sits to its left; table columns run from the
  right; text cells start at a column's right edge and numbers end at its left
  edge; the footer has the title on the right and "صفحة n من m" on the left. The
  report's own words are drawn as right-to-left paragraphs (`PdfText` with
  `rightToLeft`); every value keeps the direction of its first strong character,
  as an isolate does, so a Latin name, an e-mail address, an id or a number keeps
  its own order inside the Arabic page. The same HarfBuzz + `Unicode.Bidi`
  pipeline shapes the Arabic; since 39.7b in IBM Plex Sans Arabic (in 37.7 it was
  the bundled DejaVu font).

**Dates inside Arabic sentences.** UAX #9 rule W2 turns digits that follow Arabic
letters into Arabic numbers, and the hyphens of an ISO date then split it, so
`2026-10-01` would read `01-10-2026` (in the PDF, in Excel and in a browser).
`ReportWording` writes every date inside an Arabic sentence between two
LEFT-TO-RIGHT MARKs (U+200E), which are invisible and keep the date in order in
every viewer. Dates in their own cell or value need nothing.

**Tests.** `ReportWordingTests` (English identity, Arabic completeness, glossary
terms, zones, digits), `ArabicReportFileTests` (every type, read back: CSV header
and encoding; Excel direction, sheet names, headers and unchanged formats; PDF
text order and page direction), `ReportGoldenFileTests`, the generation and
schedule tests against PostgreSQL, and `ReportLanguageApiTests`.

## 8. Report bounds

| Bound | Default | Setting |
| --- | --- | --- |
| Period | 1–366 whole UTC days, not before 2020-01-01, not after tomorrow | fixed |
| Table rows, CSV/XLSX | 50,000 | `Reports:MaxRows` |
| Table rows, PDF | 2,000 | `Reports:MaxPdfRows` |
| File size | 25 MiB | `Reports:MaxArtifactBytes` |
| Retention | 30 days | `Reports:RetentionDays` |
| Schedules per workspace | 25 | `Reports:MaxSchedulesPerWorkspace` |

Exceeding a bound fails the execution with `report_too_large` — "Report exceeds
the supported export size." Nothing is silently truncated: breakdowns are read
one past the bound so "exactly at" and "over" are distinguishable.

## 9. Determinism and time

Whole local days of the workspace's zone, half-open `[from, to)` — the Phase 22
rule, in the zone the dashboard uses (`UTC` unless chosen, Phase 37.6). Presets
7/30/90 cover the same days as the dashboard presets and end at the start of the
local tomorrow; an explicit period's dates are local dates, and "the future" is
judged by the local today.

**The zone is kept on the execution (Phase 37.6, migration
`AddReportExecutionTimeZone`, M8).** `report_executions.time_zone varchar(64) NOT
NULL DEFAULT 'UTC'`: an execution is computed, claimed, generated and listed in the
zone it was asked for, even if the workspace's zone changes before the worker
reaches it. Executions made before 37.6 read `UTC`, which is what they were. The
execution response gains `timeZone`, the zone `periodFrom`, `periodTo` and
`periodLastDay` are local dates of. A repeated request with the same key and the
same dates in a different zone is a different report (`409`).

**So is the language (Phase 37.7, M9).** Execution responses (the request's
`202`, the list, one execution, a schedule's `lastExecution`) gain `language`
(`"en"`/`"ar"`), and schedule responses gain the schedule's `language` (M10). See
§7, "Report files in the reader's language".

**Labels.** The period note reads *"in {zone} whole days"*, day columns are headed
`Date ({zone})`, and the PDF and Excel period lines name the zone. In a UTC
workspace every label is exactly what it was. Instants (generated at, first and
last activity) stay in UTC and say so. Ordering:
days ascending; channels in fixed order (Website and WhatsApp always listed);
agents by conversations, customer messages, name, id; models by calls then
provider/model/operation; customers by last activity then id; currencies
alphabetically. Snapshot figures (open conversations by handling) are labelled
"current snapshot".

## 10. Provider cost semantics

Always "Estimated provider cost". Each currency has its own total; nothing is
converted or added across currencies. A model rated in more than one currency
shows no single figure. Unrated calls are reported, never shown as free. The
notes say it is internal accounting, not billing.

## 11. Schedules

`report_schedules`: name, type, format, Weekly|Monthly, enabled, `next_run_at`,
owner, created/updated, `disabled_reason`, `xmin` version.

- **Periods from boundaries.** Weekly is due at the start of Monday and covers the
  seven days before it; Monthly is due at the start of the 1st and covers the
  previous calendar month. Both are local days of the workspace's zone (Phase 37.6;
  UTC unless chosen), counted in dates: a week that contains a clock change is 167
  or 169 hours and still ends at Monday's start. The period is derived from the
  latest boundary ≤ now, never from when the worker ran — no partial or future
  period is possible. The occurrence keeps the zone it was queued in.
- **A change of the workspace zone (Phase 37.6).** In the same transaction as the
  change, every schedule whose next run is still to come is moved to the new zone's
  next boundary (Riyadh's Monday is three hours before UTC's). A schedule already
  due keeps its time and runs at the worker's next pass, covering the period that
  ended at the new zone's latest boundary, so no period is skipped. Moving the next
  run changes neither the owner nor the last-edited time, but it is a version
  change, like the worker's. One consequence is accepted and visible: the first
  report after a change can overlap the previous one by the difference between the
  two zones' offsets, and each says its own zone.
- **Occurrence identity.** Unique `(schedule_id, period_from)`; the insert is
  `ON CONFLICT DO NOTHING`. A crash, a replay or two instances produce one
  execution. Missed occurrences are not replayed one by one: the latest completed
  period is generated once.
- **Due claiming.** `SELECT … FOR UPDATE SKIP LOCKED` inside one transaction that
  also inserts the occurrence and advances `next_run_at`.
- **Authority.** Saving a schedule makes the editor its owner and requires
  `reports.schedule` + `reports.generate` + the data permission. Every occurrence
  re-reads the owner's membership and roles. If they no longer qualify, the
  schedule is switched off with `owner_lost_permission` and nothing is generated.
  Switching off needs only `reports.schedule`; switching on transfers ownership and
  needs everything.
- **Concurrency.** `ETag` / `If-Match` on update and on enable/disable: missing →
  428, stale → 412, no automatic retry. The worker advancing `next_run_at` is a
  version change too.
- **Delivery.** Into Reports only. No recipients field exists.
- **Language (Phase 37.7).** `language` (`en`/`ar`): the creator's when created,
  unless the create names one; kept until an edit names another. Every occurrence
  is queued with it (the occurrence insert writes it) and generated in it. See §7.

## 12. Retention

`ReportRetentionWorker` (hourly, `TimeProvider`) lists artifacts with
`expires_at <= now`, deletes the bytes first, then deletes the artifact row and
marks the execution `Expired` in one transaction. Both steps are idempotent, so
two instances sweeping together is harmless. It also deletes `.partial` files
older than a day left by crashed processes. Nothing still being generated has an
artifact row, so nothing in progress can be touched.

## 13. Schema (migration `AddReportsExportsAndScheduling`)

`report_executions`, `report_artifacts`, `report_schedules`. Composite
`(tenant_id, id)` foreign keys keep occurrences and artifacts in their own
workspace. Partial unique indexes carry both idempotency keys; a partial index on
`status IN ('Pending','Running')` serves the claim. No blob, JSON or text columns;
the row version is PostgreSQL's system `xmin`, not a physical column.

Phase 37.6 added `report_executions.time_zone` (M8). Phase 37.7 added
`report_executions.language` (M9, `AddReportExecutionLanguage`) and
`report_schedules.language` (M10, `AddReportScheduleLanguage`): each
`varchar(8) NOT NULL DEFAULT 'en'`, a model default, so every earlier row reads
English without a data step. No index: the column is only ever read with its row.

## 14. UI

`/reports` (lazy): generate form (report, period preset or dates, format),
generated-reports table with textual statuses, failure reasons, sizes and API
download links named in full. Only the report just requested is polled — every
2.5 s, at most 2 minutes, stopping on a terminal state; everything else waits for
Refresh. `/reports/schedules`: create, edit (with the version read), switch on and
off, next run, last report, the platform's reason for switching one off, and a
plain statement that reports are available in Reports and nothing is emailed.
