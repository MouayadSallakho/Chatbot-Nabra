# Phase 36.8: AI Agents and Knowledge ("a teammate you craft, a library you trust")

Date: 2026-10-19. Branch `master`. Not committed (see section 9).
Evidence: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-08\` (written `<evidence>` below).

## 1. Summary

- AI Agents is now a teammate you craft. The list shows each agent's status (a word plus its own marker) and which channels use it. Create is a guided page that says plainly nothing goes into service until you activate. The editor has four numbered sections (identity, instructions, behaviour, knowledge).
- From 80rem, a live "Customer view" sits beside the editor and looks like the chat customers see: the workspace's own accent colour, the agent's name, the honesty line and the greeting, all taken from the real widget settings.
- Knowledge is a tidy library. Each document shows its type, its size, its indexing state in exactly the API's words, and when it last changed. Upload is a drop zone with a real "Choose a file" button and clear limits and errors.
- Choosing an agent's knowledge is two lists, "Selected" and "Available", that a keyboard can work through without losing its place.
- Everything is in English and Arabic (293 new messages), and every place people type mixed-language text keeps each paragraph in its own direction.

## 2. Starting point

- Branch `master`, HEAD `c352f9e` ("feat: give the numbers words, and the charts both voices"), which is the accepted Phase 36.7 checkpoint. The tree was clean.
- Local stack: `omniai-p36-api` (Development), `omniai_pg25` and `omniai_redis18` were already running, with `/health/ready` 200 and both migrations applied. The dev server on 4200 was already running. The provider canary passed before any traffic (`<evidence>\canary-start.log`).
- Baseline: the production build passed with one existing warning (`embed-chat.scss` 4.07 kB). Startup JavaScript was 411,087 B raw, 135,652 B gzip and 119,947 B brotli (`<evidence>\startup-js-baseline.json`).
- What the API returns (`<evidence>\api-inventory.json`, read-only GETs as owner, support and analyst):
  - Agents: 4 (two Active, one Draft, one Disabled). The list has no instructions and no channel information.
  - Channels: one website chat endpoint (Active) and one WhatsApp connection (Draft), both on Northwind Concierge. Each list names its agent (`aiAgentId`). The widget has a greeting and the accent `#2563eb`.
  - Knowledge: 14 typed documents, 13 `NeedsIndex` and 1 `Disabled`. None is indexed, because no provider is configured, and none came from a file. The list publishes `contentLength` (characters) and `sourceFileType`, but not the file's bytes.
  - Readiness: the API has three states, `Ready`, `NeedsIndex` and `Disabled`. A failed index attempt is stored on the server, but no endpoint returns it (see Decisions and Backend asks).
  - Permissions: support can see knowledge (read-only) and WhatsApp, but not agents or website chat. The analyst sees neither area.
- One slip, with no effect on the product: my first inventory command ran the copied 36.7 script, which re-read the same read-only analytics and report endpoints and overwrote `p36-07\api-inventory.json` with a fresh read. Nothing was written to the API or the database.

## 3. Changes by area

`git status --short` at the end (41 modified, 8 new paths including this report):

```
 M docs/design/MASTER.md
 M frontend/src/app/features/ai-agents/agent-create/agent-create.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/ai-agents/agent-editor/agent-editor.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/ai-agents/agent-editor/agent-preview.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/ai-agents/agent-list/agent-list.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/ai-agents/ai-agent.routes.ts
 M frontend/src/app/features/knowledge/agent-knowledge/agent-knowledge.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/knowledge/knowledge-create/knowledge-create.html|.scss|.ts
 M frontend/src/app/features/knowledge/knowledge-editor/knowledge-editor.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/knowledge/knowledge-list/knowledge-list.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/knowledge/knowledge-upload/knowledge-upload.html|.scss|.spec.ts|.ts
 M frontend/src/app/shared/icon/icon-paths.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles.scss
?? frontend/src/app/features/ai-agents/agent-channels.ts
?? frontend/src/app/features/ai-agents/agent-words.ts (+ agent-words.spec.ts)
?? frontend/src/app/features/knowledge/knowledge-file.ts (+ knowledge-file.spec.ts)
?? frontend/src/app/features/knowledge/knowledge-words.ts
?? frontend/src/styles/_authoring.scss
?? docs/phase-reports/PHASE_36_8_AGENTS_KNOWLEDGE_REPORT.md
```

