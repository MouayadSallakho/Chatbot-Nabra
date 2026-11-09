# Phase 38.2b: Inbox, needs a person, unread and the sounds

Part 38.2b of Phase 38.2, as split by the approved plan: **B4, B5 and the sounds wired to the inbox (F)**. 38.2a (B1, B2, B3, B6, B7) was checkpointed as `9e105ab`.

## 1. Summary

- The inbox has a "Needs a person" view with its own count, kept in the address, and the overview says how many conversations are waiting.
- Rows show an unread dot with a heavier name. A conversation is marked read only when its newest message has really been looked at.
- Nabra now plays the three sounds: a failed AI turn, a conversation needing a person, and a new message on a conversation assigned to you.
- They play from a hidden tab too (2.9 s after a failed turn, with the tab hidden for 10.5 minutes), and never for your own actions.
- However busy the workspace, one browser makes at most one needs-a-person read every 5 seconds, from one tab.

## 2. Starting point

Branch `master`, HEAD `9e105ab`, worktree clean. Phase 38.2a accepted.

## 3. Changes by area

```
 M frontend/e2e/deep-link-newest-first.e2e.ts
 M frontend/e2e/sound-test.e2e.ts
 M frontend/e2e/support/fixtures.ts
 M frontend/e2e/support/global-setup.ts
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/sounds/sounds.spec.ts
 M frontend/src/app/core/sounds/sounds.ts
 M frontend/src/app/features/conversations/conversation-composer/conversation-composer.spec.ts
 M frontend/src/app/features/conversations/conversation-composer/conversation-composer.ts
 M frontend/src/app/features/conversations/conversation-context/conversation-context.spec.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.html
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.spec.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.html
 M frontend/src/app/features/conversations/conversation-list/conversation-list.scss
 M frontend/src/app/features/conversations/conversation-list/conversation-list.spec.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.ts
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.html
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.spec.ts
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.ts
 M frontend/src/app/features/conversations/conversations-api.ts
 M frontend/src/app/features/conversations/customer-label.spec.ts
 M frontend/src/app/features/conversations/customer-label.ts
 M frontend/src/app/features/overview/overview.html
 M frontend/src/app/features/overview/overview.spec.ts
 M frontend/src/app/features/overview/overview.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
?? docs/phase-reports/PHASE_38_2B_INBOX_REPORT.md
?? frontend/e2e/needs-a-person.e2e.ts
?? frontend/e2e/support/sounds.ts
?? frontend/src/app/core/sounds/inbox-signal-rules.spec.ts
?? frontend/src/app/core/sounds/inbox-signal-rules.ts
?? frontend/src/app/core/sounds/inbox-signals.spec.ts
?? frontend/src/app/core/sounds/inbox-signals.ts
```

**B4, needs a person.** A two-link switch, "All" and "Needs a person (N)", kept in `?view=needs-person` beside `?page=`. The view reads `filter=needs-person`. The number comes from the shared read below, or from the list's own `total` while that view is showing. The overview has one attention row linking to the view, with no read of its own. The empty view says "Nobody is waiting for a person".

**B5, unread.** An ink dot with a hidden word "Unread", and a heavier name. The transcript watches the newest row against whichever element scrolls. When that row is at least half visible, the tab is visible and the window is focused, held for one second, the conversation posts `POST …/read { sequence }`. It only moves forward, one request at a time, and a failure is silent until the next sighting.

**F, the sounds.** `core/sounds/inbox-signals.ts` and the pure `inbox-signal-rules.ts`, loaded lazily:
- One reader per browser and workspace, by a long-lived Web Lock. Other tabs are told the result over a `BroadcastChannel` and read nothing.
- Bursts of events are coalesced, with at most one read every 5 seconds and one trailing read.
- The first read is a silent baseline, and a tab that inherits the lock keeps the snapshot.
- Decisions follow plan §3.7: an id new to the set, a new message on my assigned conversation that I am not viewing, and a new `AiTurnFailed` (at most 3 small reads per read).
- Own actions are silent for 5 seconds, measured from the start of the read.

