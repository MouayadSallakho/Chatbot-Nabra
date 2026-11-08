# Phase 38.0: Frontend plan

Plan only. No product code changed in this step. From 38.1 on, this document decides which item belongs to which step (rule 20), including the owner's decisions recorded in §4.3.

## 1. Summary

- Nothing changes yet for the people who use Nabra. This step checked every Phase 37 contract, measured today's frontend, and placed every item of the list in a step.
- Every contract the list names is documented and answers live on the dev API (66 live checks). The frontend uses none of them yet. Today an Arabic reader can still see the server's English sentence on most refusals, and every signed-out page logs a 401.
- The plan keeps the current design and adds about 300 sentences in English and Arabic over the phase, split by screen so each step's Arabic can be read on its own.
- 38.1 and 38.2 are too large for one session each, so they are split into 38.1a/38.1b and 38.2a/38.2b. 38.3, 38.4, 38.5 and 38.6 stay whole.
- Seven decisions are yours before 38.1 (§4.3). The biggest: the end-to-end package, whether sounds start on, and desktop notifications.

## 2. Starting point

| Fact | Value |
| --- | --- |
| Branch | `master` |
| HEAD | `1a9313d` feat: write report files in the reader's language, English or Arabic, right to left in Arabic |
| Worktree | clean (`git status --short` printed nothing) |
| Dev stack | `omniai-p36-api` (127.0.0.1:5027, AI stand-in on), `omniai-p37-netns`, `omniai-p37-mailpit` (UI 127.0.0.1:8026), `omniai_pg25`, `omniai_redis18`: all up; 13 migrations applied; readiness 200; the dev server was already serving :4200 |
| Provider canary | **PASS** before any traffic: every provider variable and every `OMNIAI_LIVE_*` absent, no SMTP username or password, SMTP host loopback, `OMNIAI_DEV_AI_STANDIN` listed as information |
| Phase 36.12 live stack | `omniai_p36_live` running; left alone |

## 3. Changes by area

`git status --short` after this step:

```
?? docs/phase-reports/PHASE_38_0_FRONTEND_PLAN_REPORT.md
```

That file is the only repository change. Evidence is in `C:\Users\mouay\Desktop\nabra-frontend-evidence\p38-0\`:

| Folder | What |
| --- | --- |
| `openapi\` | `openapi-v1.json` (live document, 96 paths, 95 schemas); `contract-probe.json`/`.log` and `contract-probe-2.json`/`.log` (every live check, field names, statuses and codes only) |
| `gates\` | `format-check.log`, `test-ci.log`, `build.log`, `i18n-check.log`, `startup-js.txt` |
| `chrome\` | `baseline.json` (signed-out pages, deep link, analytics, one `conversationChanged`), `signed-out-extra.json` (invitation page, Arabic) |
| `scratch\` | the probe and measurement scripts (no password inside; they read the demo file into memory) |

Skills used: `frontend-engineering-lead` (gated plan). `api-contracts-integration`, `angular-enterprise`, `frontend-testing-quality` and `web-performance` were not loaded as separate skills in this session; their rules (three-way contract check, where code lives, test levels, budgets) are applied below from the lead skill's baselines.

### 3.1 How the plan was built

- Read: `frontend-asks.md` (A1 to E11, section F); sections 3, 7 and 8 (and each Revision) of `PHASE_37_1` to `PHASE_37_7`; `problem-codes.md`; the named architecture sections (`human-handoff-realtime-inbox.md` §7–§9 and §14, `unified-customer-identity.md` C9a, `knowledge-base-rag.md` §37.5a, `embeddable-web-chat-widget.md`, `workspace-plans-entitlements-quotas.md` §5 and §9, `authentication-tenant-membership.md` on `users.language`); `MASTER.md` §4–§11 and `GLOSSARY.md`.
- Code: every `*-api.ts`, the two admin realtime services, the startup path (`index.html`, `main.ts`, `app.config.ts`, `AuthService`, the interceptor), the error model (`api-error.ts`), the inbox, the account menu, the routes. The hub (`WorkspaceInboxHub.cs`) and a few backend constants were read, not changed.

Facts that shape the plan most:

| Fact | Where | Consequence |
| --- | --- | --- |
| Refusals show the server's English `detail` for 400, 401, 404, 409, 410, 413, 415 and 422 | `core/api/api-error.ts:137-147`, `readableMessage` at `:288` | Arabic readers see English on most refusals; A1 changes this in one function that about 40 screens already go through (`failure.message`) |
| Sign-in shows the server's sentence verbatim; 429 and 503 fall into generic words | `features/auth/login/login.ts:26` | A2, A3 |
| Startup asks `/api/auth/me` on every non-visitor page; a signed-out page logs a 401 | `core/auth/auth-service.ts:82`, `app.config.ts:79` | A4 replaces it with `/api/auth/session` |
| Two hub connections on the inbox page: `ConversationRealtime` (static SignalR import, conversation group) and `NotificationRealtime` (lazy import, member group) | `core/realtime/conversation-realtime.ts:57`, `core/notifications/notification-realtime.ts:33` | A7. The hub allows both joins on one connection (`Subscribe` needs `conversations.view`; `SubscribeNotifications` needs membership; both have unsubscribe methods) |
| A transcript is read forward from the first message, 200 per request, up to 50 requests | `conversation-detail.ts:54,64,357-380` | B2: today a 232-message deep link downloads all 232 to show 100 |
| Handoff dividers are inferred from a change of replying voice | `features/conversations/transcript-rows.ts` | B3 replaces them with server events |
| "Waiting for the team" is inferred on rows (person handling, customer wrote last) | `conversation-list.ts:126` | Kept; B4's server view covers all three needs-a-person branches |
| Taking over does **not** assign; assignment is routing only | `Conversation.cs:108-109`, handoff doc §6 | The "new message" sound can only read `assignedTo`; "taken over by me" is not on rows (Backend ask BA-2) |
| Conversation lists are clamped to 50 per page; transcripts to 200 | `ReadConversationsHandler.cs:59,70` | The sound read uses one 50-row page |
| Customer directory reads carry only the English `label`, no `labelCode` | live probe | B1 drops the client label rule in the inbox only; the customers screens keep it (BA-1) |
| Agents read two channel lists to show which channels use them | `features/ai-agents/agent-channels.ts:50-51` | C4 removes both reads |
| The widget form offers only Left/Right | `website-chat-widget.ts:134` | C5 |
| Report names in notifications come from a regular expression on the English body | `core/notifications/notification-words.ts:27` | E7 |
| Analytics and reports print "UTC" in 8 places, and the custom report range counts days in UTC; plan quotas also say UTC, correctly | §3.10 | E2 changes only the zone-following ones |
| The edge computes the inline script's CSP hash at image build; `default-src 'self'` covers same-origin audio | `deploy/edge/csp-hashes.mjs`, `omniai.conf.template:88,99` | C8 and the sounds need no `deploy/` change |
| `MASTER.md` §4: no font preload, because hashed font names cannot be written into `index.html` | `docs/design/MASTER.md` §4 | B7 uses the FontFace API instead of `<link rel=preload>` |
| The 37.1 Revision changed the install snippet to `data-nabra-key` (old snippets still work) | `PHASE_37_1` R1.1; live | C10 is superseded (§3.3) |

### 3.2 Contract check

One row per contract. **Documented** names the authority; **Live** is what the dev API answered in this step (`openapi\contract-probe*.log`); **Used today** is the frontend at `1a9313d`.

| Item | Contract | Documented | Live | Used today |
| --- | --- | --- | --- | --- |
| A1 | `code` on every 400/409/412/422 | `problem-codes.md`, "The guarantee" | yes: 18 refusals probed, each with its code | read by `toApiError`, but the message is `detail`; screens branch only on `antiforgery.invalid`, `auth.reset.invalid`, `auth.password.too_short`, `auth.throttled`/`.unavailable` (reset pages), `entitlement.*` |
| A1 | `codes` (every password rule) | `problem-codes.md`, Accounts | yes: `["auth.password.too_short"]` on register | reset page only |
| A1 | `limit` on file refusals | `problem-codes.md`, Knowledge | yes: CSV of 65 columns → 422 `knowledge.file.too_many_columns`, `limit` 64 | no |
| A1 | `Retry-After` on 429 | `problem-codes.md` `auth.throttled` | yes: `Retry-After: 900` | parsed (`retryAfterSeconds`); shown only by the visitor chat |
| A1 | `validation.failed`, `precondition.failed`, `precondition.required`, `precondition.if_match.invalid` | "The guarantee" | yes: `range=bogus` 400; stale tag 412; no `If-Match` 428; `If-Match: nonsense` 400 | no |
| A1 | `state.conflict` | "The guarantee" | not probed (no refusal that changes nothing) | no |
| A2 | `POST /api/auth/login` 429 `auth.throttled` + `Retry-After` | `problem-codes.md`; 37.3 R1 | yes, sixth failure of a random unknown address | no (generic words) |
| A2 | 503 `auth.throttle.unavailable` | same | not probed (needs Redis stopped) | no |
| A3 | `auth.credentials.invalid` (401) | `problem-codes.md` | yes | no (detail shown) |
| A3 | `auth.email.taken`, `auth.registration.incomplete`, `auth.password.*` + `codes` | same | yes, all three | no |
| A3 | `auth.email.invalid`, `auth.registration.rejected` | same | not probed | no |
| A4 | `GET /api/auth/session` → `{authenticated:false}` or `{authenticated:true,user:{id,email,language}}`; `no-store`; 429/503 | `authentication-tenant-membership.md` D34; OpenAPI `SessionResponse` | yes: signed out exactly `{"authenticated":false}`, `Cache-Control: no-store`, no `Set-Cookie`; signed in, `user` equals `/me` byte for byte | no |
| A4 | `GET /api/auth/me` unchanged, 401 signed out | D34 | yes | yes (startup) |
| A6 | `language` on `/api/auth/me` | OpenAPI `CurrentUserResponse` | yes (`en` for the demo owner) | no: `AuthenticatedUser` has two fields |
| A7 | `/hubs/workspace-inbox`: `conversationChanged(conversationId)`, `notificationsChanged`; `Subscribe`, `SubscribeNotifications`, `Unsubscribe*` | handoff §9; `WorkspaceInboxHub.cs` | `conversationChanged` frames observed in Chrome | yes, over two connections |
| B1 | `customer { id, displayName, labelCode, reference, shortCode }` on list items and detail | C9a; OpenAPI `ConversationCustomerResponse` | yes on both; `labelCode` seen: `AnonymousVisitor`. No WhatsApp conversation in Northwind's first 50 rows, so the `••••1234` mask was not observed | no |
| B2 | `tail`, `before`, `limit`; `previousCursor` | handoff §14.2; OpenAPI parameters | yes: `tail=true&limit=40` ascending with `previousCursor`; paging back to null covered the whole transcript | no (forward `after=` only) |
| B2/B6 | `transcript.cursor.conflict` | `problem-codes.md` | yes | no |
| B3 | `include=events` → `events[{ id, kind, at, afterSequence, actor, reason }]` | handoff §14.3; OpenAPI `TranscriptEventResponse` | yes: `AiTurnFailed`, `actor` null, `reason` `generation_failed`. `TakenOver`/`ReturnedToAi` were not in the 50 conversations sampled | no (inferred) |
| B3/B6 | `transcript.include.invalid` | `problem-codes.md` | yes | no |
| B4 | `filter=needs-person`, `total` filtered | handoff §14.4; OpenAPI | yes: 125 of 964 | no |
| B4/B6 | `conversations.filter.invalid` | `problem-codes.md` | yes | no |
| B5 | `unread` (list, detail), `lastReadSequence` (detail) | handoff §14.5 | yes (48 of 964 unread for the demo owner) | no |
| B5 | `POST …/{id}/read { sequence }` → 204; `conversations.read.sequence.invalid` | same | `-1` → 400 with the code. A valid mark was **not** sent (it would move the demo owner's marker) | no |
| C1 | `lastIndexAttempt { status, failureCode, startedAt, completedAt } \| null` on list and detail; 8 failure codes | `knowledge-base-rag.md` (37.5a) | yes on both | no |
| C2 | `sourceByteLength` on list items | same | yes | no |
| C3 | `knowledge.file.*` codes and `limit` | `problem-codes.md` | yes: `unsupported_type` 415, `too_many_columns` 422 + `limit`; nothing was created | no (detail shown) |
| C4 | `channels [{ kind, id, status }]` on agent list and detail | `ai-agent-vertical-slice.md`; OpenAPI `AiAgentChannelResponse` | yes | no: two list reads instead |
| C5 | `launcherPosition: Left\|Right\|Start\|End` on widget GET/PUT and the embedded read | `embeddable-web-chat-widget.md` §8 | GET answers `widget.launcherPosition` (nested under `widget`); `"Top"` → 400 `website-chat.launcher_position.invalid`; Start/End not written (it would change Northwind's widget) | form offers Left/Right; the embed forwards all four |
| C6/C7 | `workspaceName`, `greeting`, `accentColor`, `theme` on `GET /api/public/webchat/{key}`; `workspaceName` on `GET /api/public/embed/{key}` | §8 of the widget doc; OpenAPI | yes (embed read with `X-OmniAI-Embed-Origin`); accent is `#RRGGBB` in the seed, theme `System` | hosted page reads `agentName`, `available`; embed reads appearance but not `workspaceName` |
| C9 | knowledge, agent, website-chat, WhatsApp, workspace codes | `problem-codes.md` | yes for those probed | no |
| C10 | snippet attribute | 37.1 R1 | `data-nabra-key` | shown as sent |
| D1 | `GET /api/tenants/{id}` `ETag`; `PUT …/name { name }` with `If-Match` → 200 + `ETag`; 400 `workspace.name.invalid`, 403, 404, 412, 428 | 37.5a §3.4; OpenAPI | `ETag` yes; 428, 400 (`If-Match` and name), 412, 403 (support) all as documented; the tenant's tag was unchanged afterwards. A successful rename was not sent | no |
| D2 | `memberCount` on roles list and detail | `tenant-rbac.md` | yes | no |
| D3 | `GET …/invitations?status=…&page&pageSize` → `{ items, page, pageSize, total }`; `invitations.status.invalid` | T21; the OpenAPI summary (status is read from the query string and not declared as a parameter, by design, 37.5b §3.2) | yes: Pending 0, Accepted 10, Revoked 1, Expired 2; bogus → 400 | no (one page of 100, no filter) |
| D4 | `language?` on `POST …/invitations` | 37.3 §3.6; OpenAPI `CreateInvitationRequest` | in the schema; not sent (it queues an email) | no |
| D5 | `groupKey` on permissions; `workspace.manage` | `tenant-rbac.md` | yes: 11 keys | labels by `code` already (`permission-words.ts:163-168`); areas by the English `group` |
| D6 | `currentPlan { code, name, limits }`, `fallbackPlan \| null` | B25; OpenAPI | yes; `fallbackPlan` null on the dev stack | no (`currentPlanName` only) |
| D7 | role, invitation, team, email-address and billing codes | `problem-codes.md` | `invitations.status.invalid` yes | no |
| E1 | `timeZone` + `ETag` on the tenant; `PUT …/time-zone`; `workspace.time_zone.invalid` | 37.6 §3.1; OpenAPI | yes: `UTC`; `asia/riyadh` → 400 | no |
| E2 | `period.timeZone`; catalogue `timeZone`; execution `timeZone` | `analytics-usage-foundation.md` §4; reports §9 | yes, all `UTC` | typed (`analytics-api.ts:32`, `reports-api.ts:42`) but every screen prints "UTC" itself |
| E3 | `compare=previous` → `previous { period, summary, aiOperations }`, absent otherwise; `analytics.compare.invalid` | §4, §8 | yes, all three | no |
| E4 | `firstActivityOn` | §8 | yes (a date) | no |
| E5 | `aiUsage.byPurpose[]` | `provider-usage-cost-accounting.md` §17 | yes, five purposes in the documented order | no |
| E6 | catalogue `reports[].type`, `parameters[] { code, values }` | reports §2 | yes | names and descriptions already by `type` (`reports-words.ts`); `parameters` not read |
| E7 | `items[].parameters.reportType` on notifications | `operational-notifications.md` §8 | yes: 32 of 35 items | no (regular expression) |
| E8 | `language?` on report and schedule writes; `language` on executions, schedules, `lastExecution`; `language.unsupported` | reports §7, §11 | yes (executions `ar`, `en`; `fr` refused) | no |
| E9 | `models[].provider` `DevelopmentStandIn` | cost accounting §16 | yes | provider not shown |