Screenshots:

- **Before:** `<evidence>\before\{owner,support}-{en,ar}-light-{1440,390}-{agents,agent-new,agent-editor,knowledge,knowledge-new,knowledge-upload,knowledge-editor}.png`, 56 files, with `before.json`.
- **After:**
  - `<evidence>\matrix\`: `{owner,support}-{en,ar}-{light,dark}-{1920,1440,1280,1024,768,390}-{route}.png`, 384 files, with `matrix.json`.
  - `<evidence>\peek\`: quick looks during the build.
  - `<evidence>\paths\`: the two full paths.

### AI Agents list

- **Rows:** each row shows the name (`dir="auto"`), the status and the last update.
  - The status is a word plus its own marker: a tick for Active, a hollow ring for Draft, and a filled dot for Disabled.
  - The row is one link, and page-in-URL paging is unchanged.
- **Channels column:**
  - It names the channels that use each agent ("Website chat", "WhatsApp (Draft)"). A channel that is not on carries its own status word.
  - The column is joined on the client from the website chat and WhatsApp lists. Each list is read only with its own permission (`AgentChannels`).
  - A reader who may see no channel gets no column. A list that fails reads "Not checked", never "Not on a channel".
- **States:** loading (a skeleton in the row shape), error with Try again, empty (first use), and no permission, all translated.
- Before: `before\owner-en-light-1440-agents.png`. After: `matrix\owner-en-light-1440-agents.png`, `matrix\owner-ar-dark-390-agents.png`.

### Create an agent

- **Guided page:** two numbered sections (Identity, Instructions), guidance beside the instructions, and counters in the reader's digits. Beside them, a "What happens next" panel shows the real lifecycle (Draft, then Active) and four steps: create, preview, give it knowledge, activate.
- The button says "Create draft agent".
- **New:** submitting with errors moves focus to the first invalid field. The double-submit guard and the server-word errors are unchanged.
- Before: `before\owner-en-light-1440-agent-new.png`. After: `matrix\owner-en-light-1440-agent-new.png`, `paths\path-01-create-invalid.png`.

### Agent editor

- **Four numbered sections:**
  - Identity: the name.
  - Instructions: guidance, `dir="auto"`, and a counter.
  - Behaviour:
    - the lifecycle path, with the current step outlined and marked `aria-current`;
    - the status sentence;
    - Activate, or Disable with confirmation, exactly as before;
    - "Where it answers", linking each channel's page.
  - Knowledge: the new two-list assignment and the retrieval test.
- **Behaviour unchanged:**
  - each section saves on its own, with entity tags, `If-Match`, the 412/428 stale notice and reload-with-warning, and 409 handled as a state problem;
  - the unsaved-changes guard: route and `beforeunload`, checked in Chrome;
  - Disable confirmation.
- **Customer view:** beside the form from 80rem (sticky on screens at least 46rem tall), and after it below that. It is drawn as the website widget:
  - the header in the workspace accent, with a readable foreground from `readableForeground`;
  - the agent's name and "Answers are generated by AI.";
  - the widget's greeting;
  - the AI's bubbles in the accent's tint, the customer's neutral;
  - a composer where Enter sends and Shift+Enter adds a line.
- **Where the customer view's look comes from:**
  - The accent and greeting come from the real widget settings of a website chat that uses the agent. An agent on no website chat shows the widget default, and the panel says so.
  - The preview answers from the saved name and instructions. It says so whenever the editor holds unsaved edits.
  - Single-turn and nothing saved, as before. A failure keeps the message in the box, with no automatic retry.
- **Fixed:** the editor kept no page heading while loading or after a failed load. Before, support saw no `h1` (`before\support-en-light-1440-agent-editor.png`).
- **Fixed late, found by looking:** the sticky preview was not sticking, because its column was only as tall as the preview. The column now runs the form's height, and the preview sticks only on screens at least 46rem tall, where the transcript gives up height first so the composer never falls below the fold.
- **Fixed:** a reader without `ai-agents.view` who opens an agent's address now gets the no-access state with no request sent. Before, the GET was refused with 403 and Chrome logged it. Found by the first matrix run (48 cells, all this one cause; `<evidence>\matrix-run1.log`).
- After: `matrix\owner-en-light-1920-agent-editor.png`, `matrix\owner-ar-light-1440-agent-editor.png`, `paths\path-02-created-editor.png`, `paths\path-03-preview-503.png`.

### Agent knowledge assignment

- **Two lists:** "Selected for this agent" and "Available", side by side when the section is at least 40rem wide (container query), stacked otherwise.
  - Each document shows its title (`dir="auto"`), its real state badge, and one labelled button ("Add “Returns policy”", "Remove “…”"). The accessible name starts with the visible word.
- **Keyboard:**
  - After a move, focus stays in the list the reader left, on the document that took its place (or the list's heading when it empties).
  - The move is announced politely ("Added “…” to this agent. Not saved yet.").
- **Unchanged:** Save and Discard, the stale-version notice that keeps the selection, and reporting the agent's new version to the editor. Read-only readers see both lists with no buttons.
- **Retrieval test:** it moved into the same section, with translated facts ("Rank 1 · 82% match · passage 1").

### Knowledge list

- **Columns:**
  - the document title (`dir="auto"`, which fixes the scrambled markup-looking titles in Arabic);
  - the type ("Written", PDF, Word document, Text file, CSV);
  - the size in characters, with the right plural;
  - the indexing state in exactly the API's words, with the index date under it when there is one;
  - the last change.
- **Actions:** "Upload file" (secondary) and "New document" (primary). Empty, error, loading and no-permission states, all translated.
- Before: `before\owner-ar-light-1440-knowledge.png` (English in Arabic, broken RTL titles). After: `matrix\owner-ar-light-1440-knowledge.png`.

### Knowledge create, editor and upload

- **Create:** the same guided pattern (Title, Content) with a "What belongs here" panel and "Have it in a file? Upload it instead".
- **Editor:**
  - The document form is on the main side.
  - Beside it:
    - an Indexing card: state badge, sentence, last change and last indexed, index result or failure, the cost note, and Index document / Index again;
    - Availability: take out of use, with confirmation as before;
    - Source file, for uploaded documents: the file, format, size and when the text was read, plus "Choose a new file" (a real button) and Replace file.
  - The header shows the type and size.
  - "Indexing failed" (danger badge) appears only right after an index request that really failed here.
- **Upload:**
  - A drop zone with "Drag a file here, or [Choose a file]". The button opens the browser's own picker, and the native input stays out of the tab order.
  - A file card shows the name (`dir="auto"`), type, size and Remove.
  - Only one file is taken at a time, and the page says so.
  - Local refusals come before sending (empty, over 10 MB, unsupported). They use the formats and size from `knowledge-limits.ts`.
  - Server refusals: 413 and 415 are said in our words with the real limit. 422 keeps the server's specific sentence in English and gets one translated sentence in Arabic. "Nothing was saved" stays.
  - The notes column says the file is not kept, indexing is a separate step, and scanned PDFs are not read.
- After: `paths\path-04-upload-refused.png`, `paths\path-05-upload-chosen.png`, `paths\path-06-uploaded-index-failed.png`, `matrix\owner-en-light-1440-knowledge-upload.png`, `matrix\owner-ar-dark-390-knowledge-upload.png`.

### Shared

- **`styles/_authoring.scss`:** a new global layer for:
  - the authoring frame, numbered form sections, the unsaved note, counters and guidance;
  - the lifecycle path, the Draft badge marker and the customer view;
  - the drop zone, file card, chooser, facts and help lists.

  No component stylesheet passed its 4 kB budget.

- **Words modules:** `agent-words.ts` (status words, markers and sentences), `knowledge-words.ts` (states, types, the character plural) and `knowledge-file.ts` (format, size and refusal words, shared by upload and replace).
- **Icons:** two glyphs, `file` and `upload`.
- **MASTER.md:** nine new decision rows, and `_authoring.scss` listed in the implementation.

### Arabic

- **293 new messages:** the catalogue went from 1,004 to 1,297 messages, all translated, with `i18n:check` at 0 errors and 0 warnings.
- **Glossary:** it is respected: وكلاء الذكاء الاصطناعي، المعرفة، الفهرسة، مستند، دردشة الموقع، واتساب، مسودة/مفعّل/معطّل (and معطّلة for a channel), نبرة.
- **Counts:** "Characters" uses all six Arabic plural forms. Elsewhere counts are written as "label: number" ("الأحرف المتبقية: 19,682 من 20,000") to avoid agreement errors.
- **Digits and sizes:** Western digits everywhere. File sizes come from `Intl` ("457 بايت"; the limit is CLDR's short form "10 م.ب.").
- **Direction:** every place people type or read user content is `dir="auto"`: names, instructions, titles, content, test messages, the greeting, replies and file names. In a `<textarea>` Chrome then lays out each paragraph in its own direction (`unicode-bidi: plaintext`), so an Arabic line inside English instructions sits right-aligned.

## 4. Decisions

1. **States are the API's, and only the API's.** The prompt lists four states (Needs index, Indexed, Failed, Disabled). The API publishes three, and no endpoint returns a failed attempt. The list shows the three. "Indexing failed" appears on the editor only after a request that really failed while the reader was there. Inferring "failed" from timestamps or notifications would be fake, so a persisted state is a backend ask.
2. **"Size" in the list is characters, not bytes.** The list publishes `contentLength`. The source file's bytes are only on the document, and the editor shows them. This is a backend ask.
3. **"Channels that use it" is joined on the client**, from the two channel lists, each read with its own permission. Both are whole and small. "Not checked" on failure is never read as "none".
4. **The preview follows the widget direction, not today's widget.** The widget itself is redesigned in 36.9. The preview uses the direction's look (accent header, the AI in the accent tint, the customer neutral) from real settings only, and the shared `.customer-view` rules sit in a global layer the widget can adopt. It follows the admin's theme: tokens are scoped to `:root`, so the widget's forced Light or Dark cannot be shown inside the admin. The widget's own default accent (`#2563eb`) is the one literal colour, annotated.
5. **"Behaviour" means what the API can express:** whether the agent is in service (the lifecycle) and where customers meet it (channels). There are no tone or handoff settings in the API, so none were invented.
6. **The two-list chooser instead of checkboxes.** What an agent uses reads at a glance, and focus continuity plus announcements keep keyboard and screen-reader users oriented. Drag and drop between the lists was rejected: it is not needed and would need a keyboard alternative anyway.
7. **Guided create, not a wizard.** Two fields do not need steps; the reader needs to know nothing goes into service on Create.
8. **The upload drop zone uses the platform's drag events**, with no library. The button is the path for keyboard, screen readers and phones, and both run the same checks.
9. **Error copy for uploads.** Server sentences are English only and carry no code. 413 and 415 use our words with the real limits. 422 keeps the server's sentence in English (it names the reason) and gets one general translated sentence in Arabic.
10. **Removed after looking:**
    - a coloured state edge on the indexing card: on a rounded card it curved into the bracket shape MASTER rejected in 36.4;
    - the dimming of non-current lifecycle steps: it put badge text at risk of falling under 4.5:1;
    - the subtitle opacity on the accent header: it would have voided the computed foreground's guarantee.
