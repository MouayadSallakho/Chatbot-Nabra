# Phase 38.2a: Inbox, who the customer is and newest messages first

Part 38.2a of Phase 38.2, as split by the approved plan: **B1, B2, B3, B6, B7**. B4 (needs a person), B5 (unread) and the sounds wired to the inbox (F) are 38.2b.

## 1. Summary

- Every conversation now leads with the customer, by name or as "Visitor 7KQ2MX" / «زائر 7KQ2MX», on the rows, the heading and the details panel.
- A conversation opens at its newest 40 messages, deep links included, and older pages arrive as the reader goes up, with their place kept to within 1 px.
- The transcript shows what really happened, from the server: "The AI could not reply", "{email} took over", "{email} returned it to the AI". Nothing is guessed any more.
- Seven inbox refusals are worded by code in both languages.
- The Arabic font preload ships as designed, but measured **no real gain** on a slow deep link (§5, §9): that one is your call.

## 2. Starting point

Branch `master`, HEAD `6485e11`, worktree clean. Phase 38.1 accepted.

## 3. Changes by area

```
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/features/conversations/conversation-context/conversation-context.html
 M frontend/src/app/features/conversations/conversation-context/conversation-context.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.html
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.scss
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.spec.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.html
 M frontend/src/app/features/conversations/conversation-list/conversation-list.scss
 M frontend/src/app/features/conversations/conversation-list/conversation-list.spec.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.ts
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.html
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.scss
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.spec.ts
 M frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.ts
 M frontend/src/app/features/conversations/conversation-words.ts
 M frontend/src/app/features/conversations/conversations-api.ts
 M frontend/src/app/features/conversations/conversations.routes.ts
 M frontend/src/app/features/conversations/transcript-rows.spec.ts
 M frontend/src/app/features/conversations/transcript-rows.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
?? docs/phase-reports/PHASE_38_2A_INBOX_REPORT.md
?? frontend/e2e/deep-link-newest-first.e2e.ts
?? frontend/src/app/core/i18n/arabic-fonts.spec.ts
?? frontend/src/app/core/i18n/arabic-fonts.ts
?? frontend/src/app/features/conversations/conversation-context/conversation-context.spec.ts
?? frontend/src/app/features/conversations/conversation-problems.spec.ts
?? frontend/src/app/features/conversations/conversation-problems.ts
?? frontend/src/app/features/conversations/customer-label.spec.ts
?? frontend/src/app/features/conversations/customer-label.ts
```

**B1, the customer.** New `customer-label.ts`: the rule by `labelCode`, and `<app-customer-label>`, which isolates a name (`dir="auto"`) and a code or masked number (`dir="ltr"`, `translate="no"`). Rows now read customer and time, then the last message, then the chips. The heading is the customer, and the channel moved into the facts line under it. The client's label rule is gone from the inbox; the customers screens keep `customerName()` until BA-1, as planned.

**B2, newest first.** `tail=true&limit=40&include=events` on open, `before={previousCursor}&limit=40&include=events` going up, and today's forward read with `include=events` for live arrivals. An `IntersectionObserver` watches the top edge with a 400 px margin against whichever element scrolls at that width. The reader's place is put back from the distance to the bottom, with `overflow-anchor: none`. The "Load newer messages" button and the 100-message drawing window are gone: what is loaded is now what is on screen.

**B3, dividers.** `transcriptRows` places server events by `afterSequence`, and `mergeEvents` de-duplicates by `id`. The voice-change guess is deleted. An event kind this build cannot word is not drawn.

**B6, refusals.** `conversation-problems.ts`, registered by the inbox routes, so it costs the startup JavaScript nothing.

**B7, the Arabic font.** `core/i18n/arabic-fonts.ts` calls `document.fonts.load()` once per English page, on the first list, detail or transcript response holding Arabic.

### Adopted contracts

