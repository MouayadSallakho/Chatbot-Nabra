# Phase 38.1b: Foundation (one realtime connection, the sounds)

## 1. Summary

- The bell and the inbox now share one live connection on every page, instead of opening two on the inbox. Both still update live, and after a dropped connection both catch up by themselves.
- The account menu has a "Sounds" switch, on by default, for members who can read conversations.
- A new "In this browser" section on Notification preferences has the volume, "Play a test sound" and desktop alerts, which stay off until a person turns them on.
- The three kalimba sounds are in the product and load only when first needed. Inbox events do not trigger them yet: that wiring is 38.2b, as planned.
- The switches on Notification preferences had been drawing at 16 px because of an older style conflict. They now draw as designed, at 40×24.

## 2. Starting point

| Fact | Value |
| --- | --- |
| Branch | `master` |
| HEAD | `bff23e0` feat: word account refusals by code in both languages, end the signed-out 401, and add browser journeys |
| Worktree | clean (`git status --short` printed nothing) |
| Part built | 38.1b of the approved plan: A7 and A8. A1 to A6 and the A9 harness were 38.1a |
| Dev stack | running at the start; canary PASS before any new traffic |

## 3. Changes by area

`git status --short`, grouped:

```
 M frontend/e2e/support/fixtures.ts  global-setup.ts
 M frontend/src/app/core/notifications/notification-realtime.ts
 M frontend/src/app/core/realtime/conversation-realtime.ts  conversation-realtime.spec.ts
 M frontend/src/app/features/notifications/notification-preferences/  (.html .scss .ts .spec.ts)
 M frontend/src/app/layout/app-shell/app-shell.ts
 M frontend/src/app/layout/profile-menu/profile-menu.ts  .html  .spec.ts
 M frontend/src/app/shared/icon/icon-paths.ts
 M frontend/src/locale/messages.json  messages.ar.json
?? docs/phase-reports/PHASE_38_1B_FOUNDATION_REPORT.md
?? frontend/e2e/one-connection.e2e.ts  sound-test.e2e.ts  support/api.ts
?? frontend/public/sounds/  (three .v1.wav files)
?? frontend/src/app/core/notifications/notification-realtime.spec.ts
?? frontend/src/app/core/realtime/workspace-realtime.ts  workspace-realtime.spec.ts
?? frontend/src/app/core/sounds/  (7 files and their specs)
```

Nothing in `backend/` or `deploy/` changed. No package was added.

| Area | What changed and why |
| --- | --- |
| One connection (A7) | New `core/realtime/workspace-realtime.ts` owns the page's connection to `/hubs/workspace-inbox`. SignalR is imported lazily through an async builder token. The shell starts it once and it follows the workspace: a chosen workspace opens it, another replaces it, sign-out closes it. It always joins notifications, and joins conversations only while the member holds `conversations.view`, leaving if that is lost. After a reconnect it re-joins both groups, and only then tells everybody to re-read |
| Facades | `ConversationRealtime` and `NotificationRealtime` keep their signatures, so no screen changed. A screen still on the page during a workspace switch hears nothing of the next workspace |
| The files | The three sounds, copied to `public/sounds/` with `.v1` names. SHA-256 matches the pack exactly: `56c4aa0c…`, `76e9c14b…`, `404d920d…` |
| The sound code | `sounds.ts` and `sound-settings.ts` are the only parts in the startup bundle. The player, the rules (`sound-rules.ts`), the tab election (`tab-claim.ts`) and the desktop alerts (`desktop-alerts.ts`) are lazy. One sound per 2 seconds, AI failed before needs a person before new message, one tab plays, unlocks on the first real gesture, silent when audio is unavailable |
| Account menu | A `menuitemcheckbox` "Sounds" switch and a "Sound and alert settings" link, both only with `conversations.view` |
| Notification preferences | The "In this browser" section, outside the email form and its unsaved-changes guard: volume, test sound, desktop alerts. The menu link lands on its heading |
| Style fix | The email switches and the new one take the `.switch` class. Without it the global checkbox rule won on specificity and drew a 16 px box (§4) |
| End-to-end | Two new journey files, run in both languages. Global setup also creates an active agent and a website chat, so a visitor can write in |