11. **Heading case.** The list heading stays "AI Agents", matching the navigation label and the tab title (a shell spec asserts it). Sentence case for that term is a product-wide change for a later phase.
12. **Deferred:**
    - On narrow screens the preview sits after the long form. A "jump to preview" link was considered and left out to keep one way through the page.
    - Drag and drop onto "Replace file": the button is there; the drop zone is only on Upload, as the prompt asks.

Skills used: `frontend-pro-max:ui-ux-design-lead`, `enterprise-erp-ux`, `motion-interaction`, `angular-enterprise`, `frontend-security` and `frontend-testing-quality`, plus the `design-system-tokens` contrast, RTL and token scripts. None was missing.

## 5. Verification

| Check                                                                                  | Result                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| -------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `npm run format:check`                                                                 | **PASS**: all matched files use Prettier style (`<evidence>\final-format.log`).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `npm run test:ci`                                                                      | **PASS**: 88 files, 1,030 tests, 0 failures (`<evidence>\final-test.log`; was 86 files / 990 tests). New and rewritten specs cover the channels join (permission-gated, partial on failure), status markers, the four sections, the customer view (real accent and greeting, readable foreground, Enter/Shift+Enter, `dir="auto"`, unsaved notice), two-list moves (focus continuity, announcement, read-only), the drop zone (button opens the picker, drop takes one file, refusals before sending, Remove returns focus), file and knowledge words, "Indexing failed" only after a real failure, focus on the first invalid field, and an editor that asks nothing of the server for a reader without agent access. Every spec uses DI stand-ins; there is no `vi.mock`.                                                                                             |
| `npm run build`                                                                        | **PASS**: one warning, the existing `embed-chat.scss` 4.07 kB (also in the baseline). No new budget warning. Initial total 303.90 kB raw / 72.17 kB transfer (baseline 295.86 / 71.04; the growth is the global `_authoring.scss` layer, +8.6 kB raw / +1.23 kB transfer in `styles.css`). The lazy `agent-editor` chunk is 51.3 kB raw / 12.0 kB transfer (was 33.9 / 8.2), with the channel clients and the new assignment section.                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Startup JavaScript (loader + application chunk, production)                            | **PASS**: baseline 135,652 B gzip / 119,947 B brotli; final 135,478 B gzip / 119,938 B brotli (−0.13% / −0.01%) (`<evidence>\startup-js-*.json`).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `npm run i18n:check`                                                                   | **PASS**: 1,297 messages, 0 errors, 0 warnings. The extraction still prints the two duplicate-id warnings for `analytics.table.conversations` and `analytics.series.customers` from 36.7; this phase's own duplicates were fixed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Chrome matrix                                                                          | **PASS**: 384 cells (`<evidence>\matrix\matrix.json`, `matrix.log`). <br>• Routes: the agents list, new agent, two agent editors (one on a website chat, one created here), the knowledge list, new document, upload, and the uploaded document's editor. <br>• Coverage: widths 1920/1440/1280/1024/768/390, light and dark, English and Arabic, owner and support. <br>• Per cell: page overflow 0, clipped content none, console errors 0, exactly one `h1`, unnamed controls 0, shell targets under 24 px none. <br>• The first run (`matrix-run1.log`) had 48 cells with a console 403, all support opening an agent's address; fixed (section 3), then the full run was repeated. <br>• After the last change (the sticky preview, CSS only), the two agent-editor routes were run again: 96 cells, 0 issues (`matrix-editor-rerun\`, `matrix-editor-rerun.log`). |
| Full path 1: create → edit → preview → activate (Chrome DevTools MCP, real API, owner) | **PASS** (`<evidence>\paths\`). <br>• Create: an empty submit showed both field errors; creating "P36.8 Returns Desk" with English + Arabic instructions opened the editor on the Draft with no second read. <br>• Edit: an added line raised "Unsaved changes" and the preview's unsaved notice; leaving through the sidebar asked "You have unsaved changes on this page. Leave and discard them?", and dismissing stayed; Save instructions cleared both. <br>• Knowledge: Add moved a document and focus, and Save knowledge passed the new version to the editor. <br>• Activate: Active, with the current lifecycle step outlined and no stale notice. <br>• Preview: an Arabic question (Enter sends) got the honest 503 sentence inside the frame; the message stayed in the box.                                                                               |
| Full path 2: upload a knowledge document                                               | **PASS** (`<evidence>\paths\`). <br>• Refused before sending: through the "Choose a file" button, `quarterly-review.pptx` was refused with "That file is not one Nabra can read. Choose a PDF, Word (.docx), plain text or CSV file." Upload stayed disabled and nothing was sent. <br>• Uploaded: `northwind-exchange-policy.txt` (457 bytes, English + Arabic) with a title; the editor opened showing the source file, "Text file · 378 characters". <br>• Indexed with no provider: Index document got 503, and the page showed "Indexing failed" and the sentence. The backend raised its own notification (bell 8 → 9).                                                                                                                                                                                                                                           |
| Contrast (`contrast_check.py`, built CSS)                                              | **PASS**: 31 pairs × light, dark, light + more, dark + more. Per mode: 28 pass, 0 fail, 3 informational (section-number ring, guidance edge, customer-view frame: decorative hairlines). Tightest required: 3.39:1 (drop-zone boundary, dark, UI 3:1); tightest text 4.68:1 (Active badge, light). Customer view with the widget's default accent: white on `#2563eb` 5.16:1; text on the AI tint 15.28:1 (light) / 14.21:1 (dark); sender 6.75:1 / 8.17:1. The AI bubble's tinted border is 1.56:1 to the page, which is informational: the bubble is identified by its sender label, fill and side, as in the inbox (`<evidence>\contrast\*.md`).                                                                                                                                                                                                                     |
| Keyboard                                                                               | **PASS**: in Chrome, Enter and Space on an "Add" button moved the document; focus went to the next document's button with a visible ring (`:focus-visible`); the target is 71 × 32 px; the move was announced. The preview sends on Enter and adds a line on Shift+Enter (Chrome and spec). "Choose a file" is a real button and opens the picker (spec; the MCP opened the picker through it to choose files). After Remove, focus returns to it (spec). After a failed create, focus is on the first invalid field (spec). Screenshot: `<evidence>\keyboard\keyboard-01-chooser-focus.png`.                                                                                                                                                                                                                                                                           |
| Sticky preview                                                                         | **PASS** (`<evidence>\sticky.json`, `sticky\`). In the tallest state (unsaved notice plus a failed send) at 1440×900 and 1920×1080, the customer view stays 16 px under the top bar to the bottom of the page, with the composer in view (bottom at 684 px). At 1280×720 the screen is too short to hold it, so it scrolls with the page, whole.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| Reduced motion                                                                         | **PASS**: with `prefers-reduced-motion: reduce`, every transition and animation this phase added computes to 1e-05 s (lifecycle step, chooser buttons, customer view and bubbles, drop zone, list rows); the tokens are 0.01 ms (`<evidence>\rtl-motion.json`). Nothing moves on load.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| RTL mirroring                                                                          | **PASS** (`<evidence>\rtl-motion.json`, 1440 px). <br>• The preview column moves from the right (x 969–1401) to the left (x 39–471). <br>• Section numbers sit at the reading start (x 1307 in Arabic, 109 in English). <br>• "Selected" comes first in reading order (right in Arabic). <br>• The lifecycle path runs right to left. <br>• The back arrow mirrors (`matrix(-1, 0, 0, 1, 0, 0)`). <br>• Knowledge rows start at the right with the chevron on the left. <br>• User text is `dir="auto"` everywhere (instructions, titles, preview input).                                                                                                                                                                                                                                                                                                               |
| Lints                                                                                  | **PASS**: <br>• `erp_screen_lint.py`: 0 findings. Three HIGH "destructive without confirmation" leads were the Remove buttons that undo an unsaved local choice; they are annotated with the reason. <br>• `motion_audit.py`: 0 HIGH / 0 MEDIUM. <br>• `rtl_logical_lint.py`: 0. <br>• `token_lint.py`: 0 after annotating five deliberate values (the widget default accent and four 2px optical gaps). <br>• `security_scan.py` on `features/`: 0 at medium or above; no `innerHTML`, bypass or `eval` (`<evidence>\lint\`).                                                                                                                                                                                                                                                                                                                                          |
| Provider canary                                                                        | **PASS** at start and end: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_*` and `Email__Smtp__*` are absent (names only; `<evidence>\canary-start.log`, `canary-end.log`). No provider was called: the preview and index requests were refused by the API with 503 because none is configured.                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |

## 6. See it live

- http://localhost:4200/ai-agents
- http://localhost:4200/ai-agents/new
- http://localhost:4200/ai-agents/01a14d54-192a-7014-895c-7851b906ead0 (on a website chat: the customer view in the real accent and greeting)
- http://localhost:4200/ai-agents/01a1518c-fd76-7afc-8e69-84885dece9df (the agent created in path 1)
- http://localhost:4200/knowledge, http://localhost:4200/knowledge/new, http://localhost:4200/knowledge/upload
- http://localhost:4200/knowledge/01a1518f-7cc6-7a89-870c-628b1856c762 (the document uploaded in path 2)
- Accounts: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-08\demo-accounts-p36-08.md`. It points to `p36-01\demo-accounts.md`, which holds the password. Use the owner for everything, and support for the read-only knowledge and the agent permission state.

The dev server (`npm start`, port 4200) and the API (`omniai-p36-api`, 127.0.0.1:5027) are running.

## 7. Backend asks

1. **A persisted indexing outcome on each document.** For example, `lastIndexAttempt: { status, failureCode, at }` on both the document and the list summary. The server already records attempts (`KnowledgeIndexAttempt`, with stable failure codes). Without it, "Failed" can only be shown for an attempt made on the page, and a list can never say which documents failed earlier.
2. **The source file's size in the list summary** (`sourceByteLength`), so "size" can be the file's size where there is a file.
3. **Channels per agent.** An agent's endpoints in the agent detail and list (for example `channels: [{ kind, id, status }]`), so the client need not read two channel lists and every reader sees the same answer.
4. **Stable codes on file refusals** (`knowledge.file.no_text`, `too_many_pages`, `type_mismatch`, …) in the problem response. Today only the HTTP status and an English sentence arrive, so Arabic readers get one general sentence for every 422.
5. **Localised server sentences** for 400/409/422 agent and knowledge refusals, or codes for them, for the same reason.
6. Still open from earlier phases: the transitive `System.Security.Cryptography.Pkcs` NuGet-audit issue (36.1); a purpose breakdown, comparison period, localised report catalogue and first-activity date (36.7).

## 8. Risks and open issues

- **No real AI answer was ever seen.** No provider was called, by rule, so neither the preview nor indexing has shown a real answer locally. Specs and the honest 503 path cover them.
- **The preview looks like the 36.9 widget, not today's one.** It follows the direction the widget is moving to, so until 36.9 ships the preview and today's widget differ (today the customer bubble is filled with the accent). The shared `.customer-view` layer is meant to be adopted by the widget in 36.9.
- **"Indexing failed" lasts only while the reader is on the page** (decision 1). Reloading shows "Needs index", which is what the API says.
- **Arabic copy has not had a native review**, as in earlier phases. Plural forms and the glossary are checked by script.
- **The agent-editor chunk grew** to 51.3 kB raw (lazy), with the channel clients and the assignment section. It is loaded only on that page.
- **Synthetic data:** one agent and one knowledge document were added to Northwind through the real API (see the accounts file), and the backend raised one "indexing failed" notification.

## 9. Proposed checkpoint commit message (not committed)

```
feat: craft agents like teammates, keep knowledge like a library

AI Agents: each row says its status as a word with its own marker (tick,
hollow ring for a draft, dot) and which channels use it, joined from the
website chat and WhatsApp lists the reader may see ("Not checked" when a
list fails). Create is a guided page that says nothing goes into service
until you activate. The editor is four numbered sections (identity,
instructions, behaviour, knowledge) with every save, stale-version notice,
unsaved-changes guard and confirmation unchanged, and beside it from 80rem
a customer view drawn as the website widget, in the workspace's real accent
and greeting, answering from what is saved.

Knowledge: type, size, indexing state in exactly the API's words and last
change per document; "Indexing failed" only after a request that really
failed. Upload is a drop zone with a real "Choose a file" button, limits
from knowledge-limits.ts, and refusals in plain words. An agent's knowledge
is two lists, Selected and Available, that keep a keyboard user's place and
announce each move.

Shared authoring rules in styles/_authoring.scss (no component budget
grows), two glyphs (file, upload), 293 new messages in English and Arabic,
dir="auto" wherever people type. Frontend only; no new dependency or endpoint.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
