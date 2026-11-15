# Phase 39.7b: the report files (backend)

Part b of Phase 39.7, as the approved plan splits it (`PHASE_39_0_REDESIGN_PLAN_REPORT.md` §3.4 note 2, §3.6, §12): the PDF report renderer in `backend/`. Nothing under `frontend/` or `deploy/` changed. Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-7b\`.

## 1. Summary

- Report PDFs are now set in the product's own type: Geist for English text and every figure, IBM Plex Sans Arabic for Arabic, titles and headings in each family's 600 weight, figures tabular so numbers line up. Both fonts are embedded, with their OFL licences beside them.
- Column headings wrap onto two lines instead of being cut off with "…"; a number column's heading now sits over its numbers (at the right in English, at the left in Arabic).
- Tables are as wide as their content and start at the reader's margin, instead of being stretched across the page with the spare width spread over every column. Customer ids, names and channel lists that were cut before are now whole (only the widest English table still shortens its id column).
- Arabic shaping in Plex Arabic was checked letter by letter and is correct, so Arabic moved off DejaVu. Files are smaller: an English PDF is about 125 KB instead of 755 KB.
- Excel and CSV files are unchanged, byte for byte (CSV) and part by part (Excel), in both languages.

## 2. Starting point

- Branch `master`, HEAD `3e5f5a5` (Phase 39.7a), worktree clean (`git status --short` empty), checked at the start.
- The dev stack was up (API `omniai-p36-api` with the AI stand-in, Mailpit, dev server on :4200).
- Skills loaded (rule 16): `frontend-pro-max:dotnet-backend-architect` (the renderer is .NET). The four skills the prompt names (`enterprise-erp-ux`, `ui-ux-design-lead`, `angular-enterprise`, `web-performance`) are for the screens of 39.7a; this part has no screen, Angular code or web asset, so they were not loaded, and their rules had nothing to apply to.
- The run was unattended (`claude -p`). No question was needed.

## 3. Changes by area

`git status --short` at the end:

```
 M backend/src/OmniAI.Infrastructure/OmniAI.Infrastructure.csproj
 M backend/src/OmniAI.Infrastructure/Reports/Rendering/PdfReportRenderer.cs
 M backend/src/OmniAI.Infrastructure/Reports/Rendering/PdfText.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/agent-activity.pdf.canonical
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/ai-human-handling.pdf.canonical
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/channel-usage.pdf.canonical
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/conversation-activity.pdf.canonical
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/customer-activity.pdf.canonical
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/provider-cost.pdf.canonical
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/provider-usage.pdf.canonical
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/whatsapp-delivery.pdf.canonical
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/PdfUnicodeTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/ReportGoldenFileTests.cs
 M docs/architecture/reports-exports-scheduling.md
