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
| PDF | SkiaSharp PDF backend + HarfBuzz + Unicode.Bidi | A4 landscape, title, workspace label (only if the requester holds `workspace.view`), UTC period, generated time, summary, tables, notes, "Page n of m". Bundled font embedded; no fetched font, image, link, annotation, JavaScript, form or open action. |

### Unicode PDF text

Every PDF line goes through `PdfText`:

1. **Clean.** Control characters become spaces. Explicit bidi embeddings,
   overrides and isolates (U+202A-U+202E, U+2066-U+2069) are removed, so user
   text cannot reorder its neighbours ("Trojan Source").
2. **Bidi.** `Unicode.Bidi` (a port of the Rust `unicode-bidi` crate: UAX #9,
   Unicode 16) gives the paragraph direction and the line's visual runs with
   their embedding levels.
3. **Shape.** HarfBuzz (`SkiaSharp.HarfBuzz`) shapes each run in the direction its
   level dictates, never guessed from the script, so digits and punctuation inside
   Arabic go the right way. Joining, the lam-alef ligature, marks and kerning come
   from the font's OpenType tables. Nothing reverses a string, and no letter form
   is chosen in code.
4. **Draw.** Runs are placed in visual order as positioned glyph runs that also
   carry their UTF-8 source and cluster map, so the PDF text layer is real text.

Alignment:
- A table cell whose text is right-to-left aligns to the cell's right edge.
- Numbers align right, as before.
- A label's value starts at its own value column.

**Font.** DejaVu Sans and DejaVu Sans Bold 2.37, taken from the Debian/Ubuntu
`fonts-dejavu-core` 2.37-8 package.
- **Loading:** embedded as assembly resources; never read from the host and never
  fetched.
- **Coverage:** Latin, Latin accents and Arabic, with the Arabic GSUB features
  `init`, `medi`, `fina`, `rlig` and `liga`.
- **Licence:** Bitstream Vera / Arev terms, which allow use, embedding and
  redistribution, including commercially. The notices are kept in
  `Reports/Rendering/Fonts/LICENSE-DejaVu.txt`, which is copied to the build
  output.
- No CJK or other large font families are bundled.

**Libraries** (all MIT):
- `SkiaSharp` and `SkiaSharp.HarfBuzz` 4.152.1
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
final). Copy and search work, and the forms are compatibility-equivalent to the
letters.

**Size.** The Linux Skia build embeds the fonts whole rather than as a subset, so
a PDF is about 0.75 MB before its data. That is well inside the 25 MiB bound.

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

UTC whole days, half-open `[from, to)` — the Phase 22 rule. Presets 7/30/90 cover
the same days as the dashboard presets and end at tomorrow's midnight. Ordering:
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

- **Periods from boundaries.** Weekly is due Monday 00:00 UTC and covers the seven
  days before it; Monthly is due on the 1st 00:00 UTC and covers the previous
  calendar month. The period is derived from the latest boundary ≤ now, never from
  when the worker ran — no partial or future period is possible.
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

## 14. UI

`/reports` (lazy): generate form (report, period preset or dates, format),
generated-reports table with textual statuses, failure reasons, sizes and API
download links named in full. Only the report just requested is polled — every
2.5 s, at most 2 minutes, stopping on a terminal state; everything else waits for
Refresh. `/reports/schedules`: create, edit (with the version read), switch on and
off, next run, last report, the platform's reason for switching one off, and a
plain statement that reports are available in Reports and nothing is emailed.