**The end-to-end suite.** A new journey (`needs-a-person.e2e.ts`). The sound observer moved to `e2e/support/sounds.ts`. The long conversation for the deep-link journey is now built once by global setup (see §4).

### Adopted contracts

| Screen | Endpoint, event or code | Fields read | Tests |
| --- | --- | --- | --- |
| Inbox, "Needs a person" | `GET …/conversations?page=&pageSize=&filter=needs-person` | `items`, `total` | `conversation-list.spec`, `needs-a-person.e2e` |
| Inbox rows | `GET …/conversations` | `unread` | `conversation-list.spec`, e2e |
| Conversation | `GET …/conversations/{id}` | `lastReadSequence` | `conversation-detail.spec` |
| Conversation, marking read | `POST …/conversations/{id}/read { sequence }` → 204 | none | `conversation-detail.spec`, `conversation-transcript.spec` |
| Overview, attention row | the shared read's `total` | `total` | `overview.spec` |
| Count and sounds | `conversationChanged`, then `GET …/conversations?filter=needs-person&page=1&pageSize=50` | `id`, `lastActivityAt`, `handling`, `assignedTo`, `lastMessageSender`, `customer`, `total` | `inbox-signal-rules.spec`, `inbox-signals.spec`, e2e |
| AI-failed sound | `GET …/messages?tail=true&limit=1&include=events` | `events[{ id, kind }]` | `inbox-signals.spec`, e2e |

### Screenshots

