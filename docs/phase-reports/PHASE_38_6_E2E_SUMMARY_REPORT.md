# Phase 38.6: End-to-end journeys, the full pass, and the Phase 38 summary

## 1. Summary

- The automated browser journeys now cover everything Phase 38 promised, in English and Arabic: 23 journeys, each run in both languages (46). The whole suite passed twice with no retries: 46/46 in 3.3 min, then 46/46 in 3.0 min.
- New journeys prove that a slow AI reply keeps the visitor's "typing" and the inbox's "not replied yet" until it arrives, and that a person can take over, answer the visitor live and hand the conversation back to the AI, which answers again.
- No journey depends on another any more: each sets up or puts back the state it needs (the Arabic journeys alone: 22/22).
- The full Chrome pass covered the 28 routes Phase 38 touched, in both languages, both themes and four widths: 448 page loads with no overflow, no console error, one `h1` each, no unnamed control and no axe finding; keyboard paths and reduced motion clean. With sounds off, a failed AI turn still shows on screen and plays nothing.
- Nothing a user sees changed in this step. `PHASE_38_SUMMARY.md` closes the phase: every item, one list of backend asks, all 332 Phase 38 Arabic strings for review, and notes for the redesign.

## 2. Starting point

- Branch `master`, HEAD `311c807` ("feat: count days in the workspace time zone, compare with the previous period, and write reports in the reader's language"), the 38.5 commit. `git status --short` was empty. Matches "Phase 38.5 accepted".
- Dev stack: API `omniai-p36-api` (up 2 h), Mailpit, Postgres and Redis running; the dev server on 4200. The API was recreated with `bash p36-stack.sh api` (stand-in on) and the provider canary passed before any traffic (§5).
- Skills loaded before building, all four present: `frontend-pro-max:frontend-testing-quality`, `frontend-engineering-lead`, `web-performance`, `frontend-security`.

## 3. Changes by area

`git status --short` at the end (nothing under `backend/`, `deploy/` or `frontend/src/` changed):

```
 M frontend/e2e/knowledge-index.e2e.ts
 M frontend/e2e/rename-workspace.e2e.ts
 M frontend/e2e/sign-in-throttled.e2e.ts
 M frontend/e2e/support/fixtures.ts
 M frontend/e2e/support/global-setup.ts
 M frontend/e2e/time-zone.e2e.ts
 M frontend/playwright.config.ts
?? docs/phase-reports/PHASE_38_6_E2E_SUMMARY_REPORT.md
?? docs/phase-reports/PHASE_38_SUMMARY.md
?? frontend/e2e/slow-reply.e2e.ts
?? frontend/e2e/take-over.e2e.ts
```

7 files changed (+151 / −65), 2 journeys added (174 lines), 2 documents added.

### 3.1 What changed and why

**Two new journeys, on a second chat link.**
- `e2e/slow-reply.e2e.ts`: a visitor on the hosted page (`?lang=en|ar`, a browser of their own) writes a message with `[[slow]]`. The visitor sees the agent typing; once the API shows the message stored, the inbox opens the conversation and shows "The AI has not replied to the last message yet" (`awaiting-ai`); at that same moment the visitor still sees typing and no reply. The reply arrives at least 10 s after the send, ends both waits, and the inbox shows it live.
- `e2e/take-over.e2e.ts`: the visitor writes and the AI answers; the owner takes over (the button becomes "Return to AI", focus moves to the composer, the "took over" divider is drawn); the owner's reply reaches the visitor live after the "A person from the support team joined" line; returning to the AI moves focus back to "Take over" and draws "returned it to the AI"; the visitor's next message is answered by the AI again, live in the inbox, with the visitor's second divider.
- **Why a second chat link.** The public chat counts new conversations per network *and per chat link*: five in five minutes. The run's first link was exactly at five (global setup, `needs-a-person` and `one-connection` in both languages). Global setup now creates a second active agent with its own website chat (one link per agent), and the two new journeys start their four conversations there. The limits are unchanged and nothing goes around them; the e2e workspace is on the development plan, which allows it.

