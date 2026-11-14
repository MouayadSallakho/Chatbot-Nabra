# Phase 39.4b: the inbox thread

Step 39.4b of the Calm Glass redesign (plan: `PHASE_39_0_REDESIGN_PLAN_REPORT.md` §3.2.4, §3.6 "39.4b inbox: thread", §12). 39.4a (the list) is committed as `cf7b109`. Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-4b\`.

The run was cut once: the computer slept at 19:21 local time (14 November) during the after screenshots and woke at 21:10. The worktree survived intact; Docker and the dev stack did not. Docker Desktop and the p36 stack were restarted with the `stack.md` commands, the provider canary passed before any traffic (`gates/canary-resume.txt`), the demo accounts were signed in again, and every Chrome check, screenshot and gate in section 5 was run again after the restart on the final code.

## 1. Summary

- From 1280 px the inbox is three solid panels: the list, the conversation, and a customer panel (264 px) with the customer's avatar, label, "Customer since", their number of conversations, their other conversations as links, and this conversation's facts.
- The conversation speaks in three voices: the customer on the start side in cream; the AI on the end side in emerald with its small orb; the team on the end side in gold with the person's avatar and **display name** (the email where there is none) under the bubble. Takeover, return and "the AI could not reply" lines say who and when.
- While a reply is owed, a calm line says "The AI has not replied to the last message yet." beside the breathing orb; it is not shown for a conversation returned to the AI unanswered, because the AI will not answer those.
- The header says who is answering in words ("The AI is answering" / "A person is answering"), "Handled by <name>" beside it, and offers Take over / Return to AI and a new **Assign** menu; the composer is one rounded field with "Send reply" inside it. Your own reply carries your name at once.
- A reader at the end of a conversation stays at the end when someone takes it over (the header grows), so the next message is in view instead of behind "1 new message".

## 2. Starting point

- Branch `master`, HEAD `cf7b109` ("lay the inbox out as solid panels…" (Phase 39.4a)), worktree clean (`git status --short` empty), as the prompt's starting point says.
- Dev stack running (p36: `omniai-p36-api` with the AI stand-in, Mailpit, Postgres, Redis; dev server on `http://localhost:4200`); provider canary PASS before any traffic (`gates/canary-start.txt`).
- Skills loaded before any work: `frontend-pro-max:enterprise-erp-ux`, `frontend-pro-max:ui-ux-design-lead`, `frontend-pro-max:angular-enterprise`, `frontend-pro-max:motion-interaction`. The 21st.dev MCP ("magic") failed to connect ("Not authenticated"), so nothing was sourced from it; the thread is built from our own primitives (plan §12 allows this).

## 3. Changes by area

`git status --short` (26 modified, plus this report):

```text
 M docs/architecture/human-handoff-realtime-inbox.md
 M docs/design/MASTER.md
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/features/conversations/conversation-composer/conversation-composer.html
 M frontend/src/app/features/conversations/conversation-composer/conversation-composer.scss
 M frontend/src/app/features/conversations/conversation-context/conversation-context.html
 M frontend/src/app/features/conversations/conversation-context/conversation-context.scss
 M frontend/src/app/features/conversations/conversation-context/conversation-context.spec.ts
 M frontend/src/app/features/conversations/conversation-context/conversation-context.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.html
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.scss
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.spec.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.ts
 M frontend/src/app/features/conversations/conversation-inbox/conversation-inbox.scss
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.html
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.scss
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.spec.ts
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.ts
 M frontend/src/app/features/conversations/conversations.routes.ts
 M frontend/src/app/features/conversations/inbox-placeholder/inbox-placeholder.ts
 M frontend/src/app/shared/avatar/avatar.spec.ts
 M frontend/src/app/shared/avatar/avatar.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_primitives.scss
 M frontend/src/styles/_tokens.scss
?? docs/phase-reports/PHASE_39_4B_INBOX_THREAD_REPORT.md
```

Nothing under `backend/` or `deploy/` changed. No package was added, upgraded or removed.

### 3.1 The frame (`conversation-inbox`, `conversation-detail`, `inbox-placeholder`)