In `C:\Users\mouay\Desktop\nabra-frontend-evidence\`:
- **Before:** `p38-2a\chrome\shots\after\`, which is this step's starting code: `list-` and `detail-` × `en`/`ar` × `light`/`dark` × `390`/`768`/`1280`/`1920`.
- **After:** `p38-2b\chrome\shots\`: `list-`, `needs-person-`, `detail-`, `overview-` over the same matrix, plus `needs-person-view.png`, `needs-person-empty-en.png`, `needs-person-empty-ar.png`, `failed-count.png`.

## 4. Decisions

| Decision | Why | Rejected |
| --- | --- | --- |
| A burst closes after 750 ms of quiet, capped at 2 s | The plan says "within 750 ms of the first, capped at 2 s". A fixed 750 ms would make the cap meaningless, so the window slides | A fixed window |
| `aria-current="true"` on the view links | The open conversation's row already says `aria-current="page"`, and a page should not have two | `page` on both |
| A customer name is regular weight when read, strong when unread | The heavier name has to mean something, so read rows step down | Strong on both, with only the dot to tell them apart |
| Turning sounds off forgets the snapshot | Otherwise everything that changed meanwhile would sound as news when they came back on | Keeping it |
| No small transcript reads while nobody can hear | The count needs only the main read | Reading anyway |
| A tab that inherits the lock reads once at once, from the snapshot it heard | Picks up anything that changed during the handover, with no false sound (measured) | Waiting for the next event |
| One journey covers the live arrival, the failure, the count and the sound | The prompt asks for one; the plan named two | Two journeys |
| The long conversation is built once by global setup | The public chat allows 5 new conversations per network per 5 minutes, and my new journey made it six. The first full run failed on that | Retrying, or waiting inside a journey |

Differences from the plan (rule 20): the `aria-current` value, and the journey count. Skills: `angular-enterprise`, `enterprise-erp-ux`, `api-contracts-integration` and `web-performance`, all loaded in this session before the work began.

Nothing is deferred. Desktop alerts now carry their title and body, as §4.3 #3 asked.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS** |
| `npm run test:ci` | **PASS**: 112 files, 1,522 tests, 0 failures (38.2a: 110 and 1,423). Free memory before: 9.48 GB |
| `npm run build` | **PASS**: 0 warnings, budgets met, initial 313.82 kB raw / 73.78 kB transfer. Free memory before: 9.54 GB |
| Startup JavaScript | **144,193 gzip**: +68 on 38.2a's 144,125; +3.81% on the 38.0 baseline of 138,907 (ceiling 152,797). Inside the 145,000 target, 807 B left |
| `npm run i18n:check` | **PASS**: 2,146 messages, 0 errors, 0 warnings |
| `npm run e2e` | **PASS** on the final code: **24 journeys, 24 passed, 0 failed, 0 flaky, 1.2 min**, no retries. Free memory before: 8.38 GB. The first full run had 1 failure (`one-connection`, Arabic): my new journey pushed the suite past the public chat's limit of 5 new conversations. Fixed by starting fewer, not retried |
| Backend gates | **NOT RUN**: no change in `backend/` or `deploy/` |
| Chrome matrix (installed Chrome through Playwright, temporary profile; not the MCP, so no password in tool arguments) | **PASS**: list, needs-a-person, detail and overview × 4 widths × 2 themes × 2 languages = **64 rows**: 0 page overflow, 0 console errors, one `h1` each, 0 axe violations (WCAG 2 A/AA and 2.2 AA, which covers unnamed controls and contrast) |
| Keyboard path | **PASS**: Enter on "Needs a person" opens the view with focus staying on the link; rows and pager as before |
| Contrast | **PASS**: axe `color-contrast` in the matrix, both themes |
| Reduced motion | **PASS**: this step adds no motion. The token collapse was measured in 38.2a and is unchanged |
| RTL mirroring | **PASS**: the switch, dot, count and rows mirror; codes stay left-to-right (screenshots) |
| View, count, empty | **PASS**: count 129 from 1 read; the view survives a reload; the empty state is correct in English and Arabic |
| Mark read | **PASS**: 0 posts from the list alone; 0 before the second is up; 1 after looking, and the dot goes; 0 when scrolled away from the end. Hidden tab and unfocused window are covered by specs |
| One reader across three tabs | **PASS**: reads at open by tab `[1, 0, 0]`; both inbox tabs show the same count |
| Request cost of a burst | **PASS**: 12 events over 25 s cost **4 needs-a-person reads** from one tab (ceiling at 5 s: 6), and 0 from the others |
| Requests, one `conversationChanged` | **PASS**: **4**: the baseline's 3 (detail, list, transcript) plus 1 needs-a-person read, as the plan set |
| Handover | **PASS**: when the reading tab closes, the other reads within 1.5 s, plays nothing for that read, and still hears a later failure |
| Visitor's message (hosted chat path) | **PASS**: at the top of the list live, with its unread dot (e2e) |
| `[[fail]]` | **PASS**: the divider, count up by exactly 1, the `ai-failed` sound |
| Take over, then return to the AI without a reply | **PASS**: both dividers, the count holds at 130, the row stays in the view, and it leaves once somebody answers |
| New-message sound | **PASS**: plays for my assigned conversation when not viewing it; 0 sounds while it is open and visible |
| Own action | **PASS**: 0 sounds from this browser's own takeover |
| **Hidden tab, 10.5 minutes** (headed Chrome without Playwright's flags, plain DevTools protocol, one real click first) | **PASS**: `document.visibilityState` read `hidden` throughout; hidden for **630 s**; `ai-failed` played **2.9 s** after sending, while still hidden; hub connections opened 1, closed 0 |
| `erp_screen_lint`, `perf_scan` | **PASS** on the inbox: no findings. On the overview, 1 existing HIGH on the "Try again" button, a false positive (its label comes through a template, and axe passes it) |
| Mailpit | **NOT RUN**: this step sends no email |
| Provider canary | **PASS**, at the start, before `e2e`, and after the stack restart |
| Secret scan | **PASS**: 0 files with the demo password in the evidence folder and this report |

Things worth knowing about how these ran:
- The first live run found a real defect: the list asked for the count from inside an effect, which Angular refuses (NG0602). The list's spec used a stand-in and could not see it. Fixed, with a regression test on the real service.
- The first two hidden-tab attempts did not count. One crashed on the `[[fail]]` message's designed 503. The other was not armed: my synthetic click was not a gesture, so audio stayed locked. Tracing showed the read and small read happening correctly. The script now refuses to run unarmed.
- One matrix row failed when the machine entered standby during the run, and another run crashed on a page reload. The reported 64 of 64 is a clean run. The last matrix and hidden-tab runs held a per-process keep-awake request that ended with them; no system setting changed.
- After the gates, the connection dropped and the stack went down. It was restarted with the `stack.md` commands and the canary passed; no code changed, so the gates were not repeated.

## 6. See it live

Sign in at http://localhost:4200/sign-in as **`p36.owner@omniai.example`** (password in `demo-accounts.md`), workspace Northwind Operations. Click once anywhere first: the browser allows sound only after a gesture.

- **The view:** http://localhost:4200/conversations, then "Needs a person" and its count. The address keeps `?view=needs-person`.
- **Unread:** a dot and a heavier name. Open one and look at it: a second later the dot goes. Scroll up first and it stays.
- **A failure, with sound:** open http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw in a private window and send `[[fail]]`. Within a few seconds the count goes up by one and the AI-failed sound plays. Open the conversation for the divider.
- **Silence for yourself:** press Take over. No sound.
- **The sounds switch** is in the account menu; volume and desktop alerts are in Notification preferences, "In this browser".
- **The overview** shows "N conversations need a person": http://localhost:4200/overview.

The API (with the AI stand-in) and the dev server are running.

## 7. Backend asks

No new ones. These still stand: BA-2 (who handles a conversation, so the new-message sound can include conversations taken over without being assigned) and BA-7 (a `needsPerson` flag on rows, so a row can show all three needs-a-person reasons).

## 8. New Arabic for my review

| English | Arabic | Where |
| --- | --- | --- |
| Conversation views | طرق عرض المحادثات | Name of the switch, for screen readers |
| All | الكل | Inbox switch |
| Needs a person | بحاجة إلى الفريق | Inbox switch; desktop alert body |
| Nobody is waiting for a person | لا أحد بانتظار الفريق | Empty view, heading |
| A conversation appears here when a customer is waiting for somebody on the team. | تظهر المحادثة هنا حين ينتظر عميل ردًّا من أحد أعضاء الفريق. | Empty view, body |
| Unread | غير مقروءة | The dot's name, for screen readers |
| New message | رسالة جديدة | Desktop alert body |
| No conversations need a person | لا توجد محادثات بحاجة إلى الفريق | Overview row (zero form) |
| {count} conversation needs a person | محادثة واحدة بحاجة إلى الفريق | Overview row (one) |
| {count} conversations need a person | محادثتان بحاجة إلى الفريق | Overview row (two) |
| {count} conversations need a person | {count} محادثات بحاجة إلى الفريق | Overview row (few) |
| {count} conversations need a person | {count} محادثة بحاجة إلى الفريق | Overview row (many, other) |
| A customer wrote last and is waiting for somebody on the team. | كتب العميل آخر رسالة وينتظر ردًّا من أحد أعضاء الفريق. | Overview row, detail |
| Open the list | فتح القائمة | Overview row, action |

"Needs a person" uses الفريق because the glossary's Person voice is الفريق and it is gender-neutral.

## 9. Risks and open issues

- **Day-one unread.** Every conversation reads as unread for each member until opened, as the plan recorded, so the dots will be everywhere at first.
- **The demo owner's read markers now move** when conversations are opened, including during these checks. That is the feature working.
- **The hidden-tab check had DevTools attached,** which the plan's method requires and which may make the browser gentler than usual. A tab the browser discards can play nothing. The count and dots are right again at the next load.
- **Sound needs one click after each page load.** It is the browser's rule, and the plan's.
- **Beyond the first 50 rows.** A colleague's takeover of an old conversation that lands past page 1 is not heard: a stated limit, never a false sound. A row that returns to page 1 reads as a new entry.
- **New-message sound covers assigned conversations only** until BA-2, per your decision 7d.
- **The end-to-end suite is now exactly at the public chat's limit** of 5 new conversations per 5 minutes. A later journey that needs another live visitor has to share one.
- **Synthetic data.** Northwind gained a number of `[[fail]]` conversations from these checks, so its needs-a-person count is higher than before (135). The checks created 3 accounts with "E2E check …" workspaces, and the two e2e runs one account and "E2E …" workspace each.

## 10. Proposed checkpoint commit message

```
feat: add the needs-a-person view, unread dots and marking read, and play the inbox sounds
```
