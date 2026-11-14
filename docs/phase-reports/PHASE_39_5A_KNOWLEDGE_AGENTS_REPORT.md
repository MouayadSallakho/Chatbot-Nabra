# Phase 39.5a: knowledge and AI agents (Calm Glass)

Step 39.5a of the approved plan (`PHASE_39_0_REDESIGN_PLAN_REPORT.md` §3.4 note 17, §3.6 row 39.5a, §12). Channels, the widget and the hosted page are 39.5b.

Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-5a\` (`shots\`, `chrome\`, `gates\`, `e2e\`, `scratch\`).

Skills loaded before any work: `frontend-pro-max:enterprise-erp-ux`, `frontend-pro-max:angular-enterprise` and `frontend-pro-max:ui-ux-design-lead`. The two skills for the widget and the hosted page's first impression and weight (`first-impression-experience`, `web-performance`) were not loaded: their subjects belong to 39.5b. This step measured the visitor pages' weight only because it changes their shared transcript. The 21st.dev MCP ("magic") failed to connect ("Not authenticated"), so nothing was sourced from it; everything is built from our own primitives, as plan §12 allows.

## 1. Summary

- **Knowledge.** The knowledge list and the agent list are now calm panels of rounded rows. Each document shows a glyph for its kind, and each agent the AI's orb (faded when the agent is not in service). States are chips with a word and a glyph: Indexed, Needs index, Indexing now, Indexing failed with its reason, Indexing stopped, and Disabled. The list no longer misses indexing that ends during its first load (note 17).
- **Editors and forms.** The editors, the create pages and the upload page are solid panels with soft tiles for facts, files and channels. Their skeletons have the loaded rows' shape. On phones the row chevron sits at the row's end.
- **Agent preview.** It speaks in the three voices, seen from the visitor's side: their own words in cream at the end, the AI in emerald beside its orb, a person in gold with a person mark and the word "Support". The orb breathes while the AI types. Because the transcript is shared, the widget and the hosted page show the same bubbles now; their header, greeting and composer stay for 39.5b.
- **Small fixes.** In Arabic, «صندوق المحادثات» stays on one line in the side bar beside its count. The page header's back link is a 44 px target on touch screens. Screen readers now hear a speaker and their words apart. The agent-create help list has its missing spaces back ("Create. The agent…").

## 2. Starting point

- Branch `master`, HEAD `0a125de` (Phase 39.4b), worktree clean (`git status --short` printed nothing).
- Dev stack running: the API (`omniai-p36-api`, `/health/ready` 200, AI stand-in on), Mailpit and the dev server on :4200. The provider canary passed before any traffic.

## 3. Changes by area

`git status --short` (38 files, +883/−188; no file under `backend/` or `deploy/`, no package change):

```text
 M docs/design/MASTER.md
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/features/ai-agents/agent-create/agent-create.html
 M frontend/src/app/features/ai-agents/agent-create/agent-create.spec.ts
 M frontend/src/app/features/ai-agents/agent-create/agent-create.ts
 M frontend/src/app/features/ai-agents/agent-editor/agent-editor.html
 M frontend/src/app/features/ai-agents/agent-editor/agent-editor.scss
 M frontend/src/app/features/ai-agents/agent-editor/agent-editor.ts
 M frontend/src/app/features/ai-agents/agent-editor/agent-preview.scss
 M frontend/src/app/features/ai-agents/agent-editor/agent-preview.spec.ts
 M frontend/src/app/features/ai-agents/agent-list/agent-list.html
 M frontend/src/app/features/ai-agents/agent-list/agent-list.scss
 M frontend/src/app/features/ai-agents/agent-list/agent-list.spec.ts
 M frontend/src/app/features/ai-agents/agent-list/agent-list.ts
 M frontend/src/app/features/ai-agents/agent-words.spec.ts
 M frontend/src/app/features/ai-agents/agent-words.ts
 M frontend/src/app/features/chat/hosted-chat/hosted-chat.spec.ts
 M frontend/src/app/features/knowledge/agent-knowledge/agent-knowledge.html
 M frontend/src/app/features/knowledge/agent-knowledge/agent-knowledge.ts
 M frontend/src/app/features/knowledge/knowledge-create/knowledge-create.html
 M frontend/src/app/features/knowledge/knowledge-editor/knowledge-editor.html
 M frontend/src/app/features/knowledge/knowledge-editor/knowledge-editor.ts
 M frontend/src/app/features/knowledge/knowledge-file.spec.ts
 M frontend/src/app/features/knowledge/knowledge-list/knowledge-list.html
 M frontend/src/app/features/knowledge/knowledge-list/knowledge-list.scss
 M frontend/src/app/features/knowledge/knowledge-list/knowledge-list.spec.ts
 M frontend/src/app/features/knowledge/knowledge-list/knowledge-list.ts
 M frontend/src/app/features/knowledge/knowledge-upload/knowledge-upload.html
 M frontend/src/app/features/knowledge/knowledge-words.ts
 M frontend/src/app/layout/sidebar/sidebar.scss
 M frontend/src/app/shared/chat/chat-panel.spec.ts
 M frontend/src/app/shared/chat/chat-transcript.html
 M frontend/src/app/shared/chat/chat-transcript.scss
 M frontend/src/app/shared/chat/chat-transcript.ts
 M frontend/src/app/shared/chip/chip.spec.ts
 M frontend/src/app/shared/chip/chip.ts
 M frontend/src/app/shared/page-header/page-header.scss
 M frontend/src/styles/_authoring.scss