**Order dependencies removed** (section 1 asks that no journey depend on another or on the order):
- `sign-in-throttled`: the Arabic project relied on the English one having already spent the address's five failures. Each project now tries up to six times and stops at the first 429, so either one alone, or both in any order, passes. A normal run still spends exactly six failures.
- `time-zone`: it assumed the workspace was on UTC. It now puts it on UTC through the API first (it already did at the end).
- `rename-workspace`: the name was put back only if every step passed. A `finally` now restores the name read at the start, through the API with `If-Match`.
- `knowledge-index`: it read "the first document" of the list, which could be the other language's upload. Each project now uploads `e2e-returns-<lang>.txt` and finds its own row by name.
- `playwright.config.ts`: the comment that said the projects must run in this order now says no journey depends on it.

**The suite's support:** `fixtures.ts` adds `e2eHandoverChatKey()` and `openVisitor()` (a visitor's own browser context, no session, `?lang`); `global-setup.ts` makes agent-with-chat a helper and calls it twice.

**Nothing else.** No product file, string, style, package or budget changed. The summary (`PHASE_38_SUMMARY.md`) is new.

### 3.2 Adopted contracts (exercised end to end for the first time)

| Screen | Endpoint, event or code | Fields read | Tests |
| --- | --- | --- | --- |
| Hosted chat (visitor) | `POST /api/public/webchat/{key}/conversations`; `POST …/conversations/{id}/messages` (held about 11 s by `[[slow]]`); the public realtime ticket and hub | `conversationId`; reply `sender`; voice changes for the handoff lines | `slow-reply`, `take-over` (en, ar) |
| Conversation detail | `GET /api/tenants/{id}/conversations/{id}` (`messageCount`, handling); `GET …/messages?tail=true&limit=40&include=events`; `conversationChanged` | `handling`, last message `sender`, `events[] { kind: TakenOver, ReturnedToAi, AiTurnFailed }` | `slow-reply` (`awaiting-ai`), `take-over` |
| Handling bar | `POST …/take-over`, `POST …/return-to-ai` with `If-Match` | new handling, ETag | `take-over` |
| Composer | `POST …/conversations/{id}/messages` with `clientMessageId` | the stored message | `take-over` |
| Workspace settings (rename restore) | `GET /api/tenants/{id}` (ETag), `PUT …/name` with `If-Match` | `name` | `rename-workspace` |

### 3.3 Screenshots

- **Before:** none. This step changed no screen; the 38.1a to 38.5 "before" captures stand.
- **After:** the matrix's captures at 390 and 1920 px, every route, both languages and themes: `<evidence>\p38-6\chrome\shots\<route>-<lang>-<theme>-<width>.png`; the account menu `chrome\shots\menu\`; the sounds section `chrome\shots\sounds-section-{en,ar}.png`; the sounds-off pass `chrome\shots\sounds-off-failed-turn-en.png`; the numbers `chrome\shots\numbers\`.

## 4. Decisions

