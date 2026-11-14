# Phase 39.4a: the inbox list

Step 39.4a of the Calm Glass redesign (plan: `PHASE_39_0_REDESIGN_PLAN_REPORT.md` §3.2.4, §3.6 "39.4a inbox: list", §12). The thread is 39.4b. Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-4a\`.

## 1. Summary

- From 1280 px the inbox is two solid panels on the ground: the list (352 px) and the open conversation beside it. The list's heading says "Inbox" («صندوق المحادثات»), like the navigation and the browser tab.
- Three views as one control: All · Needs a person (with its count) · Mine (the conversations assigned to the reader or taken over by them).
- Each row shows the customer's avatar with the channel's badge, their label, the time and one line of the last message, then at most two chips: why it waits for a person (coral when the AI could not reply), and who has it, said once ("Handled by Sara", else "Assigned to …", by display name) or "AI is answering".
- Unread is quieter: a heavier name and a small dot at the end of the row, never a fill. "Mark all as read" asks how many are unread first and says the number in its confirmation; the reader's other tabs clear their dots live.
- On a phone the bottom bar says «المحادثات» on one line, so the Arabic bar is back to 64 px like the English one.

## 2. Starting point

- Branch `master`, HEAD `99e0392` ("float the navigation in a glass side bar …" (Phase 39.3)), worktree clean (`git status --short` empty), as the prompt's starting point says.
- Dev stack running (p36: `omniai-p36-api` with the AI stand-in, Mailpit, Postgres, Redis); provider canary PASS before any traffic (`gates/canary.txt`). Dev server on `http://localhost:4200`.
- Skills loaded before any work: `frontend-pro-max:angular-enterprise`, `frontend-pro-max:enterprise-erp-ux`, `frontend-pro-max:ui-ux-design-lead`, `frontend-pro-max:motion-interaction`. The 21st.dev MCP ("magic") failed to connect this session ("Not authenticated"), so nothing was sourced from it; the list is built from our own primitives (plan §12 allows this).

## 3. Changes by area

`git status --short` (22 modified, 3 new paths plus this report):

```text
 M docs/architecture/human-handoff-realtime-inbox.md
 M docs/design/MASTER.md
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/realtime/conversation-realtime.spec.ts
 M frontend/src/app/core/realtime/conversation-realtime.ts
 M frontend/src/app/core/realtime/workspace-realtime.spec.ts
 M frontend/src/app/core/realtime/workspace-realtime.ts
 M frontend/src/app/features/conversations/conversation-inbox/conversation-inbox.scss
 M frontend/src/app/features/conversations/conversation-list/conversation-list.html
 M frontend/src/app/features/conversations/conversation-list/conversation-list.scss
 M frontend/src/app/features/conversations/conversation-list/conversation-list.spec.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.ts
 M frontend/src/app/features/conversations/conversations-api.ts
 M frontend/src/app/features/conversations/conversations.routes.ts
 M frontend/src/app/features/conversations/inbox-placeholder/inbox-placeholder.ts
 M frontend/src/app/layout/bottom-bar/bottom-bar.html
 M frontend/src/app/layout/bottom-bar/bottom-bar.scss
 M frontend/src/app/layout/navigation.ts
 M frontend/src/locale/GLOSSARY.md
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_tokens.scss
?? docs/phase-reports/PHASE_39_4A_INBOX_LIST_REPORT.md
?? frontend/src/app/features/conversations/inbox-rows.spec.ts
?? frontend/src/app/features/conversations/inbox-rows.ts
?? frontend/src/app/features/conversations/mark-all-read/
```

### 3.1 The contracts adopted (39.1 report §7)

- `ConversationsApi.unreadCount()` → `GET …/conversations/unread-count` (`{ count }`) and `markAllRead()` → `POST …/conversations/read-all` (`{ marked }`): no `If-Match`, no key (idempotent, forward-only), the XSRF header from the interceptor as on every write.
- `ConversationView` gains `mine` → `GET …/conversations?filter=mine`.
- `WorkspaceMember.displayName` (optional in the type, always sent since 39.1); names are `displayName ?? email` through `memberName()` (a blank name counts as none).
- `conversationsRead`: `WorkspaceRealtime` listens on the page's one connection (the member group is already joined by `SubscribeNotifications`) and tells `onConversationsRead` listeners; `ConversationRealtime.listenRead(tenantId, …)` keeps only the current workspace's (compared without letter case). The list re-reads its page in place. It is not replayed after a reconnect: the list already re-reads on the conversation listeners' `null`. Documented in `human-handoff-realtime-inbox.md` §16.3.