```

### 3.1 What changed and why

- **`.page-calm` (styles/_authoring.scss).** The knowledge and agent pages carry this modifier on `.page`. Under it:
  - cards become solid panels (radius 24, `--color-panel`, a 1 px line, the panel's 20/24 px padding, no rule under the header, no fill behind the footer actions);
  - section numbers become soft tiles and guidance becomes a soft tile;
  - a data list is one panel of radius-16 rows, 1 px apart, with a hover tint, a pressed selected tint and the column head in the quiet caption;
  - facts become soft tiles;
  - on wide grids a long title takes three lines at most;
  - on phones the chevron sits at the row's end, level with its middle (it used to sit on a line of its own).

  The `.card` and `.data-list` primitives stay untouched for the screens later steps redesign; 39.8b makes the modifier the default.
- **Record glyphs.** A 36 px soft tile at the start of each title cell holds the document's kind (written text, a table for CSV, a file; `knowledgeGlyph`) or the agent's orb. Agents not in service get a resting, faded orb (`.is-resting`). Both are `aria-hidden`: the Type column and the status chip say the same in words.
- **States as chips.**
  - `knowledgeStateTone` and `agentStatusTone` now return `ChipTone`, and every state badge on these screens became an `app-chip`: list, editor header, readiness, the agent-knowledge chooser, the agent header, the lifecycle path, and the create page.
  - `app-chip` gains a `draft` tone: the neutral chip with a hollow ring.
  - The lifecycle path outlines the current step's chip.
- **Note 17 (knowledge-list.ts).** An event that arrives while a full read is in flight (the first read, a page change, Try again) is counted. If any arrived and the read succeeded, the page is read once more, quietly, after it. Before this change such an event returned early and was lost.
- **Skeletons (note 11).** The knowledge and agent list skeletons now have the loaded geometry: the head row, then rows of a glyph tile, the title bar, and a bar or chip per column. That includes the channels column when the reader may see channels, and a width for the end-aligned Size bar.
- **Upload and editor tiles.** The drop zone is a soft tile with a dashed control border and the upload glyph on a 56 px panel disc. The chosen file is a soft tile. The agent-knowledge chooser lists are radius-16 tiles. The agent editor's channels are 44 px soft tiles with a chevron (links to each channel). The preview's frame is a radius-24 line. Its "Clear" button stays beside the heading instead of dropping under it.
- **The shared transcript in three voices (shared/chat).** Seen from the visitor's side (the agent preview, the widget and the hosted page all use it):
  - their own words on the end side in cream (`--voice-customer-own-bubble`);
  - the AI on the start side beside the 26 px orb, in the AI voice, or in the widget's look the workspace accent's checked tint (as before), with the agent's name inside;
  - a person on the start side in gold with a 26 px gold person mark and the word "Support" (Q5);
  - event lines between them (`role="note"`).

  Only the newest message rises into place. While the AI types, the orb breathes beside the dots. An explicit space now separates the speaker from the words, so a screen reader no longer reads "SupportIt ships today." or "ConciergeWe close at six."
- **The side bar (owner's assistant's note).** «صندوق المحادثات» stays on one line beside its count without changing the name. Items end with 6 px instead of 10. The count pill sits 6 px from the label, with 4 px padding instead of 6. The label is `nowrap`, with an ellipsis only as a guard for a font that never arrives; the link's accessible name is the whole label. Measured in Chrome: the label needs 103.5 px; it now has 105.5 px beside "150" and about 104 px beside "999+" (it had 97.5).
- **The page header's back link** is a 44 px target on touch screens (`pointer: coarse`), with −8 px block margins so the title does not move (rule 14; the matrix found it at 28 px).
- **Agent create.** The help list's bold step titles ran into their sentences ("Create.The agent…", «الإنشاء.يُحفظ…»), because no space survived between them. Added `{{ ' ' }}`, as the upload page already does.
- **MASTER.md.** Five decision-log rows, §13.4 (this step's contrast pairs) and §14.4 (the components), plus the side bar's one-line rule in §14.1.

### 3.2 Screens

| Screen | What changed | Components and tokens it now uses | Tests |
| --- | --- | --- | --- |
| Knowledge list | Calm panel of rows; glyph tiles; state chips with the reason or the indexed date under them; geometry skeleton; note 17 | `.page-calm`, `.record-title`/`.record-glyph`, `app-chip` (success, warning, danger, info, neutral), `--radius-panel`, `--radius-tile`, `--color-panel(-soft)`, `--color-hover`, `--color-selected` | knowledge-list.spec (chips, glyphs, skeleton, 3 note-17 cases), knowledge-file.spec (tones, glyphs), arabic-render (Arabic chips); e2e knowledge-index |
| Knowledge, new | Calm panels; section numbers as tiles | `.page-calm`, inputs as 39.2b | knowledge-create.spec (unchanged, green) |
| Knowledge, upload | Calm panels; drop zone tile with the glyph disc; file tile | `.page-calm`, `.drop-zone`, `.file-card`, `--color-control-border` | knowledge-upload.spec (green); e2e knowledge-index (upload path) |
| Knowledge editor | Chips in the header and in Indexing; facts as tiles; calm panels | `app-chip`, `.facts` tiles | knowledge-editor.spec (green); e2e knowledge-index (`readiness`, `index-button`, `attempt-started`) |
| Agent knowledge (in the editor) | Chooser lists as tiles; chips in place of badges; the retrieval check unchanged | `app-chip` sm, `.chooser-list` | agent-knowledge.spec (green) |
| AI agents list | Calm panel of rows; orb tiles (resting when not in service); status chips; geometry skeleton | `app-orb`, `app-chip` (success, draft, neutral) | agent-list.spec (chips, orbs, skeleton), agent-words.spec |
| AI agent, new | Calm panels; lifecycle chips; spaces in the help list | `app-chip` draft/success | agent-create.spec (spaces, chips) |
| AI agent editor | Calm panels; status and lifecycle chips; channel tiles | `app-chip`, `.behaviour-channel` | agent-editor.spec (green); e2e agent-preview-reply (`agent-channels`) |
| Agent preview | The shared chat in three voices; breathing orb while typing; Clear beside the heading | `ChatPanel`, `.bubble-ai`, `.bubble-team`, `app-orb`, `--voice-*` | agent-preview.spec (voices), chat-panel.spec (voices, marks, orb), arabic-render (Arabic voices); e2e agent-preview-reply |
| Shell side bar (Arabic) | «صندوق المحادثات» on one line | sidebar.scss | Chrome measurement; sidebar.spec (green) |
| Page header (touch) | Back link 44 px | page-header.scss | Chrome matrix (touch < 44: 0) |
| Widget, hosted page (side effect) | The same three-voice bubbles (header, greeting and composer unchanged) | shared/chat | hosted-chat.spec, embed-chat.spec, chat-panel.spec; e2e hosted-page-ar, slow-reply, take-over |

### 3.3 Screenshots

All under `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-5a\shots\`, named `<screen>-<width>-<lang>-<theme>-<before|after>.png`:

- **Before** (HEAD `0a125de`): 40 files, 10 screens × {1440, 390} × {en-light, ar-dark}. The screens: `knowledge`, `knowledge-new`, `knowledge-upload`, `knowledge-editor`, `agents`, `agent-new`, `agent-editor`, `agent-preview` (the editor scrolled to the preview), `knowledge-analyst`, `agents-analyst`.
- **After:** 80 files, the same 10 screens × {1440, 1024, 768, 390} × {en-light, ar-dark}.
- **After, states and flows:**
  - `agent-preview-replied|typing|failed-{1440-en-light,390-ar-dark}-after.png`;
  - `knowledge-note17-1440-en-light-after.png`;
  - `{knowledge,agents,knowledge-editor,agent-editor}-loading-{1440-en-light,390-ar-dark}-after.png` (skeletons);
  - `knowledge-1280-en-light-<mode>-after.png` and `agent-preview-1280-en-light-<mode>-after.png` for each of `transparency-media`, `transparency-app`, `reduced-motion`, `forced-colors`;
  - `hosted-chat-replied|typing|failed-{1440-en-light,390-ar-dark}-after.png`;
  - `widget-open-1280-{en-light,ar-dark}-after.png`.
- `*-wip.png` files are interim captures taken while building. They are not part of the review.

To compare: `knowledge-1440-en-light-before.png` with `-after.png`, `agents-768-…`, `agent-preview-1440-ar-dark-…` (the side bar's «صندوق المحادثات» on one line), and `knowledge-390-en-light-…` (the chevron).

## 4. Decisions

1. **A page modifier (`.page-calm`), not a restyle of `.card` and `.data-list`.** Both primitives serve customers, team, roles, channels and settings, which later steps redesign. Restyling them now would have changed those screens untested. Rejected: per-component styles (a copy per screen, against the 4 kB budgets).
2. **The shared transcript moved to the three voices in this step.** The owner's assistant asked for the preview in the three voices, and the preview must stay the chat customers see (Phase 36.11). The widget and the hosted page therefore show the same bubbles now. Their glass header, 64 px orb with the greeting, glass composer, the per-language greeting and the accent fallback all stay for 39.5b. In the widget's look the AI bubble keeps the workspace accent's checked tint (as today); whether the accent may replace emerald is 39.5b's decision.
3. **Chip tones for statuses** follow Q4: Indexed and Active take the tick; Needs index and Indexing stopped the triangle (both still need an index, and the words differ); Indexing failed the octagon; Indexing now the "i"; Disabled the dot; Draft a hollow ring (new `draft` tone in the shared chip, beside the existing `.badge-draft` the channels still use).
4. **The orb in agent rows**, resting for agents not in service. calm-glass.md names the orb's two sizes (26 px in threads, 64 px in the widget) but not lists. Its use here is decorative, the 26 px size, and `aria-hidden`.
5. **Note 17 covers every full read** (first load, page change, Try again), not only the first. After a failed read it does not re-read quietly (Try again reads anyway); the events of a failed read are forgotten. Verified live: the first read was answered with the state from before indexing and held while a second tab indexed. The list made a second read and showed "Indexed" (`chrome/results-after.json` → `flows.note17`: reads 2).
6. **Beyond the two lists' screens:**
   - the back link's 44 px touch target (rule 14; layout-neutral; every page with a back link);
   - the speaker/word spaces in the shared transcript (an accessibility fix the new spec exposed; `hosted-chat.spec` now compares whitespace-normalised text);
   - the agent-create spacing bug.
7. **A long name clamps at three lines in list grids.** The full name stays in the link's accessible name and on the record's page. Rejected: unlimited wrapping (seven lines for one agent at 768 px).
8. **Not changed, and left to its step:** the phone's bottom bar breaks "Knowledge", "Customers" and "Overview" mid-word at 320–390 px when they don't fit (39.3 shell; see §9). The Arabic hosted page shows the workspace's English greeting (the per-language greeting is 39.5b).
9. **Departures from the specification or the boards:** none beyond decision 4. No board covers knowledge or agents; they follow calm-glass.md §5–§6 and the plan.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**: all matched files use Prettier style (`gates/format-final.txt`). |
| `npm run test:ci` | **PASS**: 140 files, 2,043 tests, 0 failures (39.4b: 2,032; +11). Free memory 10.73 GB before (`gates/test-ci-final.txt`). |
| `npm run build` | **PASS**: 0 warnings, budgets met (`anyComponentStyle` 4 kB included). Initial 334.75 kB raw / 76.27 kB transfer. **Startup JS 148,525 B** gzip: before 148,535 B (39.4b), HEAD rebuilt in this session 148,534 B; 38.0 baseline 138,907 B (+6.9%); ceiling 152,797 B. Free memory 10.63 GB before (`gates/build-final.txt`, `gates/startup-js-final.json`). |
| `npm run i18n:check` | **PASS**: 2,503 messages, 0 errors, 0 warnings (unchanged; no message added) (`gates/i18n-check-final.txt`). |
| `npm run e2e` | **PASS, twice**, both full suites with the throttling journey. Run 1: 46/46 (23 journeys × en-ltr, ar-rtl), 3.1 min, started 23:34:45, after the transcript and list changes. Run 2: 46/46, 3.3 min, started 00:23:05 on the final code (48 min apart). No retries, no failures. `NABRA_E2E_OUT` and `NABRA_E2E_STATE_DIR` in `p39-5a\e2e\`. Free memory 11.0 and 10.69 GB before (`gates/e2e-1.log`, `gates/e2e-2.log`). |
| Backend gates | **NOT RUN**: nothing under `backend/` changed in this step. |
| Chrome matrix | **PASS**: Playwright driving the installed Google Chrome with temporary profiles (the Chrome DevTools MCP worked, but it cannot write screenshots outside its roots; it was used for a spot check of the Arabic hosted page: one `h1`, every control named, no errors). 10 screens (owner and analyst) × {390, 768, 1280, 1920} × {en, ar} × {light, dark} = **160 loads**: overflow 0; exactly one `h1` everywhere; unnamed controls 0; console errors 0; at most 2 blurred layers; blur on rows 0; targets under 24 px 0, under 44 px on touch 0; `dir` correct; axe (WCAG 2.2 AA, at 1280 px, 40 loads) 0 violations (`chrome/matrix-after1.json`, `-after2.json`). |
| Modes | **PASS**: 8 screens × {prefers-reduced-transparency, the in-app "Reduce transparency", reduced motion, forced colours} = 32 loads. Blurred layers 0 under both transparency settings and forced colours; no running animation under reduced motion (its one blurred layer is the side bar's glass); overflow 0, one `h1`, 0 unnamed, 0 console (`chrome/results-after.json` → `modes`). |
| Keyboard | **PASS**: Tab reaches the first knowledge and agent rows with a visible 2 px ring (21 and 20 stops), and Enter opens them, in English and Arabic. In the preview, Enter sends and Clear is reachable and clears (`keyboard`). |
| Flows (AI stand-in) | **PASS**: preview with a reply, `[[slow]]` (the breathing orb, the dots, "<agent> is typing…"), and `[[fail]]` (the honest failure; the message stays in the box), in en-light 1440 and ar-dark 390. Hosted page (en-light 1440, ar-dark 390): a reply, a slow reply, a failure. Widget on English and Arabic host pages (light and dark): a reply beside the orb. A person taking over: covered by the e2e `take-over` journey (twice per run, en and ar). The only console errors are the stand-in's deliberate 503 for `[[fail]]`, and on the synthetic host page its own `/favicon.ico` 404. |
| Visitor pages' weight and LCP | **Measured**, HEAD vs this step, production builds, opening only, throttled phone (390×844, 4× CPU, 150 ms, 1.6 Mbps), median of 3 (`chrome/weight.json`). Hosted English: 244,531 → 245,835 B transfer (script 168,031 → 169,048), LCP 4,464 → 4,044 ms. Hosted Arabic: 384,733 → 386,037 B, LCP 5,352 → 5,272 ms. Widget frame (`/embed`): 268,927 → 270,248 B, LCP 4,456 → 4,656 ms. About +1.3 kB (+0.5%) each, from the transcript's new markup and the orb in the shared chunk; the LCP moves are within run-to-run noise on this throttle. The 39.0 transfer baseline for the widget does not exist yet; 39.5b owns it. |
| Contrast of every new pair | **PASS**: 36 measurements (18 pairs × light/dark) from computed colours, translucent layers composited. Text 4.95–15.91:1, graphics and edges 3.62–9.24:1 (`chrome/contrast.json`, MASTER.md §13.4). The person mark and the typing dots reuse 39.4b's measured voice pairs. |
| RTL mirroring | **PASS**: Arabic at every width. Logical properties only. The transcript's marks come first in reading order, so they sit on the start side (the right in Arabic). The chevrons mirror. User text keeps `dir="auto"`. |
| Touch targets | **PASS**: 0 under 44 px on touch after the back-link fix (it was 28 px on 4 screens). Channel tiles 44 px; chips are not controls. |
| Fonts | **Unchanged**: no font file, subset or preload changed. Layout shift on Arabic loads of `/knowledge` and `/ai-agents`, font swap included: **CLS 0** (`weight.json`). |
| Provider canary | **PASS** at the start, before each e2e run and at the end (`gates/canary-*.txt`): every listed provider variable absent, `Email__Smtp__Host` loopback, `OMNIAI_DEV_AI_STANDIN` present (info). |
| Secrets | **PASS**: see §9.6. The temporary cookie jars (`p39-5a\tmp\`) were deleted; the e2e state folders were removed by the suite's teardown. |
| Stack at the end | The API (`/health/ready` 200) and the dev server (:4200) left running. The servers this step started (the widget host on :8099, the production-build servers on :4301 and :4302) are stopped. The HEAD export used for the comparison was removed, its `node_modules` junction first. |

## 6. See it live

- Sign in at http://localhost:4200/sign-in with the **Owner** `p36.owner@omniai.example`. The **Analyst** `p36.analyst@omniai.example` shows the read-only paths. Passwords are in `omniai-redesign-evidence\p36-01\demo-accounts.md`, never copied here.
- **Knowledge:** http://localhost:4200/knowledge.
  - The rows' chips: "Indexing failed" with its reason, "Indexing stopped", "Disabled".
  - Upload a small `.txt` file at `/knowledge/upload`, then press **Index document** in the editor with the list open in a second tab: the row turns to "Indexed" by itself.
  - Narrow the window to a phone: the chevron sits at each row's end.
- **AI agents:** http://localhost:4200/ai-agents (resting orbs for Disabled and Draft; the Draft ring). Open **P38.3 Visitor Desk** and, in "Customer view", ask anything: a reply arrives in the AI's bubble beside the orb.
  - Add `[[slow]]` to the message for the breathing orb and "typing…".
  - Add `[[fail]]` for the honest failure; your message stays in the box.
- **Arabic:** switch the language in the account menu. The side bar's «صندوق المحادثات» now sits on one line beside its count.
- **Visitor side** (the shared bubbles): http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw (`?lang=ar` for Arabic). Each new conversation counts against the link's limit of 5 starts per 5 minutes.

## 7. Backend asks

None new. The screens use only what the API offers today: the knowledge list and attempt fields, the agents and their channels, the stateless preview and the retrieval check. BA39-4 (quick replies for the widget) stays with 39.5b.

## 8. New Arabic for my review

**No new or changed Arabic string.** The catalogue is unchanged (2,503 messages).

Two existing Arabic texts now read differently on screen without any wording change:

| Where | What changed |
| --- | --- |
| Agent create, "What happens next" | A space now separates each bold step from its sentence: «الإنشاء. يُحفظ الوكيل مسودةً…» (it read «الإنشاء.يُحفظ…»). |
| Side bar | «صندوق المحادثات» on one line beside its count (the name is unchanged). |

## 9. Risks and open issues

1. **The widget and the hosted page already show the three-voice bubbles.** Their header, greeting and composer are still the old look until 39.5b, so for one step the visitor surfaces mix the two looks.
2. **Phone bottom bar (39.3), not fixed here.** Labels that do not fit break mid-word. Measured (natural width at the active weight against the room):
   - English 390 px: "Knowledge" 64 / 64 px (wraps);
   - English 360 px: "Customers" 62.5 / 58 and "Knowledge" 64 / 58;
   - English 320 px: "Overview" 54 / 50, plus "Customers" and "Knowledge";
   - Arabic 320 px: «المحادثات» 51.8 / 50.

   A fix needs short labels (new strings) or a type change, both shell decisions. Proposed for 39.8b, or a follow-up you choose.
3. **`.page-calm` is a transitional layer.** Each later screen step adds it, and 39.8b must make it the default and delete it, or the two looks persist.
4. **Synthetic data written by the checks:**
   - three uploaded and indexed documents titled "p39-5a note17 …" in Northwind (the note-17 flow ran three times);
   - four visitor conversations on the Northwind hosted link (hosted page en/ar, widget en/ar);
   - preview requests through the stand-in ($0, word counts).

   The e2e runs made their usual synthetic workspaces. Nothing was deleted.
5. **The 39.0 plan has no widget transfer baseline.** This step compared HEAD with itself (+0.5%); 39.5b sets the baseline it reports against.
6. **Secret scan:** `p39-0\scratch\secret-scan.mjs`, pointed at `p39-5a` and this report, scanned 773 files (the evidence folder, `frontend/src`, `frontend/e2e`, the Playwright config and this report): 0 with the demo password, 0 with an invitation link (`gates/secret-scan.txt`).

## 10. Proposed checkpoint commit message

```text
feat: lay knowledge and AI agents out as calm panels of rounded rows, show each document's kind and each agent's orb, say every state as a chip with a word and a glyph (Indexed, Needs index, Indexing now, Indexing failed with its reason, Indexing stopped, Disabled; Active, Draft, Disabled), draw the lists' skeletons in their real shape, re-read the knowledge list once when indexing ends during its first read, speak the agent preview (and the visitor's chat) in the three voices with the breathing orb, keep «صندوق المحادثات» on one line in the side bar, and give the back link a 44 px touch target (Phase 39.5a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