- **The gaps, against section 1's list.** Already covered and kept: signing in and a signed-out page without a 401 (`sign-in`, `signed-out-no-401`); a visitor's message arriving live with its unread dot, and `[[fail]]` with the divider, the needs-a-person count and the AI-failed sound (`needs-a-person`); knowledge indexing and the agent preview with the stand-in (`knowledge-index`, `agent-preview-reply`); the hosted page with `?lang=ar`, right to left from the first paint (`hosted-page-ar`); the rename with its stale notice and the time zone with its labels, in the suite's own workspace (`rename-workspace`, `time-zone`); an Arabic invitation and an Arabic report (`invitation-language`, `arabic-report`). **Missing, now added:** `[[slow]]` (`slow-reply`) and a takeover with a return to the AI (`take-over`). The plan's 2a/2b journey "takeover divider" was proved only on the seeded long conversation; this one drives the buttons.
- **A second chat link rather than a wait.** Spacing the journeys out to stay under five conversations per five minutes would make the suite slow and timing-dependent; a second agent and link in the run's own workspace gives the new journeys their own allowance through the same limits. Rejected: raising the limit for e2e runs (a backend setting, and it would hide the real behaviour).
- **`slow-reply` opens the conversation once the message is stored** instead of waiting on an already open inbox, because the server announces the turn only when it settles (BA-13). The journey says so in its comment. Rejected: a fixed sleep (flaky) and clicking Refresh in a loop (the same thing, less honest).
- **Independence proved, not assumed.** Besides the reasoning per journey (§3.1): the Arabic project alone without the throttling journey (22/22), and the Arabic throttling journey alone in a fresh window (1/1, spending its own six attempts).
- **The 15-minute spacing.** Each full run spends six failed sign-ins: run 1's throttling journey ran at 18:16:02, run 2's at 18:32:57 (17 minutes later), and the Arabic-alone check at 19:04, so no 15-minute window saw more than six.
- **Axe in the Chrome pass, not in the suite.** The test inventory notes that the suite has no axe checks. Adding `@axe-core/playwright` is a new package (rule 3), so axe runs in the live pass from the evidence folder's copy, as in 38.1a to 38.5.
- **The Arabic paint measured on the production configuration** (`ng serve --configuration production --port 4300`, stopped afterwards with its process tree), as 38.2a did; the dev-build run is kept as `arabicpaintDevBuild` in `results.json` but is not comparable (LCP about 42 s against 20 s).
- **Two production advisories left for your decision** (§9): `npm audit --omit=dev` reports `@angular/router` 22.1.5 (three SSR-only denial-of-service advisories; this app has no SSR) and `zod` 4.4.3 (pulled by `@angular/forms` and the CLI; not in the built bundle). Upgrading is a package change (rule 3). Neither was added by Phase 38.
- **Deferred:** nothing from this step's list.

## 5. Verification