### 3.2 The list (`conversation-list`, `inbox-rows.ts`, `mark-all-read/`)

- **Head:** the heading (`h1` when nothing is open, `h2` beside an open conversation; `aria-labelledby` on the list's `section`), the live state, the description (as a page only), the views (`app-segmented` with links: `view-all`, `view-needs-person` + `needs-person-count`, new `view-mine`), and two quiet actions: `app-mark-all-read` (new) and Refresh (`refresh-list`, kept).
- **Rows** (`inboxRow()` in `inbox-rows.ts`, a pure function with its own spec): avatar (`app-avatar`, initials of a name, or a code's first two characters: "7K", "12" for `••••1234`), the channel in visually hidden words, `row-customer` (unread: weight 600), the time, the sender and one line of preview (`dir="auto"`), then one line of chips:
  - `row-reason`: the server's `needsPerson`/`needsPersonReason`, in every view (every row in the needs-a-person view); coral (`failed`) for `AiTurnFailed`, gold (`need`) otherwise;
  - who has it, once (note 13): `row-handled-by` (taken over) else new `row-assigned` (assigned), gold `team` chips, the name in a `bdi` (`dir="auto"` for a display name, `ltr` for an email); a chip that does not fit beside the others takes a second chip line (never a third), and is cut at the name's own end when it must be;
  - `row-voice`: "AI is answering" while the AI has it and nothing waits; "Person" for a person's conversation whose taker the API does not know (pre-37.4 takeovers);
  - `row-unread`: the 9 px primary dot at the line's end, with "Unread" for a screen reader.
- **Selected:** `--color-selected` tint, the 2 px `edge-marker` and `aria-current="page"` (kept).
- **Keyboard:** Up/Down/Home/End between rows (kept), every row still an ordinary link in the Tab order.
- **States:** loading (six skeleton rows of the real row's shape, lines at `1lh`), error with retry, refresh failure kept above the rows, three empty states (all, needs a person, new: Mine), no permission (the heading and the refusal, no views or actions).
- **Mark all as read** (`MarkAllRead`): count first; 0 → a toast "Every conversation is already read." and no dialog; otherwise `app-confirm-dialog` (glass, focus on Cancel, Escape and backdrop cancel) titled "Mark every conversation as read?" with the count and "Only your own unread conversations change; everybody else on the team keeps theirs."; confirm → `read-all` → a toast with the number marked, and the list re-reads at once. A second press while busy is ignored (the button is never disabled, so focus stays). Refusals are worded by code (`toApiError`), as an error toast.

### 3.3 The frame (`conversation-inbox`, `inbox-placeholder`)

- From 1280 px: `grid-template-columns: var(--inbox-list-width) minmax(0, 1fr)`, 12 px gap (`--column-gap`), 14 px from the window (`--shell-padding`; the side bar keeps its own 12 px on the start side). Both panes are solid panels: `--color-panel`, 1 px `--color-line`, `--radius-panel`, `overflow: clip`. The list's rows scroll inside its panel.
- Below 1280 px: the list is a page (a panel in the page frame), the conversation another (unchanged).
- The placeholder sits on the panel colour.

### 3.4 Shell follow-ups from 39.3

- `NavItem.shortLabel`: the inbox's is `nav.inboxShort` ("Inbox" / «المحادثات»); the bottom bar shows `shortLabel ?? label`. The Arabic `--bottombar-height: 4.875rem` override is deleted, so the bar is 64 px in both languages. The side bar, the More sheet, the top bar's area title, the page heading and the tab title keep «صندوق المحادثات».
- `GLOSSARY.md`: the Inbox row's Match becomes `صندوق المحادثات/المحادثات`, with a note that the short form belongs to the bottom bar only. (Prettier re-aligned the glossary table, so the diff shows every row; only the Inbox row changed.)
- The page title (`title.inbox`) and the list heading (`inbox.title`) say "Inbox"; `title.conversations` and `inbox.heading` are gone.

### 3.5 Tokens

`_tokens.scss` component tier: `--inbox-list-width: 22rem`, `--unread-dot-size: 0.5625rem`, `--live-dot-size: 0.4375rem`. Every other value in the new styles is an existing token.

### 3.6 Screens

| Screen | What changed | Components and tokens | Tests |
| --- | --- | --- | --- |
| Inbox (list), all three views | panel, head, views with Mine, rows with avatar and chips, quiet unread, mark all read, skeleton | `app-segmented`, `app-avatar`, `app-chip`, `app-voice-chip`, `app-confirm-dialog`, `app-empty-state`; `--color-panel`, `--color-line`, `--radius-panel`, `--radius-tile`, `--color-selected`, `--color-hover`, `--chip-*`, `--unread-dot-size`, `--inbox-list-width` | `conversation-list.spec.ts` (35), `inbox-rows.spec.ts` (8), `mark-all-read.spec.ts` (6), `arabic-render.spec.ts` (+3), e2e needs-a-person, one-connection, slow-reply, take-over |
| Inbox with a conversation open (1280 px and wider) | two panels, 12 px apart | `--column-gap`, `--shell-padding` | e2e deep-link-newest-first, take-over |
| Inbox, nothing open | placeholder on the panel | `--color-panel` | — |
| Phone bottom bar | one-line «المحادثات», 64 px in Arabic | `NavItem.shortLabel`, `--bottombar-height` | `arabic-render.spec.ts`, `bottom-bar.spec.ts` (unchanged), Chrome live check |

Screenshots (`<evidence>\shots\`), 1440 and 390 px, English light and Arabic dark at least:

- before (18): `inbox-*`, `inbox-needs-person-*`, `inbox-open-1440-*`, `inbox-support-*`, `inbox-denied-*` with `-before`;
- after: the same screens plus `inbox-mine-*`, `inbox-mark-all-*` (the confirmation), `inbox-loading-*` (the skeleton), 1024 and 768 px, the modes (`inbox-reduced-transparency-*`, `inbox-reduced-motion-*`, `inbox-forced-colors-*`), and the live flows (`live-mark-all-*`, `flow-slow-*`, `flow-failed-*`, `flow-taken-over-*`), all with `-after`.

## 4. Decisions

1. **One "who has it" chip** (note 13): the taker when somebody took the conversation over, else the assignee. Both are gold `team` chips: in the inbox only voice chips appear (Q4).
2. **"AI is answering" only when nothing waits.** It is today's product meaning of `handling = Ai` ("The AI is answering" in the detail), but beside "The AI could not reply" or "Returned to the AI unanswered" it would contradict the row, so the reason chip replaces it.
3. **The reason chip in every view**, from the server's `needsPerson` (38.7a), not only in the needs-a-person view; a summary without the field falls back to today's inference.
4. **The unread dot at the end of the chips' line** (board Inbox), not before the name (today); the name's weight is the second cue and the word "Unread" the third.
5. **"Mark all as read" in a quiet tool line under the views**, with its word, not an icon-only button: it is rare and consequential, and 352 px leaves room. Refresh stays beside it.
6. **No dialog when nothing is unread**: a toast says so. The dialog's count is the count asked for; the toast after it uses `marked` (a message can arrive in between).
7. **The deferred visitor toast ("Visitor … wrote. The AI is answering.", plan §4) does not ship.** The row moving to the top with its dot, the needs-a-person count and the opt-in sounds already say it; a toast per message would stack on a busy inbox and repeat the list the reader is looking at; and "The AI is answering" can be untrue a second later (a failed turn, a takeover). Sounds keep Phase 38's rules unchanged. Recorded in MASTER.md §12.
8. **The conversation panel holds the customer panel until 39.4b.** The plan's three panes need the thread and the customer panel as separate panels; that split is the thread's work (39.4b), so here the open conversation is one panel with its details inside.
9. **Glossary short form by widening the Match** (`صندوق المحادثات/المحادثات`), as the owner's assistant asked; the check still flags «صندوق الوارد». The cost: «المحادثات» alone now passes for "Inbox" anywhere, so the note limits it to the bottom bar.

10. **A "who has it" chip that does not fit takes a second chip line.** Shrinking it beside a reason chip left 4 px of an e-mail (measured), and cutting the RTL container showed the address's tail («…ple»). Now the name is a flex item cut at its own end, and the chip moves under the reason when even the words cannot fit: at most two chip lines, found in Chrome on the owner's Mine view and fixed before the after captures.

Departures from the specification or the board:

| Where | Specification / board | Shipped | Why |
| --- | --- | --- | --- |
| Board Inbox: search field | shown | not shipped | no search in the API (BA39-3, plan §3.2.4) |
| Board Inbox: "Live" | one word | "Live updates on" / "Live updates paused" | today's words, which also say the paused state |
| Board Inbox: row name 14 px | 14 px | `--text-body-strong` (15 px, 500; 600 unread) | the spec's body size; no 14 px text token with a safe Arabic line height |
| Board Inbox: time 12 px | 12 px | `--text-caption` (12 px Latin, 13 px Arabic) | the token's own Arabic floor |
| Board Inbox: a chip only on some rows | some rows bare | every row has one | a row always says who answers (AI, a person, or who has it) |
| Board Inbox: no "Mark all as read" or Refresh | — | a quiet tool line | the 39.1 contract (note 6) and today's Refresh |
| Spec §6 rows: preview only | — | "who wrote it" before the preview | today's behaviour (rule 4), quiet |
| Three panes | list · thread · customer panel | list · conversation (with its details inside) | decision 8 |

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS** ("All matched files use Prettier code style!"; `gates/format-check.log`) |
| `npm run test:ci` | **PASS**: 140 files, 2,006 tests, 0 failures (HEAD: 138 / 1,979). Free memory before: 12.85 GB. New: `inbox-rows.spec.ts` (8), `mark-all-read.spec.ts` (6); changed: `conversation-list.spec.ts` (35, +8), `conversation-realtime.spec.ts` (+2), `workspace-realtime.spec.ts` (+1), `arabic-render.spec.ts` (+3, the list rendered in Arabic, the plural sentences, the short label). `gates/test-ci.log` |
| `npm run build` | **PASS**: 0 warnings, no budget exceeded (component styles under 4 kB). Free memory before: 12.86 GB. **Startup JavaScript 148,457 B gzip** (loader + application chunk, `scratch/startup-js.mjs`), HEAD 148,413 B (+44 B: the `conversationsRead` handler and the short label), 38.0 baseline 138,907 B (+6.9 %), ceiling 152,797 B. The inbox's code stays in its lazy chunk (`conversation-inbox` 31.01 kB raw / 8.32 kB transfer). `gates/build.log`, `gates/startup-js.json` |
| `npm run i18n:check` | **PASS**: 2,493 messages, 0 errors, 0 warnings (the glossary accepts the short form). `gates/i18n-check.log` |
| `npm run e2e` | **PASS**: 46 journeys (23 × en-LTR, ar-RTL), 46 passed, 0 failed, 0 retries, 3.2 min (started 16:47:54, free memory 12.9 GB), full suite including the throttling journey. `NABRA_E2E_OUT` / `NABRA_E2E_STATE_DIR` under `<evidence>\e2e\`. Canary PASS before it. `gates/e2e-1.log`. **Second full run: 46 passed, 0 failed, 0 retries, 3.0 min** (started 17:46:04, 58 minutes after the first, free memory 13.91 GB; canary PASS before it; `gates/e2e-2.log`). Neither run was limited by the 15-minute rule. |
| Backend gates | **NOT RUN**: nothing under `backend/` changed in this step. |
| Chrome matrix | **PASS**, Playwright library from outside the repository driving the installed Google Chrome (headless, temporary profiles; the Chrome DevTools MCP was not used, so the long matrix could run unattended in one process). Owner: All, Needs a person, Mine and an open conversation; analyst: the refusal; × 390/768/1280/1920 × en/ar × light/dark = **80 loads, 0 problems**: no page overflow, exactly one `h1`, no unnamed control, no console error or warning, no blur on a row, at most three blurred layers, no target under 24 px, no touch target under 44 px below 992 px, no row overflow, axe (WCAG 2.2 AA rules) clean at 1280. `chrome/results-after.json` |
| Modes | **PASS**: reduced transparency by media query and by the in-app setting (0 blurred layers), reduced motion (row transition 0.01 ms; nothing moves), forced colours (rows as links, chips bordered, the unread dot in CanvasText), each at 1440 and 390, en-light and ar-dark: no overflow, one `h1`, no unnamed control, no console error. |
| Keyboard | **PASS** (en and ar at 1440, en at 390): from the heading, Tab goes All → Needs a person → Mine → Mark all as read → Refresh → the first row; Down, End and Home move between rows; Enter opens the conversation and focus lands on its `h1`; focus ring 2 px primary. The dialog opens on Cancel; Escape closes it (39.2b component). |
| Live flows (AI stand-in) | **PASS**, on the owner's open list while a visitor writes and the support agent acts: `[[slow]]` → the row arrives live with "Customer …" and "AI is answering", the AI's reply 12 s later; `[[fail]]` (visitor told 503) → "The AI could not reply" in coral; the support agent's take-over → "Waiting for the team" and "Handled by p36.support@…" live; their return → the take-over chip goes. No console errors. Shots `flow-*`. |
| Mark all as read, live | **PASS**: the count read first (`GET unread-count`), the dialog names it, `POST read-all`, toast "3 conversations marked as read."; the owner's second tab (Arabic, dark) dropped its dot live with one list read; pressed again with nothing unread → no dialog, toast "Every conversation is already read." |
| Sounds | **PASS** through the e2e journeys `needs-a-person` (the failed-turn sound plays, nothing for the baseline) and `sound-test`; unchanged code (`InboxSignals`, sound rules). |
| p95 of a 50-row render | **116 ms** (p50 90 ms, max 127 ms, 30 runs; the API's real first 50 rows served for each list read; click on a view to 50 rows painted, two frames; dev server build). **Layout shift during five in-place refreshes: 0.** |
| Contrast | **PASS**: 80 measurements of the new pairs in Chrome (rest, hover and selected rows, both themes; MASTER.md §13.3). The one value under 3:1 is the gold chip's decorative dot on its own fill (1.91 light), a 39.2b pair: the word carries the meaning. |
| RTL | **PASS**: logical properties only; `dir="auto"` on previews and display names, `dir="ltr"` on codes and emails (cut at their own end: «استلام المحادثة: p36.owner@omniai.e…»); the view count is an LTR island; the dot sits at the line's inline end. |
| Touch targets | **PASS**: views and quiet buttons 44 px on coarse pointers, rows ≥ 98 px tall; matrix found none under 44 px below 992 px. |
| Fonts | **No change** in this step (39.2a's files and per-language preload unchanged); no new font weight (the list uses 400/500/600 already loaded). |
| Provider canary | **PASS** at the start, before each e2e run and at the end (`gates/canary.txt`, `gates/canary-resume.txt`, `gates/canary-end.txt`): every provider name absent, SMTP host loopback, the stand-in switch listed as info. |

## 6. See it live

- Stack: the p36 dev API on `http://127.0.0.1:5027` (AI stand-in on), the dev server on `http://localhost:4200`, Mailpit on `http://127.0.0.1:8026`; all left running.
- Sign in as **Owner** (`p36.owner@omniai.example`) or **Support agent** (`p36.support@omniai.example`) from `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`, workspace Northwind Operations.
- `http://localhost:4200/conversations` at 1440 px: the list panel beside the conversation panel. Try All · Needs a person · Mine; Up/Down/Home/End on the rows.
- As the support agent (who still has about a thousand unread conversations): "Mark all as read" shows the count; Cancel leaves everything as it was. Open a second tab first to watch its dots go when you confirm.
- Write to `http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw` with `[[slow]]` or `[[fail]]` in the message and watch the row; take the conversation over as the support agent and watch the owner's row.
- Switch to Arabic and to a phone width (390 px): the bottom bar reads «المحادثات» on one line, 64 px tall.
- The **Analyst** (`p36.analyst@omniai.example`) sees the refusal under "Inbox".

## 7. Backend asks

None new. BA39-3 (search) stays open: the board's search field is not shipped.

## 8. New Arabic for my review

| English | Arabic | Where |
| --- | --- | --- |
| Inbox | صندوق المحادثات | list heading (`inbox.title`), tab title (`title.inbox`) |
| Inbox (short) | المحادثات | phone bottom bar only (`nav.inboxShort`) |
| Mine | محادثاتي | the third view |
| Nothing is yours right now | لا توجد محادثات لك الآن | Mine, empty |
| Conversations assigned to you, or that you took over, appear here. | تظهر هنا المحادثات المسندة إليك، والمحادثات المستلَمة من قِبلك. | Mine, empty |
| AI is answering | يردّ الذكاء الاصطناعي | row chip |
| Mark all as read | تحديد الكل كمقروء | the action and the dialog's button |
| Mark every conversation as read? | تحديد كل المحادثات كمقروءة؟ | dialog title |
| Only your own unread conversations change; everybody else on the team keeps theirs. | يقتصر ذلك على المحادثات غير المقروءة لديك، ولا يتغيّر شيء لدى بقية أعضاء الفريق. | dialog body |
| Every conversation is already read. | كل المحادثات مقروءة بالفعل. | toast, nothing unread |
| No conversations are unread. (zero) | لا توجد محادثات غير مقروءة. | dialog count |
| 1 conversation in this workspace is unread. | في مساحة العمل هذه محادثة واحدة غير مقروءة. | dialog count (one) |
| 2 … (two) | في مساحة العمل هذه محادثتان غيرُ مقروءتين. | dialog count (two) |
| {n} … (few, 3–10) | في مساحة العمل هذه {n} محادثات غير مقروءة. | dialog count |
| {n} … (many, 11–99; other) | في مساحة العمل هذه {n} محادثة غير مقروءة. | dialog count |
| Every conversation was already read. (zero) | كانت كل المحادثات مقروءة بالفعل. | toast after marking |
| 1 conversation marked as read. | حُدِّدت محادثة واحدة كمقروءة. | toast (one) |
| 2 … (two) | حُدِّدت محادثتان كمقروءتين. | toast (two) |
| {n} … (few) | حُدِّدت {n} محادثات كمقروءة. | toast |
| {n} … (many, other) | حُدِّدت {n} محادثة كمقروءة. | toast |

Kept as they were (now on chips): «استلام المحادثة: …» (Handled by, Q8), «مسندة إلى …» (Assigned to). "Mark as read" uses «تحديد», never «تعليم» (38.2a ruling).

## 9. Risks and open issues

- **The thread and the customer panel are 39.4b.** The open conversation is one panel that still holds its details panel; bubbles, the header and "Handled by" in the thread are unchanged.
- **E-mail names wrap.** Until members set display names (39.6a adds the dialog), an e-mail beside a reason chip takes a second chip line (rows of 124 px instead of 98).
- **The glossary check is a little looser for "Inbox"** (decision 9).
- **The notifications panel's Arabic still says «تعليم … كمقروء»** (`notifications.markRead`, `notifications.markAllRead`), against the 38.2a ruling that the inbox uses «تحديد». Not in this step's scope; it belongs to 39.6b (notifications).
- **The 50-row p95 was measured on the dev server's build** (development mode), so production is expected to be faster; no production-serve measurement was run.
- **Visitor conversations created by the checks:** four "P39.4a" conversations in Northwind (the `[[slow]]`/`[[fail]]` flows and the mark-all checks); the owner's markers were marked read twice by the live check; the support agent took one conversation over and returned it.

## 10. Proposed checkpoint commit message

```text
feat: lay the inbox out as solid panels, give the list All · Needs a person · Mine, show each row's avatar and channel badge with at most two chips (why it waits, who has it once by display name, or "AI is answering"), make unread a dot and a heavier name, add "Mark all as read" that names the count first and clears the member's other tabs live, call the page "Inbox", and give the phone's bottom bar a one-line «المحادثات» (Phase 39.4a)
```
