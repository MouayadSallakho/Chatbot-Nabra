# Phase 38 summary: the frontend adopts the Phase 37 contracts

Phase 38 took the Phase 37 backend contracts into the Angular frontend, on the Phase 36 "Two Voices" design, with no backend or `deploy/` change. It ran from the plan (38.0, 2026-11-07) to the end-to-end suite and full pass (38.6, 2026-11-11). Phase 39, the redesign, should read this first.

## 1. What changed for the people who use Nabra

**Everyone, in English and in Arabic**
- A refusal says what went wrong in the reader's language, worded by its stable code, from one shared place. The server's English sentence is never shown to anyone, in either language. A code the app does not know reads with the generic words.
- Signing in after too many failures says when to try again ("Try again in 15 minutes") and keeps the button disabled until then. An outage says signing in is unavailable for a moment. After a password reset the page says "You can sign in now".
- Signed-out pages no longer log a 401. A sign-in in another tab is noticed when the page regains focus, never on a timer.
- The account menu says which language the account's emails are written in.

**The inbox**
- Each conversation leads with the customer: their name, or "Visitor 7KQ2MX" / «زائر 7KQ2MX», "WhatsApp customer ••••1234".
- A conversation opens at its newest 40 messages, deep links included; older pages arrive as the reader goes up, keeping their place.
- The transcript draws what really happened, from the server: "The AI could not reply", "… took over", "… returned it to the AI". Nothing is inferred any more.
- A "Needs a person" view with its own count, kept in the address; the Overview says how many conversations are waiting.
- Unread dots, and a conversation is marked read only when its newest message has really been seen.
- Three kalimba sounds: an AI reply failed, a conversation needs a person, a new message on a conversation assigned to you. One tab plays, at most one sound every 2 seconds, never for your own actions, and never as the only signal. A "Sounds" switch in the account menu (on by default); volume, a test sound and desktop alerts (off until a person turns them on) under Notification preferences, "In this browser".
- The bell and the inbox share one live connection.

**Knowledge, agents and channels**
- Knowledge says how indexing went after a reload ("Indexing now", "Indexing failed" with the reason, "Indexing stopped"), and the list shows each file's size. A refused file says why, with its limit.
- The agent screens read an agent's channels from the agent itself (two requests fewer).
- The widget settings offer the Start and End corners and say that visitors see the workspace's name. The chat link has a language choice.
- The hosted chat page shows the workspace's name, greeting, accent and theme with no flash, and opens in Arabic or English from the first paint (`?lang`, else the visitor's browser).

**Workspace administration**
- A new Workspace settings page: everyone with `workspace.view` reads the name and time zone; `workspace.manage` holders change them. A save made from a stale page is refused with a notice that keeps what was typed.
- Invitations in four lists (Waiting, Accepted, Revoked, Expired), paged in the address; the invite form asks which language the email should be in, starting in the sender's language.
- Roles show how many members hold each; the editor says "Saving changes access for N members". Areas and permissions are named by key in both languages.
- Billing shows the plan's limits ("Unlimited" for none) and the fallback plan when there is one.

**Insights and reports**
- Every day in Analytics, the Overview, report periods and schedules is counted and labelled in the workspace's time zone; changing the zone asks first when schedules would move.
- Figures say how they moved against the previous period, in words with an arrow, and how far back the data goes.
- AI usage is broken down by purpose, one line per currency, never adding currencies together.
- Reports and schedules offer the file language (English file / Arabic file), tag each generated report with it, and notifications name reports in the reader's language.
- On phones, the Overview's "Needs attention" rows keep their status word.

**Quality behind it**
- An end-to-end suite of 23 journeys, each run in English (left to right) and Arabic (right to left), against the real dev stack with the AI stand-in and Mailpit: 46 journeys, run twice with no retries.

## 2. The commits

| Step | Commit | What |
| --- | --- | --- |
| 38.0 | `903e685` | The plan: every Phase 37 contract checked three ways, the baseline numbers, the owner's decisions |
| 38.1a | `bff23e0` | Refusals by code (registry, account codes), sign-in throttling, the session check, "You can sign in now", emails' language, the e2e harness |
| 38.1b | `6485e11` | One live connection for the bell and the inbox; the sound service, setting and files |
| 38.2a | `9e105ab` | Customer first, newest messages first, dividers from the server, the inbox codes, the Arabic font preload |
| 38.2b | `91d6f36` | Needs a person, unread and mark read, the sounds wired to the inbox |
| 38.3 | `942e269` | Knowledge indexing outcome and size, file refusals, agent channels, widget corners and name, the hosted page's look and language, area codes |
| 38.4 | `98753c9` | Workspace settings and safe rename, invitation lists and language, members per role, areas by key, plan limits, admin codes |
| 38.5 | `311c807` | Workspace time zone, previous-period deltas, data since, usage by purpose, report codes and languages, the Overview on phones, E11, the `detail` fallback removed |
| 38.6 | not committed | The suite completed (two journeys added, three order dependencies removed), the full pass, the numbers, this summary |

## 3. The final numbers

Measured in 38.6 on the worktree described in its report (`PHASE_38_6_E2E_SUMMARY_REPORT.md` §5), against the 38.0 baseline (`PHASE_38_0_FRONTEND_PLAN_REPORT.md` §3.13). Dev stack, installed Chrome, Northwind, 1280 px unless said.