**Adopted contracts**

| Screen | Endpoint, event or code | Fields read | Tests |
| --- | --- | --- | --- |
| Bell | `/hubs/workspace-inbox`: `SubscribeNotifications`, `UnsubscribeNotifications` is not called; event `notificationsChanged` | none (the event only says something changed) | `workspace-realtime.spec.ts`, `notification-realtime.spec.ts`, `notification-bell.spec.ts`; e2e `one-connection` |
| Inbox list, open conversation | `Subscribe`, `Unsubscribe`; event `conversationChanged` | the conversation id | `workspace-realtime.spec.ts`, `conversation-realtime.spec.ts`; e2e `one-connection` |
| Account menu, "In this browser" | permission `conversations.view` | held permissions | `profile-menu.spec.ts`, `notification-preferences.spec.ts`, `sound-*.spec.ts`, `tab-claim.spec.ts`, `desktop-alerts.spec.ts`, `sounds.spec.ts`; e2e `sound-test` |

**Screenshots** (`<evidence>` is `C:\Users\mouay\Desktop\nabra-frontend-evidence`):

| | Where |
| --- | --- |
| Before, account menu | `<evidence>\p38-1a\chrome\shots\menu-*.png` |
| After, account menu | `<evidence>\p38-1b\chrome\shots\menu-*.png` (en and ar, light and dark, 390 and 1280 px) |
| After, every touched page | `<evidence>\p38-1b\chrome\shots\` (`overview-`, `conversations-`, `preferences-*.png`) |
| After, "In this browser" | `in-this-browser-section-en.png`, `-ar.png`, `in-this-browser-en.png`, `-ar.png` |
| After, desktop alerts | `desktop-grant.png`, `desktop-prompt.png`, `desktop-deny.png` |
| After, the live inbox | `one-connection-1280.png` |

Skills loaded: `angular-enterprise`, `api-contracts-integration`, `frontend-architecture`, `frontend-security`, `frontend-testing-quality`.

## 4. Decisions

**Differences from the prompt, following the plan (rule 20):** the volume, test sound and desktop alerts are on Notification preferences, not in the account menu (§4.3 #7b).

**Additions to the plan's design, for you to accept or change:**

- **The unlock also listens to `pointerup` and asks the browser whether this is a real gesture.** The plan said `pointerdown` or `keydown`. On a touch screen the browser counts the finger lifting, not landing, and a key like Shift is an event but not a gesture. Creating the audio context too early would log a console warning.
- **Sounds are offered only with `conversations.view`.** Section F's rules are all about the inbox, and §4.3 #2 asks about "members who can view conversations".
- **The test button plays even with Sounds switched off.** It was asked for by name, and it is how a person checks the volume before turning them on. It does not take part in the tab election, and it outranks every other sound. A second press within 2 seconds is dropped.
- **A setting changed in one tab applies to the others at once,** through the `storage` event. Otherwise turning sounds off in one tab would not silence the tab that happens to be playing.
- **A file that fails on the network is tried again next time;** a file that will not decode is not.
- **Desktop alert wording.** The alert takes its title and reason from its caller, so the three reasons arrive with 38.2b, as the plan lists them.
- **The switches on Notification preferences (not in the plan).** The page's switch styling had been defeated by a global checkbox rule of higher specificity since it was written, so all five email switches drew as 16 px boxes. The new switch uses the same class and would have been under the 24 px floor. The fix adds the design system's `.switch` class, and the size goes from 22 to 24 px high so it meets that floor. This is the one visible change to something that already existed.
- **Global setup adds an agent and a website chat,** which 38.1a deferred to the first step needing them.

**Rejected:** a slider inside the account menu (it breaks an ARIA menu), and letting the connection close when the last screen leaves (the bell needs it on every page).

**Deferred, as planned:** which inbox events cause a sound, who handles a conversation, and the 10-minute hidden-tab check are 38.2b. `Sounds.request()` has no caller yet.

## 5. Verification

| Check | Result |
| --- | --- |
| Worktree at start | **PASS**: `master`, `bff23e0`, clean |
| Provider canary | **PASS**, before the first traffic and again before the final end-to-end run |
| `npm run format:check` | **PASS** |
| `npm run test:ci` | **PASS**: **106 files, 1,352 tests**, 0 failures (38.1a: 98 and 1,246). Free memory before: **6.44 GB**. An earlier run had 2 failures, both in my new specs (a shared document, a wrong expectation) and some unhandled rejections from a real gap in `Sounds.load`; all fixed |
| `npm run build` | **PASS**: 0 warnings, budgets met; initial 313.56 kB raw / 73.66 kB transfer. Free memory before: **6.48 GB** |
| Startup JavaScript | 40 files, 435,712 B raw / **143,630 B gzip** / 127,189 B brotli. Before this step: 141,263 B. 38.0: 138,907 B. **+2,367 B in this step (over its +1,000 B share)**, **+4,723 B (+3.40%)** in total, inside the 10% ceiling and the 145,000 B phase target. The cost is the connection owner (1,400 B) and the settings (589 B), which the shell and the menu need at once |
| `npm run i18n:check` | **PASS**: **2,118 messages** (+12), 0 errors, 0 warnings |
| `npm run e2e` | **PASS** on the final code: **20 journeys, 20 passed, 0 failed, 0 flaky, 55 s**, no retries. Free memory before: **6.33 GB**. An earlier full run had **2 failures**, the same journey in both languages: my report request lacked `clientRequestId`. Fixed, not retried until it passed |
| Backend gates | **NOT RUN**: no backend change |
| Chrome matrix (Playwright, installed Chrome, temporary profile) | **PASS**: 3 routes (`/overview`, `/conversations`, `/notifications/preferences`) × 4 widths × 2 themes × 2 languages = **48 cells**, plus the open account menu in **16 cells**. Page overflow 0, console errors 0, one `h1` everywhere, 0 unnamed controls, 0 axe WCAG A/AA violations (contrast included), `dir` correct in all 64 |
| 24 px targets | One element under 24 px: the existing `.section-link` on the overview, unchanged here; axe passes it through its spacing exception. The volume is 44 px high; the switches are 24 px, and 44 px on touch |
| Why not the DevTools MCP | Signing in through it would put a password into tool arguments. Same reason as 38.1a |
| One connection, live | **PASS**: on `/conversations/{id}` one negotiation, one websocket, joins `SubscribeNotifications` then `Subscribe`. A visitor's message appeared live, and a finished report moved the bell from nothing to 1, with no further negotiation |
| Requests per event | **PASS**: one `conversationChanged` (the assignment probe of 38.0) caused **3 reads** (list, detail, transcript) and **0 bell reads**, equal to the 38.0 baseline |
| Reconnect, live | **PASS**: with the socket cut by the check, one new negotiation, both groups joined again, and the inbox and the bell both re-read. The one console line in that check is SignalR's own for the deliberate cut |
| Sign-out, live | **PASS**: the websocket closed |
| Signed-out page | **PASS**: 0 negotiations, 0 websockets, 0 sound requests |
| No `conversations.view`, live | **PASS**: a member with only `workspace.view` joined the notification group alone, and saw no switch and no section |
| Sound files lazy, live | **PASS** in both languages: 0 requests and 0 audio contexts at load; after the first click 1 context and still 0 requests; after the test button exactly one file. The files are not in any startup chunk |
| The sound itself | **PASS**: the 1.23-second buffer started on a running audio context, observed from the journey's own wrappers, with no hook in product code. Headless Chrome has no speakers, so **no person has listened to it yet** (§6) |
| Keyboard | **PASS**: arrows pass every menu item including the switch and the link; Enter opens the link and focus lands on the section heading, in view; arrows move the volume; Enter plays the test sound |
| Desktop permission, live | **PASS**: 0 prompts before the click, 1 after it. Already granted turns on with no prompt. A refusal stays off and shows the note |
| Desktop alerts, unit only | The alert itself (who, why, tag, language, click opens the conversation) is tested with fakes. Nothing calls it until 38.2b |
| Web Locks and several tabs, unit only | One tab plays, a hidden tab stands aside, and the channel fallback are tested with fakes. **NOT RUN live**: nothing requests an inbox sound yet; it belongs with 38.2b |
| Losing `conversations.view` while connected | Unit test only |
| Reduced motion | **PASS** by inspection: this step adds no animation, and the switch keeps its reduced-motion rule |
| RTL | **PASS**: `dir` correct in all 64 cells, the volume and the switches mirror (`in-this-browser-section-ar.png`), logical properties only |
| Throttling | Not changed by this step. The throttling journey still passes in both languages. It spent 6 failures at 10:44 and 6 at 11:23 UTC, each with a new random address |
| AI stand-in | Used by the live inbox checks: the visitor's messages were answered by the stand-in. No AI behaviour changed |
| Mailpit | **NOT RUN**: this part sends no email |
| Security scan (`security_scan.py`, `core`) | **PASS**: 0 findings at medium or above. Heuristic. No `innerHTML`, no bypass, no logging in the new code |
| Memory floor | No gate started under 6 GB. Free memory fell to 5.3 to 5.99 GB several times, and the one allowed page-cache drop only reached 5.41 GB, so I waited each time |
| Synthetic data | 14 accounts and 10 workspaces in `omniai_p36`, each with an "E2E" or `chk.` prefix (3 end-to-end runs, 4 Chrome runs, one of which stopped early) |
| Secret hygiene | No demo password was used in this step. Random passwords stayed in memory. `tmp\` was deleted |

## 6. See it live

The API and dev server are running.

- Sign in as `p36.owner@omniai.example` (password in `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`) and open the account menu at the top. "Sounds" is on, with a check beside it.
- Choose **Sound and alert settings**. You land on "In this browser". Move the volume and press **Play a test sound**. **Please listen to this one: it is the check I could not do.**
- Turn on **Desktop alerts**. Your browser asks only then. Nothing will alert you until 38.2b.
- http://localhost:4200/conversations: open the network panel, filter on `negotiate`, and reload. There is one.
- Switch to العربية in the account menu to read the section in Arabic, right to left.
- Sign in as `p36.analyst@omniai.example`, whose role cannot read conversations: neither the switch nor the section appears.

## 7. Backend asks

None new. Still open from 38.0: BA-2 (who handles a conversation), which limits the "new message" sound in 38.2b.

## 8. New Arabic for your review

| Id | English | Arabic | Where |
| --- | --- | --- | --- |
| `account.sounds` | Sounds | الأصوات | Account menu |
| `account.soundSettings` | Sound and alert settings | إعدادات الصوت والتنبيهات | Account menu |
| `notificationPreferences.browserHeading` | In this browser | في هذا المتصفح | Notification preferences |
| `notificationPreferences.browserLede` | These apply only to this browser, and take effect as soon as you change them. | تنطبق هذه الخيارات على هذا المتصفح فقط، ويبدأ مفعولها فور تغييرها. | same |
| `notificationPreferences.soundsOff` | Sounds are off. You can turn them on from the account menu. | الأصوات متوقفة. يمكنك تشغيلها من قائمة الحساب. | same |
| `notificationPreferences.volume` | Volume | مستوى الصوت | same |
| `notificationPreferences.testSound` | Play a test sound | تشغيل صوت تجريبي | same |
| `notificationPreferences.testSoundPlayed` | Test sound played. | تم تشغيل الصوت التجريبي. | same, for screen readers |
| `notificationPreferences.desktopAlerts` | Desktop alerts | تنبيهات سطح المكتب | same |
| `notificationPreferences.desktopAlertsDescription` | Show an alert on your computer when Nabra is in the background. It names the customer and the reason, never what they wrote. Your browser asks for your permission the first time. | اعرض تنبيهًا على جهازك عندما تكون نبرة في الخلفية. يذكر التنبيه اسم العميل والسبب، ولا يذكر أبدًا ما كتبه. سيطلب متصفحك إذنك في المرة الأولى. | same |
| `notificationPreferences.desktopUnsupported` | This browser cannot show desktop alerts. | لا يدعم هذا المتصفح تنبيهات سطح المكتب. | same |
| `notificationPreferences.desktopRefused` | Your browser is not allowing alerts from Nabra. To turn them on, allow notifications for this site in your browser's settings, then try again. | متصفحك لا يسمح بالتنبيهات من نبرة. لتشغيلها، اسمح بالإشعارات لهذا الموقع من إعدادات المتصفح، ثم حاول مجددًا. | same |

For your reading: the glossary has `الإشعارات` for Notifications and nothing for "alerts". I used `التنبيهات` for desktop alerts, so they read as different from the in-app notifications. The note about the browser's own setting keeps `الإشعارات`, because that is what the browser calls it.

## 9. Risks and open issues

- **No one has heard the sounds in Nabra yet.** The checks prove the right file started on a running audio context, but only a person can judge the level at 70%.
- **Startup JavaScript went over this step's share** by 1,367 B. 1,370 B is left under the 145,000 B target, against the 2,500 B the remaining steps planned, so that target may be missed by about 1,100 B. The 10% ceiling is not at risk.
- **Sounds still make no noise by themselves.** The test button is the only caller until 38.2b.
- **Several tabs have only been tested with fakes,** and so have the real desktop alert and its click.
- **A real refusal of the browser's prompt** was simulated by the check, because headless Chrome never answers that prompt. The unit tests cover both a refusal and a dismissal.
- **The email switches look different now,** from a small box to the intended switch, on a page this step otherwise only adds to.
- **Memory is tight:** the gates here started between 6.3 and 6.5 GB, after several waits.

## 10. Proposed checkpoint commit message

```
feat: share one live connection between the bell and the inbox, and add the notification sounds