**Result.** Every contract is documented and present live. **No documentation–live disagreement** was found, so nothing stops the plan. Three remarks, none blocking:
- The OpenAPI `ProblemDetails` schema lists no `code`, `codes` or `limit` (they are extension members), and a few writes declare `200` with no schema. Live responses carry them; documentation ask BA-5.
- Two of the probe's own checks were first wrong and corrected in a second run: `launcherPosition` sits under `widget`, and the embedded read needs `X-OmniAI-Embed-Origin`.
- Not probed live, documented only: 503 `auth.throttle.unavailable`, `state.conflict`, `auth.email.invalid`, `auth.registration.rejected`, and the two handoff event kinds.

### 3.3 Where the list and the reports differ (the reports win)

| Item | The list says | The reports and the live API say | Plan |
| --- | --- | --- | --- |
| C10 | note that the snippet keeps `data-omniai-key` | 37.1 Revision 1 moved the snippet to `data-nabra-key`; the loader still reads the old name | **Outside Phase 38.** Nothing to explain; and naming `data-omniai-key` on screen would print the code name (rule 18) |
| B1 | drop the client copy of the label rule | the label code exists on conversation reads only | Dropped in the inbox; kept for the customers screens until BA-1 |
| B7 | "preload the Arabic font" | `MASTER.md` §4 rules out `<link rel=preload>` for hashed fonts | Same effect through `document.fonts.load()` (§3.8) |
| A4 | "signed-in flows keep `/me`" | `/session` returns the same user as `/me` (37.5b test) | One request at startup instead of `/me`; `/me` stays the fallback and the signed-in read (§3.5) |

### 3.4 The plan: one row per item

Step names: **1a** = 38.1a, **1b** = 38.1b, **2a** = 38.2a, **2b** = 38.2b, **3** = 38.3, **4** = 38.4, **5** = 38.5, **6** = 38.6. States: L loading, E empty, X error, P no permission. Strings are estimates of new English messages, each with its Arabic; the glossary terms named are the ones the wording must follow.

#### A. Foundation and accounts