| Screen | Endpoint, event or code | Fields read | Tests |
| --- | --- | --- | --- |
| Inbox rows | `GET …/conversations` | `customer { displayName, labelCode, reference, shortCode }`, `lastMessagePreview` | `conversation-list.spec`, `customer-label.spec`, `arabic-render.spec` |
| Conversation heading | `GET …/conversations/{id}` | `customer`, `channel` | `conversation-detail.spec` |
| Details panel | same | `customer`, and WhatsApp `customerReference` when there is no customer | `conversation-context.spec` |
| Transcript, opening | `GET …/messages?tail=true&limit=40&include=events` | `items`, `previousCursor`, `events` | `conversation-detail.spec`, `deep-link-newest-first.e2e` |
| Transcript, going up | `GET …/messages?before={n}&limit=40&include=events` | same | `conversation-detail.spec`, `conversation-transcript.spec`, e2e |
| Transcript, live | `conversationChanged`, then `GET …/messages?limit=200[&after=n]&include=events` | `items`, `nextCursor`, `events` | `conversation-detail.spec` |
| Dividers | `events[{ id, kind, afterSequence, actor }]` | as listed | `transcript-rows.spec`, `conversation-transcript.spec`, `arabic-render.spec` |
| Refusals | `conversations.filter.invalid`, `conversations.read.sequence.invalid`, `transcript.cursor.conflict`, `transcript.include.invalid`, `whatsapp.conversation.not_on_whatsapp`, `whatsapp.message.required`, `whatsapp.message.too_long` | `code` | `conversation-problems.spec`, `arabic-render.spec` |

### Screenshots