Gates ran one at a time, never beside a browser check or another gate. Logs in `<evidence>\p38-6\gates\`.

| Check | Result |
| --- | --- |
| Worktree at start | **PASS**: `master`, `311c807`, clean |
| Provider canary | **PASS** before any traffic, and again before each e2e run (`canary-e2e-run1.txt`, `canary-e2e-run2.txt`, `canary-e2e-throttle-ar.txt`): every listed secret and `OMNIAI_LIVE_*` absent, SMTP username and password absent, SMTP host loopback (Mailpit), `OMNIAI_DEV_AI_STANDIN` listed as info |
| `npm run format:check` | **PASS** (`format-check.log`) |
| `npm run test:ci` | **PASS**: **120 files, 1,766 tests, 0 failures** (21.5 s). Free memory before: 6.41 GB. Unchanged from 38.5: no spec or product file changed |
| `npm run build` | **PASS**: 0 warnings, budgets met; initial 314.47 kB raw / 73.87 kB transfer. Free memory before: 6.77 GB |
| Startup JavaScript (loader + application chunk, gzip, same tool as 38.0) | **145,199 B** (43 files, 438,236 raw, 128,523 brotli). 38.5: 145,212; 38.0 baseline: 138,907, so **+4.5%**, under the 10% ceiling (152,797). No change from this step |
| `npm run i18n:check` | **PASS**: **2,392 messages, 0 errors, 0 warnings** |
| `npm run e2e`, run 1 | **PASS**: **46 journeys (23 × en-ltr, ar-rtl), 46 passed, 0 failed, 0 retries, 3.3 min**, after the canary; 6.66 GB free before (`e2e-run1.log`, `<evidence>\p38-6\e2e-run1\e2e-results.json`) |
| `npm run e2e`, run 2 | **PASS**: **46 passed, 0 failed, 0 retries, 3.0 min**; 6.82 GB free before (`e2e-run2.log`, `e2e-run2\`) |
| Independence | **PASS**: the Arabic project alone without the throttling journey, **22/22** (1.5 min, `e2e-ar-alone.log`); the Arabic throttling journey alone in a fresh window, **1/1** (`e2e-throttle-ar-alone.log`) |
| Trial runs while building (subsets, reported for honesty) | Trial 1 (`trial-1.log`): 8 passed, **2 failed**: `slow-reply` in both languages, because an open inbox is not told of the stored message until the turn ends (BA-13). The journey was changed to open the conversation once the message is stored, not retried. Trial 2: `slow-reply` 2/2 |
| Synthetic data | Six e2e runs (two trials, two full runs, two independence runs) each created one synthetic account and one "E2E" workspace with two agents and two chat links: **6 accounts, 6 workspaces** (35 → 41 `e2e-` workspaces). Northwind: 3 conversations through its hosted chat (the sounds-off pass with one `[[fail]]` turn, the `conversationChanged` count with one self-assignment by the owner, and an Arabic conversation for the paint) |
| Backend gates | **NOT RUN**: no file under `backend/` changed in this step (or in Phase 38) |
| Chrome matrix | **PASS**: **28 routes × en/ar × light/dark × 390/768/1280/1920 = 448 page loads: 0 page overflow (document and main), 0 console errors or warnings, exactly one `h1` on each, 0 unnamed controls, 0 axe findings** (tags WCAG 2.0/2.1 A and AA and 2.2 AA, which include `color-contrast` and `target-size`), `dir` right on all 448 (`chrome\matrix.json`). Routes: the 5 signed-out pages; `/overview`, `/conversations`, `?view=needs-person`, the 232-message conversation, `/notifications/preferences`; `/knowledge`, a document, `/knowledge/upload`, `/ai-agents`, an agent, a website chat, the hosted chat `/chat/{key}?lang=`; `/workspace-settings`, `/team`, `?invitations=revoked`, `/team/invite`, `/roles`, a role, `/billing`; `/analytics`, `/reports`, `/reports/schedules`, `/notifications`. Plus the open account menu in 16 cells, 0 problems (`chrome\menu.json`). Playwright library driving the installed Chrome with a temporary profile, not the Chrome DevTools MCP, because signing in through the MCP would put the demo password in tool arguments (rule 10) |
| Keyboard paths and focus | **PASS**: Tab through every route in two cells (en light 1280, ar dark 390): 56 walks, **every stop shows a focus ring, none off screen, focus never falls to the page** (`chrome\keyboard.json`). Focused boxes under 24 px are inline text links (WCAG 2.5.8's inline exception) and 16 px checkboxes and radios whose labels are the targets; axe `target-size` passed everywhere (touch size is a Phase 39 note) |
| Contrast | **PASS**: axe `color-contrast` on all 448 loads and the menu's 16, both themes |
| Reduced motion | **PASS**: on all 28 routes with `prefers-reduced-motion: reduce`, no running animation and transitions of 0.01 ms (150 ms without) (`chrome\motion.json`) |
| RTL mirroring | **PASS**: `dir="rtl"` and `lang="ar"` on all 224 Arabic loads; the take-over and slow-reply journeys check the visitor page's direction; captures at 390 and 1920 px in `chrome\shots\` |
| The sound controls' names | **PASS**, en and ar: the switch "Sounds" / «الأصوات» (`menuitemcheckbox`, checked), "Sound and alert settings" / «إعدادات الصوت والتنبيهات», the slider "Volume" / «مستوى الصوت» with value text "70%", "Play a test sound" / «تشغيل صوت تجريبي», the switch "Desktop alerts" / «تنبيهات سطح المكتب» |
| One pass with sounds off | **PASS**: with sounds off and a click on the page, a visitor's `[[fail]]` turn (503 by design) raised the needs-a-person count 141 → 142 and drew "The AI could not reply" on the transcript, while **nothing played and no sound file was fetched**; the menu switch read off and Notification preferences said "Sounds are off. You can turn them on from the account menu." The unread dot was read the moment the conversation opened, before the 1-second "seen" rule, so that reading proves nothing; `needs-a-person` asserts the dot goes |
| AI stand-in | Used by `slow-reply` (`[[slow]]`, reply at least 10 s later), `take-over` (replies before and after the handoff), `needs-a-person` (`[[fail]]`), `agent-preview-reply`, `knowledge-index`, in both languages; replies start "Local test reply:" / «رد تجريبي محلي:» |
| Mailpit | `invitation-language` in both runs: the subject "Nabra: You have been invited to a workspace" / «نبرة: تلقّيت دعوة إلى مساحة عمل» and the HTML's language, read as booleans; no body or link kept |
| Section 3 numbers | See `PHASE_38_SUMMARY.md` §3: deep link 11 requests (1 transcript read, 12,660 B, 40 rows; first message 1.4 s); one `conversationChanged` 4 reads; analytics 8 requests with one dashboard read; Arabic paint (production configuration) fallback 1,100 ms; E11 p50 290 / p95 308 ms (`chrome\results.json`) |
| Security pass over Phase 38 | **PASS**: the skill's scanner on `frontend/` (297 files): 0 findings at medium or above (`security-scan.md`). By hand on every line Phase 38 added under `frontend/src`: no `innerHTML`, `bypassSecurityTrust*`, `eval`, timers for the session check, tokens in storage or logs; `postMessage` only on same-origin `BroadcastChannel`s with messages matched by `kind`; `Notification.requestPermission` only from the desktop-alerts switch's click; the session check only on load, focus and visibility. `npm audit --omit=dev`: 2 high advisories, not reachable here (§4, §9) |
| Test inventory | 0 critical, 0 high, 0 medium; 1 low (no axe inside the suite; §4) (`test-inventory.md`) |
| Secrets | **PASS**: `scratch\secret-scan.mjs` over the evidence folder, `frontend/src`, `frontend/e2e`, `playwright.config.ts`, this report and the summary: **813 files, 0 with the demo password, 0 with an invitation link**. The demo password was read only into the check script's memory; the e2e account's password is generated per run and held in its process only. `<evidence>\p38-6\tmp` (the cookie jar, the 4300 server's log) **deleted**; the e2e cookie state was deleted by the suite's teardown |

## 6. See it live

Dev server `http://localhost:4200`, API `http://127.0.0.1:5027` with the AI stand-in, Mailpit `http://127.0.0.1:8026`, all left running. Sign in as the demo **owner** (`p36.owner@omniai.example`) from `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`; workspace Northwind Operations.