| Measure | 38.0 | End of Phase 38 | Note |
| --- | --- | --- | --- |
| Unit and component tests | 96 files, 1,169 tests | **120 files, 1,766 tests**, 0 failures | +24 files, +597 tests |
| Messages (`i18n:check`) | 2,077 | **2,392**, 0 errors, 0 warnings | +331 new, 16 ids retired with their rewrites |
| Startup JavaScript (loader + application chunk, gzip) | 138,907 B | **145,199 B** (+4.5%) | Under the 10% ceiling (152,797 B); 199 B over the plan's 145,000 target, accepted by the owner in 38.5 |
| Production build | 0 warnings, budgets met, initial 313.56 kB | 0 warnings, budgets met, initial **314.47 kB** raw / 73.87 kB transfer | Budgets unchanged |
| End-to-end journeys | none | **23 journeys × 2 languages = 46**, passed twice with no retries (3.3 and 3.0 min) | |
| Signed-out pages | 2 requests, 1 console 401 | 0 console errors, no 401 | e2e `signed-out-no-401` |
| Deep link to the 232-message conversation | 12 requests: tenants ×2, 2 hub negotiations, 2 transcript reads (69,970 B), 100 rows drawn | **11 requests**: tenants ×1, 1 negotiation, **1 transcript read of 12,660 B**, 40 rows drawn; first message at 1.4 s (median of 3) | 38.2a measured 10; the 11th is the needs-a-person read that 38.2b added by design (one per page, shared by the count and the sounds) |
| One `conversationChanged`, inbox open | 3 reads (detail, list, transcript) | **4**: the same 3 plus 1 needs-a-person read | As planned in 38.0 §3.13 |
| Analytics page | 7 requests, dashboard 5,685 B | **8 requests**, **one** dashboard read with `compare=previous` and the zone, 6,176 B | The 8th is the needs-a-person read |
| Arabic text on an English deep link (production configuration, 150 ms latency, 200 KB/s, 5 cold loads, median) | (38.2a: text 19,307 ms, fallback shown 1,072 ms) | text 20,138 ms, LCP 20,264 ms, font asked 91 ms before the text, **fallback shown 1,100 ms** | The preload still does not remove the late swap (owner's ruling in 38.2a: keep it; Phase 39 revisits font loading) |
| Reports screen (E11): open to the first row, 9 runs | (37.7 / 38.5: p50 503, p95 about 1 s) | **p50 290 ms, p95 308 ms**; the executions read p50 13.2 / p95 15.7 ms over 200 reads (20 of 41 rows) | Dev server; the page makes the same reads as in 38.5 |
| Chrome pass | — | **28 routes × 2 languages × 2 themes × 4 widths = 448 page loads, 0 problems**; account menu 16 cells, 0 problems | Overflow, console, one `h1`, unnamed controls, axe WCAG 2.2 AA (contrast and target size included) |

## 4. Every item of the list

The list: `Nabra-Frontend-Pack\reference\frontend-asks.md`, A1 to E11 and section F.

| Item | What it is | State | Step, commit |
| --- | --- | --- | --- |
| A1 | Refusals worded by `code`, both languages, one shared registry (`core/problems/`), area catalogues loaded with their screens | Adopted | 38.1a `bff23e0` (mechanism, account codes); each area in its step; the English `detail` fallback removed in 38.5 `311c807` |
| A2 | Sign-in throttling and outages, with when to try again | Adopted | 38.1a `bff23e0` |
| A3 | Sign-in and registration refusals by code | Adopted | 38.1a `bff23e0` |
| A4 | Session check on signed-out pages; no console 401 | Adopted | 38.1a `bff23e0` |
| A5 | "You can sign in now" after a reset | Adopted | 38.1a `bff23e0` |
| A6 | "Emails in English / Arabic" in the account menu | Adopted | 38.1a `bff23e0` |
| A7 | One realtime connection for the bell and the inbox | Adopted | 38.1b `6485e11` |
| A8 | Sound service, setting and files | Adopted | 38.1b `6485e11` (wired to the inbox in 38.2b `91d6f36`) |
| A9 | End-to-end suite | Adopted | harness 38.1a `bff23e0`; journeys in every step; completed in 38.6 (this step, uncommitted): 23 journeys × 2 languages |
| B1 | Customer first on rows | Adopted | 38.2a `9e105ab` (the customers screens keep their own label until BA-1) |
| B2 | Transcripts open at the newest 40, older pages on scroll | Adopted | 38.2a `9e105ab` |
| B3 | Failed-turn and handoff dividers from the server | Adopted | 38.2a `9e105ab` |
| B4 | "Needs a person" view and count | Adopted | 38.2b `91d6f36` |
| B5 | Unread dots and marking read | Adopted | 38.2b `91d6f36` |
| B6 | The four inbox refusals by code | Adopted | 38.2a `9e105ab` |
| B7 | Arabic font preload on inbox pages | Adopted as built; measured no real gain (owner's ruling: keep; font loading revisited in Phase 39) | 38.2a `9e105ab` |
| C1 | Last indexing attempt after a reload | Adopted | 38.3 `942e269` |
| C2 | File size in the knowledge list | Adopted | 38.3 `942e269` |
| C3 | File refusals by code with the bound | Adopted (the archive limit has no unit, so no number: BA-12) | 38.3 `942e269` |
| C4 | Agent channels from the agent read | Adopted | 38.3 `942e269` |
| C5 | Start/End launcher corners | Adopted | 38.3 `942e269` |
| C6 | "Visitors see the workspace's name" | Adopted | 38.3 `942e269` |
| C7 | Hosted page: name, greeting, accent, theme | Adopted | 38.3 `942e269` |
| C8 | Hosted page language (`?lang`, browser, English), with a language choice on the hosted link | Adopted for the hosted page; the embedded widget follows the visitor's browser until BA-3 | 38.3 `942e269` |
| C9 | Knowledge, agent, channel and workspace refusals by code | Adopted | 38.3 `942e269` |
| C10 | Snippet attribute note | Not adopted: superseded by 37.1 Revision 1 (the plan, §3.4) | — |
| D1 | Workspace settings page, rename with `If-Match` and the stale notice | Adopted | 38.4 `98753c9` |
| D2 | Members per role | Adopted | 38.4 `98753c9` |
| D3 | Invitation lists by status, paged in the URL | Adopted | 38.4 `98753c9` |
| D4 | Invitation language | Adopted | 38.4 `98753c9` |
| D5 | Areas and permissions by key | Adopted | 38.4 `98753c9` |
| D6 | Plan limits and the fallback plan | Adopted (finite limits and a fallback plan proved by specs only on this stack: BA-4.3) | 38.4 `98753c9` |
| D7 | Team, role, invitation, email and billing refusals by code | Adopted | 38.4 `98753c9` |
| E1 | Workspace time zone | Adopted | 38.5 `311c807` |
| E2 | Dates and days in the workspace zone | Adopted | 38.5 `311c807` |
| E3 | Previous-period deltas | Adopted | 38.5 `311c807` |
| E4 | "Data since" and honest empty states | Adopted | 38.5 `311c807` |
| E5 | AI usage by purpose, currencies never added | Adopted | 38.5 `311c807` |
| E6 | Report names and options by code | Adopted | 38.5 `311c807` |
| E7 | Notification report names by code | Adopted | 38.5 `311c807` |
| E8 | Report file language | Adopted | 38.5 `311c807` |
| E9 | Stand-in provider label | Not adopted: no provider column is shown; the model name `local-stand-in` already says it (the plan, §3.4) | — |
| E10 | The Overview on phones | Adopted | 38.5 `311c807` |
| E11 | Measure the reports screen | Done: measured in 38.5 `311c807` and again in 38.6 | 38.5, 38.6 |
| F | Kalimba sounds: needs a person, new message (assigned conversations only, until BA-2), AI failed; one tab plays; at most one every 2 s; switch, volume, test button; desktop alerts opt-in | Adopted | files and service 38.1b `6485e11`; rules wired 38.2b `91d6f36` |
| G | Redesign, records search, RUM, Messenger/Instagram, backend launch work | Outside Phase 38 (RUM confirmed outside by the owner, §4.3 #6) | — |

## 5. Backend asks, for the next backend step

One list from the whole phase. BA-1 to BA-5, BA-7 and BA-9 to BA-13 go to a short backend step after 38.6, before Phase 39 (38.0 §7). BA-6 goes with the tuning before launch; BA-8 only if RUM is wanted later.

**Status after Phase 38.8 (2026-11-13).** Every ask the backend step took is closed: 38.7a and 38.7b added the contracts, and 38.8 adopted them on the screens (`PHASE_38_8_ADOPT_AND_PATCH_REPORT.md`). BA-6 and BA-8 stay open, with their "when".

| Id | Ask | Why, and what ships meanwhile | From | When (38.0 §7) | Status (38.8) |
| --- | --- | --- | --- | --- | --- |
| BA-1 | `labelCode` and `shortCode` on the customer directory reads (`GET …/customers`, `GET …/customers/{id}`) | The inbox leads with the server's label (B1); the customers screens still build their own label in the client | 38.0 | Backend step after 38.6 | Closed: 38.7a (fields), 38.8 (the customers screens use the inbox label; the client rule is gone) |
| BA-2 | Who handles a conversation: `takenOverBy { userId }` (or `handledBy`) on list items and the detail | The "new message" sound counts only conversations assigned to the member; a takeover does not assign (§4.3 #7d) | 38.0 | Backend step after 38.6 | Closed: 38.7a (`takenOverBy`), 38.8 (sound counts takeovers; heading and row name who handles it) |
| BA-3 | `v1.js` adds the host page's language to the widget frame URL (`lang=en\|ar`) | The embedded widget follows the visitor's browser; once the URL carries `lang`, the pre-paint script applies it with no frontend change | 38.0, 38.3 | Backend step after 38.6 | Closed: 38.7a (`v1.js` sends `lang`); proved in Chrome in 38.8, no frontend change |
| BA-4 | Specific codes for customers (display name, merge), replies and handoff conflicts, notification preferences, reports and schedules, which still answer `validation.failed` / `state.conflict` | Since 38.5 no reader sees the server's English `detail`; these refusals read generically in both languages | 38.0, 38.5 | Backend step after 38.6 | Closed: 38.7b (codes), 38.8 (every code worded in English and Arabic) |
| BA-4.1 | Declare `201 Created` with its body for `POST …/invitations` in OpenAPI | Documentation only; the client treats any 2xx alike, and the e2e journey asserts the real 201 | 38.4 | Backend step after 38.6 | Closed: 38.7b |
| BA-4.2 | Declare the role write responses (without `memberCount`) in OpenAPI | Documentation only; the editor keeps the count from its last read | 38.4 | Backend step after 38.6 | Closed: 38.7b |
| BA-4.3 | A seeded plan with finite limits, or a fallback plan, on the development stack | Finite limits and the fallback column are proved by specs only | 38.4 | Backend step after 38.6 | Closed: 38.7b (fallback plan on the dev stack, checked live in 38.8); a finite current plan stays an owner choice (38.7b §8.3) |
| BA-5 | OpenAPI completeness: the problem extension members; the invitations `status` parameter; response schemas for writes and customer routes; `previous` optional on the analytics dashboard; a schema for `GET /api/me/tenants` | Hand-written types, checked live per step | 38.0, 38.5 | Backend step after 38.6 | Closed: 38.7b (document), 38.8 (hand-written types checked against it and fixed) |
| BA-6 | The session-check budget (600 per 10 minutes per network, shared by everyone behind one address) | Startup falls back to `/me` on 429; focus checks pause until `Retry-After` | 38.0 | With the tuning before launch | Open: with the tuning before launch |
| BA-7 | Optional `needsPerson` flag on list items and the detail | Rows show the one needs-a-person reason they can infer ("Waiting for the team") | 38.0 | Backend step after 38.6 | Closed: 38.7a (`needsPerson`, `needsPersonReason`), 38.8 (each row says its reason) |
| BA-8 | An endpoint for web-vitals, only if RUM is wanted later | RUM stays outside Phase 38 (§4.3 #6) | 38.0 | Only if RUM is wanted | Open: only if RUM is wanted |
| BA-9 | `scheduleCount` on the tenant read, or a dry-run answer on `PUT …/time-zone` | The schedules question needs `reports.view`; a manager without it is always asked | 38.5 | Backend step after 38.6 | Closed: 38.7b (`scheduleCount`), 38.8 (the question uses it for every manager) |
| BA-10 | Let an index attempt finish when its browser request is cancelled | A reload of the tab that pressed Index settles the attempt as "Indexing stopped" | 38.3 (unnumbered there) | Backend step after 38.6 | Closed: 38.7a (detached attempt), 38.8 (no reload or stay-on-page wording; `timed_out` worded) |
| BA-11 | A signal that indexing ended (an event, or a read to wait on) | "Indexing now" stays until the page is read again; the editor says to reload | 38.3 (unnumbered there) | Backend step after 38.6 | Closed: 38.7a (`knowledgeChanged`), 38.8 (list and editor update by themselves) |
| BA-12 | Name the unit of `limit` on `knowledge.file.archive_too_large` | The sentence gives no number | 38.3 (unnumbered there) | Backend step after 38.6 | Closed: 38.7b (`limitUnit`), 38.8 (the refusal names its number and unit) |
| BA-13 | Announce `conversationChanged` when the customer's message is stored, before the AI is asked, not only when the turn settles | The message is committed first (`CustomerTurnRunner.BeginTurnAsync`), but the one announcement comes after the reply or failure (`AnswerAsync`). An inbox already open shows nothing new, and no "The AI has not replied yet" line, until the turn ends: about 11 s with `[[slow]]`, up to the provider's timeout in production. A person who opens or refreshes the conversation meanwhile sees the line (the e2e journey does exactly that) | 38.6 (new) | Backend step after 38.6 | Closed: 38.7a (stored announcement), 38.8 (visitor de-duplication; inbox shown live; `slow-reply` rewritten) |

## 6. For Phase 39 (the redesign)

Noticed during Phase 38 and left alone on purpose: rule 8 kept the "Two Voices" design. None of these is a defect against today's checks (the 38.6 pass is clean); they are what the redesign should decide.

1. **Arabic font loading.** The inbox's Arabic preload starts the font only about 90 ms before the text needs it, so on a slow link the fallback still shows for about 1.1 s (38.2a, 38.6). The new type system should decide subsets, `unicode-range` and what is preloaded for Arabic pages.
2. **Report files** keep DejaVu Sans until the brand font, and their table headings truncate (the list's section G, 39.7).
3. **Small form controls on touch.** Checkboxes and radios draw at 16×16 px; their labels are the click targets, and axe's 24 px `target-size` passes everywhere, but on touch rule 14 asks for 44 px (invite form, report form, analytics range). The segmented controls hide their radios and use the label: one component should own that pattern.
4. **A global checkbox rule** (`input[type='checkbox']:not(.switch)`, specificity 0,2,1) beats component styles, so every page switch must carry the class `switch` (38.1b). The new form controls should not need the escape.
5. **The conversation header on a phone** (heading, facts, handling bar and help) takes about a third of a 390 × 900 screen before the first message.
6. **Unread on day one:** every conversation with a message is unread for every member until opened (961 in Northwind), so dots are everywhere at first. A quieter unread treatment, or "mark all read" (which needs a backend route), is a design decision.
7. **People are shown by email address** on transcript bubbles, takeover dividers and assignees: the API has no display name for a member.
8. **Time zones by IANA id** ("Asia/Riyadh"), not localised names (38.0 §4.2). The report files print the id too.
9. **The hosted chat greeting** is the workspace's own text in one language, so an Arabic visitor can read an English greeting under Arabic chrome. Per-language greetings would need a backend field.
10. **The Overview's "Needs attention" rows on phones** now keep the status word and truncate the title (E10); the row itself may want a different shape.
11. **Skeletons** were adjusted to the final geometry in 38.5. Any new layout needs its skeletons redrawn with it.
12. **The end-to-end suite** finds elements by `data-testid` and by the words in `messages*.json`. The redesign keeps both, and the suite then shows at once whether a journey still works in both directions.

## Appendix A. Arabic for the owner's review

Every new or changed Arabic string of Phase 38: 332 (331 new, 1 changed), grouped by screen, with the English beside it. Each step's report already listed its own strings, and the owner's Revisions changed some; this is the state at the end of the phase (`messages.ar.json` at `1a9313d` against the 38.6 worktree, which changed no string). Placeholders such as `{$count}` and `{$INTERPOLATION}` are filled in at run time; `.zero` … `.other` rows are the plural forms.

### Signing in, accounts and the account menu (35)

| Id | English | Arabic |
| --- | --- | --- |
| `account.sounds` | Sounds | الأصوات |
| `account.soundSettings` | Sound and alert settings | إعدادات الصوت والتنبيهات |
| `problem.auth.password.too_short` | Use at least {$count} characters. | استخدم {$count} حرفًا على الأقل. |
| `problem.auth.password.requires_non_alphanumeric` | Add at least one character that is not a letter or a digit. | أضف رمزًا واحدًا على الأقل ليس حرفًا ولا رقمًا. |
| `problem.auth.password.requires_digit` | Add at least one digit. | أضف رقمًا واحدًا على الأقل. |
| `problem.auth.password.requires_lower` | Add at least one lowercase letter. | أضف حرفًا إنجليزيًا صغيرًا واحدًا على الأقل. |
| `problem.auth.password.requires_upper` | Add at least one uppercase letter. | أضف حرفًا إنجليزيًا كبيرًا واحدًا على الأقل. |
| `problem.auth.password.requires_unique_chars` | Use more different characters. | استخدم عددًا أكبر من الأحرف المختلفة. |
| `problem.language.unsupported` | That language is not available. | هذه اللغة غير متاحة. |
| `problem.auth.credentials.invalid` | The email address or password is incorrect. | البريد الإلكتروني أو كلمة المرور غير صحيحة. |
| `problem.auth.registration.incomplete` | Enter an email address and a password. | أدخل البريد الإلكتروني وكلمة المرور. |
| `problem.auth.registration.rejected` | This account could not be created. Check your details and try again. | تعذّر إنشاء هذا الحساب. راجع بياناتك ثم حاول مجددًا. |
| `problem.auth.email.taken` | That email address is already in use. Sign in, or use a different address. | هذا البريد الإلكتروني مستخدم بالفعل. سجّل الدخول، أو استخدم عنوانًا آخر. |
| `problem.auth.email.invalid` | That email address cannot be used. Check it and try again. | لا يمكن استخدام هذا البريد الإلكتروني. تحقّق منه ثم حاول مجددًا. |
| `problem.auth.throttled.noWait` | Too many attempts. Wait a moment and try again. | محاولات كثيرة جدًا. انتظر قليلًا ثم حاول مجددًا. |
| `problem.auth.throttled.minutes.zero` | Too many attempts. Try again in {$count} minutes. | محاولات كثيرة جدًا. حاول مرة أخرى بعد لحظات. |
| `problem.auth.throttled.minutes.one` | Too many attempts. Try again in {$count} minute. | محاولات كثيرة جدًا. حاول مرة أخرى بعد دقيقة. |
| `problem.auth.throttled.minutes.two` | Too many attempts. Try again in {$count} minutes. | محاولات كثيرة جدًا. حاول مرة أخرى بعد دقيقتين. |
| `problem.auth.throttled.minutes.few` | Too many attempts. Try again in {$count} minutes. | محاولات كثيرة جدًا. حاول مرة أخرى بعد {$count} دقائق. |
| `problem.auth.throttled.minutes.many` | Too many attempts. Try again in {$count} minutes. | محاولات كثيرة جدًا. حاول مرة أخرى بعد {$count} دقيقة. |
| `problem.auth.throttled.minutes.other` | Too many attempts. Try again in {$count} minutes. | محاولات كثيرة جدًا. حاول مرة أخرى بعد {$count} دقيقة. |
| `problem.auth.throttled.seconds.zero` | Too many attempts. Try again in {$count} seconds. | محاولات كثيرة جدًا. حاول مرة أخرى بعد لحظات. |
| `problem.auth.throttled.seconds.one` | Too many attempts. Try again in {$count} second. | محاولات كثيرة جدًا. حاول مرة أخرى بعد ثانية. |
| `problem.auth.throttled.seconds.two` | Too many attempts. Try again in {$count} seconds. | محاولات كثيرة جدًا. حاول مرة أخرى بعد ثانيتين. |
| `problem.auth.throttled.seconds.few` | Too many attempts. Try again in {$count} seconds. | محاولات كثيرة جدًا. حاول مرة أخرى بعد {$count} ثوانٍ. |
| `problem.auth.throttled.seconds.many` | Too many attempts. Try again in {$count} seconds. | محاولات كثيرة جدًا. حاول مرة أخرى بعد {$count} ثانية. |
| `problem.auth.throttled.seconds.other` | Too many attempts. Try again in {$count} seconds. | محاولات كثيرة جدًا. حاول مرة أخرى بعد {$count} ثانية. |
| `problem.auth.throttle.unavailable` | Signing in is unavailable for a moment. Nothing was changed. Try again shortly. | تسجيل الدخول غير متاح الآن. لم يتغير شيء. حاول مجددًا بعد قليل. |
| `problem.field.check` | Check this field. | تحقّق من هذا الحقل. |
| `signIn.waitOver` | You can try signing in again now. | يمكنك محاولة تسجيل الدخول الآن. |
| `resetPassword.done.canSignIn` | You can sign in now with your new password. Every other session on this account has been signed out. | يمكنك الآن تسجيل الدخول بكلمة المرور الجديدة. وسُجِّل خروج هذا الحساب من كل مكان آخر كان مفتوحًا فيه. |
| `account.emailsInEnglish` | Emails in English | رسائل البريد بالإنجليزية |
| `account.emailsInArabic` | Emails in Arabic | رسائل البريد بالعربية |
| `problem.email.too_long` | This email address is too long. Check it and try again. | عنوان البريد الإلكتروني هذا طويل جدًا. تحقّق منه ثم حاول مجددًا. |
| `problem.email.invalid_characters` | Remove the spaces or hidden characters from the email address. | احذف المسافات أو الأحرف المخفية من عنوان البريد الإلكتروني. |

### Overview (9)

| Id | English | Arabic |
| --- | --- | --- |
| `overview.attention.needsPerson.zero` | No conversations need a person | لا توجد محادثات بحاجة إلى الفريق |
| `overview.attention.needsPerson.one` | {$count} conversation needs a person | محادثة واحدة بحاجة إلى الفريق |
| `overview.attention.needsPerson.two` | {$count} conversations need a person | محادثتان بحاجة إلى الفريق |
| `overview.attention.needsPerson.few` | {$count} conversations need a person | {$count} محادثات بحاجة إلى الفريق |
| `overview.attention.needsPerson.many` | {$count} conversations need a person | {$count} محادثة بحاجة إلى الفريق |
| `overview.attention.needsPerson.other` | {$count} conversations need a person | {$count} محادثة بحاجة إلى الفريق |
| `overview.attention.needsPerson.detail` | A customer wrote last and is waiting for somebody on the team. | كتب العميل آخر رسالة وينتظر ردًّا من أحد أعضاء الفريق. |
| `overview.action.needsPerson` | Open the list | فتح القائمة |
| `overview.week.zone` | Whole days {$zone}. | أيام كاملة {$zone}. |

### Inbox and transcript (20)

| Id | English | Arabic |
| --- | --- | --- |
| `transcript.awaitingAi` (changed) | The AI has not replied to the last message yet. | لم يردّ الذكاء الاصطناعي على الرسالة الأخيرة بعد. |
| `problem.transcript.refused` | These messages could not be loaded. Reload the page and try again. | تعذّر تحميل هذه الرسائل. أعد تحميل الصفحة وحاول مرة أخرى. |
| `problem.conversations.filter.invalid` | This list of conversations could not be shown. Open the full list instead. | تعذّر عرض هذه القائمة من المحادثات. افتح القائمة الكاملة بدلًا منها. |
| `problem.conversations.read.sequence.invalid` | This conversation could not be marked as read. Try again. | تعذّر تحديد هذه المحادثة كمقروءة. حاول مرة أخرى. |
| `transcript.earlier.failed` | Could not load earlier messages. | تعذّر تحميل الرسائل الأقدم. |
| `transcript.earlier.loaded` | Earlier messages loaded. | حُمّلت الرسائل الأقدم. |
| `transcript.start` | Start of the conversation | بداية المحادثة |
| `transcript.event.aiFailed` | The AI could not reply | تعذّر على الذكاء الاصطناعي الرد |
| `transcript.event.takenOver` | {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} took over | استلام المحادثة: {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} |
| `transcript.event.returned` | {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} returned it to the AI | إعادة المحادثة إلى الذكاء الاصطناعي: {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} |
| `inbox.customer.visitor` | Visitor | زائر |
| `inbox.customer.whatsApp` | WhatsApp customer | عميل واتساب |
| `inbox.customer.unnamed` | Customer | عميل |
| `inbox.view.label` | Conversation views | طرق عرض المحادثات |
| `inbox.view.all` | All | الكل |
| `inbox.view.needsPerson` | Needs a person | بحاجة إلى الفريق |
| `inbox.needsPerson.empty.heading` | Nobody is waiting for a person | لا أحد بانتظار الفريق |
| `inbox.needsPerson.empty.body` | A conversation appears here when a customer is waiting for somebody on the team. | تظهر المحادثة هنا حين ينتظر عميل ردًّا من أحد أعضاء الفريق. |
| `inbox.unread` | Unread | غير مقروءة |
| `inbox.alert.newMessage` | New message | رسالة جديدة |

### Notification preferences: sounds and desktop alerts (10)

| Id | English | Arabic |
| --- | --- | --- |
| `notificationPreferences.browserHeading` | In this browser | في هذا المتصفح |
| `notificationPreferences.browserLede` | These apply only to this browser, and take effect as soon as you change them. | تنطبق هذه الخيارات على هذا المتصفح فقط، ويبدأ مفعولها فور تغييرها. |
| `notificationPreferences.soundsOff` | Sounds are off. You can turn them on from the account menu. | الأصوات متوقفة. يمكنك تشغيلها من قائمة الحساب. |
| `notificationPreferences.volume` | Volume | مستوى الصوت |
| `notificationPreferences.testSound` | Play a test sound | تشغيل صوت تجريبي |
| `notificationPreferences.testSoundPlayed` | Test sound played. | تم تشغيل الصوت التجريبي. |
| `notificationPreferences.desktopAlerts` | Desktop alerts | تنبيهات سطح المكتب |
| `notificationPreferences.desktopAlertsDescription` | Show an alert on your computer when Nabra is in the background. It names the customer and the reason, never what they wrote. Your browser asks for your permission the first time. | يظهر لك تنبيه على جهازك عندما تكون نبرة في الخلفية. يذكر التنبيه اسم العميل والسبب، ولا يذكر أبدًا ما كتبه العميل. سيطلب متصفحك إذنك في المرة الأولى. |
| `notificationPreferences.desktopUnsupported` | This browser cannot show desktop alerts. | لا يدعم هذا المتصفح تنبيهات سطح المكتب. |
| `notificationPreferences.desktopRefused` | Your browser is not allowing alerts from Nabra. To turn them on, allow notifications for this site in your browser's settings, then try again. | متصفحك لا يسمح بالتنبيهات من نبرة. لتشغيلها، اسمح بالإشعارات لهذا الموقع من إعدادات المتصفح، ثم حاول مجددًا. |

### Knowledge (50)

| Id | English | Arabic |
| --- | --- | --- |
| `knowledge.state.indexing.sentence` | Indexing is running. Reload this page in a moment to see how it ended. | تجري الفهرسة الآن. أعد تحميل هذه الصفحة بعد قليل لترى كيف انتهت. |
| `knowledge.state.stopped.sentence` | The last attempt stopped before it finished, so this document still needs an index. Try again. | توقفت المحاولة الأخيرة قبل اكتمالها، فما زال هذا المستند يحتاج إلى فهرسة. حاول مرة أخرى. |
| `knowledge.facts.attemptStarted` | Last attempt started | بدء آخر محاولة |
| `knowledge.facts.attemptFinished` | Last attempt finished | انتهاء آخر محاولة |
| `knowledge.cell.neverIndexed` | Never indexed | لم يُفهرس بعد |
| `problem.knowledge.file.overLimit` | This file is past one of the limits a document may have. Split it and upload the part that matters. | تجاوز هذا الملف أحد الحدود المسموح بها للمستند. قسّمه وارفع الجزء المهم منه. |
| `problem.knowledge.file.multipart_required` | This file could not be sent. Reload the page and try again. | تعذّر إرسال هذا الملف. أعد تحميل الصفحة وحاول مرة أخرى. |
| `problem.knowledge.file.required` | Choose a file before uploading. | اختر ملفًا قبل الرفع. |
| `problem.knowledge.file.type_mismatch` | This file's content does not match its type. Save it again in the right format and upload it. | محتوى هذا الملف لا يطابق نوعه. احفظه مرة أخرى بالصيغة الصحيحة ثم ارفعه. |
| `problem.knowledge.file.not_utf8` | This text file is not saved as UTF-8. Save it as UTF-8 and upload it again. | لم يُحفظ هذا الملف النصي بترميز UTF-8. احفظه بهذا الترميز ثم ارفعه مرة أخرى. |
| `problem.knowledge.file.damaged` | This file could not be read. It may be damaged. Try saving it again. | تعذّرت قراءة هذا الملف، وقد يكون تالفًا. جرّب حفظه مرة أخرى. |
| `problem.knowledge.file.encrypted` | This PDF is protected with a password. Remove the password and upload it again. | ملف PDF هذا محميّ بكلمة مرور. أزل كلمة المرور ثم ارفعه مرة أخرى. |
| `problem.knowledge.file.no_text` | No text could be found in this file. Scanned pages and images are not read. | لم يُعثر على نص في هذا الملف. الصفحات الممسوحة ضوئيًا والصور لا تُقرأ. |
| `problem.knowledge.file.header_required` | This CSV file needs a first row that names its columns. | يجب أن يحوي الصف الأول من ملف CSV هذا أسماء أعمدته. |
| `problem.knowledge.file.too_many_pages` | The file has more than {$limit} pages. Split it and upload the part that matters. | عدد صفحات الملف أكبر من الحد المسموح به، وهو {$limit}. قسّمه وارفع الجزء المهم منه. |
| `problem.knowledge.file.too_many_characters` | The file holds more than {$limit} characters of text. Split it and upload the part that matters. | عدد أحرف النص في الملف أكبر من الحد المسموح به، وهو {$limit}. قسّمه وارفع الجزء المهم منه. |
| `problem.knowledge.file.too_many_rows` | The file has more than {$limit} rows. Split it and upload the part that matters. | عدد صفوف الملف أكبر من الحد المسموح به، وهو {$limit}. قسّمه وارفع الجزء المهم منه. |
| `problem.knowledge.file.too_many_columns` | The file has more than {$limit} columns. Remove the ones you do not need and upload it again. | عدد أعمدة الملف أكبر من الحد المسموح به، وهو {$limit}. احذف ما لا تحتاج إليه ثم ارفعه مرة أخرى. |
| `problem.knowledge.file.cell_too_long` | A cell in the file is longer than {$limit} characters. Shorten it and upload the file again. | عدد أحرف إحدى خلايا الملف أكبر من الحد المسموح به، وهو {$limit}. اختصرها ثم ارفع الملف مرة أخرى. |
| `problem.knowledge.file.archive_too_large` | This Word document is too large once opened. Split it, or save a simpler copy. | حجم مستند Word هذا بعد فتحه أكبر من المسموح به. قسّمه أو احفظ نسخة أبسط منه. |
| `problem.knowledge.file.name.required` | This file has no name. Rename it and upload it again. | ليس لهذا الملف اسم. سمِّه ثم ارفعه مرة أخرى. |
| `problem.knowledge.file.name.unreadable` | This file's name could not be read. Rename it and upload it again. | تعذّرت قراءة اسم هذا الملف. غيّر اسمه ثم ارفعه مرة أخرى. |
| `problem.knowledge.file.name.too_long` | This file's name is too long. Shorten it and upload it again. | اسم هذا الملف طويل جدًا. اختصره ثم ارفعه مرة أخرى. |
| `problem.knowledge.title.required` | Give this document a title. | اكتب عنوانًا لهذا المستند. |
| `problem.knowledge.title.too_long` | This title is too long. Shorten it and save again. | هذا العنوان طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| `problem.knowledge.title.control_characters` | A title has to be one line of plain text. | يجب أن يكون العنوان سطرًا واحدًا من نص عادي. |
| `problem.knowledge.content.required` | Write something in this document. | اكتب شيئًا في هذا المستند. |
| `problem.knowledge.content.too_long` | This document is too long. Shorten it and save again. | هذا المستند طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| `problem.knowledge.content.control_characters` | This text has characters that cannot be saved. Paste it as plain text and try again. | يحوي هذا النص رموزًا لا يمكن حفظها. الصقه نصًا عاديًا وحاول مرة أخرى. |
| `problem.knowledge.document.already_enabled` | This document is already in use. | هذا المستند قيد الاستخدام بالفعل. |
| `problem.knowledge.document.already_disabled` | This document is already out of use. | هذا المستند خارج الاستخدام بالفعل. |
| `problem.knowledge.indexing.in_progress` | This document is being indexed right now, so nothing was sent. Reload in a moment to see how it ended. | تجري فهرسة هذا المستند الآن، فلم يُرسَل شيء. أعد التحميل بعد قليل لترى كيف انتهت. |
| `problem.knowledge.index.too_many_chunks` | This document is too long to index. Split it into smaller documents. | هذا المستند أطول من أن تتم فهرسته. قسّمه إلى مستندات أصغر. |
| `problem.knowledge.source.missing` | This document was written here, so there is no file to replace. | كُتب هذا المستند هنا، فليس له ملف يُستبدل. |
| `problem.knowledge.assignment.document_unknown` | One of these documents is no longer in this workspace. Reload the page and choose again. | أحد هذه المستندات لم يعد في مساحة العمل هذه. أعد تحميل الصفحة واختر من جديد. |
| `problem.knowledge.assignment.too_many` | An agent cannot draw on this many documents. Remove some and save again. | لا يمكن للوكيل الاعتماد على هذا العدد من المستندات. أزل بعضها ثم احفظ مرة أخرى. |
| `problem.knowledge.search.question.required` | Write a question to test. | اكتب سؤالًا لتجربته. |
| `problem.knowledge.search.question.too_long` | This question is too long. Shorten it and try again. | هذا السؤال طويل جدًا. اختصره وحاول مرة أخرى. |
| `knowledge.state.indexing` | Indexing now | تجري الفهرسة الآن |
| `knowledge.state.stopped` | Indexing stopped | توقفت الفهرسة |
| `knowledge.failure.provider_unavailable` | The AI provider could not be reached. | تعذّر الوصول إلى مزوّد الذكاء الاصطناعي. |
| `knowledge.failure.provider_timeout` | The AI provider took too long to answer. | استغرق مزوّد الذكاء الاصطناعي وقتًا أطول من اللازم للرد. |
| `knowledge.failure.provider_response_invalid` | The AI provider's answer could not be used. | تعذّر استخدام رد مزوّد الذكاء الاصطناعي. |
| `knowledge.failure.quota_exhausted` | This workspace has used all the AI usage its plan allows. | استنفدت مساحة العمل هذه حصة الذكاء الاصطناعي المسموح بها في باقتها. |
| `knowledge.failure.document_changed` | The document changed while it was being indexed. | تغيّر المستند أثناء فهرسته. |
| `knowledge.failure.cancelled` | Indexing was cancelled. | أُلغيت الفهرسة. |
| `knowledge.failure.interrupted` | Indexing was interrupted before it finished. | انقطعت الفهرسة قبل اكتمالها. |
| `knowledge.failure.indexing_error` | Something went wrong while indexing. | حدث خطأ أثناء الفهرسة. |
| `knowledge.attempt.stopped` | It stopped before it finished. | توقفت قبل اكتمالها. |
| `knowledge.size.typed` | Typed text | نص مكتوب |

### AI agents (8)

| Id | English | Arabic |
| --- | --- | --- |
| `problem.agents.name.required` | Give this agent a name. | اكتب اسمًا لهذا الوكيل. |
| `problem.agents.name.too_long` | This name is too long. Shorten it and save again. | هذا الاسم طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| `problem.agents.name.control_characters` | A name has to be plain text on one line. | يجب أن يكون الاسم نصًا عاديًا في سطر واحد. |
| `problem.agents.instructions.required` | Write instructions for this agent. | اكتب تعليمات لهذا الوكيل. |
| `problem.agents.instructions.too_long` | These instructions are too long. Shorten them and save again. | هذه التعليمات طويلة جدًا. اختصرها ثم احفظ مرة أخرى. |
| `problem.agents.instructions.control_characters` | These instructions have characters that cannot be saved. Paste them as plain text and try again. | تحوي هذه التعليمات رموزًا لا يمكن حفظها. الصقها نصًا عاديًا وحاول مرة أخرى. |
| `problem.agents.disable.draft` | A draft was never in service, so there is nothing to switch off. | لم تدخل المسودة الخدمة قط، فلا يوجد ما يُعطَّل. |
| `problem.ai.message.invalid` | This message cannot be sent. Check that it is not blank or too long. | لا يمكن إرسال هذه الرسالة. تأكد من أنها ليست فارغة أو طويلة جدًا. |

### Channels: website chat, widget, WhatsApp, the visitor's chat (32)

| Id | English | Arabic |
| --- | --- | --- |
| `problem.whatsapp.conversation.not_on_whatsapp` | This conversation did not come from WhatsApp, so a reply cannot go there. | لم تصل هذه المحادثة عبر واتساب، لذا لا يمكن إرسال الرد إليه. |
| `problem.whatsapp.message.required` | Write a message before sending it. | اكتب رسالة قبل إرسالها. |
| `problem.whatsapp.message.too_long` | This message is longer than WhatsApp allows. Shorten it and send it again. | هذه الرسالة أطول مما يسمح به واتساب. اختصرها وأعد إرسالها. |
| `problem.websiteChat.agent.required` | Choose an agent for this chat link. | اختر وكيلًا لرابط الدردشة هذا. |
| `problem.websiteChat.agent.unusable` | This agent cannot be used: it already has a chat link, or it is no longer in this workspace. | لا يمكن استخدام هذا الوكيل: فله رابط دردشة بالفعل، أو لم يعد في مساحة العمل هذه. |
| `problem.websiteChat.origin.invalid` | One of the websites is not a valid address. Use the scheme and domain only, with no path. | أحد المواقع ليس عنوانًا صالحًا. اكتب البروتوكول والنطاق فقط، دون مسار. |
| `problem.websiteChat.origin.too_many` | This chat link lists more websites than it may. Remove some and save again. | يضم رابط الدردشة هذا مواقع أكثر من المسموح به. أزل بعضها ثم احفظ مرة أخرى. |
| `problem.websiteChat.launcher_position.invalid` | That corner is not available. Choose one from the list. | هذا الموضع غير متاح. اختر واحدًا من القائمة. |
| `problem.websiteChat.theme.invalid` | That theme is not available. Choose one from the list. | هذا المظهر غير متاح. اختر واحدًا من القائمة. |
| `problem.websiteChat.accent_color.required` | Choose an accent colour. | اختر اللون المميِّز. |
| `problem.websiteChat.accent_color.invalid` | Write the colour as # and six digits or letters from a to f, for example #2563eb. | اكتب اللون بالعلامة # متبوعة بستة أرقام أو أحرف من a إلى f، مثل #2563eb. |
| `problem.websiteChat.greeting.too_long` | This greeting is too long. Shorten it and save again. | هذه التحية طويلة جدًا. اختصرها ثم احفظ مرة أخرى. |
| `problem.websiteChat.greeting.control_characters` | This greeting has characters that cannot be saved. Paste it as plain text and try again. | تحوي هذه التحية رموزًا لا يمكن حفظها. الصقها نصًا عاديًا وحاول مرة أخرى. |
| `problem.whatsapp.agent.required` | Choose an agent for this number. | اختر وكيلًا لهذا الرقم. |
| `problem.whatsapp.agent.not_in_workspace` | This agent is not in this workspace. Reload the page and choose again. | هذا الوكيل ليس في مساحة العمل هذه. أعد تحميل الصفحة واختر من جديد. |
| `problem.whatsapp.identifier.invalid` | A WhatsApp ID must be 1 to 64 digits, and digits only. | يجب أن يتكون معرّف واتساب من أرقام فقط، من رقم واحد إلى 64 رقمًا. |
| `problem.whatsapp.display_phone_number.too_long` | This display number is too long. Shorten it and save again. | رقم العرض هذا طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| `problem.whatsapp.access_token.required` | Enter the access token. | أدخل رمز الوصول. |
| `problem.whatsapp.access_token.too_long` | This access token is too long. Check that it was copied correctly. | رمز الوصول هذا طويل جدًا. تأكد من نسخه بشكل صحيح. |
| `problem.whatsapp.connection.in_service` | Take this number out of service before changing it. | أخرج هذا الرقم من الخدمة قبل تغييره. |
| `problem.whatsapp.connection.access_token_missing` | Add an access token before putting this number into service. | أضف رمز وصول قبل إدخال هذا الرقم في الخدمة. |
| `problem.whatsapp.phone_number.in_use` | This number is already connected, so it cannot be added again. | هذا الرقم متصل بالفعل، فلا يمكن إضافته مرة أخرى. |
| `websiteChat.detail.language` | Chat page language | لغة صفحة الدردشة |
| `websiteChat.detail.language.auto` | Automatic | تلقائية |
| `websiteChat.detail.language.hint` | Automatic follows each visitor's browser. Choosing a language adds it to the link. | الخيار التلقائي يتبع لغة متصفح كل زائر، واختيار لغة محددة يضيفها إلى الرابط. |
| `widget.workspaceName.note` | Visitors see your workspace's name at the top of the chat. | يرى الزوار اسم مساحة العمل في أعلى الدردشة. |
| `widget.position.start` | Start: right in Arabic pages, left in English pages | البداية: اليمين في الصفحات العربية، واليسار في الصفحات الإنجليزية |
| `widget.position.end` | End: left in Arabic pages, right in English pages | النهاية: اليسار في الصفحات العربية، واليمين في الصفحات الإنجليزية |
| `problem.chat.message.required` | Write a message before sending it. | اكتب رسالة قبل إرسالها. |
| `problem.chat.message.too_long` | This message is too long. Shorten it and send it again. | هذه الرسالة طويلة جدًا. اختصرها ثم أرسلها مرة أخرى. |
| `problem.chat.message.in_progress` | This message is still being answered. Please wait for the reply. | ما زال الرد على هذه الرسالة جاريًا. انتظر الرد من فضلك. |
| `problem.chat.message.already_failed` | This message could not be answered. Send a new message to try again. | تعذّر الرد على هذه الرسالة. أرسل رسالة جديدة للمحاولة مرة أخرى. |

### Workspace settings: name and time zone (45)

| Id | English | Arabic |
| --- | --- | --- |
| `problem.workspace.name.invalid` | Enter a workspace name of up to 200 characters, on one line. | اكتب اسمًا لمساحة العمل لا يتجاوز 200 حرف، في سطر واحد. |
| `problem.workspace.slug.taken` | That workspace slug is already taken. Choose a different one. | هذا المعرّف المختصر لمساحة العمل مستخدم بالفعل. اختر معرّفًا آخر. |
| `problem.workspace.slug.invalid` | Use lowercase letters, numbers and single hyphens for the workspace slug, between 3 and 63 characters. | استخدم في المعرّف المختصر لمساحة العمل أحرفًا لاتينية صغيرة وأرقامًا وشرطات مفردة، بين 3 و63 حرفًا. |
| `workspaceSettings.heading` | Workspace settings | إعدادات مساحة العمل |
| `workspaceSettings.description` | This workspace's own details. Everyone in it can see them. | تفاصيل مساحة العمل هذه. يستطيع كل من فيها الاطلاع عليها. |
| `workspaceSettings.noAccess.heading` | You do not have access to the workspace settings | ليس لديك وصول إلى إعدادات مساحة العمل |
| `workspaceSettings.noAccess.body` | Seeing this workspace's settings needs a permission your roles do not include. Ask somebody who administers this workspace if you need it. | الاطلاع على إعدادات مساحة العمل هذه يحتاج إلى صلاحية لا تتضمنها أدوارك. اطلبها من أحد مسؤولي مساحة العمل إن كنت تحتاج إليها. |
| `workspaceSettings.loading` | Loading the workspace settings… | جارٍ تحميل إعدادات مساحة العمل… |
| `workspaceSettings.loadFailed` | Could not load the workspace settings | تعذّر تحميل إعدادات مساحة العمل |
| `workspaceSettings.tryAgain` | Try again | إعادة المحاولة |
| `workspaceSettings.stale.body` | Your name has not been saved and is still in the field. Reload the latest version to see the saved name; what you typed stays, so you can save it again or change it. | لم يُحفظ الاسم الذي كتبته، وما زال في الحقل. حمّل أحدث نسخة لترى الاسم المحفوظ؛ يبقى ما كتبته، فتستطيع حفظه مرة أخرى أو تغييره. |
| `workspaceSettings.stale.reload` | Reload latest version | تحميل أحدث نسخة |
| `workspaceSettings.name.heading` | Name | الاسم |
| `workspaceSettings.name.lede` | Visitors see this name on the chat page and the widget. | يرى الزوار هذا الاسم في صفحة الدردشة وفي نافذة الدردشة. |
| `workspaceSettings.name.label` | Workspace name | اسم مساحة العمل |
| `workspaceSettings.name.saved` | Saved name: | الاسم المحفوظ: |
| `workspaceSettings.unsaved` | Unsaved changes | تغييرات غير محفوظة |
| `workspaceSettings.saving` | Saving… | جارٍ الحفظ… |
| `workspaceSettings.saveName` | Save name | حفظ الاسم |
| `workspaceSettings.readOnly` | Only people who can manage this workspace can change these. | لا يغيّر هذه الإعدادات إلا من يستطيع إدارة مساحة العمل. |
| `workspaceSettings.renamed` | The workspace is now called {$name}. | أصبح اسم مساحة العمل «{$name}». |
| `title.workspaceSettings` | Workspace settings | إعدادات مساحة العمل |
| `nav.workspaceSettings` | Workspace settings | إعدادات مساحة العمل |
| `zone.phrase.utc` | in UTC | بالتوقيت العالمي |
| `zone.phrase.named` | in {$zone} time | بتوقيت {$zone} |
| `zone.label.utc` | UTC | بالتوقيت العالمي |
| `zone.label.named` | {$zone} time | بتوقيت {$zone} |
| `workspaceSettings.stale.changed` | This workspace was changed somewhere else. | عُدّلت إعدادات مساحة العمل هذه في مكان آخر. |
| `workspaceSettings.zone.heading` | Time zone | المنطقة الزمنية |
| `workspaceSettings.zone.lede` | Days in analytics, report periods and report schedules are counted in this zone. | تُحسب الأيام في التحليلات وفترات التقارير وجدولة التقارير بهذه المنطقة. |
| `workspaceSettings.zone.stale.body` | Your time zone has not been saved and is still selected. Reload the latest version to see the saved one; your choice stays, so you can save it again or change it. | لم تُحفظ المنطقة الزمنية التي اخترتها، وما زالت محددة. حمّل أحدث نسخة لترى المنطقة المحفوظة؛ يبقى اختيارك، فتستطيع حفظه مرة أخرى أو تغييره. |
| `workspaceSettings.zone.find` | Find a time zone | ابحث عن منطقة زمنية |
| `workspaceSettings.zone.noMatch` | No time zone matches. Try a city or a region, such as Riyadh or Europe. | لا توجد منطقة زمنية مطابقة. جرّب اسم مدينة أو منطقة بالإنجليزية، مثل Riyadh أو Europe. |
| `workspaceSettings.zone.findHint` | Type a city or a region to shorten the list below. | اكتب اسم مدينة أو منطقة بالإنجليزية لتقصير القائمة أدناه. |
| `workspaceSettings.zone.label` | Time zone | المنطقة الزمنية |
| `workspaceSettings.zone.hint` | A change counts days in the new zone from the next time a page reads them, and moves every scheduled report to the next start of a day in the new zone. Reports already generated keep the zone they were made in. | بعد التغيير تُحسب الأيام بالمنطقة الجديدة في المرة التالية التي تقرؤها فيها أي صفحة، وتنتقل كل جدولة تقارير إلى أقرب بداية يوم في المنطقة الجديدة. وتحتفظ التقارير المُنشأة من قبل بالمنطقة التي أُنشئت بها. |
| `workspaceSettings.zone.saved.label` | Saved time zone: | المنطقة الزمنية المحفوظة: |
| `workspaceSettings.zone.useBrowser` | Use this browser's zone: | استخدم منطقة هذا المتصفح: |
| `workspaceSettings.zone.save` | Save time zone | حفظ المنطقة الزمنية |
| `workspaceSettings.zone.confirm.body` | Every report schedule moves to the next start of a day in the new zone, and days in analytics and new reports are counted in it from now on. Reports already generated keep their zone. | تنتقل كل جدولة تقارير إلى أقرب بداية يوم في المنطقة الجديدة، وتُحسب بها من الآن الأيام في التحليلات والتقارير الجديدة. وتحتفظ التقارير المُنشأة من قبل بمنطقتها. |
| `workspaceSettings.zone.confirm.action` | Change time zone | تغيير المنطقة الزمنية |
| `workspaceSettings.zone.confirm.cancel` | Keep the current zone | الإبقاء على المنطقة الحالية |
| `workspaceSettings.zone.confirm.heading` | Change the time zone to {$zone}? | هل تريد تغيير المنطقة الزمنية إلى {$zone}؟ |
| `workspaceSettings.zone.saved` | The time zone is now {$zone}. | أصبحت المنطقة الزمنية الآن {$zone}. |
| `problem.workspace.time_zone.invalid` | Nabra does not recognise that time zone. Choose one from the list. | لا تتعرّف نبرة على هذه المنطقة الزمنية. اختر واحدة من القائمة. |

### Team and invitations (42)

| Id | English | Arabic |
| --- | --- | --- |
| `team.invite.created.language` | Email language | لغة البريد |
| `team.invite.language` | Invitation email language | لغة بريد الدعوة |
| `team.invite.language.hint` | When Nabra emails this invitation, it writes the email in this language. It starts as the language you are using now. | حين ترسل نبرة هذه الدعوة بالبريد الإلكتروني، تكتب الرسالة بهذه اللغة. تبدأ باللغة التي تستخدمها الآن. |
| `team.invitations.views` | Invitation lists | قوائم الدعوات |
| `team.invitations.loading` | Loading invitations… | جارٍ تحميل الدعوات… |
| `team.column.sent` | Sent | تاريخ الإرسال |
| `team.invitation.noRolesPast` | No roles | بلا أدوار |
| `problem.invitations.status.invalid` | That list of invitations does not exist. Choose another one. | قائمة الدعوات هذه غير موجودة. اختر قائمة أخرى. |
| `problem.invitations.role.unassignable` | One of the chosen roles is no longer in this workspace. Reload the page and choose again. | أحد الأدوار المختارة لم يعد في مساحة العمل هذه. أعد تحميل الصفحة واختر مرة أخرى. |
| `problem.invitations.email.already_member` | This address already belongs to a member. Change their roles from the team page instead. | هذا العنوان لعضو موجود بالفعل. غيّر أدواره من صفحة الفريق بدلًا من ذلك. |
| `problem.invitations.email.already_invited` | An invitation for this address is still waiting. Revoke it first to send a new one. | ما زالت دعوة لهذا العنوان بانتظار القبول. ألغِها أولًا لترسل دعوة جديدة. |
| `problem.invitations.revoke.closed` | This invitation was already accepted, revoked or expired, so there is nothing to revoke. | قُبلت هذه الدعوة أو أُلغيت أو انتهت صلاحيتها بالفعل، فلا شيء لإلغائه. |
| `problem.invitations.id.invalid` | This invitation could not be found. Reload the page. | تعذّر العثور على هذه الدعوة. أعد تحميل الصفحة. |
| `problem.team.member.self` | You cannot change your own access here. Ask another administrator to do it. | لا يمكنك تغيير وصولك أنت من هنا. اطلب ذلك من مسؤول آخر. |
| `problem.team.member.owner_protected` | The owner's access is not changed here, and the Owner role is never given from the team page. | لا يُغيَّر وصول المالك من هنا، ولا يُمنح دور المالك من صفحة الفريق أبدًا. |
| `problem.team.member.protected` | This member's access is protected and cannot be changed here. | وصول هذا العضو محمي ولا يمكن تغييره من هنا. |
| `problem.team.role.unassignable` | One of the chosen roles is no longer in this workspace. Reload the page and choose again. | أحد الأدوار المختارة لم يعد في مساحة العمل هذه. أعد تحميل الصفحة واختر مرة أخرى. |
| `team.accepted.zero` | No invitation has been accepted yet. | لم تُقبل أي دعوة بعد. |
| `team.accepted.one` | {$count} invitation was accepted. | قُبلت دعوة واحدة. |
| `team.accepted.two` | {$count} invitations were accepted. | قُبلت دعوتان. |
| `team.accepted.few` | {$count} invitations were accepted. | قُبلت {$count} دعوات. |
| `team.accepted.many` | {$count} invitations were accepted. | قُبلت {$count} دعوة. |
| `team.accepted.other` | {$count} invitations were accepted. | قُبلت {$count} دعوة. |
| `team.revoked.zero` | No invitation has been revoked. | لم تُلغَ أي دعوة. |
| `team.revoked.one` | {$count} invitation was revoked. | أُلغيت دعوة واحدة. |
| `team.revoked.two` | {$count} invitations were revoked. | أُلغيت دعوتان. |
| `team.revoked.few` | {$count} invitations were revoked. | أُلغيت {$count} دعوات. |
| `team.revoked.many` | {$count} invitations were revoked. | أُلغيت {$count} دعوة. |
| `team.revoked.other` | {$count} invitations were revoked. | أُلغيت {$count} دعوة. |
| `team.expired.zero` | No invitation has expired. | لم تنتهِ صلاحية أي دعوة. |
| `team.expired.one` | {$count} invitation expired before it was used. | انتهت صلاحية دعوة واحدة قبل استخدامها. |
| `team.expired.two` | {$count} invitations expired before they were used. | انتهت صلاحية دعوتين قبل استخدامهما. |
| `team.expired.few` | {$count} invitations expired before they were used. | انتهت صلاحية {$count} دعوات قبل استخدامها. |
| `team.expired.many` | {$count} invitations expired before they were used. | انتهت صلاحية {$count} دعوة قبل استخدامها. |
| `team.expired.other` | {$count} invitations expired before they were used. | انتهت صلاحية {$count} دعوة قبل استخدامها. |
| `pager.invitations.zero` | of {$count} invitations | من {$count} دعوة |
| `pager.invitations.one` | of {$count} invitation | من دعوة واحدة |
| `pager.invitations.two` | of {$count} invitations | من دعوتين |
| `pager.invitations.few` | of {$count} invitations | من {$count} دعوات |
| `pager.invitations.many` | of {$count} invitations | من {$count} دعوة |
| `pager.invitations.other` | of {$count} invitations | من {$count} دعوة |
| `pager.invitations.label` | Invitation pages | صفحات الدعوات |

### Roles and permissions (22)

| Id | English | Arabic |
| --- | --- | --- |
| `roles.members.zero` | No members | لا أعضاء |
| `roles.members.one` | {$count} member | عضو واحد |
| `roles.members.two` | {$count} members | عضوان |
| `roles.members.few` | {$count} members | {$count} أعضاء |
| `roles.members.many` | {$count} members | {$count} عضوًا |
| `roles.members.other` | {$count} members | {$count} عضو |
| `roles.saveReach.zero` | No member holds this role yet, so saving changes nobody's access. | لا يحمل أي عضو هذا الدور بعد، لذا لن يغيّر الحفظ وصول أحد. |
| `roles.saveReach.one` | Saving changes access for {$count} member. | سيغيّر الحفظ وصول عضو واحد. |
| `roles.saveReach.two` | Saving changes access for {$count} members. | سيغيّر الحفظ وصول عضوَين. |
| `roles.saveReach.few` | Saving changes access for {$count} members. | سيغيّر الحفظ وصول {$count} أعضاء. |
| `roles.saveReach.many` | Saving changes access for {$count} members. | سيغيّر الحفظ وصول {$count} عضوًا. |
| `roles.saveReach.other` | Saving changes access for {$count} members. | سيغيّر الحفظ وصول {$count} عضو. |
| `roles.facts.holders` | Held by | يحمله |
| `roles.column.members` | Members | الأعضاء |
| `problem.roles.name.required` | Enter a name for the role. | اكتب اسمًا للدور. |
| `problem.roles.name.too_long` | This role name is too long. Shorten it and save again. | اسم الدور هذا طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| `problem.roles.name.control_characters` | This role name has characters that cannot be saved. Type it on one line as plain text. | في اسم الدور هذا أحرف لا يمكن حفظها. اكتبه في سطر واحد نصًا عاديًا. |
| `problem.roles.name.taken` | Another role in this workspace already has this name. Choose a different one. | يحمل دور آخر في مساحة العمل هذه الاسم نفسه. اختر اسمًا مختلفًا. |
| `problem.roles.permission.invalid` | One of the permissions could not be read. Reload the page and choose again. | تعذّرت قراءة إحدى الصلاحيات. أعد تحميل الصفحة واختر مرة أخرى. |
| `problem.roles.permission.unknown` | One of the permissions is not available on this installation. Reload the page and choose again. | إحدى الصلاحيات لم تعد متاحة. أعد تحميل الصفحة واختر مرة أخرى. |
| `problem.roles.system.protected` | The Owner role is protected: it cannot be renamed and always has every permission. | دور المالك محمي: لا يمكن تغيير اسمه، ويملك دائمًا كل الصلاحيات. |
| `problem.roles.id.invalid` | This address does not lead to a role. Go back to the roles and choose one. | لا يقود هذا العنوان إلى أي دور. ارجع إلى الأدوار واختر دورًا. |

### Plan, usage and billing (17)

| Id | English | Arabic |
| --- | --- | --- |
| `problem.entitlement.aiagents.exhausted` | This workspace has reached the number of AI agents its plan allows. Nothing was saved. | بلغت مساحة العمل هذه الحد الأقصى لوكلاء الذكاء الاصطناعي في باقتها، ولم يُحفظ شيء. |
| `problem.entitlement.webchatendpoints.exhausted` | This workspace has reached the number of website chat links its plan allows. Nothing was created. | بلغت مساحة العمل هذه الحد الأقصى لروابط دردشة الموقع في باقتها، ولم يُنشأ شيء. |
| `problem.entitlement.whatsappconnections.exhausted` | This workspace has reached the number of WhatsApp numbers its plan allows. Nothing was created. | بلغت مساحة العمل هذه الحد الأقصى لأرقام واتساب في باقتها، ولم يُنشأ شيء. |
| `problem.entitlement.knowledgedocuments.exhausted` | This workspace has reached the number of knowledge documents its plan allows. Nothing was saved. | بلغت مساحة العمل هذه الحد الأقصى لمستندات المعرفة في باقتها، ولم يُحفظ شيء. |
| `billing.limits.heading` | Plan limits | حدود الباقة |
| `billing.limits.lede.fallback` | What this workspace may hold on the plan in effect now, and on the fallback plan it moves to if its subscription lapses. | ما يمكن لمساحة العمل هذه أن تضمّه في الباقة السارية الآن، وفي الباقة البديلة التي تنتقل إليها إذا انقطع اشتراكها. |
| `billing.limits.lede` | What this workspace may hold on the plan in effect now. | ما يمكن لمساحة العمل هذه أن تضمّه في الباقة السارية الآن. |
| `billing.limits.region` | Plan limits table | جدول حدود الباقة |
| `billing.limits.caption` | Plan limits, by plan | حدود الباقة، لكل باقة |
| `billing.limits.column.limit` | Limit | الحد |
| `billing.limits.fallback` | Fallback plan | الباقة البديلة |
| `problem.billing.offer_id.invalid` | No plan was chosen. Choose one and try again. Nothing was charged. | لم تُختر أي باقة. اختر باقة وحاول مرة أخرى. لم يُخصم أي مبلغ. |
| `problem.billing.checkout.unknownoffer` | That plan is no longer on sale. Reload the page to see what is. Nothing was charged. | لم تعد هذه الباقة معروضة للبيع. أعد تحميل الصفحة لترى المعروض الآن. لم يُخصم أي مبلغ. |
| `problem.billing.checkout.offernotavailable` | That plan has been withdrawn from sale. Nothing was charged. | سُحبت هذه الباقة من البيع. لم يُخصم أي مبلغ. |
| `problem.billing.checkout.alreadysubscribed` | This workspace already has a subscription. Change it from Manage payment and invoices. Nothing was charged. | لدى مساحة العمل هذه اشتراك بالفعل. غيّره من «إدارة الدفع والفواتير». لم يُخصم أي مبلغ. |
| `problem.billing.checkout.offernotpurchasable` | That plan cannot be bought on this installation. Nothing was charged. | لا يمكن شراء هذه الباقة حاليًا. لم يُخصم أي مبلغ. |
| `problem.entitlement.members.exhausted` | This workspace has reached the number of members its plan allows, so nobody else can join it now. Nothing was changed. | بلغت مساحة العمل هذه الحد الأقصى للأعضاء في باقتها، فلا يمكن لأحد آخر الانضمام إليها الآن. لم يتغيّر شيء. |

### Analytics and provider usage (31)

| Id | English | Arabic |
| --- | --- | --- |
| `analytics.never.heading` | No conversations yet | لا محادثات بعد |
| `analytics.never.body` | Numbers appear here once customers start conversations on a connected channel. Any provider usage is still reported below. | تظهر الأرقام هنا حين يبدأ العملاء محادثات على قناة متصلة. ويبقى استخدام المزوّدين، إن وُجد، معروضًا أدناه. |
| `analytics.period.whole` | {$from} – {$to}, whole days {$zone}. | من {$from} إلى {$to}، أيام كاملة {$zone}. |
| `analytics.period.running` | {$from} – {$to}, whole days {$zone}. Today is counted until {$time}. | من {$from} إلى {$to}، أيام كاملة {$zone}. يُحسب اليوم حتى {$time}. |
| `analytics.compare.period` | Compared with {$from} – {$to}. | مقارنةً بالفترة من {$from} إلى {$to}. |
| `analytics.since.none` | No conversation has started in this workspace yet. | لم تبدأ أي محادثة في مساحة العمل هذه بعد. |
| `analytics.since` | Data since {$date}. | البيانات منذ {$date}. |
| `analytics.change.zeroBefore` | None in the previous period | لا شيء في الفترة السابقة |
| `analytics.change.noEarlier` | No earlier data to compare | لا بيانات سابقة للمقارنة |
| `analytics.change.sameSoFar` | So far, the same as the previous period | حتى الآن، مثل الفترة السابقة |
| `analytics.change.same` | The same as the previous period | مثل الفترة السابقة |
| `analytics.change.upSoFar` | So far, up {$percent} on the previous period | حتى الآن، أعلى بنسبة {$percent} من الفترة السابقة |
| `analytics.change.up` | Up {$percent} on the previous period | أعلى بنسبة {$percent} من الفترة السابقة |
| `analytics.change.downSoFar` | So far, down {$percent} on the previous period | حتى الآن، أقل بنسبة {$percent} من الفترة السابقة |
| `analytics.change.down` | Down {$percent} on the previous period | أقل بنسبة {$percent} من الفترة السابقة |
| `usage.purposes.heading` | Calls by purpose | الاستدعاءات بحسب الغرض |
| `usage.purposes.note` | Why each call was made. Searches from the knowledge test made before this breakdown existed are counted under knowledge searches for replies. | سبب كل استدعاء. عمليات البحث من اختبار المعرفة التي أُجريت قبل إضافة هذا التفصيل محسوبة ضمن البحث في المعرفة للردود. |
| `usage.purposes.region` | Calls by purpose table | جدول الاستدعاءات بحسب الغرض |
| `usage.purposes.caption` | Provider calls by purpose, with estimated cost | استدعاءات المزوّدين بحسب الغرض، مع التكلفة التقديرية |
| `usage.purposes.purpose` | Purpose | الغرض |
| `usage.purpose.conversationGeneration` | Replies to customers | الردود على العملاء |
| `usage.purpose.conversationRetrieval` | Knowledge searches for replies | البحث في المعرفة للردود |
| `usage.purpose.knowledgeIndex` | Indexing knowledge documents | فهرسة مستندات المعرفة |
| `usage.purpose.agentPreview` | Agent previews | معاينة الوكلاء |
| `usage.purpose.retrievalTest` | Knowledge search tests | اختبار البحث في المعرفة |
| `usage.purpose.unpriced.zero` | No call is unpriced | لا استدعاء غير مسعَّر |
| `usage.purpose.unpriced.one` | {$count} call not priced | استدعاء واحد غير مسعَّر |
| `usage.purpose.unpriced.two` | {$count} calls not priced | استدعاءان غير مسعَّرين |
| `usage.purpose.unpriced.few` | {$count} calls not priced | {$count} استدعاءات غير مسعَّرة |
| `usage.purpose.unpriced.many` | {$count} calls not priced | {$count} استدعاءً غير مسعَّر |
| `usage.purpose.unpriced.other` | {$count} calls not priced | {$count} استدعاء غير مسعَّر |

### Reports and schedules (11)

| Id | English | Arabic |
| --- | --- | --- |
| `schedules.deliveryNoteZone` | Generated reports are available in Reports. The file is never emailed or sent anywhere else; if you turn on Reports in your notification preferences, you get an email that links here. Weekly schedules run every Monday for the previous seven days; monthly schedules run on the first of the month for the previous calendar month. | تتوفر التقارير المُنشأة في صفحة التقارير. لا يُرسَل الملف بالبريد الإلكتروني ولا إلى أي مكان آخر، وإن فعّلت التقارير في تفضيلات الإشعارات تصلك رسالة فيها رابط إلى هنا. تعمل الجدولة الأسبوعية كل يوم اثنين للأيام السبعة السابقة، والشهرية في أول كل شهر للشهر الميلادي السابق. |
| `reports.field.language` | File language | لغة الملف |
| `schedules.zoneNote` | Days and times on this page are {$zone}. | الأيام والأوقات في هذه الصفحة {$zone}. |
| `schedules.column.nextRunZone` | Next run ({$zone}) | التشغيل التالي ({$zone}) |
| `schedules.last.readyAt` | Ready · {$when} | جاهز · {$when} |
| `reports.column.periodPlain` | Period | الفترة |
| `reports.range.hintZone` | Whole days {$zone}, including today. At most {$max} days. | أيام كاملة {$zone}، ومنها اليوم. {$max} يومًا على الأكثر. |
| `reports.download.nameLanguage` | Download {$title}, {$period}, {$format}, {$language} | تنزيل {$title}، {$period}، {$format}، {$language} |
| `reports.error.future` | Choose dates up to today. Days are counted in the workspace's time zone. | اختر تواريخ حتى اليوم. تُحسب الأيام بالمنطقة الزمنية لمساحة العمل. |
| `reports.language.en` | English file | ملف إنجليزي |
| `reports.language.ar` | Arabic file | ملف عربي |

### Changed, and removed ids

- `transcript.awaitingAi` was «لم يرد الذكاء الاصطناعي على الرسالة الأخيرة. حين يتعذّر عليه الرد لا يُرسَل شيء إلى العميل، ولا يظهر شيء هنا.», now «لم يردّ الذكاء الاصطناعي على الرسالة الأخيرة بعد.».
- Removed with their screens' rewrites (their sentences live on under new ids above): `agents.channels.failed`, `agents.channels.notChecked`, `analytics.period.sentence`, `conversation.loadNewer`, `conversation.title`, `passwordReset.throttled`, `reports.column.period`, `reports.download.name`, `reports.range.hint`, `resetPassword.done.lead`, `schedules.column.nextRun`, `schedules.deliveryNote`, `schedules.last.ready`, `team.facts.waiting`, `team.invitations.truncated`, `team.past.summary`.