- Open one connection to the workspace hub per page, owned by the shell:
  it follows the workspace, joins conversations only with
  conversations.view, and re-joins before everybody re-reads after a
  reconnect.
- Add the three kalimba sounds and a lazy player: one sound per two
  seconds by priority, one tab plays, unlocked by the first gesture,
  silent when audio is unavailable. Inbox events reach it in 38.2b.
- Add a Sounds switch to the account menu and an In this browser section
  with the volume, a test sound and desktop alerts, asked for only after
  a click.
- Draw the Notification preferences switches at their intended size.
- Add browser journeys for the test sound and for the bell and inbox
  live on one connection, in English and Arabic.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-11-08)

**The note.** A toggle's description should describe, not give an order, and «ما كتبه» should say whose words. One Arabic value changed in `frontend/src/locale/messages.ar.json`. English, ids and product code are unchanged, and no test asserted the old Arabic, so §1 to §7 and §9 stand. §10 still fits.

This row replaces the one of the same id in §8.

| Id | English | Arabic | Where |
| --- | --- | --- | --- |
| `notificationPreferences.desktopAlertsDescription` | Show an alert on your computer when Nabra is in the background. It names the customer and the reason, never what they wrote. Your browser asks for your permission the first time. | يظهر لك تنبيه على جهازك عندما تكون نبرة في الخلفية. يذكر التنبيه اسم العميل والسبب، ولا يذكر أبدًا ما كتبه العميل. سيطلب متصفحك إذنك في المرة الأولى. | Notification preferences |