- **A slow reply:** open `http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw?lang=ar` in a private window and send «هل وصل طلبي؟ [[slow]]». The agent shows as typing for about 11 s. Meanwhile, in the signed-in window, open that conversation from Conversations: it says «لم يردّ الذكاء الاصطناعي على الرسالة الأخيرة بعد.» until the reply arrives, then shows the reply. (An inbox opened *before* the send learns of the message only with the reply: BA-13.)
- **Take over and hand back:** in that conversation press "Take over", type a reply and send it; the private window shows "A person from the support team joined" and your reply. Press "Return to AI", then write again from the private window: the AI answers.
- **Sounds off:** account menu → "Sounds" off; from the private window send «[[fail]]». The "Needs a person" count rises and the transcript says "The AI could not reply", with no sound.
- **The suite:** from `frontend/`, after `bash p36-stack.sh canary`, set `NABRA_E2E_OUT` and `NABRA_E2E_STATE_DIR` to a folder outside the repository and run `npm run e2e` (about 3 minutes; 15 minutes apart, because each run spends six failed sign-ins).
- **The phase:** `docs/phase-reports/PHASE_38_SUMMARY.md`.

## 7. Backend asks

One new: **BA-13**. The whole phase's list, BA-1 to BA-13, is in `PHASE_38_SUMMARY.md` §5.