- The inbox's second grid column no longer draws a panel; what fills it draws its own: from 1280 px the open conversation is a grid of two solid panels, the thread (`minmax(0, 1fr)`) and the customer panel (`--customer-panel-width`, 264 px), 12 px apart (`--column-gap`); the placeholder and the error/denied frames are a panel of their own. "Details" still shows or hides the customer panel (remembered per browser, `InboxState`). Below 1280 px the conversation is a page, and the panel opens on demand above the transcript (today's behaviour).
- **Header** (board Inbox): back link (below 1280 px; 44 px on touch now), the customer's label as the `h1` (title-2), one quiet facts line (channel · started · messages · status), quiet Refresh (icon with a hidden word) and Details (pressed look while open). Then the handling row: the voice chip saying it in words (`handlingSentence`: "The AI is answering" / "A person is answering"), `handled-by` beside it (note 14), and at the end Take over (primary) or Return to AI (secondary) and **Assign**, both 40 px; the help sentence under it (still the buttons' description). The voice track stays. On a phone the one-line header of 39.3 is unchanged; the facts, the handling row and Refresh open with Details.
- **Assign** (`app-menu`, trigger `data-testid="assignee"`): "Assigned to" label, then `menuitemradio` items "Nobody" and the people who can reply (`displayName ?? email`, a name in its own direction, an email left to right), the current one ticked; a former assignee the server no longer offers stays listed. Choosing sends the same conditional `PUT …/assignment` with `If-Match`; a 412 shows the stale notice and is never retried; focus returns to the trigger.
- **Skeleton** (`data-testid="conversation-skeleton"`): the header (title, facts line, chip and action), three bubbles (the business's with its orb), the composer; from 1280 px the customer panel's avatar, two lines and two tiles beside it.
- **Own reply:** the reply's response still answers `sentBy: null` (39.1 decision 8), so `onSent` puts the signed-in member (`AuthService.user()`: id, email, display name) on it until the re-read replaces it with the server's.

### 3.2 The transcript (`conversation-transcript`)

- One `li.message` per message (`message-customer`, `-ai`, `-team`), a grid: the `.bubble` (text, and on WhatsApp the delivery word), the speaker's mark beside it on the business's side (AI: `app-orb`, 26 px; team: `app-avatar` `size="xs"` `tone="member"`, 26 px gold, initials of the display name or the address's first letter), and the meta line under the bubble: customer "time" (the word "Customer" for a screen reader), AI "<agent> · time" (emerald label), team "<name> · time" (gold label, `data-testid="message-author"`). "· time" never wraps apart. Bubbles grow to 72 % (`--message-max-width`), 12 px apart.
- Event lines: the words, then " · time" (`<time datetime>`); who by `displayName ?? email`; failed coral 600, takeover gold, return emerald (39.2b's tones).
- Day separator: a quiet panel-soft pill, still an `h3`.
- Waiting line: the words on the AI's side with the breathing orb (`[thinking]="true"`), shown only while the AI handles, the customer wrote last, no failed turn after it, and the server's `needsPersonReason` is not `ReturnedUnanswered` (new input from the detail).
- A message arriving while the reader is here rises 8 px and fades (`bubble-rise` on the row); none on opening.
- **Pinned to the end:** a `ResizeObserver` on the transcript keeps a reader who was at the end at the end when its pane changes height; `settleIn` follows when the reader was pinned. Found in the live flow (the header grows on a takeover, so the next message landed behind "1 new message"), fixed and covered by a spec.

### 3.3 The customer panel (`conversation-context`)

- Customer card: 64 px avatar with the channel badge, the label (`app-customer-label`), the masked number beside a name, and "Customer since <date>" from the customer read.
- Tiles: Conversations (the customer read) and Channel (the conversation).
- "Earlier conversations" (only with `customers.view`): the three newest of the customer's **other** conversations (the server's order), each a panel-soft link "Website chat · Closed, 4 Sep" to `/conversations/{id}`; "No earlier conversations." when there are none; a link to the customer's page ("See all their conversations" when there are more than three, else "Open the customer's page"). Loading keeps the loaded geometry (the count tile, the since line, one line and the link), failure says it quietly with Try again, and nothing ever covers the thread.
- "Conversation" facts for every reader of the conversation: AI agent, Assigned to (`displayName ?? email`), Started, Last activity, Messages, Status; the routing hint for a reader who may assign.
- One read per customer (`GET …/customers/{id}`, `CustomersApi` now also provided by the conversations route): a live update re-reads the conversation, never the customer; no read at all without `customers.view`. Documented in `human-handoff-realtime-inbox.md` §17.

### 3.4 The composer (`conversation-composer`)

One panel-soft field (radius 20, `--radius-composer`) with the control border (3:1, owner decision Q3) and the product's focus ring on the frame; the box (`#conversation-reply`, two lines, grows with `field-sizing`) and "Send reply" (`send-reply`) inside it at the end; the hint, the keys and the count under it. Idempotency key, draft per conversation, Ctrl+Enter, the limit and the failure/retry are unchanged.

### 3.5 Tokens, shared pieces and docs

- `_tokens.scss`: `--customer-panel-width: 16.5rem`, `--radius-composer: 20px`, `--message-max-width: 72%`. Every other value is an existing token.
- `app-avatar`: `size="xs"` (the orb's 26 px) and `tone="member"` (the team's gold, `--member-avatar-*`).
- `_primitives.scss`: the conversation's skeleton shapes and the handling chip's sweep moved here from the component (its stylesheet would have been 5.37 kB against the 4 kB budget; budgets unchanged).
- `MASTER.md`: §14.3 "The thread and the customer panel", four decision-log rows, §14.2 frame line updated. `human-handoff-realtime-inbox.md` §17.

### 3.6 Screens

| Screen | What changed | Components and tokens | Tests |
| --- | --- | --- | --- |
| Conversation, 1280 px and wider | thread and customer panel as two panels; header, handling row with "Handled by", Assign menu; three voices with orb and avatar; timed event lines; day pill; waiting line with the orb; composer field | `app-voice-chip`, `app-menu`, `app-orb`, `app-avatar` (xs, member), `app-customer-label`, `.bubble-*`, `.handoff-divider`; `--color-panel(-soft)`, `--color-line`, `--radius-panel/-tile/-input/-composer`, `--customer-panel-width`, `--message-max-width`, `--voice-*-label`, `--chip-*` | `conversation-detail.spec.ts` (+8), `conversation-transcript.spec.ts` (+10, 3 changed), `conversation-context.spec.ts` (+4), `arabic-render.spec.ts` (+2, 1 changed), e2e deep-link-newest-first, take-over, slow-reply, needs-a-person |
| Conversation below 1280 px (tablet, phone) | the same thread as a page; customer panel on demand above the transcript; phone one-line header unchanged; back link 44 px on touch | as above | `conversation-detail.spec.ts` (phone header tests unchanged and passing) |
| Customer panel | new: card, tiles, earlier conversations, facts; loading/failed/empty; no read without `customers.view` | `app-avatar` lg, `.customer-tile`, `.earlier-row`, `.skeleton` | `conversation-context.spec.ts`, `conversation-detail.spec.ts` |
| Composer | one panel-soft field with Send inside | `--radius-composer`, `--color-control-border`, `--color-focus-ring/-halo` | `conversation-composer.spec.ts` (unchanged, passing), e2e take-over |
| Loading | thread + panel skeleton of the real geometry | `.skeleton-*` | `conversation-detail.spec.ts` (skeleton test) |
| Inbox, nothing open | placeholder is its own panel | `--color-line`, `--radius-panel` | — |

Screenshots in `<evidence>\shots\` (PNG):

- **before** (22, taken on `cf7b109`): `thread-long-*`, `thread-events-*`, `thread-failed-*`, `thread-named-*`, `thread-support-*` at 1440 and 390, English light and Arabic dark; `thread-details-390-*`.
- **after** (73): the same screens at 1440, 1024, 768 and 390 (`thread-details-*` at 1024/768/390 with Details open), `thread-loading-*` (the skeleton, 1440 and 390), the modes (`thread-reduced-transparency-*`, `thread-reduced-transparency-setting-*`, `thread-reduced-motion-*`, `thread-forced-colors-*`, 1440 with the Assign menu open and 390), and the live flows (`flow-slow-*` the waiting line, `flow-replied-*`, `flow-failed-*`, `flow-taken-over-*`, `flow-team-reply-1440-en-light` (the owner) and `-ar-dark` (the second member), `flow-returned-*`).

## 4. Decisions

1. **The customer panel is shown to every reader of the conversation; only the customer read's extras need `customers.view`.** The plan said the panel is hidden without that permission, but the facts it holds today (label, masked number, channel, AI agent, assignee, times, status) belong to the conversation and every `conversations.view` reader sees them now; hiding them would remove behaviour (rule 4). "Customer since", the count and earlier conversations appear only with `customers.view`, and the read is never sent without it. At least as safe as the plan (rule 20).
2. **No WhatsApp window in the header.** The board's "window open, 23 h left" is not in the API (no field on the conversation or the WhatsApp read), and today's UI shows none ("as today" is nothing). Working it out in the browser from the last customer message would copy a server rule (`CustomerServiceWindow`, 24 h) that the server may change and could disagree with what it enforces. Backend ask BA39-6.
3. **Assign moved into the header as a menu** (the plan's "take over / return / assign" in the header). The native select in the old details panel is gone; `data-testid="assignee"` is on the menu's trigger (no e2e journey used the select or `#conversation-assignee`). The panel still shows who it is assigned to, with the routing hint.
4. **The handling chip says the sentence** ("The AI is answering" / "A person is answering"), so the separate sentence beside it is gone; "Handled by <name>" sits beside the chip (note 14). On a phone the compact chip keeps the short "AI" / "Person".
5. **A 26 px gold avatar beside team replies.** The specification gives the AI its orb and is silent for the team; without a mark the team's bubbles would sit 34 px further out than the AI's on the same side. The avatar keeps both on one edge and says "a person" without colour alone.
6. **Who wrote it goes under the bubble** (board Inbox; spec "the person's name under it"), replacing 39.2b's speaker line inside the bubble in the inbox. The customer's line is the time alone, as on the board, with "Customer" for a screen reader.
7. **No waiting line for a conversation returned to the AI unanswered.** The orb "breathes while the AI is answering"; for `ReturnedUnanswered` the AI will not answer, so the line would be untrue. The server's reason decides.
8. **Pinned to the end** (3.2): a bug found live, fixed in this step.
9. **The panel's loading shapes match the usual loaded geometry** (count tile, since line, one line and the link), measured: layout shift on the panel's arrival went from 0.008 to 0.
10. **Earlier conversations show channel · status and the date**, three at most, then a link to the customer's page. The board's titles ("Grinder warranty") are sample text: the API has no subject per conversation.
11. **The help sentence stays visible beside the buttons from 1280 px.** At 1280 px the thread is 369 px wide and the header 259–279 px tall; hiding the sentence would shorten it, but it explains a consequence (messages sent while a person had it stay unanswered). "Details" gives the thread 276 px back.
12. **The skeleton and the sweep live in `_primitives.scss`** to keep the component under its 4 kB style budget; budgets unchanged.
13. **Sounds and toasts unchanged** (Phase 38 rules; 39.4a decision 7).

Departures from the specification or the board:

| Where | Specification / board | Shipped | Why |
| --- | --- | --- | --- |
| Board Inbox: header "window open, 23 h left" | shown | not shipped | not in the API (BA39-6), decision 2 |
| Board Inbox: AI bubbles without an orb | no orb | orb beside each AI bubble | the specification wins (plan §4) |
| Board Inbox: team bubbles without a mark | none | 26 px gold avatar | decision 5 |
| Board Inbox: chip "Handled by Sara" in the header | one chip | voice chip in words + "Handled by Sara" beside it | note 14 places "Handled by" beside the handling chip |
| Board Inbox: "Assign" as a primary button | primary | secondary menu trigger | one primary per area: Take over is the primary |
| Board Inbox: composer border in the line colour | `line` | `--color-control-border` | owner decision Q3 (3:1 for a control's edge) |
| Board Inbox: "Send" | "Send" | "Send reply" | today's words (rule 4) |
| Board Inbox: earlier conversations with titles | titles | channel · status, date | no subject in the API (BA39-7) |
| Plan §3.2.4: panel hidden without `customers.view` | hidden | shown with the conversation's own facts; extras need the permission | decision 1 |
| Board Inbox: "Customer since 3 September" | day and month | the long date with the year | `LocaleFormat` `longDate`, the same in both languages |

## 5. Verification

All on the final code after the restart, one gate at a time. Free memory before each heavy gate is in its log.

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**: "All matched files use Prettier code style!" (`gates/format-check.log`) |
| `npm run test:ci` | **PASS**: 140 files, **2,032 tests**, 0 failures (39.4a: 140 / 2,006). Free memory before: 10.4 GB. `gates/test-ci.log` |
| `npm run build` | **PASS**: 0 warnings, no budget exceeded (the conversation's component style first came to 5.37 kB; moved to the primitives, decision 12). Free memory before: 10.28 GB. **Startup JavaScript 148,535 B gzip** (loader + application chunk, `scratch/startup-js.mjs`; `gates/startup-js.json`), HEAD `cf7b109` 148,457 B (39.4a's recorded figure, same commit; +78 B: the avatar's `xs` size and `member` tone, which the shell already loads), 38.0 baseline 138,907 B (+6.9 %), ceiling 152,797 B. The thread's code stays in its lazy chunk (`conversation-detail` 76.41 kB raw / 17.48 kB transfer). `gates/build.log` |
| `npm run i18n:check` | **PASS**: 2,503 messages, 0 errors, 0 warnings (39.4a: 2,493; +10). `gates/i18n-check.log` |
| `npm run e2e` | **PASS**: 46 journeys (23 × en-LTR, ar-RTL), **46 passed, 0 failed, 0 retries, 3.4 min**, full suite including the throttling journey (started 18:59:54 UTC, free memory 10.44 GB, canary PASS before it). `NABRA_E2E_OUT` = `<evidence>\e2e\run1`, `NABRA_E2E_STATE_DIR` = `<evidence>\tmp\e2e-state`. `gates/e2e-1.log`. Second full run: see the row below |
| `npm run e2e`, second full run | **PASS**: **46 passed, 0 failed, 0 retries, 5.2 min**, full suite, started 19:15:24 UTC (15.5 minutes after the first; free memory 12.37 GB; canary PASS before it), so neither run was limited by the 15-minute rule. Slower than the first in total, spread across the journeys and the setup; the slowest journey (the `[[slow]]` reply) took 18.4 s against 15.3 s, inside its limits. `NABRA_E2E_OUT` = `<evidence>\e2e\run2`. `gates/e2e-2.log` |
| Backend gates | **NOT RUN**: nothing under `backend/` changed in this step. |
| Chrome matrix | **PASS**, Playwright library from outside the repository driving the installed Google Chrome (headless, temporary profiles; the Chrome DevTools MCP was not used, so the long matrix ran unattended in one process). Owner on three conversations (232 messages with team replies; takeover + return; three failed turns), the support agent (composer), the analyst (refusal) × 390/768/1280/1920 × en/ar × light/dark = **80 loads, 0 problems**: no page overflow, exactly one `h1`, no unnamed control, no console error or warning, at most three blurred layers (1 at rest, 2 with a menu), no blur on a message, no message outside the thread, no target under 24 px, none under 44 px on touch widths, axe (WCAG 2.2 AA rules) clean at 1280. The first pass found the back link 28 px tall on touch widths (390, 768): fixed (44 px on a coarse pointer) and the affected half re-run clean. `chrome/results-after.json` (`matrix1`, `matrix2`) |
| Modes | **PASS**: reduced transparency by media query and by the in-app setting (0 blurred layers, the Assign menu solid), reduced motion (orb still, `--orb-breath` 1, rise `8px × 0`; fades only), forced colours (bubbles outlined, menu solid), each at 1440 (menu open) and 390, en-light and ar-dark: no overflow, one `h1`, no unnamed control, no console message. |
| Keyboard | **PASS** (support agent, en and ar at 1440, en at 390): from the heading, Tab goes Refresh → Details → Take over → Assign → the transcript region → the customer panel's link → the shell; Assign opens on "Nobody" with Enter, ArrowDown moves, Escape returns to the trigger; Take over puts focus in the composer, Return to AI puts it on Take over; on a phone Details opens with Enter and keeps focus. Focus ring 2 px. |
| Live flows (AI stand-in) | **PASS**, owner's open thread (English) while a visitor writes and the support agent (Arabic, dark, given the synthetic display name "Sara Haddad" for the run and cleared after) acts from their own browser: `[[slow]]` → the waiting line with the breathing orb 2 s after sending, the reply 13.5 s after; `[[fail]]` (visitor told 503) → the coral "The AI could not reply · 9:53 PM" and the reason on the row; the second member's take-over → "A person is answering", "Handled by Sara Haddad", "Sara Haddad took over · 9:53 PM" live; their reply → "Team, Sara Haddad · 9:53 PM" with the gold avatar on both sides, and on their own screen their name at once (`sentBy: null` covered); their return → "Sara Haddad returned it to the AI · …" and the chip back to the AI. No console messages. `chrome/results-after.json` (`flows`), shots `flow-*` |
| Sounds | **PASS** through the e2e journeys `needs-a-person` (the failed-turn sound) and `sound-test`; `InboxSignals` and the sound rules are unchanged. |
| p95 of a 50-row list render | **93 ms** (p50 76, max 96; 30 runs) with a conversation open beside it, on the **production build** (`prod-serve.mjs` on :4300, warm; the first, cold run gave 163 ms). Dev-server runs varied 98–229 ms. Opening a thread to its newest 40 bubbles painted: p95 167 ms (production, warm). `chrome/results-prod.json` |
| Layout shift when messages arrive | **0 in the thread.** Traced with sources: two arrivals on an open thread (one normal, one `[[slow]]`) 0; five realtime re-reads 0 (after decision 9; 0.008 before). In the full flow 0.003 remains, and its source is the list row's preview (`row-text`, 39.4a's list) whose sender word changes width ("Customer" → "AI"), not the thread. |
| Contrast | **PASS**: 46 measurements of the new pairs in Chrome (both themes, four conversations; MASTER.md §13 style): quiet text on the panel 5.73 / 6.05, team speaker 6.22 / 9.38, AI speaker 8.75 / 6.71, event lines ≥ 6.22 / 6.71, day pill 5.17 / 5.47, handling chip 6.2 / 6.74, composer edge 3.41 vs its field and 3.78 vs the panel (dark 3.27 / 3.62, ≥ 3:1), tiles 5.17 / 5.47, link 8.75 / 6.71. "Handled by" uses `--color-text-secondary`, the same pair as the section headings (9.98 / 10.24). |
| RTL | **PASS**: logical properties and a grid that mirrors; customer on the start side, business on the end side, the orb and avatar outside the bubble on the end side; `dir="auto"` on messages and display names, `dir="ltr"` on emails and codes; "· time" stays together; Arabic events «تعذّر على الذكاء الاصطناعي الرد · 7:10 م», «استلام المحادثة: Sara Haddad · 7:10 م»; no gendered verb (spec checks «تتولا» is absent). |
| Touch targets | **PASS** after the back-link fix: no control under 44 px on touch widths in the thread. |
| Fonts | **No change**: 39.2a's files and per-language preload; the thread uses weights 400/500/600 already loaded; no new face. |
| Provider canary | **PASS** at the start, after the restart, and before the e2e runs (`gates/canary-start.txt`, `canary-resume.txt`, `canary-e2e1.txt`, `canary-e2e2.txt`, `canary-end.txt`): every provider name absent, SMTP host loopback, the stand-in switch listed as info. |

## 6. See it live

- Stack: the p36 dev API on `http://127.0.0.1:5027` (AI stand-in on), the dev server on `http://localhost:4200`, Mailpit on `http://127.0.0.1:8026`; all left running.
- Sign in as **Owner** (`p36.owner@omniai.example`) and, in another browser, **Support agent** (`p36.support@omniai.example`) from `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`, workspace Northwind Operations.
- At 1440 px open `http://localhost:4200/conversations/01a15013-917d-75a7-870c-98ceb0b9e7f8` (232 messages): team replies in gold with their avatar and name under the bubble, the customer panel on the right. "Details" hides and shows it.
- `http://localhost:4200/conversations/01a1d5f5-01b4-78f6-9b5b-47bd7861869d`: takeover and return lines with who and when; Assign (owner) opens the menu of people.
- Write to `http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw` with `[[slow]]` in the message, open it from the owner's list: the waiting line with the breathing orb, then the reply. Then `[[fail]]`: the coral line. Take it over as the support agent and reply: the owner sees "Handled by …" and the gold reply live.
- Switch to Arabic and dark, and to 390 px: the one-line header; Details opens the facts, the handling row and the customer panel.
- The **Analyst** (`p36.analyst@omniai.example`) sees the refusal on a conversation link.

## 7. Backend asks

| Id | Ask | What ships meanwhile |
| --- | --- | --- |
| BA39-6 | WhatsApp customer-service window on the conversation (or WhatsApp) read: open or closed, and when it closes | Nothing about the window in the header (decision 2) |
| BA39-7 (optional) | A short subject or first line per conversation on the customer read, for the panel's "Earlier conversations" | Channel · status and the date |

BA39-3 (search) stays open.

## 8. New Arabic for my review

| English | Arabic | Where |
| --- | --- | --- |
| Assign | إسناد | the header's menu button (`conversation.assign`) |
| Customer since {date} | عميل منذ {date} | customer panel, under the name (`customerPanel.since`) |
| Conversations | المحادثات | customer panel tile |
| Channel | القناة | customer panel tile |
| Earlier conversations | المحادثات السابقة | customer panel heading |
| Loading the customer… | جارٍ تحميل بيانات العميل… | customer panel, for a screen reader while it reads |
| Could not load this customer's other conversations. | تعذّر تحميل محادثات العميل الأخرى. | customer panel, failed read |
| No earlier conversations. | لا توجد محادثات سابقة. | customer panel, none |
| See all their conversations | عرض كل محادثات العميل | link to the customer's page (more than three) |
| Open the customer's page | فتح صفحة العميل | link to the customer's page |

Kept as they were, now in new places: «تمّ استلام المحادثة من قِبل …» (Handled by, beside the chip; owner decision Q8), «استلام المحادثة: …» and «إعادة المحادثة إلى الذكاء الاصطناعي: …» (the event lines, now followed by « · » and the time), «يردّ الذكاء الاصطناعي» / «يردّ أحد أعضاء الفريق» (now the chip's words), «مسندة إلى» (the menu's label), «لا أحد» (Nobody). The event lines' messages are unchanged: only their placeholders now carry a display name.

## 9. Risks and open issues

- **The thread is narrow at 1280 px** (369 px with the side bar and the customer panel open; the header 259–279 px tall). Readable and checked, but tight; "Details" frees 276 px. A container query that hides the help sentence in a narrow thread would fit within the budget if the owner prefers it.
- **0.003 layout shift in the list** when a row's sender word changes width (39.4a's row preview), seen in the full live flow; not in the thread.
- **Nobody has a display name yet** (39.6a adds the dialog), so real data still shows emails; the live check set "Sara Haddad" on the support agent only for its run and cleared it (204 both times).
- **Data left by the checks** in Northwind: three visitor conversations "P39.4b" (slow, failed, taken over, replied to and returned by the support agent); conversation TV3Y89 taken over and returned twice by the support agent (keyboard check); two arrival probes on conversation ED8YET; the e2e suite's own workspaces (removed by its teardown).
- **The run was cut by sleep** (19:21–21:10 local); every check was re-run after the restart, and the machine was held awake for the rest of the run.
- **Perf numbers on the dev server vary a lot**; the production-build figures are the ones to compare.
- **Clean-up:** the cookie jars and the e2e state are deleted; `<evidence>\tmp\` keeps only `devserver.log`, the running dev server's own output (held open by it; no secrets). The local production server on :4300 and the keep-awake process are stopped; the dev API and the dev server are left running.

## 10. Proposed checkpoint commit message

```text
feat: lay the open conversation out as a thread and a customer panel from 1280 px, speak in three voices (the customer in cream, the AI in emerald with its orb, the team in gold with the person's avatar and display name under the bubble), give takeover, return and failed-turn lines who and when, show a calm waiting line with the breathing orb, say who is answering and "Handled by" beside it with Take over / Return to AI and an Assign menu in the header, make the composer one rounded field, keep a reader at the end when the header grows, and add the customer's history for readers who may see customers (Phase 39.4b)
```