?? backend/src/OmniAI.Infrastructure/Reports/Rendering/Fonts/Geist/
?? backend/src/OmniAI.Infrastructure/Reports/Rendering/Fonts/IBMPlexSansArabic/
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/PdfTypeAndTableTests.cs
?? docs/phase-reports/PHASE_39_7B_REPORT_FILES_REPORT.md
```

### 3.1 The fonts (`backend/src/OmniAI.Infrastructure/Reports/Rendering/Fonts/`)

| Face | Files | Source | Size | Licence |
| --- | --- | --- | --- | --- |
| Geist 1.900 | `Geist/Geist-Regular.ttf`, `Geist/Geist-SemiBold.ttf` | upstream release `vercel/geist-font` v1.9.2 (`geist-font-v1.9.2.zip`, SHA-256 `15f066d5…95d3d8`), the static TTFs | 126,048 + 127,872 B | `Geist/OFL.txt` (SIL OFL 1.1, from the same release) |
| IBM Plex Sans Arabic 1.005 | `IBMPlexSansArabic/IBMPlexSansArabic-Regular.ttf`, `…-SemiBold.ttf` | IBM's release `@ibm/plex-sans-arabic@1.1.0` (`ibm-plex-sans-arabic.zip`, SHA-256 `f0391558…714013`), `fonts/complete/ttf` | 236,708 + 245,544 B | `IBMPlexSansArabic/OFL.txt` (SIL OFL 1.1, Reserved Font Name "Plex", the release's `LICENSE.txt`) |
| DejaVu Sans 2.37 (kept) | `DejaVuSans.ttf`, `DejaVuSans-Bold.ttf` | unchanged since 37.7 | unchanged | `LICENSE-DejaVu.txt` (unchanged) |

- The frontend's woff2 files could not be reused: they are `unicode-range` subsets (Geist's is the variable font), and a PDF needs full static glyph sets. The TTF releases of the same families are what the prompt allows; they are files, not packages. Hashes of the four TTFs and two licences: `p39-7b\font-hashes.txt`.
- `OmniAI.Infrastructure.csproj` embeds the four TTFs as resources (`OmniAI.Reports.Fonts.<file>`) and copies the two OFL texts to the build output as `Reports/Rendering/Fonts/OFL-Geist.txt` and `OFL-IBMPlexSansArabic.txt`, beside `LICENSE-DejaVu.txt`.
- **Why DejaVu stays, as the last resort only.** Measured with fontTools (`scratch\coverage.py`): Geist draws 5 of 144 Greek and 134 of 256 Cyrillic code points and no Hebrew; Plex Arabic draws none of them. Without DejaVu a customer named «Ελένη» or «Алексей» would print as missing-glyph boxes, which the 37.7 files drew correctly. A face is embedded only in a file that uses it: an English file carries Geist only, an Arabic file Plex Arabic and Geist, and DejaVu appears only when a value needs it (tests below).

### 3.2 `PdfText.cs` (text engine)

- **Font per script.** After the bidi algorithm splits a line into runs, each run is split by script: Arabic letters → Plex Arabic; Latin letters and every digit → Geist (the plan's "digits in Geist in both languages"); letters of any other script → DejaVu. Spaces, punctuation, symbols and marks take the family of the letter before them (or after, at a run's start), so a word or a phrase is one piece in one face and is shaped as a whole. A character a face lacks goes to the next face that has it, so nothing the bundled fonts can draw becomes a box. Pieces of a right-to-left run are placed last first.
- **Tabular figures.** HarfBuzz is now called directly through `HarfBuzzSharp` (already a direct package reference) with the `tnum` feature, because Skia's `SKShaper` takes no features. Geist's figures are proportional by default (advances 384–663 units); with `tnum` every figure is the same width, so numbers line up in their columns, as the app does with `tabular-nums`. Plex Arabic's figures are already tabular.
- The HarfBuzz font is built exactly as `SKShaper` built it (the typeface's own stream through `ToHarfBuzzBlob`), one per `PdfText`, disposed with it. A first version that built the HarfBuzz face from a copied blob and the typeface from a disposed `SKData` crashed the test host after a few renders and garbled the text layer; it never left the scratch runs (section 4).
- `Wrap` now fits every line it returns, not only the last one, and `WidestWord` gives the narrowest box a heading can wrap into uncut.
- The text layer, `Clean` (control characters and bidi overrides removed) and the visual-order cluster maps are unchanged.

### 3.3 `PdfReportRenderer.cs` (layout)

- **Columns sized by content.** A column wants its widest value (of the first 200 rows) or its heading on one line, plus 5 pt padding on each side. The table is as wide as its columns and starts at the reader's margin (left in English, right in Arabic); its rule and row shading are as wide as the table. Before, the spare width was spread equally over every column, so a two-column table spanned the page with its numbers far from their labels.
- **Headings wrap, never cut.** When the columns want more than the page, each column keeps at least its widest value and its heading's longest word; the width left over goes to the headings that still want it, in proportion; and headings wrap at their spaces, bottom-aligned on a shared last line. The title, the heading block and the first row are kept together on one page; the heading block repeats on every page a table continues on, as before.
- **Only when even that does not fit** (the English customer-activity table) do text columns give up width, by a water level: the narrow ones keep their whole width and only the widest is shortened (never below 60 pt), with an ellipsis. A number is never shortened. In the fixture this shortens only the customer id; 37.7 shortened the id, two names and two channel lists.
- **A number column's heading aligns with its numbers**: at the column's right edge in English, its left edge in Arabic. A text column's heading starts where its values start (left in English, right in Arabic).
- **Row and line heights.** Rows are 16 pt (15 before) with the text 1 pt lower, and an Arabic report's lines get 1.6 × their size, because Plex Arabic is 1.5 em tall (Geist 1.3 em, DejaVu 1.16 em).
- **Unchanged:** page size and margins, the colours, the mirrored Arabic layout, the label/value pairs, the footer, the metadata (`Creator` "Nabra reports", `Producer` "Nabra"), and nothing active or remote in the file.
- **No mark.** The renderer has never drawn a product mark or name on the page (only the metadata names "Nabra"), so there was nothing to replace with the two-circle mark (the operator's note: "if the renderer draws a product mark or name today"). See section 4.

### 3.4 Tests

| File | Change |
| --- | --- |
| `PdfTypeAndTableTests.cs` (new, 15 tests) | Arabic in Plex Arabic and Latin and digits in Geist (regular and 600); a phrase stays one piece in one face; Greek, Cyrillic and Hebrew names set whole in DejaVu with no missing glyph; tabular figures; an English file embeds only Geist; an Arabic file embeds Plex Arabic and Geist and DejaVu only when a value needs it; both OFL files copied to the build output; a heading too long for its column wraps and is never cut (English and Arabic, read back with PdfPig, every value whole); a number column's heading ends where its numbers end in English and starts where they start in Arabic, a text column's heading lines up with its values; a small table stays at its reader's half of the page |
| `PdfUnicodeTests.cs` | glyph comparisons against the face each script now uses; `شركة` is read with Plex Arabic's sheen-reh ligature (U+FD0D, the same two letters under NFKC) |
| `ReportGoldenFileTests.cs` | remarks record the 39.7b recapture and the new font sizes; logic unchanged |
| `Golden/en/*.pdf.canonical` (8) | recaptured through the test's own write mode (`OMNIAI_WRITE_REPORT_GOLDEN=1`) after the old and new pages were compared as images; the same run rewrote the 8 CSV and 8 workbook golden files and they came out byte-identical (git shows no change) |

### 3.5 Docs

`docs/architecture/reports-exports-scheduling.md`: the pipeline (font per script, `tnum`), the new **Tables** rules, the fonts table with sources, licences and why DejaVu stays, the text-layer ligature, file sizes, and the golden recapture.

### 3.6 Before and after

Every page of every report type in both languages, rendered by poppler at 110 dpi from the fixture files (`files\before\` from HEAD `3e5f5a5`, `files\after\` from this step):

- `shots\report-pdf-<kind>-p<page>-a4landscape-<en|ar>-light-<before|after>.png` (38 pages: 18 before, 20 after);
- `pdf-images\compare\<lang>-<kind>-<page>.png`: before and after side by side (20 sheets);
- `pdf-images\zoom\`: 300 dpi crops of the Arabic title block, notes and table headings, before and after, for the letter-by-letter check;
- `pdf-images\<before|after>\*.fonts.txt`: `pdffonts` for every file.

| Screen | What changed | Components / tokens | Tests |
| --- | --- | --- | --- |
| Report PDF, English (8 kinds) | Geist 400/600, tabular figures, tables by content, wrapping headings, numeric headings over their numbers | `PdfText`, `PdfReportRenderer`; no tokens (backend) | `PdfTypeAndTableTests`, `PdfUnicodeTests`, `ReportRenderingTests`, golden files |
| Report PDF, Arabic (8 kinds) | Plex Arabic 400/600 with Geist figures, the same table rules mirrored (numeric headings at the left edge), taller lines | same | `PdfTypeAndTableTests`, `ArabicReportFileTests`, `PdfUnicodeTests` |
| Report Excel and CSV, both languages | nothing | — | golden files, `ArabicReportFileTests`, `p39-7b\scratch\xlsx_same.py` |

## 4. Decisions

- **Plex Arabic for Arabic (plan risk 4 closed).** One Arabic file of each kind was rendered and the customer-activity page compared with the DejaVu one at 300 dpi: joined initial/medial/final forms, lam-alef, shadda, damma and tanween, «إلى», «إلكتروني», ISO dates kept in order inside sentences (the 37.7 LRM marks), Latin names, e-mail addresses and ids isolated, Western digits. Nothing is shaped worse than DejaVu did; Plex is the font the app uses. Plex also forms its own ligatures (sheen-reh «شر», the stacked «في» and «تي»), exactly as the browser draws them in the app.
- **SemiBold (600), not Bold, for titles and headings,** as the specification's title weights (600) and the app's headings. Before, DejaVu Bold.
- **DejaVu kept as the last resort** (3.1), instead of removing it: removing it would regress Greek, Cyrillic and Hebrew names to boxes. It is the plan's "replacing DejaVu Sans" for Latin and Arabic, with a fallback the plan did not foresee.
- **Tables are not stretched to the page width.** Alternative rejected: giving the spare width to text columns only, which still pushed numbers away from their headings in two-column tables. A table at its natural width reads like the app's calm panels and keeps each number beside its label.
- **Water level, not proportional shrinking,** when a table cannot fit: proportional shrinking cut the short "Customer" and "Channels" values too.
- **Text values still end with an ellipsis** when a table cannot fit, rather than wrapping onto two lines: the plan and the prompt ask for wrapping headings, and wrapping every value would change every row's height. Only the English customer-activity table's id column is affected in the fixture.
- **HarfBuzz called directly** instead of `SKShaper`, to pass `tnum`. No package was added, upgraded or removed: `HarfBuzzSharp` was already a direct reference. `SkiaSharp.HarfBuzz` is now used only for `ToHarfBuzzBlob`; removing it is not proposed.
- **The first version crashed, and was fixed before anything else ran.** It built the HarfBuzz face from `Blob.FromStream` over a byte copy and the Skia typeface from an `SKData` it disposed. Rendering one file worked; several in one process crashed the test host (three `--blame-crash` dumps) or garbled the text layer ("Customectivity"). Building both exactly as 37.7 and `SKShaper` did (typeface from the resource stream, HarfBuzz font from the typeface's own stream) removed it; 77 PDF tests and the whole suite then passed. The crash dumps (in the ignored `TestResults\`) were deleted.
- **No mark in the files.** The renderer draws no mark or product name today, so there was nothing to replace. Adding one (an image or vector in the header) would be new content, which this step's items do not list; `brand.md`'s line "report files" is therefore left for the owner (section 9).
- **Time zones keep the IANA id** in the files, as plan §3.4 note 8 says ("report files keep the id (backend, unchanged)").
- **The page's colours are unchanged** (ink `#1F232B`, quiet grey, rule grey, shade `#F4F5F7`): the items name the fonts and the tables, not colours.
- **Golden files.** Regenerated only through the test's own write mode, after comparing old and new pages as images (3.6); the comparison run right after passed 48 of 48.
- **Departures from the specification or a board:** none on screen. In the PDF, headings use 9.5 pt semibold, not the specification's screen scale; a printed A4 table has no counterpart in `calm-glass.md`.

## 5. Verification

| Check | Result |
| --- | --- |
| Backend restore with the NuGet audit on (`--force --no-http-cache`) | **PASS**: restore exit 0, no NU19xx |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: no vulnerable packages in any of the 8 projects. No package was added, upgraded or removed |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: 0 warnings, 0 errors |
| `dotnet test` (`-m:1`, `mcr.microsoft.com/dotnet/sdk:10.0`, throwaway `pgvector/pgvector:pg17` and `redis:7-alpine` on a private network, all removed; 0 leftover test databases) | **PASS**: Api.IntegrationTests 1,192 passed, 2 skipped; Application.Tests 921; Domain.Tests 939; Infrastructure.IntegrationTests 1,325 passed, 1 skipped. **4,377 passed, 3 skipped, 0 failed** (39.1 Revision 1 reported 4,362; this step adds the 15 test cases of `PdfTypeAndTableTests` (10 facts, two theories with 3 and 2 cases), and 4,362 + 15 = 4,377). Log: `gates\backend-gates-run1.log`, `gates\run1\test.log` |
| `PublicNameTests` | **PASS** (3 tests, in the run above). Its scan covers the backend's text files, including the two new OFL texts; no file a user reads says the code name. The PDF tests also check the metadata ("Nabra reports" / "Nabra") and that "OmniAI" appears nowhere in a file |
| PDF tests, focused (`PdfUnicodeTests`, `ArabicReportFileTests`, `ReportRenderingTests`, `PdfTypeAndTableTests`) | **PASS**: 77 of 77 (`logs\pdf5.log`) |
| Golden files | **PASS**: recaptured with the test's own write mode (`logs\golden-write.log`), then compared: 48 of 48 (`logs\golden-compare.log`). Git shows only the 8 `.pdf.canonical` files changed; the 8 CSV and 8 workbook golden files were rewritten identical |
| Excel and CSV keep their structure, both languages | **PASS**: the 16 CSVs (8 kinds × en/ar) are byte-identical before and after; the 16 workbooks are identical part by part, relationship ids numbered and the core times set aside (`scratch\xlsx_same.py`) |
| One PDF of each kind in each language, opened and checked (fixture) | **PASS**: 16 files rendered page by page with poppler (`pdf-images\after\`), fonts listed with `pdffonts` (English: Geist Regular and SemiBold, plus Plex Arabic only in files with an Arabic value, such as the fixture customer named in Arabic; Arabic: Plex Arabic Regular and SemiBold plus Geist; DejaVu in none), headings, alignment and wrapping looked at page by page; before/after sheets in `pdf-images\compare\` |
| Arabic shaping (plan risk 4) | **PASS**: 300 dpi crops of the title block, notes and table headings, before (DejaVu) and after (Plex), compared letter by letter (`pdf-images\zoom\`): joined forms, lam-alef, marks, dates in order, Latin isolated, Western digits. Plex is kept for Arabic |
| Dev API rebuilt (`p36-stack.sh build`, then `api`) | **PASS**: exit 0; stand-in on, fallback plan free; the API answers ready; `OFL-Geist.txt`, `OFL-IBMPlexSansArabic.txt` and `LICENSE-DejaVu.txt` are in `/app/Reports/Rendering/Fonts/` (`gates\dev-build.log`, `dev-api.log`) |
| Provider canary | **PASS** right after the API was recreated, before any traffic: every provider variable absent, SMTP host loopback, the stand-in listed as info (`gates\canary-after-rebuild.txt`) |
| Live PDFs from the rebuilt API (`scratch\live-reports.mjs`) | **PASS**: 16 of 16. For each of the 8 kinds in the catalogue, a PDF asked for in English and in Arabic for Northwind (last 30 days) completed and downloaded with its server name; English files carry Geist (and Plex Arabic where Northwind has an Arabic customer name), Arabic files Plex Arabic and Geist; 127–391 KB in English, 345–531 KB in Arabic. Pages rendered to `live\images\` and looked at, including the 38-page customer-activity file in both languages (the heading block repeats on every page; every id whole) |
| `npm run e2e` | **PASS**: 46 of 46 (23 journeys × en-ltr and ar-rtl), 0 failures, no retries, 3.7 min (21:06 to 21:10 UTC), one full run including the throttling journey. `arabic-report` downloads a real file through the browser in both projects. `NABRA_E2E_OUT` / `NABRA_E2E_STATE_DIR` under `p39-7b\e2e\` (the state folder is removed by the suite's teardown) |
| `npm run format:check`, `test:ci`, `build`, `i18n:check` | **NOT RUN**: no file under `frontend/` changed, so nothing they check changed (as in 39.1). 39.7a's results stand: 145 files / 2,215 tests, startup JS 148,821 B (ceiling 152,797), i18n 2,563 messages |
| Chrome matrix (widths, themes, languages, reduced transparency and motion, forced colours, overflow, console, `h1`, unnamed controls, keyboard) | **NOT RUN**: no screen changed. The changed output is the PDF page, checked as images above |
| Contrast of new colour pairs, RTL mirroring, touch targets | No new colour (the PDF's colours are unchanged). RTL mirroring of the PDF: **PASS** (`ArabicReportFileTests`, `PdfTypeAndTableTests`). Touch targets: not applicable |
| Fonts | PDF only: English files embed Geist Regular + SemiBold (whole TTFs, about 125 KB per file in total); Arabic files add Plex Arabic Regular + SemiBold (about 345 KB per file). The web fonts, their preloads and layout shift are untouched (39.2a) |
| Free memory before each heavy gate | backend run: 8.42 GB; e2e: 10.37 GB. Never under 6 GB, so the page cache was not dropped |
| Secret scan (`scratch\secret-scan.py`, file names only) | **PASS**: 143 files (this step's evidence and every changed repository file); the demo password matched nowhere. The other hits are the scan's own pattern list, a package name in the csproj (`Microsoft.AspNetCore.Identity…`), random bytes inside the Plex TTF, and the throwaway database's `Password=$PW` variable reference in the gate script |

## 6. See it live

- **Open:** http://localhost:4200/sign-in → Reports. Sign in as the demo **owner** (`p36.owner@omniai.example`; the password is in `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`), workspace **Northwind**.
- **Try:** create any report as **PDF** in **English** and again in **Arabic** (the form's language choice), then Download each when it is ready. Open them in a PDF reader:
  - the type is Geist (English) and IBM Plex Sans Arabic (Arabic); File → Properties → Fonts lists only those (and DejaVu only if a value is in Greek, Cyrillic or Hebrew);
  - in **Customer activity**, the long headings ("Conversations active", «آخر نشاط (بالتوقيت العالمي)») wrap onto two lines instead of ending in "…";
  - in **Estimated provider cost**, "Calls" sits right above its numbers in English, «الاستدعاءات» at the left edge above them in Arabic; the small "by currency" table is only as wide as its content;
  - Excel and CSV downloads are the same as before.
- The files the live check downloaded are in `p39-7b\live\files\` (16 PDFs, one per kind and language), and their pages in `p39-7b\live\images\`.

## 7. Backend asks

None. Everything here was inside the renderer.

## 8. New Arabic for my review

None. The renderer adds no words: every phrase in a file still comes from `ReportWording` (37.7), unchanged.

## 9. Risks and open issues

1. **Text values can still end with "…"** when a table cannot fit even with wrapped headings: in the fixture only the English customer-activity id column (the live Northwind data fits whole). Wrapping values onto two lines would fix it; it was left out because the items ask for wrapping headings, and it would change every row's height.
2. **DejaVu is still bundled** (about 1.4 MB in the assembly) as the last resort for Greek, Cyrillic, Hebrew and other scripts. It is embedded in a file only when a value needs it. Dropping it would print such names as boxes.
3. **Search and copy of Arabic ligatures.** Plex Arabic's own ligatures reach the text layer as compatibility characters (for example U+FD0D for «شر»), as DejaVu's presentation forms did. Readers that normalise (NFKC) find and copy the words; a reader that does not may not find «شركة» by search.
4. **No mark on the page.** `brand.md` lists report files among the places that show the mark, but the renderer has never drawn one, and these items do not ask for one. If the owner wants a mark (and the name) in the PDF header, that is a small follow-up in the renderer, not done here.
5. `SkiaSharp.HarfBuzz` is now used only for `ToHarfBuzzBlob`. Nothing to change now; noted for whoever next upgrades the PDF libraries.
6. Arabic lines are taller (1.6 × their size), so a long Arabic report may take a page more than before; the fixture files kept their page counts.

## 10. Proposed checkpoint commit message

```text
feat: set report PDFs in the brand's type with tables sized by their content (Geist for Latin text and every figure, IBM Plex Sans Arabic for Arabic, both embedded from their upstream TTF releases with their OFL licences beside them and 600 for titles and headings, figures tabular through HarfBuzz's tnum, DejaVu Sans kept only for scripts neither brand font draws, each table as wide as its content at its reader's margin, column headings that wrap instead of being cut, a number column's heading aligned with its numbers at the right in English and the left in Arabic, the widest text column alone shortened when a table cannot fit, taller Arabic lines), recapture the eight English PDF golden files through the test's write mode, keep Excel and CSV files unchanged, and document the fonts and table rules (Phase 39.7b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

Review 2026-11-16: accepted by the owner's assistant; the test count in 3.4 and 5 was corrected from 16 to 15.