In `C:\Users\mouay\Desktop\nabra-frontend-evidence\p38-2a\chrome\shots\`:
- `before\` and `after\`: `list-` and `detail-` × `en`/`ar` × `light`/`dark` × `390`/`768`/`1280`/`1920`, from the production build.
- `live\`: the same matrix from the dev server, plus `failed-divider.png`, `handoff-dividers.png`, `arabic-dividers.png`, `deep-link-own-scroller.png`, `deep-link-page-scroller.png`, `header-1280.png`.

## 4. Decisions

| Decision | Why | Rejected |
| --- | --- | --- |
| Seven codes worded, not four | The plan's row says four; its §3.4 table lists seven for the inbox in 2a. The table is the fuller list | Leaving the three WhatsApp codes to 38.3 |
| The drawing window is removed | The window existed to keep the opening DOM small. Opening is now 40 messages, and more are drawn only when asked for | Two windows, one over the other |
| Facts line gains the channel | The heading gave up the channel to the customer, and the channel must still be readable | A second heading line |
| The last message on a row is regular weight | The customer leads now. Two strong lines compete, and 38.2b needs a heavier name for unread | Keeping both strong |
| The "AI has not replied" note is shorter, and hidden when a failure is recorded | Its old second sentence, "nothing appears here", is no longer true | Keeping it beside the failure divider |
| The show-earlier button keeps focus and is `aria-disabled` while loading | The same element survives loading and failure, so focus is never dropped | `disabled`, which drops focus |
| An event with no actor reuses the wording "A person took over" / "Back with the AI" | The field is nullable in the contract. Says what happened without inventing who | Hiding the divider |
| Arabic dividers use a verbal noun: «استلام المحادثة: …» | The actor's gender is unknown, and a verb would have to guess it. Follows the glossary's "Take over" | «استلم …» |
| `previousCursor` and `events` are optional in the client's type | The OpenAPI document does not list them as required, though the handoff document says every page has `previousCursor`. Live reads always returned it | Failing on absence |

Differences from the plan (rule 20): only the code count above. Skills loaded: `angular-enterprise` and `api-contracts-integration` before building; `enterprise-erp-ux` and `web-performance` **after** building, used as a review of the finished work (both of their scripts report no findings). They should have been loaded first.

Deferred to 38.2b, by the plan: the needs-a-person view and count, unread dots and mark-read, the sounds.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS** |
| `npm run test:ci` | **PASS**: 110 files, 1,423 tests, 0 failures (38.1b: 106 and 1,352). Free memory before: 6.49 GB |
| `npm run build` | **PASS**: 0 warnings, budgets met, initial 313.82 kB raw / 73.78 kB transfer. Free memory before: 6.22 GB |
| Startup JavaScript | **144,121 gzip**. After 38.1b: 143,630 (+491). 38.0 baseline: 138,907 (+3.75%; ceiling +10% = 152,797). Inside the 145,000 target, 879 B left. The +491 is not attributed: one extra shared chunk appeared (41 files, was 40) |
| `npm run i18n:check` | **PASS**: 2,131 messages, 0 errors, 0 warnings |
| `npm run e2e` | **PASS**: 22 journeys, 22 passed, 0 failed, 0 flaky, 1.1 min, no retries (2 of them new: `deep-link-newest-first` in en and ar). Free memory before: 6.42 GB |
| Backend gates | **NOT RUN**: no change in `backend/` or `deploy/` |
| Chrome matrix (installed Chrome through Playwright, temporary profile, not the MCP, because signing in through it would put the password in tool arguments) | **PASS**: 2 screens × 4 widths × 2 themes × 2 languages = **32 rows**: 0 page overflow, 0 console errors, one `h1` on each, 0 axe violations (WCAG 2 A/AA and 2.2 AA, which covers unnamed controls and contrast). Run on the final code |
| Keyboard path | **PASS**: focus on show-earlier, Enter reads 40 more, focus stays on it, "Earlier messages loaded." announced |
| Contrast | **PASS**: axe `color-contrast` in the matrix, both themes |
| Reduced motion | **PASS**: `--duration-sm` collapses to 0.01ms under the setting; this step adds no motion |
| RTL mirroring | **PASS**: time sits on the left of the name in Arabic; code and number stay LTR islands |
| Deep link, own scroller (1280×900) | **PASS**: 1 read of 40 on open; 5 older pages to 232; the reader's place moved 0 to 1 px on each; "Start of the conversation" at the top |
| Deep link, page scroller (768×500) | **PASS**: same reads, same result |
| AI stand-in, hosted chat in a second context | **PASS**: visitor's message at the top of the list live, with a "Visitor …" label; arrival in the open conversation; `[[fail]]` draws "The AI could not reply" live; Take over then Return to AI draws both dividers; a reload draws the same three, in English and Arabic |
| Refusal codes, live | **PASS**: `transcript.cursor.conflict`, `transcript.include.invalid`, `conversations.filter.invalid` confirmed at 400 |
| Requests, deep link to 232 messages | **PASS**: transcript **1 read, 12,660 B, 40 drawn**. Before: 2 reads, 69,970 B, 100 drawn |
| Requests, one `conversationChanged` | **PASS**: **3** (detail, list, transcript), the same as the 38.0 baseline |
| Arabic font, deep-link paint (5 cold loads, median, production build through `ng serve`, 150 ms latency, 200 KB/s) | **Measured, no real gain.** Before / after: text in the DOM 19,039 / 19,307 ms; font requested 19,026 / 19,255 ms; font done 20,088 / 20,381 ms; **fallback shown 1,049 / 1,072 ms**; LCP 19,068 / 19,336 ms. Lab only, English page |
| `erp_screen_lint`, `perf_scan` on the inbox | **PASS**: no findings |
| Mailpit | **NOT RUN**: this step sends no email |
| WhatsApp label, live | **NOT RUN**: no live provider traffic is allowed; covered by specs (masked number, its isolation) |
| §7 parts belonging to 38.2b (unread dot, needs-a-person count, sounds, the 10-minute hidden tab) | **NOT RUN**: 38.2b |
| Provider canary | **PASS**, at the start and before `e2e`: every listed name absent, SMTP host loopback, stand-in listed as information |
| Secret scan | **PASS**: 0 files with the demo password in the evidence folder and this report |

During the work, free memory sat between 2.7 and 4 GB, and the one allowed Docker page-cache drop did not lift it. **No gate started under 6 GB**: I waited, and they ran once it recovered. While waiting I ran the conversation and Arabic specs on their own as a development check; that is not counted above.

## 6. See it live

Sign in at http://localhost:4200/sign-in as **`p36.owner@omniai.example`** (password in `demo-accounts.md`), workspace Northwind Operations.

- **The rows:** http://localhost:4200/conversations leads with the customer. Switch to Arabic for «زائر …».
- **Newest first:** http://localhost:4200/conversations/01a15013-917d-75a7-870c-98ceb0b9e7f8 opens at the newest of 232. Scroll up: older pages arrive and your place holds. At the top: "Start of the conversation".
- **From the keyboard:** Tab to "Show earlier messages" and press Enter.
- **Dividers:** open http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw in a private window and send a message, then open that conversation in the inbox. Send `[[fail]]` from the chat for "The AI could not reply"; press Take over, then Return to AI, for the other two.
- **An older conversation has no dividers.** That is correct: history starts at 37.4.

The API and the dev server are left running. The stand-in is on.

## 7. Backend asks

No new ones. BA-1 (`labelCode` and `shortCode` on the customer directory reads) still stands.

## 8. New Arabic for my review

| English | Arabic | Where |
| --- | --- | --- |
| Visitor | زائر | Customer label (rows, heading, details) |
| WhatsApp customer | عميل واتساب | Customer label |
| Customer | عميل | Customer label |
| Start of the conversation | بداية المحادثة | Top of a transcript |
| Could not load earlier messages. | تعذّر تحميل الرسائل الأقدم. | Transcript, when a page fails |
| Earlier messages loaded. | حُمّلت الرسائل الأقدم. | Screen-reader announcement |
| The AI could not reply | تعذّر على الذكاء الاصطناعي الرد | Divider |
| {email} took over | استلام المحادثة: {email} | Divider |
| {email} returned it to the AI | إعادة المحادثة إلى الذكاء الاصطناعي: {email} | Divider |
| The AI has not replied to the last message yet. (changed) | لم يرد الذكاء الاصطناعي على الرسالة الأخيرة بعد. | Note at the end of a transcript |
| These messages could not be loaded. Reload the page and try again. | تعذّر تحميل هذه الرسائل. أعد تحميل الصفحة وحاول مرة أخرى. | `transcript.cursor.conflict`, `transcript.include.invalid` |
| This list of conversations could not be shown. Open the full list instead. | تعذّر عرض هذه القائمة من المحادثات. افتح القائمة الكاملة بدلًا منها. | `conversations.filter.invalid` |
| This conversation could not be marked as read. Try again. | تعذّر تعليم هذه المحادثة كمقروءة. حاول مرة أخرى. | `conversations.read.sequence.invalid` |
| This conversation did not come from WhatsApp, so a reply cannot go there. | لم تصل هذه المحادثة عبر واتساب، لذا لا يمكن إرسال الرد إليه. | `whatsapp.conversation.not_on_whatsapp` |
| Write a message before sending it. | اكتب رسالة قبل إرسالها. | `whatsapp.message.required` |
| This message is longer than WhatsApp allows. Shorten it and send it again. | هذه الرسالة أطول مما يسمح به واتساب. اختصرها وأعد إرسالها. | `whatsapp.message.too_long` |

Removed from the catalogue: `conversation.title`, `conversation.loadNewer`.

## 9. Risks and open issues

- **The Arabic font preload does not remove the late swap (owner's decision: keep it as built).** The hint arrives in the same responses as the text, so the font starts only about 50 ms before the text, and the file then takes about a second on this slow profile. The timing differences between the two runs are not meaningful. Repeat visits are unaffected, since the fonts are cached as immutable. It is harmless and costs one call. Phase 39 changes the typography, and font loading will be looked at again there.
- **The show-earlier button can be scrolled out of view after a keyboard press,** because the reader's place is kept. Focus stays on it, and the arrow keys carry on scrolling and loading.
- **A very long read grows the DOM.** With the drawing window gone, every page the reader asks for stays drawn. Checked at 232 messages, not beyond.
- **The voice track covers the loaded messages only** until older pages arrive, and says so to screen readers.
- **Earlier conversations lose their inferred dividers,** as the plan recorded (§9): the server has no events before 37.4.
- **A message committing late in the middle** of what is loaded, while the reader is scrolled up, can shift the view by one row. Rare, and it needs both a late commit and a reader within the last 20 rows.
- **Some synthetic data was added to Northwind** through the real hosted-chat API for these checks (a few conversations, with their dividers). The e2e run made its own workspaces, named "E2E …".

## 10. Proposed checkpoint commit message

```
feat: lead the inbox with the customer, open transcripts at the newest messages, and draw what happened from the server
```

## Revision 1

Requested by the owner when accepting this report. Two Arabic values changed, and nothing else in the product.

**Decision on the Arabic font preload:** keep it as built. It is harmless and cheap. Phase 39 changes the typography, and font loading will be looked at again there (§9).

**Notes on the Arabic:**
- «يرد» without the shadda can be read as "arrive", so the note now writes «يردّ».
- «تعليم» reads first as "teaching", so the refusal now says «تحديد».

**New §8 rows** (these replace the two rows above):

| English | Arabic | Where |
| --- | --- | --- |
| The AI has not replied to the last message yet. | لم يردّ الذكاء الاصطناعي على الرسالة الأخيرة بعد. | Note at the end of a transcript |
| This conversation could not be marked as read. Try again. | تعذّر تحديد هذه المحادثة كمقروءة. حاول مرة أخرى. | `conversations.read.sequence.invalid` |

No test asserted either old value, so no test changed.