- **BA-13: announce the conversation when the customer's message is stored, not only when the turn settles.** `CustomerTurnRunner` commits the customer's message before it asks the AI (`BeginTurnAsync`), but its one `conversationChanged` comes after the reply or the failure (`AnswerAsync`). An inbox that is already open therefore shows nothing new, and no "The AI has not replied to the last message yet" line, until the turn ends: about 11 s with `[[slow]]`, up to the provider's timeout in production. A person who opens or refreshes the conversation meanwhile sees the line. Shipped meanwhile: nothing changes; the `slow-reply` journey opens the conversation once the API shows the message stored, and says why in its comment.

## 8. New Arabic for your review

None in this step: no product string was added or changed (`npm run i18n:check`: 2,392 messages, as at 38.5). The test journeys' own Arabic (visitor messages such as «هل يمكنني تغيير عنوان التوصيل؟») is test data, not product text.

**The whole phase's Arabic** (332 strings: 331 new, 1 changed, by screen with the English) is Appendix A of `PHASE_38_SUMMARY.md`.

## 9. Risks and open issues

- **Two production advisories, for your decision** (a package change, rule 3): `@angular/router` 22.1.5 has three high-severity SSR denial-of-service advisories (GHSA-ff3f-86qr-9cv3, GHSA-62vg-58rm-qff7, GHSA-57xq-rjx2-v5xh). Nabra is built without SSR, so none is reachable. `zod` 4.4.3 has one (GHSA-mrhx-c7xc-m3w5), but it comes in through `@angular/forms` and the CLI and is not in the built bundle. Both have fixes; `npm audit fix` would move them. Neither came from Phase 38.
- **BA-13:** an inbox that is already open does not show the waiting line during a slow AI reply until the reply or failure, because the server announces only then. Honest, but slower to notice than it could be.
- **The suite's limits.** It stays inside the public chat's five-per-five-minutes by using two chat links per run, and spends six failed sign-ins per full run; two full runs must be 15 minutes apart. Each run leaves one synthetic account and one "E2E" workspace in the dev database (41 now), recognisable by name for removal before launch.
- **The Arabic font swap** still shows the fallback for about 1.1 s on a slow first visit (owner's ruling: keep the preload; Phase 39 revisits font loading).
- **The deep link makes 11 requests** against the plan's "≤ 10"; the 11th is the needs-a-person read added in 38.2b on purpose (one per page, shared with the sounds and the count).
- **Numbers are lab numbers** on the dev stack (the Arabic paint on the production configuration served by `ng serve`); RUM stays outside Phase 38 (BA-8).
- **The Arabic for your review** is the whole phase's 332 strings in `PHASE_38_SUMMARY.md` Appendix A; earlier Revisions already changed some of them, and this is their final state.

## 10. Proposed checkpoint commit message

```
test: complete the end-to-end journeys and close Phase 38 with its summary

- Add the slow-reply journey (the visitor's typing and the inbox's
  "not replied yet" until a [[slow]] reply arrives) and the take-over
  journey (take over, answer the visitor live, return to the AI, which
  answers again), in English and Arabic, on a second chat link of the
  run's own workspace.
- Make every journey independent of the others and of their order:
  the throttling journey spends its own attempts, the time-zone journey
  starts from UTC, the rename restores the name if it fails, and each
  upload finds its own document.
- 23 journeys in both languages, 46 passed twice with no retries; the
  Chrome pass over the 28 routes Phase 38 touched is clean.
- Write the Phase 38 summary: what changed for the people who use Nabra,
  the commits and final numbers, every item of the list, the backend
  asks BA-1 to BA-13, all 332 Arabic strings, and notes for Phase 39.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