| Item | Step | Screens and files | Contract | Strings (glossary) | States | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A1 Refusals by code | **1a** (mechanism, generic and account codes); each area's codes in its own step (B6, C9, D7, E-codes) | new `core/problems/` (registry, core catalogue); `core/api/api-error.ts` (`codes`, `limit`, message by code) | `code`, `codes`, `limit`, `Retry-After`; generic codes | ~22 in 1a (Sign in, Workspace) | X only; no screen layout change | `problem-words.spec.ts`, `api-error.spec.ts` (each code, both languages, unknown code, `limit` and `Retry-After` interpolation, never `detail` in Arabic) | M; a catalogue that grows with every area: kept lazy per area (§3.5) |
| A2 Sign-in throttling | **1a** | `features/auth/login/` | 429 `auth.throttled` + `Retry-After`; 503 | ~8 (Sign in; minute plurals) | X; button disabled until the wait ends | `login.spec.ts` (en/ar, countdown announced politely, never every second); e2e `sign-in-throttled` | S; spending the network's failure budget in tests (§3.12) |
| A3 Sign-in and registration codes | **1a** | `login`, `register`, `password-policy.ts` | `auth.*`, `codes` | in A1's count | X | `register.spec.ts`, `login.spec.ts` | S |
| A4 Session check | **1a** | `core/auth/auth-api.ts`, `auth-service.ts`, new `session-watch.ts`, `app.config.ts` | `GET /api/auth/session` | 0 | L (boot lockup, unchanged) | `auth-service.spec.ts`, `session-watch.spec.ts` (load, focus once a minute, 429 `Retry-After`, 503, other tab signed in); Chrome: no 401 on sign-in, create-account, forgot, reset, accept-invitation, en and ar; e2e `signed-out-no-401` | M; the shared 600 per 10 minutes (BA-6) |
| A5 "You can sign in now" | **1a** | `features/auth/reset-password/` | none | 2 (Sign in) | success | `reset-password.spec.ts` | S |
| A6 "Emails in English" | **1a** | `layout/profile-menu/` | `language` from the session user | 2 (Language) | none (a read-only line) | `profile-menu.spec.ts` | S; null reads as English, the server's rule |
| A7 One realtime connection | **1b** | new `core/realtime/workspace-realtime.ts`; `ConversationRealtime` and `NotificationRealtime` become thin facades over it | `/hubs/workspace-inbox`, both joins | 0 | live / not live, quietly, as today | `workspace-realtime.spec.ts` (one connection, joins by permission, workspace switch, sign-out closes, reconnect re-reads both); Chrome: one hub connection with bell and inbox, both live | M; reconnect and permission changes |
| A8 Sound service, setting, files | **1b** (rules wired in 2b) | `public/sounds/*.v1.wav`; new lazy `core/sounds/`; account menu; Notification preferences ("In this browser" section) | none | ~12 (Notifications) | audio unavailable → silent | `sound-player.spec.ts`, `sound-rules.spec.ts` with fakes (§3.7); e2e `sound-test-button` | M; autoplay rules and several tabs |
| A9 End-to-end tests | **1a** harness, then each step adds its journeys; **6** completes | `frontend/playwright.config.ts`, `frontend/e2e/` | none | 0 | — | §3.12 | M; only if approved (§4.3 #1) |

#### B. Inbox

| Item | Step | Screens and files | Contract | Strings (glossary) | States | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| B1 Customer on rows | **2a** | `conversation-list`, `conversation-detail`, `conversation-context`, new `customer-label.ts` beside `conversation-words.ts` | `customer {…}` | 4 (Customers, WhatsApp) | as today | `customer-label.spec.ts` (four codes, unknown code, isolation of code and number), list and detail specs, en/ar | S |
| B2 Newest messages first | **2a** | `conversations-api.ts`, `conversation-detail.ts`, `conversation-transcript.ts` | `tail`, `before`, `previousCursor` | 3 (Transcript) | L top spinner for older pages; X inline retry; "start of the conversation" | transcript specs (first window 40, older pages, anchoring, live arrival unchanged); e2e `deep-link-newest-first` | **L**; two scroll containers depending on width |
| B3 Dividers from the server | **2a** | `transcript-rows.ts`, transcript template | `include=events` | 3 (Take over, Return to AI) | none | `transcript-rows.spec.ts` (de-duplication by `id`, placement by `afterSequence`, no actor, no invented history) | M; pre-37.4 handoffs lose their inferred divider (§9) |
| B4 Needs a person | **2b** | inbox list (view switch in the URL), `conversation-inbox`, overview attention row | `filter=needs-person`, `total` | 5 (Inbox) | L/E/X per view | list spec (URL view and page, count, empty), overview spec; e2e `needs-a-person` | M |
| B5 Unread and mark read | **2b** | list rows, detail, transcript (newest-row observer) | `unread`, `lastReadSequence`, `POST …/read` | 2 (Inbox) | P: no dot without `conversations.view` (no list) | specs: dot has a name; mark only when the newest row is visible, the tab visible and focused, after 1 s; never on an unseen conversation | M; day-one unread everywhere (§9) |
| B6 Four inbox codes | **2a** | `features/conversations/conversation-problems.ts` | four codes | 4 | X | catalogue spec | S |
| B7 Arabic font on inbox pages | **2a** | new `core/i18n/arabic-fonts.ts`, called by list and detail | none | 0 | none | spec (called once, only on an English page, only when Arabic text arrives); Chrome deep-link trace | S |
| F Sounds wired to the inbox | **2b** | `core/sounds/inbox-signals.ts` | the reads of B4, B3 | ~3 (desktop alert reasons, if approved) | none | §3.7; e2e `ai-failed-sound`; live check with the only tab hidden for 10 minutes | **L**; request cost per burst (§3.7) |

#### C. Knowledge, agents, channels and the public pages (38.3)

| Item | Step | Screens and files | Contract | Strings (glossary) | States | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 Last indexing attempt | 3 | knowledge list, knowledge editor, `knowledge-words.ts` | `lastIndexAttempt` | 11 (Indexing, Knowledge): Indexing now, Failed + 8 reasons, stopped | as today | list and editor specs, en/ar, every status and reason | S |
| C2 File size | 3 | knowledge list | `sourceByteLength` | 1 (typed text) | null → typed text | spec (KB, MB, Western digits) | S |
| C3 File refusals | 3 | upload and replace (`knowledge-file.ts`), `knowledge-problems.ts` | `knowledge.file.*`, `limit` | 20 | X | catalogue spec with `limit` in both languages | S |
| C4 Agent channels | 3 | agent list, agent editor; delete `agent-channels.ts` reads | `channels` | 0 (channel words exist) | "no channel yet" | specs; Chrome: two requests fewer on the agents list | S |
| C5 Start/End corners | 3 | widget settings | `launcherPosition` | 2 (Widget) | none | widget spec (four values, a widget set to Start through the API shows it) | S |
| C6 Visitors see the name | 3 | widget settings | `workspaceName` | 1 | none | spec | S |
| C7 Hosted page appearance | 3 | `features/chat/hosted-chat/`, `shared/chat/` | `workspaceName`, `greeting`, `accentColor`, `theme` | 0–1 | L/X as today | specs: strict `#RRGGBB`, AA text colour or product accent, theme with no flash, name isolated | M; contrast fallback |
| C8 Hosted page language | 3 | `src/index.html` pre-paint script, `theme-startup.spec.ts`; optional choice in widget settings | none (frontend) | 0 (+4 if §4.3 #5) | none | startup spec cases (§3.9); e2e `hosted-page-ar` | S; one inline script stays one |
| C9 Area codes | 3 | `knowledge-problems.ts`, `agent-problems.ts`, `channel-problems.ts`, workspace setup | knowledge, agent, website-chat, WhatsApp, workspace codes | ~45 | X | catalogue specs | M; volume of Arabic |
| C10 Snippet note | outside Phase 38 | — | superseded by 37.1 R1 | 0 | — | — | — |

#### D. Workspace, team, roles, invitations and billing (38.4)

| Item | Step | Screens and files | Contract | Strings (glossary) | States | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D1 Workspace settings, name | 4 | new `features/workspace-settings/` route and nav item; `TenantApi` (versioned get, rename); `WorkspaceContextService` refresh | tenant `ETag`; `PUT …/name` | ~9 (Workspace) | L skeleton; X; P read-only; stale (412) notice; unsaved-changes guard | page spec (holder, non-holder, 412, 428, 403); e2e `rename-workspace` with a stale second tab | M |
| D2 Members per role | 4 | role list, role editor | `memberCount` | ~7 (Role, Member; plurals) | as today | specs, en/ar plurals | S |
| D3 Invitation tabs | 4 | team page | `status`, paging | 8 (Invitation) | L/E per tab/X | team page spec (URL tab and page) | M |
| D4 Invitation language | 4 | invite form | `language?` | 3 (Language) | none | spec; Mailpit check described (no link kept) | S |
| D5 Areas by key | 4 | `permission-groups.ts`, `permission-words.ts`, picker, member detail | `groupKey` | 11 (Roles & permissions) | none | spec: every key in both languages, unknown key → English group | S |
| D6 Plan limits | 4 | billing page | `currentPlan`, `fallbackPlan` | ~4 (Plan, Fallback plan, Usage limit) | null limit = unlimited; null fallback → section omitted | spec | S |
| D7 Admin codes | 4 | `team-problems.ts`, `role-problems.ts`, `billing-problems.ts` | roles, invitations, team, email, billing, entitlement codes | ~36 | X | catalogue specs | M; volume of Arabic |

#### E. Insights and reports (38.5)

| Item | Step | Screens and files | Contract | Strings (glossary) | States | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| E1 Time zone setting | 5 | workspace settings page (from D1) | `timeZone`, `PUT …/time-zone` | ~7 (Workspace, Schedule) | P read-only; confirm before saving (schedules move) | spec (list, UTC first, refusal, 412) | M; browser and server zone lists differ (§9) |
| E2 Dates in the workspace zone | 5 (with E1) | `core/i18n/locale-format.ts` zone helper; the files in §3.10 | `period.timeZone`, catalogue and execution `timeZone` | ~8 rewritten | none | helper spec (UTC, Riyadh, a DST zone; en/ar); page specs | M; "today" for a custom report range must be the zone's today |
| E3 Previous period | 5 | analytics dashboard, overview strip | `compare=previous` | ~7 (Analytics) | no earlier data | spec (up, down, flat, zero previous, "so far") | M |
| E4 Data since | 5 | analytics, overview | `firstActivityOn` | 3 | null → honest empty state | spec | S |
| E5 Usage by purpose | 5 | provider usage | `aiUsage.byPurpose` | 7 | `aiUsage` null → empty | spec (currencies never added) | M |
| E6 Report options by code | 5 | reports page, schedules | `parameters` | ~2 | unknown value shown as its code, isolated | spec | S |
| E7 Notification report names | 5 | `notification-words.ts` | `parameters.reportType` | 0 (reuses report names) | null → plain sentence | spec; regular expression removed | S |
| E8 Report file language | 5 | report form, schedule form and list, execution rows | `language` | ~6 (Reports, Language) | none | specs; e2e `arabic-report` | M |
| E9 Stand-in provider label | not adopted in Phase 38 | — | — | 0 | — | — | no provider column is shown; the model name `local-stand-in` already says it |
| E10 Overview on phones | 5 | `features/overview/` styles | none | 0 | — | Chrome at 390 px, en/ar | S |
| E11 Measure the reports screen | 5 | — | none | 0 | — | Chrome requests and timings, 9 interleaved runs | S |
| Remove the English-detail fallback | 5 (last area step) | `api-error.ts` | — | 0 | — | spec | S (§3.5) |

#### The owner's decisions

| Decision | Step | Effect |
| --- | --- | --- |
| O1 Kalimba sounds (2026-11-07) | 1b files and service, 2b rules | the three files of section F, versioned |
| O2–O7 (§4.3, once answered) | 1a (#1, #7), 1b (#2, #3, #7), 3 (#5), 4 and 5 (#4), none (#6) | recorded in §4.3 before 38.1 |

### 3.5 Design: refusal wording (A1, A2, A3)

**One shared place: `core/problems/`.**
- `problem-words.ts`: a registry of wordings keyed by `code`. A wording is a string, or a function of the facts `{ limit, retryAfterSeconds, codes, language }`. Message ids are stable: `@@problem.<code>` (for example `@@problem.auth.throttled`).
- `core-problems.ts` (in the startup bundle, small): the generic codes, `antiforgery.invalid`, `language.unsupported` and the account codes, because the sign-in page needs them before any feature loads.
- **Area catalogues live with their feature and load with it:** `features/knowledge/knowledge-problems.ts` and the like, registered by the feature's routes through `provideProblemWords(...)` (an environment initializer). The registry is one place; the words arrive lazily, so 150-odd sentences never sit in the startup JavaScript.
- `toApiError()` keeps its shape. It also reads `codes` and `limit` from the body, and its `message` becomes, in order:
  1. the registered wording for `code`;
  2. otherwise the existing generic words for the kind (`MESSAGES`), and the existing special cases (a non-AI 503, an antiforgery refusal);
  3. **never `detail` on an Arabic page.** On an English page, `detail` stays as the fallback **only** for codes no catalogue has registered yet, so screens not yet moved keep working as they are (the 38.1 prompt). 38.5, the last area step, removes that fallback: from then on `detail` is never shown, as `problem-codes.md` says. See §4.3 #7c.
- `codes` become a list: the reset and registration pages show one line per broken rule (the existing `password-policy.ts` words).
- `Retry-After` reaches the words: "Too many attempts. Try again in 15 minutes." / «محاولات كثيرة. حاول مرة أخرى بعد 15 دقيقة» with all six Arabic plural forms and Western digits; under a minute, seconds.
- `limit` reaches the words in its unit: bytes as KB/MB, pages, characters, rows, columns, entries.
- **Field errors (§4.3 #7c).** When a 400 carries `errors`, `toApiError` also returns the **names** of the fields it lists, and never its messages.
  - The form marks those fields in our own words: each form maps a field name to its existing translated rule, and a field it has no sentence for gets one shared "Check this field." The server's English in the map is never shown, in either language, so a person still knows what to fix once `detail` is gone.
  - 38.1a builds this with the account forms; each other form adopts it in the step that moves its area.
  - **Today no endpoint answers with `errors`** (`problem-codes.md`, "No `errors` map in these areas", confirmed in the API source for every area) and no screen reads it. So it is proven with unit tests on a constructed body, and there is nothing to check live until an endpoint sends the map. The refusals that lose their English sentence carry one code and no field names, so BA-4 is still what gives those specific words.
- About 40 screens already show `failure.message`, so moving an area means registering its catalogue, with no template change.

**Sign-in (A2).** On 429 the form says when to try again, keeps the button disabled until then, and updates a polite live region at most once a minute (never every second). On 503 `auth.throttle.unavailable`: "Signing in is unavailable for a moment. Nothing was changed." The countdown is client time from the moment of the answer, never a parsed date.

**Which codes each screen can meet.** Codes no request from this client can produce (`*.id.invalid`, `*.page.invalid`, `team.member.protected`, and the internal consistency codes) keep the generic words.

| Screen | Codes worded | Step |
| --- | --- | --- |
| Sign in | `auth.credentials.invalid`, `auth.throttled`, `auth.throttle.unavailable`, `antiforgery.invalid` | 1a |
| Create account | `auth.registration.incomplete`, `auth.email.taken`, `auth.email.invalid`, `auth.password.*` (6 rules, `codes`), `auth.registration.rejected` | 1a |
| Forgot / reset password | already worded; moved onto the registry: `auth.reset.invalid`, `auth.password.*`, `auth.throttled`, `auth.throttle.unavailable` | 1a |
| Accept invitation | `invitations.no_longer_usable`, `invitations.account.mismatch` | 1a |
| Every conditional write | `precondition.failed`, `precondition.required`, `precondition.if_match.invalid`, `validation.failed`, `state.conflict` | 1a |
| Inbox | `conversations.filter.invalid`, `transcript.cursor.conflict`, `transcript.include.invalid`, `conversations.read.sequence.invalid`, `whatsapp.conversation.not_on_whatsapp`, `whatsapp.message.required`, `whatsapp.message.too_long` | 2a |
| Knowledge (list, create, editor, upload, agent knowledge, retrieval test) | `knowledge.title.*` (3), `knowledge.content.*` (3), `knowledge.document.already_*` (2), `knowledge.indexing.in_progress`, `knowledge.index.too_many_chunks`, `knowledge.source.missing`, `knowledge.assignment.*` (2), `knowledge.search.question.*` (2), `knowledge.file.*` (20, with `limit`), `entitlement.knowledgedocuments.exhausted` | 3 |
| AI agents (create, editor, preview) | `ai-agents.name.*` (3), `ai-agents.instructions.*` (3), `ai-agents.disable.draft`, `ai.message.invalid`, `entitlement.aiagents.exhausted` | 3 |
| Website chat | `website-chat.agent.required`, `.agent.unusable`, `.origin.invalid`, `.origin.too_many`, `.launcher_position.invalid`, `.theme.invalid`, `.accent_color.required`, `.accent_color.invalid`, `.greeting.too_long`, `.greeting.control_characters`, `entitlement.webchatendpoints.exhausted` | 3 |
| WhatsApp | the 10 admin `whatsapp.*` codes, `entitlement.whatsappconnections.exhausted` | 3 |
| Hosted chat and widget | `chat.message.required`, `.too_long`, `.in_progress`, `.already_failed` (the public 404 stays uncoded by design) | 3 |
| Workspace setup | `workspace.name.invalid`, `workspace.slug.taken`, `workspace.slug.invalid` | 3 |
| Workspace settings | `workspace.name.invalid` (4), `workspace.time_zone.invalid` (5) | 4, 5 |
| Team, invite | `invitations.role.unassignable`, `.email.already_member`, `.email.already_invited`, `.revoke.closed`, `.status.invalid`, `team.member.self`, `.owner_protected`, `team.role.unassignable`, `email.*` (4), `entitlement.members.exhausted` | 4 |
| Roles | `roles.name.*` (4), `roles.permission.invalid`, `.unknown`, `roles.system.protected` | 4 |
| Billing | the 6 `billing.*` codes | 4 |
| Analytics | `analytics.compare.invalid` | 5 |
| Reports and schedules | `language.unsupported`, `entitlement.monthlyprovideroperations.exhausted` (generation), and the generic codes this area still answers (BA-4) | 5 |
| Customers, notifications | generic codes only (BA-4) | — |

### 3.6 Design: the session check (A4)

- **Startup.** On every page that is not a visitor page, `AuthService.restore()` asks `GET /api/auth/session` (with `/api/auth/csrf` beside it, as today) instead of `/api/auth/me`. One request answers both questions, and a signed-out page never meets a 401. The user it returns is the same projection as `/me` (pinned by a server test), so nothing downstream changes.
- **`/me` keeps two jobs.** It is the fallback when the session check answers 429 or 503, or does not answer: startup then asks `/me` exactly as today, so a throttled network still lets signed-in people in (only then can the console 401 come back). And it stays the signed-in read in `AuthApi`.
- **Signed-in pages** do not ask on focus. Their ordinary reads already meet a 401 when the session ends, and the existing interceptor sends them to sign-in.
- **Signed-out pages** (sign-in, create account, forgot and reset password, accept invitation) ask again on window focus or when the tab becomes visible, **at most once a minute, never on a timer.** If it now says signed in (somebody signed in from another tab), the page goes where a fresh sign-in would go.
- **429** stops every check until `Retry-After` has passed. **503** or no answer leaves the page usable as it is.
- **The duplicate workspace read.** Every signed-in page load reads `/api/me/tenants` twice today (Chrome, §5). 38.1a traces it while reworking startup, and fixes it only if it is part of this path.

### 3.7 Design: one realtime connection (A7) and the sounds (section F)

**The connection.**
- `WorkspaceRealtime` (root) owns **one** connection to `/hubs/workspace-inbox` per page. It imports SignalR lazily, as the bell does today, through an async builder token, so tests keep their fake.
- It is started by the shell when a workspace is chosen. It joins `SubscribeNotifications(tenant)` always, and `Subscribe(tenant)` only while the member holds `conversations.view`: re-evaluated when permissions change (`Unsubscribe` when lost).
- A workspace switch, sign-out or a cleared workspace stops it. The public hub is untouched.
- Listeners: `onNotifications(cb)` for the bell, `onConversation(cb)` for the inbox list, the open conversation and (2b) the inbox signals. `ConversationRealtime.listen()` and `NotificationRealtime.listen()` keep their signatures as facades, so today's callers change little.
- After a reconnect it re-joins both groups, then calls every listener with "anything may have changed" (`null`). The bell re-reads its count; the inbox re-reads as today.
- Result: the inbox page negotiates once instead of twice. Requests per `conversationChanged` stay at the baseline (list, detail, transcript; bell none).

**Sounds: what decides each one.** The hub sends only an id, so every sound is decided from authorised reads, coalesced:
- **A burst** is the events that arrive within 750 ms of the first, capped at 2 s. One burst asks for one read.
- **One reader per browser and workspace.** Only the tab holding the long-lived Web Lock `nabra:inbox-signals:{workspaceId}` reads for the sounds and the needs-a-person count.
  - Every tab of that workspace asks for the lock; the browser grants it to one, and to the next when that tab closes.
  - The reader publishes each result (the `total`, the sounds it decided, and the snapshot of the set) on `BroadcastChannel('nabra:inbox-signals:{workspaceId}')`. The other tabs of that workspace use it and read nothing. A tab that opens asks on the channel for the last result.
  - It reads only while sounds are on or a tab of this workspace shows the count (tabs say so on the channel).
- **At most one read every 5 seconds, with one trailing read.** A read starts when its burst closes, unless the previous read started less than 5 s ago; then it waits for the 5-second mark. Events arriving while a read waits or runs cause exactly one more, so the last change is never missed. A busy workspace costs each browser **at most 12 of these reads a minute**, whatever the event rate.
- **The read.** One `GET …/conversations?filter=needs-person&page=1&pageSize=50` (the server's page maximum). Rows are most recently active first, so a conversation that just entered the set is on page 1. The same read gives the needs-a-person `total`, so the count costs nothing extra.
- **Baseline.** The first read after a page load records the set (id → `lastActivityAt`, `handling`, `assignedTo`) and plays nothing. A tab that inherits the lock starts from the last snapshot it heard on the channel, so a handover is not a new baseline and misses nothing.
- **Needs a person.** An id on page 1 that was not in the previous set. A colleague's takeover of an old conversation that lands beyond page 1 is not heard: a stated limit, never a false sound.
- **New message.** A row already in the set, `assignedTo.userId` is me, `lastMessageSender` is `Customer`, its `lastActivityAt` moved, and I am not viewing it in a visible tab (tabs say on the channel which conversation they show). "Taken over by me" has no field on rows, and taking over does not assign (handoff §6), so it is not counted until BA-2.
- **AI failed.** A row in the set handled by the AI whose `lastActivityAt` moved, or that is new. For each (at most 3 per read), the reader makes one `GET …/messages?tail=true&limit=1&include=events`; an `AiTurnFailed` whose `id` it has not seen plays. When that conversation is the one open in the reader tab, its own transcript read supplies the events and no extra read is made.
- **Request cost, per browser and workspace, however many events arrive:** from the reader tab only, **at most 1 needs-a-person read every 5 seconds (12 a minute)**, plus **1 small read per AI-handled conversation that changed in the set**: normally 0 or 1, never more than 3 per read (36 a minute at the very worst, and only while AI replies keep failing). Every other tab: 0. All of this is on top of what each page already reads.

**Sounds: when they play.**
- **Priority and the 2-second limit.** Within a burst the most important wins: AI failed, then needs a person, then new message. Plays are at least 2 s apart. A better candidate arriving inside the quiet window waits for it and then plays; a lesser one is dropped.
- **One tab plays.** Playing takes the Web Lock `nabra:sound-play` (`ifAvailable`) and holds it for 2 s, which is also the cross-tab 2-second limit. A visible tab tries at once; a hidden tab tries 400 ms later and only if no visible tab has said, over `BroadcastChannel('nabra:sounds')`, that it is visible. Where Web Locks are missing, the channel alone elects the tab.
- **Own actions are silent.** This browser's own reply, takeover, return to the AI, assignment and mark-read are recorded with their conversation id for 5 s, shared over the channel, and excluded.
- **Never the only signal.** The count, the unread dots and the dividers show the same facts on screen.

**Sounds: hidden tabs.** Sounds matter most when nobody is looking at Nabra.
- The reader keeps its Web Lock for as long as the page lives. The SignalR client documentation recommends holding one, to lower the chance that the browser puts a hidden tab to sleep; the installed client (10.0.11) itself warns that a frozen page loses its connection and messages.
- The lock lowers that chance; it does not remove it. A tab the browser discards can play nothing. On screen, the count and the dots are right again at the next page load.

**Sounds: playing them.**
- **Files.** `frontend/public/sounds/kalimba-needs-person.v1.wav`, `kalimba-new-message.v1.wav`, `kalimba-ai-failed.v1.wav`, byte-identical to the pack (SHA-256 recorded at copy: `56c4aa0c…`, `76e9c14b…`, `404d920d…`). Served `no-cache` by the edge, which is acceptable: the names are versioned and a revalidation is a 304.
- **Loaded once, lazily.** The sound code is a lazy chunk; the shell imports it after first render, and only for members with `conversations.view`. The three files (≈313 KB together) are fetched and decoded with Web Audio the first time a sound is needed or the test button is pressed. Never in the initial bundle.
- **Unlock.** Browsers play audio only after a gesture. The `AudioContext` is created or resumed on the first `pointerdown` or `keydown` after load: one passive, one-time listener. Before that, sounds are skipped silently. Turning sounds on is itself the gesture.
- **The setting.** Stored in this browser like the theme, through `readStored`/`writeStored`, which survive storage that throws: `omniai.sounds` (`on`/`off`), `omniai.sounds-volume` (0–100, default 70), `omniai.desktop-alerts` (`on`/`off`, default `off`). The keys keep the internal prefix like the others; no user sees them. The sounds' default is §4.3 #2.
- **Where it is set** (§4.3 #7b). A menu built with ARIA `menu` roles may only hold menu items, so a volume slider inside the account menu would break keyboard and screen-reader behaviour. The account menu gets a **"Sounds" switch** (`menuitemcheckbox`) and a **"Sound and alert settings"** item. That item opens an **"In this browser"** section on Notification preferences, with the volume, "Play a test sound" and (if approved) desktop alerts.
- **Volume** is a `GainNode`; a failed decode or a missing `AudioContext` is silent and never shows an error.

**Desktop notifications (only if §4.3 #3 is yes).**
- A separate switch. `Notification.requestPermission()` is called only inside that switch's click handler. A refusal shows a note on how to allow it in the browser.
- Shown only when the tab is hidden or unfocused, by the same single tab that plays.
- Title: the customer's label ("Visitor 7KQ2MX"). Body: the reason ("Needs a person", "The AI could not reply", "New message"). Never transcript text. `tag` = conversation id; `lang` and `dir` set.
- Clicking it focuses the tab, opens the conversation and closes the notification.

**Tests.**
- Vitest, with fakes for `AudioContext`, `fetch`, the clock, `BroadcastChannel`, `navigator.locks` and `Notification`: the first-read baseline; entry detection; new message (mine, viewing, hidden); AI failed (seen ids, the cap); priority and the 2-second window; own-action suppression; one tab; setting off; nothing fetched until needed; unlock on first gesture; decode failure silent; the permission prompt only from a click.
- Vitest also covers the reader: one per browser and workspace, the 5-second cap with its trailing read, other tabs fed from the channel, and a handover that keeps the snapshot.
- End-to-end (2b): a `[[fail]]` message plays the ai-failed sound and shows the divider.
- **Live check (2b), a hidden tab:** with the only Nabra tab hidden for at least 10 minutes, a `[[fail]]` message still plays its sound within a few seconds, and the realtime connection is up.
  - The report records the page's own `document.visibilityState` (it must read `hidden`), the time hidden, the time from sending to the sound, and that the hub connection never closed. One click before hiding unlocks the audio.
  - **Not driven by Playwright.** It starts Chrome with `--disable-background-timer-throttling`, `--disable-backgrounding-occluded-windows` and `--disable-renderer-backgrounding`, and turns on focus emulation for every page (both confirmed in its source), which would hide the very behaviour under test. The check runs in a headed Chrome started without those flags, with another tab in front, driven by plain DevTools-protocol calls (the `cdp.mjs` approach of Phase 36) that do not set focus emulation.
  - It takes over 10 minutes, so it is a live check of 38.2b and not a journey in the suite.

### 3.8 Design: the inbox (B)

- **Customer on rows (B1).**
  - `displayName` if set, isolated with `dir="auto"`.
  - Otherwise by `labelCode`: `AnonymousVisitor` → "Visitor 7KQ2MX" / «زائر 7KQ2MX»; `WhatsAppCustomer` → "WhatsApp customer ••••1234" / «عميل واتساب ••••1234»; `UnnamedCustomer`, or a code this build does not know → "Customer 7KQ2MX" / «عميل 7KQ2MX».
  - The short code and the number are `dir="ltr"` islands with `translate="no"`. The short code is never used as a key.
  - The customers screens keep `customerName()` until BA-1.
- **First window (B2).**
  - Every transcript opens with `GET …/messages?tail=true&limit=40&include=events`, deep links included.
  - Older pages load with `before={previousCursor}&limit=40&include=events` when an `IntersectionObserver` sentinel above the first row comes within 400 px, and from the existing "Show earlier messages" button (`data-testid="show-earlier"`), which stays as the keyboard path. When `previousCursor` is null: "Start of the conversation".
  - **Anchoring:** before prepending, the distance from the bottom is recorded; after the render it is restored (the pattern `conversation-transcript.ts` already uses), on whichever element scrolls at that width. `overflow-anchor: none` on the scroller prevents the browser compensating twice.
  - Live arrivals keep today's forward read (`after` = a few messages back, merged by sequence), now with `include=events`.
  - Today a deep link reads all 232 messages in 2 requests (69,970 B) to show 100. After: 1 request of 40, about 12 KB.
- **Dividers (B3).**
  - Events are kept in a `Map` by `id`, so a boundary event served twice is drawn once. Each is placed after the message whose sequence is its `afterSequence`.
  - `AiTurnFailed` → "The AI could not reply" (no actor). `TakenOver` → "{email} took over" (email isolated). `ReturnedToAi` → "{email} returned it to the AI".
  - The voice-change divider in `transcript-rows.ts` is removed. History before 37.4 has no events, so those conversations show no handoff divider: nothing is invented (§9).
- **Needs a person (B4).**
  - The inbox list gets a two-link view switch, "All" and "Needs a person (125)", kept in the URL (`?view=needs-person`, with `?page=` as today, `aria-current` on the chosen one). The count is the `total` of the needs-a-person read, shared with the sounds.
  - Empty view: "Nobody is waiting for a person."
  - The overview gets one attention row, "N conversations need a person", linking to that view, only for `conversations.view` holders. Its count comes from the reader's shared result (§3.7), with no read of its own.
  - No count in the sidebar: it would cost a read on every page.
- **Unread (B5).**
  - A dot with a visually hidden "Unread" and the customer's name in a heavier weight: never colour alone.
  - **Marked read** when the newest loaded message row is at least half visible in its scroller, the document is visible **and** focused, held for 1 s: then `POST …/read { sequence }` with the newest sequence on screen. Sent only if that sequence is above `lastReadSequence`, which is updated after the 204.
  - A conversation opened but scrolled away from its end is not marked. A failure is silent; the next sighting sends again. The list re-reads through the existing `InboxState.touched()`.
- **Arabic font (B7).**
  - On an English page, the first list, detail or transcript response containing Arabic script calls `document.fonts.load()` for the 400 and 600 faces of 'IBM Plex Sans Arabic', once per page.
  - The faces' `unicode-range` makes the browser fetch exactly those files, by their hashed names, while the transcript request is still in flight. An Arabic page already has them.

### 3.9 Design: the public pages (C7, C8)

- **`?lang=` before paint (C8).**
  - Visitor pages (`/chat/`, `/embed/`) take `lang=en|ar` from the query string first, then the browser's languages, then English. Admin pages keep the stored choice and ignore `?lang=`.
  - The change stays inside the one inline script. The edge rehashes it at image build, so no `deploy/` change.
  - `theme-startup.spec.ts` gains: `?lang=ar` on `/chat/` → `ar`/`rtl` with an English browser; `?lang=en` with an Arabic browser → `en`; `?lang=xx` and `?lang=AR` ignored; `?lang=ar` on an admin page ignored; storage that throws still decides.
- **Theme without a flash (C7).**
  - Before paint the hosted page follows the visitor's system preference, as now. The chat renders only after the config read, which it already waits for. The configured `Light` or `Dark` is set on `<html>` in the same task that renders it, so the chat never paints in the wrong theme.
  - Only the loading lockup can change background once, and never for `System`.
- **Accent (C7).**
  - Accepted only if it matches `^#[0-9A-Fa-f]{6}$`; anything else uses the product accent.
  - The text colour on it is the lighter or darker ink token, whichever reaches 4.5:1. If neither does, the product accent is used.
  - Bound as a CSS custom property through Angular's style binding, never through markup.
- **Name and greeting (C7).** `workspaceName` heads the chat (isolated, `dir="auto"`). The greeting is the configured text, rendered as text.
- **Embedded widget language.** The frame URL is built by `v1.js`, which the API serves. Passing the host page's language into it is **Backend ask BA-3**. Once it passes `lang`, the same pre-paint rule applies with no frontend change.
- **Optional (§4.3 #5).** A "Chat page language" choice (Automatic, English, العربية) beside the hosted link in the widget screen adds `?lang=` to the link that is shown and copied.

### 3.10 Design: workspace settings and dates (D1, E1, E2)

- **The page.**
  - A new route `/workspace-settings`, nav item "Workspace settings" («إعدادات مساحة العمل») in the Workspace group, visible with `workspace.view`.
  - Holders of `workspace.manage` edit; everyone else sees the values as text with "Only people who can manage this workspace can change these." Disabled inputs are not used.
  - **38.4:** the name, with `If-Match`, the stale-version notice on 412 ("Reload the latest" keeps the typed value), the unsaved-changes guard, a toast, and "Visitors see this name on the chat page and the widget." After a rename, the workspace list in memory is refreshed so the top bar and menu follow.
  - **38.5:** the time zone on the same page. A native `<select>` with UTC first, then `Intl.supportedValuesOf('timeZone')` grouped by region. The explanation: days in analytics, report periods and schedules follow this zone. Saving asks for confirmation when the workspace has schedules ("Scheduled reports move to the next start of a day in the new zone"). Saved with `If-Match`; both writes return a new tag.
- **One helper for zone-aware dates (E2).** In `core/i18n/locale-format.ts`:
  - `zonePhrase(zone)`: "in UTC" / «بالتوقيت العالمي» (glossary), else "in Asia/Riyadh time" / «بتوقيت Asia/Riyadh», with the id isolated: the same form as the report files (37.7).
  - Instants formatted in a given zone (the existing `timeZone` parameter).
  - `todayIn(zone)` for custom report ranges.
  - Date-only values (`yyyy-MM-dd` buckets and periods) are already the workspace's local dates, and stay formatted from their parts.
- **Every place that prints "UTC" today:**

| File | Today | 38.5 |
| --- | --- | --- |
| `features/analytics/analytics-words.ts:97` | "until" time formatted in UTC | formatted in `period.timeZone` |
| `features/analytics/analytics-words.ts:99` | "counted in UTC days … UTC" | the zone phrase |
| `features/analytics/analytics-words.ts:76` | day labels from date parts | unchanged (local dates); comment updated |
| `features/analytics/analytics-api.ts:31,51` | comments "always UTC" | comments updated |
| `features/reports/reports-page/reports-page.html:141` | "Whole days in UTC, including today" | the catalogue's zone |
| `features/reports/reports-page/reports-page.html:242` | column "Period (UTC)" | the execution's zone |
| `features/reports/reports-page/reports-page.ts:416` | custom range days by UTC arithmetic | today in the workspace zone |
| `features/reports/reports-api.ts:231-234` | period written from date parts | unchanged; label from the execution's `timeZone` |
| `features/reports/report-schedules/report-schedules.html:52` | "All times are UTC." | the zone phrase |
| `features/reports/report-schedules/report-schedules.html:245` | column "Next run (UTC)" | the zone |
| `features/reports/report-schedules/report-schedules.ts:316,349` | next run and "Ready · … UTC" in UTC | the workspace zone |
| overview strip | through `analytics-words.ts` | follows |
| `plan-usage-page.html:182`, `quota-row.ts:155-159`, `notification-words.ts:70` | quota months "(UTC)" | **unchanged: plan quota months stay UTC** (`workspace-plans-entitlements-quotas.md` §7) |
| `design-lab.html:502,508` | sample table | unchanged (development only) |

### 3.11 Design: knowledge, agents, admin screens (C, D, E in brief)

- **C1:** Started → "Indexing now" (a glyph and the word). Failed → "Indexing failed: {reason}" for the 8 reasons. Unknown → "Indexing stopped before it finished". Succeeded adds nothing. The editor shows the times.
- **C2:** sizes through `Intl.NumberFormat` units with Western digits; null → "Typed text".
- **C4:** agents read `channels`. The editor still reads the widget's look for the preview, by the WebChat channel's `id`.
- **D3:** tabs Pending (default), Accepted, Revoked, Expired, as links with the tab and the page in the URL. One read per view; only the open tab shows its total.
- **D6:** limits table, "Unlimited" for null. A null `fallbackPlan` omits that section, because null covers every "no usable fallback" case by design (37.5b).
- **E3:** each summary figure gets "↑ 12% vs the previous 30 days" (glyph and words, never colour alone). "So far" when the period includes today. "No earlier data" when the previous value is 0. The realtime figures are not compared. `compare=previous` rides on the same dashboard read: no extra request.
- **E5:** one row per purpose in the fixed order; cost one line per currency, never summed.
- **E8:** "File language: Arabic file / English file" on the report and schedule forms; a language column on schedules; a small tag (with an accessible name) on each execution.
- **E10:** below 768 px the attention row keeps the glyph and the status word; the title truncates; the action may wrap.

### 3.12 Design: end-to-end tests (A9, if approved)

- **Package:** `@playwright/test` **1.64.1**, pinned exactly (`-E`) in `devDependencies`. Released 2026-10-14; 1.65.0 is two days old. Installed with `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`: no browser download. It drives the installed Google Chrome 157 (`channel: 'chrome'`). Node 24.19 is installed.
- **Files:**
  - `frontend/playwright.config.ts`;
  - `frontend/e2e/` with journeys named `*.e2e.ts` (the unit runner's `*.spec.ts` never picks them up), `fixtures/`, `support/`, and its own `tsconfig.json`. Prettier covers them.
- **Config:**
  - projects `en-ltr` (locale `en-US`, `omniai.lang=en`) and `ar-rtl` (locale `ar-SA`, `omniai.lang=ar`, asserting `dir=rtl`);
  - `workers: 1`, `fullyParallel: false`, `retries: 0`, `timeout: 60 s` (a `[[slow]]` reply takes about 11 s);
  - `baseURL` `http://localhost:4200`, with a loopback guard like `tools/performance/lib/target.mjs`;
  - list reporter, plus JSON into the folder named by `NABRA_E2E_OUT`.
- **Script:** `"e2e": "playwright test"`, not part of `test:ci`. Its preflight checks the dev server and `/health/ready`. The provider canary is run first by the person running it (rule 9). The first AI journey asserts the stand-in's "Local test reply:" and fails loudly otherwise.
- **Signing in.** No password in any file, config or command line.
  - Global setup registers a **fresh synthetic account** through the real API (`e2e.<random>@example.test`, password from `crypto` in memory, never written). It creates the account's own workspace (named "E2E" plus the run's id, slug `e2e-<run id>`, so it can be recognised and removed before launch; a journey that renames it keeps the prefix), agent, knowledge and website chat through the API (the development plan allows them), and saves only the cookie state to `NABRA_E2E_STATE_DIR`: `<evidence>\p38-<N>\tmp\`. Global teardown deletes it.
  - Journeys that change settings use that workspace.
  - Each step's report says how many synthetic accounts and workspaces its runs created.
  - Read-only journeys on seeded Northwind data (the 232-message deep link) sign in once as the demo owner. The password is read by global setup from the file named by `NABRA_E2E_DEMO_ACCOUNTS` into memory. Without that variable those journeys are skipped and reported NOT RUN.
  - One sign-in per account per run; successes never count against the limits. The throttling journey uses one random unknown address: 6 failures in the English project; the Arabic project reuses that address and is refused at once, adding none.
- **Observing a sound.** The journey's own init script wraps `Response.prototype.arrayBuffer`, `BaseAudioContext.prototype.decodeAudioData` and `AudioBufferSourceNode.prototype.start`, and records each played file in `window.__e2eSounds`. Nothing in product code.
- **Journeys by step:**
  - 1a: sign in (en, ar); signed-out pages without a 401; sign-in throttled.
  - 1b: the test-sound button; bell and inbox live on one connection.
  - 2a: deep link newest first, older on scroll, place kept; failed-turn and takeover dividers.
  - 2b: a visitor's message arrives live with its unread dot; `[[fail]]` plays the ai-failed sound, shows the divider, raises the count; take over then return unanswered stays in the view; mark read.
  - 3: upload and index (stand-in) with the result after a reload; agent preview reply; hosted page `?lang=ar`; Start/End corner.
  - 4: rename with a stale second tab; invitation with a language (Mailpit checked by subject and language, link never kept); invitation tabs.
  - 5: time zone change and the analytics labels; previous-period deltas; an Arabic report file.
  - 6: gaps closed; the whole suite twice with no retries.
- **Time and memory (estimates, measured from 1a):** about 1–2 min at 38.1, 6–8 min at 38.6 for both projects. One headless Chrome, about 0.5–0.8 GB, beside the dev server and Docker. Run alone, after the canary.

### 3.13 Performance

- **Baseline (this step):** startup JavaScript 38 files, 421,672 B raw / **138,907 B gzip** / 122,940 B brotli (`gates\startup-js.txt`; same tool as Phase 36). Rule 15 ceiling for the phase: **152,797 B gzip**. The initial total is 313.56 kB raw / 73.66 kB transfer, against the 500 kB / 1 MB budgets (unchanged).
- **Budget per step (gzip, cumulative):** 1a ≤ +2,500 B (core catalogue, session watch); 1b ≤ +1,000 (the realtime owner; sound code lazy); 2a, 2b, 3, 4, 5 ≤ +500 each. Target ≤ 145,000 B (+4.4%). A step that exceeds its share says so in its report.
- **Loaded lazily:**
  - area refusal catalogues, with their features;
  - the sound code and files;
  - SignalR, as today;
  - the workspace settings page;
  - the zone list, which comes from `Intl`: no data shipped.
- **Request counts to beat (dev build, Chrome 157, 1280 px, Northwind):**

| Measure | 38.0 | Target |
| --- | --- | --- |
| Signed-out entry page (sign-in, create account, forgot, reset, accept invitation; en and ar) | 2 requests, **1 console 401** | 2 requests, 0 errors (1a) |
| Visitor page `/chat/{key}` | 2 requests, 0 errors | unchanged |
| Deep link, 232-message conversation | 12 requests: `/api/me/tenants` ×2, 2 hub negotiations, 2 transcript reads, 69,970 B of transcript, 100 rows rendered | ≤ 10: 1 negotiation (1b), 1 transcript read of 40, about 12 KB (2a); one tenants read if the duplicate is in the startup path (1a) |
| Analytics page | 7 requests (1 negotiation, dashboard 5,685 B) | 7: `compare=previous` and the zone ride on the same dashboard read (5) |
| One `conversationChanged`, inbox at 1280 px (list + open conversation) | 3 reads (detail, list, transcript); bell 0 | 1b: 3. 2b: 3 in each tab, plus at most 1 needs-a-person read from the reader tab, never within 5 s of its last |
| Same event on any other signed-in page | 0 (the page does not listen) | 2b: 0 in each tab except the reader, which makes at most that 1 read; for `conversations.view` holders with sounds on or the count on screen |
| Needs-a-person reads in a busy workspace, per browser and workspace | none today | 2b: **at most 12 a minute**, whatever the event rate, plus the small events reads of §3.7 (normally 0 or 1 per read, never more than 3) |

## 4. Decisions

### 4.1 Choices made in this plan

- **Split** 38.1 into 1a (A1–A6, A9 harness) and 1b (A7, A8). Split 38.2 into 2a (B1, B2, B3, B6, B7) and 2b (B4, B5, sounds wired). Each has three or more new services, and the transcript rework alone is large. 38.3, 38.4 and 38.5 fit one session each.
- **Harness in 1a**, so every later part can add journeys from the start.
- **Area catalogues lazy**, registered by their routes. The startup JavaScript stays inside rule 15.
- **The English-detail fallback** is kept on English pages only until 38.5, then removed (§3.5).
- **Session check** at startup on every non-visitor page, `/me` as the fallback, focus checks on signed-out pages only (§3.6).
- **The needs-a-person read serves three things:** the view's count, the overview row and the sounds.
- **Time zones shown by IANA id**, like the report files, not by localised names.
- **Skills.** From 38.1 on, each step loads every skill its prompt names, not only the lead skill, and its report says which ones were loaded.
- **Order and dependencies.**
  - 1a: A1 → A2, A3, A5 → A4 → A6 → A9.
  - 1b: A7 → A8.
  - 2a: B1 → B2 → B3, B6, B7.
  - 2b: B4 → B5 → F.
  - 3: C4 → C1–C3, C9 → C5, C6 → C7, C8.
  - 4: D1 → D2–D7.
  - 5: E1 → E2 → E3–E8, E10 → E11 → remove the detail fallback.
  - 6: the suite, the full pass, the summary.
- **Evidence each step produces** (in `<evidence>\p38-<part>\`): gates with memory before each; startup JavaScript before and after; Chrome before/after captures for the screens it touches (en/ar, light/dark, 390/768/1280/1920); the request counts in §3.13; e2e results; its new Arabic list.

### 4.2 Rejected or deferred

- `<link rel=preload>` for Arabic fonts (hashed names, MASTER §4): FontFace API instead.
- A sidebar needs-a-person badge: a read on every page.
- Counts on all four invitation tabs: four reads per visit.
- One read per hub event, or a read per row to find takeovers: rule F8.
- A volume slider inside the account menu: ARIA `menu` cannot hold it.
- Localised time-zone names: the report files use the id; the redesign can revisit.
- C10 and E9: not adopted (§3.3, §3.4).
- RUM: outside Phase 38 unless §4.3 #6 says otherwise.

### 4.3 Decisions for you (answer before 38.1)

| # | Question | Recommendation | Cost | Owner's answer |
| --- | --- | --- | --- | --- |
| 1 | **End-to-end tests:** add `@playwright/test` as a pinned dev dependency, using the installed Chrome? | **Yes, `1.64.1` exactly**, no browser download, harness in 38.1a | One dev dependency (with `playwright-core`), lockfile change; 1–2 min per step, 6–8 min at 38.6; about 0.5–0.8 GB while it runs; each run creates one synthetic account and workspace in the dev database | **Yes.** `@playwright/test` 1.64.1, pinned exactly, with no browser download; the harness comes in 38.1a |
| 2 | **Sounds:** on or off by default for members who can view conversations? | **On**, with the switch in the account menu; nothing plays before the first click on the page | A member who does not want them turns them off once per browser; shared offices may hear several machines | **On by default**, with the switch in the account menu |
| 3 | **Desktop notifications** in this phase, behind a click? | **Yes, off until each person turns them on** (the browser asks only after that click) | ~8 strings, one more switch, a permission flow; testable in e2e by granting the permission to the browser context | **Yes**, off until each person turns them on |
| 4 | **Default language** of the invite form (D4), the report form and the schedule form (E8) | **The language the sender is using Nabra in right now**, always sent explicitly. Editing a schedule shows and keeps its own language | None; the server's own default (the saved language) is usually the same | **The language the sender is using Nabra in**, always sent explicitly. Editing a schedule keeps its own language |
| 5 | **A language choice on the hosted chat link** in the widget screen (C8)? | **Yes:** Automatic, English, العربية | 4 strings in 38.3 | **Yes:** Automatic, English, العربية |
| 6 | **RUM:** confirm it stays outside Phase 38 | **Confirm.** There is nowhere to send the numbers (BA-8) | None | **Confirmed:** it stays outside Phase 38 |
| 7a | **The splits** 38.1a/38.1b and 38.2a/38.2b | **Approve** | Two more sessions, two more checkpoints | **Approved** |
| 7b | **Where the sound settings live:** a switch in the account menu, and volume, test sound and desktop alerts in an "In this browser" section of Notification preferences (§3.7) | **Approve** (a slider in an ARIA menu breaks keyboard and screen readers) | Differs from section F's "all in the account menu" | **Approved** |
| 7c | **Never show `detail`**, in either language, from 38.5 on | **Approve.** It is what `problem-codes.md` says, and English and Arabic readers then read the same thing | English readers of customers, reports, notification preferences and replies lose the server's specific sentence until BA-4 gives those areas codes; they see the generic words | **Approved**, with field errors kept in our own words (§3.5) |
| 7d | **The "new message" sound counts only conversations assigned to the member** until BA-2 | **Approve** | Conversations a member took over without being assigned stay silent for new messages; they still enter "Needs a person" with its sound | **Approved** |
| 7e | **The workspace settings page is visible read-only to every member** with `workspace.view`, editable with `workspace.manage` | **Approve** (the time zone explains the analytics days to everyone) | One more nav item for every member | **Approved** |

## 5. Verification

| Check | Result |
| --- | --- |
| Worktree at start | **PASS**: `master`, `1a9313d`, clean |
| Provider canary, before any traffic | **PASS** |
| `npm run format:check` | **PASS** (9 s) |
| `npm run test:ci` | **PASS**: **96 files, 1,169 tests**, 0 failures (39 s). Free memory before: **8.17 GB** |
| `npm run build` | **PASS**: 0 warnings, budgets met; initial 313.56 kB raw / 73.66 kB transfer. Free memory before: **8.03 GB** |
| Startup JavaScript (loader + application chunk, closure) | **Baseline**: 38 files, 421,672 B raw / **138,907 B gzip** / 122,940 B brotli |
| `npm run i18n:check` | **PASS**: **2,077 messages**, 0 errors, 0 warnings |
| `npm run e2e` | **NOT RUN**: the suite does not exist yet (38.1a, if approved) |
| Backend gates | **NOT RUN**: no backend change in this step |
| Contract check (live) | **PASS**: 66 checks passed in `openapi\contract-probe*.log`; the 2 other first-run results were the probe's own mistakes (wrong field path, wrong origin header), re-checked correctly (§3.2) |
| Chrome, "before" numbers | **PASS**, measured with Playwright driving the installed Google Chrome 157 with a temporary profile, **not the Chrome DevTools MCP**: signing in through the MCP would put the demo password into tool arguments (rule 10). Results in §3.13 and `chrome\*.json` |
| Chrome matrix (widths × themes × languages), contrast, reduced motion | **NOT RUN**: no screen changed in this step. The signed-out check covered en and ar, light theme, 1280 px |
| AI stand-in | Used once: a synthetic hosted-chat conversation (two visitor messages, stand-in replies) for the `conversationChanged` measurement |
| Mailpit | **NOT RUN**: no email in this step |
| Secret hygiene | The demo password was read into process memory only; it is in no file, argument, log or report. `tmp\` was not needed and was removed |

## 6. See it live

Nothing changed for users. To see today's behaviour this plan measured:
- http://localhost:4200/sign-in: the console shows one 401 from `/api/auth/me` (gone after 38.1a).
- Sign in as `p36.owner@omniai.example` (password in `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`), choose Northwind, open Conversations and the long synthetic conversation: the network panel shows two `negotiate` requests and the whole transcript downloaded.

## 7. Backend asks

| # | Ask | Why | Shipped meanwhile |
| --- | --- | --- | --- |
| BA-1 | `labelCode` and `shortCode` on the customer directory reads (`GET …/customers`, `GET …/customers/{id}`) | B1 can drop the client copy of the label rule only in the inbox; the customers screens keep it | Customers screens keep `customerName()`, translated |
| BA-2 | Who handles a conversation: a `takenOverBy { userId }` (or `handledBy`) on list items and the detail | The "new message" sound must know conversations a member took over; taking over does not assign (handoff §6), and only events say who, one transcript read per row | Only `assignedTo` counts (§4.3 #7d) |
| BA-3 | The embedded widget's language: `v1.js` adds the host page's language (it already reads it for the frame title) to the frame URL as `lang=en\|ar` | C8 for the widget; the frame URL is built by the API | The widget follows the visitor's browser, as today |
| BA-4 | Specific problem codes for customers (display name, merge), conversation replies and handoff conflicts, notification preferences, and reports and schedules, which still answer `validation.failed` / `state.conflict` | Without them those refusals get generic words in both languages once `detail` is no longer shown (§4.3 #7c) | Generic words |
| BA-5 | OpenAPI completeness: the problem extension members (`code`, `codes`, `limit`, `entitlement`, `current`, `periodFrom`/`periodTo`), the invitations `status` parameter, and response schemas for the writes and customer routes that declare none | Typed clients and contract tests | Hand-written types, checked live per step |
| BA-6 | The session-check budget: 600 per 10 minutes per network, shared by everyone behind one address | A large office's page loads plus signed-out focus checks can approach it; on 429 the client falls back to `/me`, so nobody is locked out, but the 401 returns for them | Startup falls back to `/me`; focus checks pause until `Retry-After` |
| BA-7 | Optional: a `needsPerson` flag on list items and the detail | Rows could mark all three needs-a-person branches, not only the one inferred today | The inferred "Waiting for the team" badge stays |
| BA-8 | Only if RUM is wanted later: an endpoint to receive web-vitals | Decision #6 | None |

**When they are done.** BA-1 to BA-5 and BA-7 go to a short backend step after 38.6, before Phase 39. BA-6 goes with the tuning before launch, and BA-8 only if RUM is wanted later. The 38.6 summary carries them.

Not a backend ask: `.wav` files are served `no-cache` by the edge. Adding the extension to the long cache is a `deploy/` change and is not needed (versioned names, a 304 on revalidation).

## 8. New Arabic for your review

None in this step: no product string was added or changed. Each later step lists its own. The plan's examples quoted from the list (`«زائر 7KQ2MX»`, `«عميل واتساب ••••1234»`, `«الإيميلات بالعربية»`, `«إعدادات مساحة العمل»`, `«بتوقيت Asia/Riyadh»`) are proposals for those steps, to be read then.

## 9. Risks and open issues

- **The Arabic to review is large:** about 300 sentences over the phase, half of them refusal wording. Split by step so each review is bounded.
- **Pre-37.4 handoffs lose their inferred divider** (B3). The server has no events before 37.4 and none may be invented, so older conversations show no takeover divider where today the client infers one from a change of voice.
- **Day-one unread:** every conversation with a message reads as unread for every member until opened (961 in Northwind). The dots will be everywhere at first; the needs-a-person view is the useful one.
- **The sounds:** autoplay rules (silent until the first click after load), several windows visible at once (the play lock decides), up to 12 more reads a minute per browser and workspace for members who can view conversations, and a hidden tab the browser discards, which can play nothing. A colleague's takeover of an old conversation beyond the first 50 rows is not heard.
- **Session-check budget** (BA-6).
- **Time zones:** the browser's zone list can differ from the API's tzdata (2026c) and PostgreSQL's (2026b). A zone one side lacks is refused with `workspace.time_zone.invalid`, which is worded.
- **The transcript rework** (2a) touches two scroll containers that switch with the width; anchoring must be proven at all four widths in both directions.
- **`@playwright/test` 1.64.1 with Chrome 157:** compatibility is expected; if it fails, upgrading is a new package decision.
- **e2e data growth:** each run adds one synthetic account and workspace, and the throttling journey spends 6 failed sign-ins on this network (limit 50 per 15 minutes).
- **Memory:** 8 GB free in this step with the 36.12 live stack running. Gates and e2e run alone.

## 10. Proposed checkpoint commit message

```
docs: plan Phase 38, the frontend adopting the Phase 37 contracts

- Check every Phase 37 contract three ways (documented, live on the dev
  API, used by the frontend today): all present, none used yet.
- Record the 38.0 baseline: 96 files / 1,169 tests, 2,077 messages,
  startup JavaScript 138,907 B gzip, and the requests of a deep link, the
  analytics page and one conversationChanged.
- Place A1-E11 and the sounds in 38.1-38.6, splitting 38.1 and 38.2 in
  two; design refusals by code, the session check, one realtime
  connection, the sounds, the inbox, the public pages, workspace time
  zones and the end-to-end suite; list the backend asks and the owner's
  decisions.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-11-08)

### R1.1 The notes, and what changed

| Note | What changed | Where |
| --- | --- | --- |
| Your answers to #1 to #7e | Recorded in a new "Owner's answer" column, beside each recommendation | §4.3 |
| Cap the sound and count read | One reader per browser and workspace, by a long-lived Web Lock named with the workspace id; the other tabs fed over a `BroadcastChannel`; at most one read every 5 seconds with one trailing read, so at most 12 a minute. The cost and the request table are updated | §3.7, §3.13, and the sounds line of §9 |
| Hidden tabs | The reader keeps its Web Lock, with the reason. The 10-minute hidden-tab check is added to 38.2b | §3.7, the F row of §3.4, the sounds line of §9 |
| Field errors | A 400 with `errors` marks the fields it names, in our own words, never the server's English | §3.5 |
| Skills | Each step from 38.1 on loads every skill its prompt names, and its report says which | §4.1 |
| Backend asks | One line on when each ask is done | §7 |
| End-to-end data | Every synthetic workspace is named with an "E2E" prefix; each step's report counts the accounts and workspaces it created | §3.12 |
| Digits | The Arabic example now writes 15 in Western digits | §3.5 |

### R1.2 What the notes led to, for your attention

- **The overview row no longer makes its own read.** The plan had one `pageSize=1` read there. Your rule that one tab reads for the count removes it; the row takes the shared result (§3.8).
- **The cap of 12 a minute covers the needs-a-person read only.** The small events read that confirms an AI failure stays beside it: only from the reader tab, normally 0 or 1 per read, at most 3 per read, so 36 a minute at the very worst. If you want it inside the 12, the sound for a failed reply would have to be inferred from the row without confirming the event.
- **There is no `errors` map to keep marking today.** No endpoint writes one, and no screen reads one: I checked the API source for every area, not only those `problem-codes.md` names. The field-error work is therefore proven by unit tests only until an endpoint sends the map. The refusals you were protecting (customers, reports, notification preferences, replies) carry one code and no field names, so after 38.5 they still get generic words until BA-4.
- **The digit rule is in `MASTER.md` §4 and its decisions log**, not in `GLOSSARY.md`, which does not mention digits. The example follows that rule. The glossary was not changed, because it is outside these notes.
- **The hidden-tab check cannot be driven by Playwright.** It launches Chrome with background throttling off and turns on focus emulation, so a pass would prove nothing. §3.7 says how the check runs instead.

### R1.3 Verification

No gate was re-run: this revision changes only the plan report, so no code, test, string or build input changed. The §5 results stand. `git status --short` still shows only this report; nothing is committed.

Read to write these notes accurately, with nothing changed: the API source for `errors`, the frontend for any read of it, `MASTER.md` and `GLOSSARY.md`, the installed Playwright and SignalR clients.

### R1.4 Proposed checkpoint commit message

§10 still fits and is unchanged: the answers and the caps sit inside "the owner's decisions" and "the sounds".
