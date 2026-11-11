# Phase 38.4: Workspace, team, roles, invitations, permissions and billing

Step 38.4 of Phase 38 (plan items D1–D7), on top of `942e269` (38.3). Frontend only.
Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p38-4\` (`<evidence>` below).

## 1. Summary

- **Workspace settings** is a new page. Every member can read the workspace's name, and anyone with `workspace.manage` can change it. Each save is checked against the version that was read, so a rename made in another tab is never silently overwritten. The page also tells people that visitors see this name.
- **Invitations** are now four lists: Waiting, Accepted, Revoked and Expired. The server filters and counts each list. The open list and its page are kept in the address, so they survive a reload.
- **The invite form asks which language the invitation email should be in.** It starts in the language the sender is using. Mailpit confirms that Arabic arrives in Arabic and English in English.
- **Roles** show how many members hold each one. Before saving, the role editor says "Saving changes access for N members". Areas and permissions are named by their stable keys in both languages.
- **Billing** shows the current plan's limits, and the fallback plan's when there is one. An empty limit reads "Unlimited", never 0. Every team, role, invitation, email-address and billing refusal is now worded by its code in English and Arabic.

## 2. Starting point

| | |
| --- | --- |
| Branch | `master` |
| HEAD | `942e269` feat: say how knowledge indexing went, read agent channels from the agent, and give the chat page the workspace's look and the visitor's language |
| Worktree | clean (`git status --short` empty) |
| Matches "Starting point: Phase 38.3 accepted" | yes |

Docker Desktop was down at the start (the machine had restarted). I started it, brought the dev stack back with `p36-stack.sh up`, and ran the canary before any traffic: **CANARY PASS**.

## 3. Changes by area

`git status --short` (all under `frontend/`; nothing in `backend/` or `deploy/`):

```
 M src/app/app.routes.ts                                   route /workspace-settings (lazy)
 M src/app/core/api/tenant-api.ts                          getVersioned(), rename() with If-Match
 M src/app/core/authorization/permission-codes.ts          workspace.manage; groupKey on PermissionDefinition
 M src/app/core/authorization/role-api.ts                  memberCount (list, detail; absent on writes)
 M src/app/core/i18n/arabic-render.spec.ts                 Arabic proofs for this step
 M src/app/core/i18n/plural.ts                             countMessage(): the zero form for none, in English too
 M src/app/core/sounds/sounds.spec.ts                      waits on outcomes, not fixed turns (load race, see §4)
 M src/app/core/workspace/workspace-context-service(.spec).ts  refresh(tenant) after a rename
 M src/app/features/billing/*                              plan limits table (plan names isolated), billing-problems registered
 M src/app/features/conversations/conversation-list/conversation-list.scss  link-segmented rule moved to primitives
 M src/app/features/invitations/accept-invitation/accept-invitation.ts      registers the seat refusal
 M src/app/features/roles/*                                members column, holders fact, save reach, areas by key, role-problems
 M src/app/features/team/*                                 invitation lists in the URL, invite language, team-problems
 M src/app/layout/app-shell/app-shell.spec.ts              explicit timeout for the all-routes test (see §4)
 M src/app/layout/navigation.ts                            "Workspace settings" nav item (workspace.view)
 M src/app/shared/pagination/pagination.ts                 'invitations' pager words
 M src/app/testing/test-doubles.ts                         groupKey and workspace.manage in the fixture catalogue
 M src/locale/messages.json, messages.ar.json              +100 messages with Arabic, 3 retired
 M src/styles/_primitives.scss                             a.segmented-option (links), shared by inbox and team
?? e2e/invitation-language.e2e.ts, e2e/rename-workspace.e2e.ts
?? src/app/core/api/tenant-api.spec.ts
?? src/app/features/billing/billing-problems.ts
?? src/app/features/invitations/seat-problems.ts
?? src/app/features/roles/permission-words.spec.ts, role-problems.ts
?? src/app/features/team/admin-problems.spec.ts, team-problems.ts
?? src/app/features/workspace-settings/                   routes, page (ts, html, scss, spec)
```

43 tracked files changed (+1,816 / −225) before the report, plus the new files above.

### 3.1 What changed and why

- **D1 Workspace settings** (`features/workspace-settings/`). This is a new lazy route `/workspace-settings`, with a nav item "Workspace settings" («إعدادات مساحة العمل») in the Workspace group, visible with `workspace.view` (§4.3 #7e).
  - People with `workspace.manage` get a name field. Everyone else reads the name as text, with "Only people who can manage this workspace can change these." There are no disabled fields.
  - The page reads `GET /api/tenants/{id}` with its `ETag` and saves `PUT …/name` with `If-Match`. It keeps the new `ETag` for the next save.
  - A **412** shows the shared stale-version notice. The typed name stays in the field, and "Reload latest version" reads the current name and tag while keeping what was typed, with "Saved name: …" beside it. A **428** is treated the same way and never shows a raw message.
  - `workspace.name.invalid` uses the shared catalogue's wording.
  - The page applies the unsaved-changes guard and `beforeunload`.
  - After a save, `WorkspaceContextService.refresh()` takes the server's answer, so the top bar follows without another read.
  - The lede says "Visitors see this name on the chat page and the widget."
  - The time zone has its place reserved: 38.5 adds it as a second section.
- **D2 Members per role.**
  - The roles list has a Members column ("9 members", «9 أعضاء»).
  - The editor shows a "Held by" fact. While permissions are unsaved, it says "Saving changes access for N members." beside the save («سيغيّر الحفظ وصول N أعضاء.»), or "No member holds this role yet, so saving changes nobody's access." The sentence goes away again once the changes are undone.
  - The writes carry no count (`tenant-rbac.md`), so the editor keeps the count from its last read.
- **D3 Invitation lists.**
  - Four links (Waiting, Accepted, Revoked, Expired) use the inbox's link-segmented control. Each list is its own address (`?invitations=accepted`) with its own page (`?invitationsPage=2`), separate from the members' `?page=`.
  - The page sends one read per list: `GET …/invitations?status=…&page&pageSize=20`.
  - Only the open list shows its total, as a number on its link and as a sentence ("10 invitations were accepted.").
  - Changing list shows that list's own skeleton. Turning a page keeps the rows on screen, marked busy. A reply that arrives after the person has moved on is dropped.
  - Revoking re-reads the list, and steps back a page if the last row of a later page went.
  - This replaces the old single read of 100 invitations, the folded "Past invitations" block and the "Invitations waiting" fact (§4).
- **D4 Invitation language.**
  - A native select, "Invitation email language", offers English and العربية, each named in its own language and direction.
  - It starts in the language the sender is using, is always sent (§4.3 #4), and is shown on the result as "Email language". The language word is isolated so that the cell keeps the page's alignment.
- **D5 Areas and permissions by key.**
  - Areas are named by `groupKey`, with the English `group` as the fallback. Permissions keep being named by `code`, with the English `label` as the fallback.
  - The picker, the role editor and member detail all group by key. Element ids follow the key (`area-ai-agents`, `area-roles`).
  - `workspace.manage` was added to `PermissionCode`; its words were already in place from 37.5a.
- **D6 Plan limits.**
  - A "Plan limits" card on Billing has one row per limit, in Plan & usage's order. Each row has the limit's name and its description.
  - The current plan's column is headed by its name and "In effect now". When the fallback plan is not null, its column follows, headed by its name and "Fallback plan".
  - `null` reads "Unlimited", and a limit of 0 stays 0. Numbers use the reader's language with Western digits.
- **D7 Refusals.**
  - `team-problems.ts`: 6 invitation codes, 4 team codes, 4 email-address codes and the member seat.
  - `role-problems.ts`: 8 codes.
  - `billing-problems.ts`: 5 codes, each ending "Nothing was charged."
  - Each catalogue is registered by its route. The accept page registers `seat-problems.ts` in its constructor, because its route is eager.
  - `entitlement.members.exhausted` has one sentence that is true on both screens that can meet it (inviting and accepting).

### 3.2 Adopted contracts

| Screen | Endpoint, event or code | Fields read | Tests |
| --- | --- | --- | --- |
| Workspace settings | `GET /api/tenants/{id}` (ETag); `PUT /api/tenants/{id}/name` with `If-Match` → 200 + ETag; 400 `workspace.name.invalid`, 403, 412 `precondition.failed`, 428 `precondition.required` | `name` (`id`, `slug`, `status` kept in memory) | `workspace-settings-page.spec.ts` (15), `tenant-api.spec.ts` (3), `workspace-context-service.spec.ts` (+2), `arabic-render.spec.ts`; e2e `rename-workspace` |
| Roles list | `GET …/roles` | `memberCount` | `role-list.spec.ts` (+1) |
| Role editor | `GET …/roles/{id}`; writes unchanged | `memberCount` (detail only) | `role-editor.spec.ts` (+3) |
| Role editor, create, member detail | `GET …/permissions` | `groupKey`, `code`; `group`, `label` as fallbacks | `permission-words.spec.ts` (new), `arabic-render.spec.ts` |
| Team, invitations | `GET …/invitations?status=Pending\|Accepted\|Revoked\|Expired&page&pageSize` | `items`, `total` (filtered) | `team-page.spec.ts` (rewritten, 16); e2e `invitation-language` |
| Invite form | `POST …/invitations { email, roleIds, language }` | `invitation`, `token` (unchanged) | `invite-member.spec.ts` (+3); e2e `invitation-language`; Mailpit live check |
| Billing | `GET …/billing` | `currentPlan { code, name, limits }`, `fallbackPlan \| null` | `billing-page.spec.ts` (+4) |
| Team, invite, member detail | `invitations.*`, `team.*`, `email.*`, `entitlement.members.exhausted` | `code` | `admin-problems.spec.ts` (new), `arabic-render.spec.ts` |
| Roles screens | `roles.*` | `code` | `admin-problems.spec.ts`, `arabic-render.spec.ts` |
| Billing | `billing.*` | `code` | `admin-problems.spec.ts`, `billing-page.spec.ts` |
| Accept invitation | `entitlement.members.exhausted` | `code` | `admin-problems.spec.ts` |

The live OpenAPI document (`<evidence>\openapi\openapi-v1.json`) is byte-identical to 38.0's (`d7ac8432…`). Live reads matched the documentation:
- 11 `groupKey` values, and `workspace.manage` in the catalogue;
- `memberCount` on the list and the detail;
- filtered `total` per invitation status;
- `currentPlan` with every limit `null`, and `fallbackPlan` `null`, on the development stack.

### 3.3 Before and after screenshots

All under `<evidence>\chrome\shots\`. "Before" is the committed `942e269` served on port 4201 from an export outside the repository, with the same signed-in owner and data.

| Screen | Before | After |
| --- | --- | --- |
| Workspace settings | `before\workspace-settings-{en,ar}-1280-light.png` (Page not found) | `after\workspace-settings-{en,ar}-1280-light.png` |
| Team | `before\team-{en,ar}-1280-light.png`, `before\team-accepted-*` | `after\team-{en,ar}-1280-light(-lower).png`, `after\team-{pending,accepted,revoked,expired}-en.png`, `after\team-pending-page2-en.png` |
| Invite form | `before\invite-{en,ar}-1280-light.png` | `after\invite-{en,ar}-1280-light.png`, `after\invite-language-en.png`, `after\invite-created-{en-ar,ar-en,en-default}.png` (link blanked) |
| Roles | `before\roles-{en,ar}-1280-light.png` | `after\roles-{en,ar}-1280-light.png` |
| Role editor | `before\role-editor-{en,ar}-1280-light.png` | `after\role-editor-{en,ar}-1280-light.png`, `after\role-editor-reach-{en,ar}.png` |
| Billing | `before\billing-{en,ar}-1280-light.png` | `after\billing-{en,ar}-1280-light(-lower).png` |
| Stale rename | none (no page before) | `after\stale-first-tab-en.png`, `after\stale-second-tab-en.png`, `after\stale-reloaded-en.png`, `after\hosted-renamed-390.png` |
| Analyst (no permissions) | none | `after\analyst-settings-{en,ar}.png` |
| Matrix | none | `after\matrix-<route>-<lang>-<theme>-{390,1920}.png` (56 files) |

The "before" team captures cover the page top only. The app scrolls inside its main pane, and I added section captures after the before server had been stopped. The removed "Past invitations" block is therefore not in a before capture.

## 4. Decisions

- **The plan's design was followed throughout** (§3.10, §3.11, §4.3 #4 and #7e). Where this prompt and the plan differ:
  - *Tab names.* The prompt says Pending; the lists use the owner-reviewed status words: Waiting («بانتظار القبول»), Accepted, Revoked, Expired. The API value stays `Pending`.
  - *"Reload the latest" keeps the typed value* (plan §3.10). Roles and agents discard edits on reload; this page keeps them and shows the saved name beside them. That is at least as safe: nothing is sent until the person saves again with the fresh tag.
- **Links, not a tablist,** for the invitation lists, as the inbox does: each list is an address with Back. The `a.segmented-option` rule moved from the inbox's stylesheet into `_primitives.scss` so both use one rule. The inbox looks the same, as the matrix and its e2e journeys show.
- **Removed from the team page:**
  - the 100-row read;
  - the folded "Past invitations";
  - "Only the newest N of M are shown";
  - the "Invitations waiting" fact.

  With one read per list, the page no longer knows the waiting count while another list is open (plan: "only the open tab shows its total"). Three Arabic messages were retired with them.
- **The Save button stays focusable.** It uses `aria-disabled`, not `disabled`, while there is nothing to save or a save is on its way, so focus never drops to the page after a save (checked live: focus stays on "Save name").
- **`countMessage()` in `core/i18n/plural.ts`.** English has no `zero` plural category, so "0 members" was what English showed for none. The new counts and list totals use the zero sentence for 0 in both languages. Existing callers are unchanged.
- **One seat sentence for two screens.** `entitlement.members.exhausted` lives in `features/invitations/seat-problems.ts`. The team catalogue spreads it in, and the accept page registers only that file, so it does not carry the team's words.
- **Field test id.** The top bar already used `data-testid="workspace-name"` (single-workspace accounts), so the settings field is `workspace-name-input`.
- **Two load-sensitive specs were fixed, not retried.** In the final `test:ci` runs:
  - `app-shell.spec.ts` › "gives every navigation entry a route that resolves" timed out once at the 5 s default. It loads all twelve lazy areas (one more now) and takes about 1.2 s alone. It now has an explicit 20 s limit, with the reason in a comment.
  - `sounds.spec.ts` › "prepares the player…" lost a race once against its dynamic `import('./sound-player')`. Its four positive cases now wait on the outcome (`vi.waitFor`) instead of a fixed four turns.

  After both fixes, two consecutive full runs passed.
- **Deferred:** the time zone (38.5, its place reserved).

## 5. Verification

| Check | Result |
| --- | --- |
| Worktree at start | **PASS**: `master`, `942e269`, clean |
| Provider canary before any traffic, and again before e2e | **PASS** both times (`gates\canary-e2e.txt`): every listed secret absent, the SMTP host is loopback, `OMNIAI_DEV_AI_STANDIN` info |
| `npm run format:check` | **PASS** (final, `gates\final-format-check.log`) |
| `npm run test:ci` | **PASS**: 119 files, 1,690 tests, 0 failures on the final code (`gates\final-test-ci.log`, free memory before: 8.80 GB). 38.3 had 115 / 1,612. Two earlier final runs each failed one load-sensitive test; both were fixed (§4), and the run before the last template-only fix also passed twice in a row (`final-test-ci-2.log`) |
| `npm run build` | **PASS**: 0 warnings, no budget messages, initial total 314.24 kB raw / 73.74 kB transfer (`gates\final-build.log`, free memory before: 8.62 GB) |
| Startup JavaScript (loader plus application chunk, gzip -9) | **144,853 B** (`gates\final-startup-js.txt`). 38.3 checkpoint: 144,584 (+269). 38.0 baseline: 138,907 (+5,946, **+4.28 %**, under the 10 % ceiling of 152,797). A few bytes move between builds with the hashed chunk names inside imports |
| `npm run i18n:check` | **PASS**: 2,335 messages, 0 errors, 0 warnings (38.3: 2,238; +100 new, −3 retired) |
| `npm run e2e` | **PASS**: **38 journeys, 38 passed, 0 failed, 2.1 min** on the final code (`gates\final-e2e.log`, results in `<evidence>\e2e\`, free memory before: 9.10 GB, canary just before). The first full run, 15 minutes earlier, also passed 38/38 in 2.4 min (`gates\e2e.log`). No retries. The 2 new journeys pass in en-ltr and ar-rtl |
| Backend gates | **NOT RUN**: no file in `backend/` changed |
| Chrome matrix | **PASS** (run again on the final code): 7 routes × 4 widths (390, 768, 1280, 1920) × light/dark × en/ar = **112 pages, 0 problems**: no page overflow, exactly one `h1` each, no console errors, and no axe findings for `button-name`, `link-name`, `label`, `select-name`, `aria-input-field-name`, `color-contrast`, `duplicate-id-aria`, `aria-allowed-attr`, `nested-interactive` and `target-size` (`chrome\matrix.json`). Routes: `/workspace-settings`, `/team`, `/team?invitations=revoked`, `/team/invite`, `/roles`, `/roles/{id}`, `/billing` |
| Keyboard | **PASS**: in the name field, End, typing, Tab and Enter reach "Save name" and save, and focus stays on the button (now `aria-disabled`). On Team, Tab moves from Waiting to Accepted, Enter follows it, focus stays on the link and the address gains `?invitations=accepted`. Focus ring is 2 px solid |
| Contrast | **PASS**: axe `color-contrast`, 0 findings across all 112 pages, both themes |
| Reduced motion | **PASS**: with `prefers-reduced-motion: reduce`, the list links' transitions are 0.00001 s; no animation runs on these screens |
| RTL mirroring | **PASS**: Arabic pages are `dir="rtl"` and mirrored (team, roles and invite captures). Addresses stay LTR (`dir="ltr"`), names are `dir="auto"`, and the email-language word is isolated in a `<bdi>` |
| Stale rename, two tabs (Northwind) | **PASS**: tab A `PUT …/name` returned 200 with `If-Match`; the toast named the new name and the top bar followed. Tab B returned 412 `precondition.failed`, showed the stale notice and kept "(second tab)". After reload, "Saved name: Northwind Operations (p38.4 check)" appeared while the typing stayed. The hosted chat page at 390 px showed the new title. The name was put back (200) |
| Role held by several members | **PASS**: "Analyst (read-only insights)" shows "9 members" in the list and "Held by 9 members" in the editor. Ticking a box shows "Saving changes access for 9 members." («سيغيّر الحفظ وصول 9 أعضاء.»), and unticking removes it |
| Invitation lists | **PASS**: each list sends one read (`status=…&page=1&pageSize=20`). Totals at the time: Waiting 21 (after the synthetic invitations), Accepted 10, Revoked 2, Expired 2. `?invitationsPage=2` survived a reload with "21–21 of 21 invitations". The revoked invitation left Waiting and appeared under Revoked |
| Member without the permissions (analyst, en and ar) | **PASS**: the nav shows only Overview, Analytics, Reports, Workspace settings and Plan & usage. Workspace settings shows the name as text with the read-only sentence: no field, no save. Team, Roles, Billing and Invite each show their own explanation, with 0 actions and 0 area requests sent |
| Mailpit | **PASS**: English page, Arabic chosen: `language: "ar"` sent, subject «نبرة: تلقّيت دعوة إلى مساحة عمل», HTML `lang="ar" dir="rtl"`. Arabic page, English chosen: `language: "en"`, subject "Nabra: You have been invited to a workspace", `lang="en" dir="ltr"`. English page, default: `en`. Only subjects and two booleans were read; no link left Mailpit. Each invitation was revoked afterwards |
| AI stand-in | not needed by this step; it stayed on (canary info line), and the e2e AI journeys passed through it |
| Secret scan | **PASS**: the demo password is in no file of `<evidence>` (cookie jars deleted) nor in this report (§5.1). Invitation links: no `#token`/`token=` in any kept log or capture folder; created-invitation captures show "[link hidden]" |

### 5.1 Closing checks

| Check | Result |
| --- | --- |
| Late fixes, found by eye in the captures | The email-language word right-aligned in an LTR page, and the plan name in the Arabic limits header aligned to the wrong edge. Both are now isolated in a `<bdi>` (`after\invite-created-en-ar.png`, `after\billing-ar-1280-light-lower.png`). Every gate, the matrix and e2e above were rerun after them |
| `<evidence>	mp` (cookie jars, e2e session state, the before-code export) | **Deleted**. The `node_modules` junction in the export was removed with `cmd /c rmdir` first, and the real `node_modules` was checked intact |
| Secret scan after the report was written (`scratch\secret-scan.mjs`) | **PASS**: 0 files with the demo password, 0 with an invitation link |
| Dev stack left running | API `omniai-p36-api` (canary PASS) and the dev server on `http://localhost:4200` (restarted after `tmp` was deleted; log at `<evidence>\dev-server.log`) |
| Docker | Only `p36-stack.sh up` was used. No container, volume, network, image or database was deleted; `omniai_p36_live` was not touched |
| `backend/`, `deploy/` | unchanged |

## 6. See it live

The API is at `http://127.0.0.1:5027` (with the AI stand-in), the dev server at `http://localhost:4200`, and Mailpit at `http://127.0.0.1:8026`. The demo accounts are named in `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`.

As **`p36.owner@omniai.example`** (Northwind Operations):

1. Open `http://localhost:4200/workspace-settings`. Open it in a second tab as well, rename in the first tab, then save in the second to see the stale-version notice. Use "Reload latest version", then put the name back. Open the website chat's hosted page to see the name visitors read.
2. Open `http://localhost:4200/team`. The Waiting list holds 21 synthetic `p38.p4-inv-NN@omniai.example` invitations, so `?invitationsPage=2` exists. Try Accepted, Revoked and Expired, reload on page 2, and revoke one to see it move to Revoked.
3. Open `http://localhost:4200/team/invite`. Choose العربية and invite a made-up `@omniai.example` address, then read the email in Mailpit and revoke the invitation.
4. Open `http://localhost:4200/roles`, then "Analyst (read-only insights)". It shows 9 members; tick a permission to see "Saving changes access for 9 members." Leave without saving: the guard asks first.
5. Open `http://localhost:4200/billing` for the plan limits. Every limit reads "Unlimited" on this stack, and there is no fallback column because the API returns `fallbackPlan: null` here.
6. Switch to Arabic from the account menu and repeat any of the above.

As **`p36.analyst@omniai.example`**: Workspace settings is read-only; Team, Roles and Billing are not in the navigation and explain themselves if opened by address.

## 7. Backend asks

| # | Ask | Why | Shipped instead |
| --- | --- | --- | --- |
| BA-4.1 | Declare `201 Created` (with the `CreateInvitationResponse` shape) for `POST /api/tenants/{id}/invitations` in the OpenAPI document | The endpoint answers 201 (`TeamEndpoints.cs`, `Results.Created`), but the document lists only "200 OK" with no body. Not a behaviour change, and nothing here depends on it: the client treats any 2xx alike. The e2e journey asserts the real 201 | Nothing changed client-side |
| BA-4.2 | Declare the role write responses (`POST …/roles`, `PUT …/roles/{id}/name`, `PUT …/roles/{id}/permissions`) in the OpenAPI document, without `memberCount` | They are listed as "200" with no schema, while `tenant-rbac.md` says they carry the role and no count. The editor relies on the written contract | The editor keeps the count from its last read |
| BA-4.3 | A fallback plan on the development stack, or a seeded plan with finite limits | Locally every limit is `null` and `fallbackPlan` is `null`, so the fallback column and finite limits are proved in specs only | Specs cover both, with "Unlimited" for null and 0 kept as a real limit |

No contract was missing for D1–D7.

## 8. New Arabic for my review

100 new messages; 3 retired (`team.facts.waiting`, `team.past.summary`, `team.invitations.truncated`). Ids marked `.zero` show for a count of none. Reused existing ids (`@@plan.unlimited`, `@@problem.workspace.name.invalid`, `@@problem.auth.email.invalid`, `@@team.invite.email.required`, `@@team.waiting.zero`) are not repeated here.

| Id | English | Arabic | Where |
| --- | --- | --- | --- |
| `billing.limits.heading` | Plan limits | حدود الباقة | Billing, plan limits |
| `billing.limits.lede.fallback` | What this workspace may hold on the plan in effect now, and on the fallback plan it moves to if its subscription lapses. | ما يمكن لمساحة العمل هذه أن تضمّه في الباقة السارية الآن، وفي الباقة البديلة التي تنتقل إليها إذا انقطع اشتراكها. | Billing, plan limits |
| `billing.limits.lede` | What this workspace may hold on the plan in effect now. | ما يمكن لمساحة العمل هذه أن تضمّه في الباقة السارية الآن. | Billing, plan limits |
| `billing.limits.region` | Plan limits table | جدول حدود الباقة | Billing, plan limits |
| `billing.limits.caption` | Plan limits, by plan | حدود الباقة، لكل باقة | Billing, plan limits |
| `billing.limits.column.limit` | Limit | الحد | Billing, plan limits |
| `billing.limits.fallback` | Fallback plan | الباقة البديلة | Billing, plan limits |
| `problem.billing.offer_id.invalid` | No plan was chosen. Choose one and try again. Nothing was charged. | لم تُختر أي باقة. اختر باقة وحاول مرة أخرى. لم يُخصم أي مبلغ. | Billing refusals |
| `problem.billing.checkout.unknownoffer` | That plan is no longer on sale. Reload the page to see what is. Nothing was charged. | لم تعد هذه الباقة معروضة للبيع. أعد تحميل الصفحة لترى المعروض الآن. لم يُخصم أي مبلغ. | Billing refusals |
| `problem.billing.checkout.offernotavailable` | That plan has been withdrawn from sale. Nothing was charged. | سُحبت هذه الباقة من البيع. لم يُخصم أي مبلغ. | Billing refusals |
| `problem.billing.checkout.alreadysubscribed` | This workspace already has a subscription. Change it from Manage payment and invoices. Nothing was charged. | لدى مساحة العمل هذه اشتراك بالفعل. غيّره من «إدارة الدفع والفواتير». لم يُخصم أي مبلغ. | Billing refusals |
| `problem.billing.checkout.offernotpurchasable` | That plan cannot be bought on this installation. Nothing was charged. | لا يمكن شراء هذه الباقة على هذا التثبيت. لم يُخصم أي مبلغ. | Billing refusals |
| `problem.entitlement.members.exhausted` | This workspace has reached the number of members its plan allows, so nobody else can join it now. Nothing was changed. | بلغت مساحة العمل هذه الحد الأقصى للأعضاء في باقتها، فلا يمكن لأحد آخر الانضمام إليها الآن. لم يتغيّر شيء. | Invite form and invitation acceptance |
| `roles.members.zero` | No members | لا أعضاء | Roles list, Members column; role editor facts |
| `roles.members.one` | {$count} member | عضو واحد | Roles list, Members column; role editor facts |
| `roles.members.two` | {$count} members | عضوان | Roles list, Members column; role editor facts |
| `roles.members.few` | {$count} members | {$count} أعضاء | Roles list, Members column; role editor facts |
| `roles.members.many` | {$count} members | {$count} عضوًا | Roles list, Members column; role editor facts |
| `roles.members.other` | {$count} members | {$count} عضو | Roles list, Members column; role editor facts |
| `roles.saveReach.zero` | No member holds this role yet, so saving changes nobody's access. | لا يحمل أي عضو هذا الدور بعد، لذا لن يغيّر الحفظ وصول أحد. | Role editor, beside Save permissions |
| `roles.saveReach.one` | Saving changes access for {$count} member. | سيغيّر الحفظ وصول عضو واحد. | Role editor, beside Save permissions |
| `roles.saveReach.two` | Saving changes access for {$count} members. | سيغيّر الحفظ وصول عضوَين. | Role editor, beside Save permissions |
| `roles.saveReach.few` | Saving changes access for {$count} members. | سيغيّر الحفظ وصول {$count} أعضاء. | Role editor, beside Save permissions |
| `roles.saveReach.many` | Saving changes access for {$count} members. | سيغيّر الحفظ وصول {$count} عضوًا. | Role editor, beside Save permissions |
| `roles.saveReach.other` | Saving changes access for {$count} members. | سيغيّر الحفظ وصول {$count} عضو. | Role editor, beside Save permissions |
| `roles.facts.holders` | Held by | يحمله | Role editor facts |
| `roles.column.members` | Members | الأعضاء | Roles list header |
| `problem.roles.name.required` | Enter a name for the role. | اكتب اسمًا للدور. | Role refusals |
| `problem.roles.name.too_long` | This role name is too long. Shorten it and save again. | اسم الدور هذا طويل جدًا. اختصره ثم احفظ مرة أخرى. | Role refusals |
| `problem.roles.name.control_characters` | This role name has characters that cannot be saved. Type it on one line as plain text. | في اسم الدور هذا أحرف لا يمكن حفظها. اكتبه في سطر واحد نصًا عاديًا. | Role refusals |
| `problem.roles.name.taken` | Another role in this workspace already has this name. Choose a different one. | يحمل دور آخر في مساحة العمل هذه الاسم نفسه. اختر اسمًا مختلفًا. | Role refusals |
| `problem.roles.permission.invalid` | One of the permissions could not be read. Reload the page and choose again. | تعذّرت قراءة إحدى الصلاحيات. أعد تحميل الصفحة واختر مرة أخرى. | Role refusals |
| `problem.roles.permission.unknown` | One of the permissions is not available on this installation. Reload the page and choose again. | إحدى الصلاحيات غير متاحة على هذا التثبيت. أعد تحميل الصفحة واختر مرة أخرى. | Role refusals |
| `problem.roles.system.protected` | The Owner role is protected: it cannot be renamed and always has every permission. | دور المالك محمي: لا يمكن تغيير اسمه، ويملك دائمًا كل الصلاحيات. | Role refusals |
| `problem.roles.id.invalid` | This address does not lead to a role. Go back to the roles and choose one. | لا يقود هذا العنوان إلى أي دور. ارجع إلى الأدوار واختر دورًا. | Role refusals |
| `team.invite.created.language` | Email language | لغة البريد | Invite form |
| `team.invite.language` | Invitation email language | لغة بريد الدعوة | Invite form |
| `team.invite.language.hint` | When Nabra emails this invitation, it writes the email in this language. It starts as the language you are using now. | حين ترسل نبرة هذه الدعوة بالبريد الإلكتروني، تكتب الرسالة بهذه اللغة. تبدأ باللغة التي تستخدمها الآن. | Invite form |
| `team.invitations.views` | Invitation lists | قوائم الدعوات | Team page, invitations |
| `team.invitations.loading` | Loading invitations… | جارٍ تحميل الدعوات… | Team page, invitations |
| `team.column.sent` | Sent | تاريخ الإرسال | Team page, history lists |
| `team.invitation.noRolesPast` | No roles | بلا أدوار | Team page, history lists |
| `problem.invitations.status.invalid` | That list of invitations does not exist. Choose another one. | قائمة الدعوات هذه غير موجودة. اختر قائمة أخرى. | Invitation refusals |
| `problem.invitations.role.unassignable` | One of the chosen roles is no longer in this workspace. Reload the page and choose again. | أحد الأدوار المختارة لم يعد في مساحة العمل هذه. أعد تحميل الصفحة واختر مرة أخرى. | Invitation refusals |
| `problem.invitations.email.already_member` | This address already belongs to a member. Change their roles from the team page instead. | هذا العنوان لعضو موجود بالفعل. غيّر أدواره من صفحة الفريق بدلًا من ذلك. | Invitation refusals |
| `problem.invitations.email.already_invited` | An invitation for this address is still waiting. Revoke it first to send a new one. | ما زالت دعوة لهذا العنوان بانتظار القبول. ألغِها أولًا لترسل دعوة جديدة. | Invitation refusals |
| `problem.invitations.revoke.closed` | This invitation was already accepted, revoked or expired, so there is nothing to revoke. | قُبلت هذه الدعوة أو أُلغيت أو انتهت صلاحيتها بالفعل، فلا شيء لإلغائه. | Invitation refusals |
| `problem.invitations.id.invalid` | This invitation could not be found. Reload the page. | تعذّر العثور على هذه الدعوة. أعد تحميل الصفحة. | Invitation refusals |
| `problem.team.member.self` | You cannot change your own access here. Ask another administrator to do it. | لا يمكنك تغيير وصولك أنت من هنا. اطلب ذلك من مسؤول آخر. | Member access refusals |
| `problem.team.member.owner_protected` | The owner's access is not changed here, and the Owner role is never given from the team page. | لا يُغيَّر وصول المالك من هنا، ولا يُمنح دور المالك من صفحة الفريق أبدًا. | Member access refusals |
| `problem.team.member.protected` | This member's access is protected and cannot be changed here. | وصول هذا العضو محمي ولا يمكن تغييره من هنا. | Member access refusals |
| `problem.team.role.unassignable` | One of the chosen roles is no longer in this workspace. Reload the page and choose again. | أحد الأدوار المختارة لم يعد في مساحة العمل هذه. أعد تحميل الصفحة واختر مرة أخرى. | Member access refusals |
| `problem.email.too_long` | This email address is too long. Check it and try again. | عنوان البريد الإلكتروني هذا طويل جدًا. تحقّق منه ثم حاول مجددًا. | Email-address refusals |
| `problem.email.invalid_characters` | Remove the spaces or hidden characters from the email address. | احذف المسافات أو الأحرف المخفية من عنوان البريد الإلكتروني. | Email-address refusals |
| `team.accepted.zero` | No invitation has been accepted yet. | لم تُقبل أي دعوة بعد. | Team page, Accepted list total |
| `team.accepted.one` | {$count} invitation was accepted. | قُبلت دعوة واحدة. | Team page, Accepted list total |
| `team.accepted.two` | {$count} invitations were accepted. | قُبلت دعوتان. | Team page, Accepted list total |
| `team.accepted.few` | {$count} invitations were accepted. | قُبلت {$count} دعوات. | Team page, Accepted list total |
| `team.accepted.many` | {$count} invitations were accepted. | قُبلت {$count} دعوة. | Team page, Accepted list total |
| `team.accepted.other` | {$count} invitations were accepted. | قُبلت {$count} دعوة. | Team page, Accepted list total |
| `team.revoked.zero` | No invitation has been revoked. | لم تُلغَ أي دعوة. | Team page, Revoked list total |
| `team.revoked.one` | {$count} invitation was revoked. | أُلغيت دعوة واحدة. | Team page, Revoked list total |
| `team.revoked.two` | {$count} invitations were revoked. | أُلغيت دعوتان. | Team page, Revoked list total |
| `team.revoked.few` | {$count} invitations were revoked. | أُلغيت {$count} دعوات. | Team page, Revoked list total |
| `team.revoked.many` | {$count} invitations were revoked. | أُلغيت {$count} دعوة. | Team page, Revoked list total |
| `team.revoked.other` | {$count} invitations were revoked. | أُلغيت {$count} دعوة. | Team page, Revoked list total |
| `team.expired.zero` | No invitation has expired. | لم تنتهِ صلاحية أي دعوة. | Team page, Expired list total |
| `team.expired.one` | {$count} invitation expired before it was used. | انتهت صلاحية دعوة واحدة قبل استخدامها. | Team page, Expired list total |
| `team.expired.two` | {$count} invitations expired before they were used. | انتهت صلاحية دعوتين قبل استخدامهما. | Team page, Expired list total |
| `team.expired.few` | {$count} invitations expired before they were used. | انتهت صلاحية {$count} دعوات قبل استخدامها. | Team page, Expired list total |
| `team.expired.many` | {$count} invitations expired before they were used. | انتهت صلاحية {$count} دعوة قبل استخدامها. | Team page, Expired list total |
| `team.expired.other` | {$count} invitations expired before they were used. | انتهت صلاحية {$count} دعوة قبل استخدامها. | Team page, Expired list total |
| `workspaceSettings.heading` | Workspace settings | إعدادات مساحة العمل | Workspace settings page |
| `workspaceSettings.description` | This workspace's own details. Everyone in it can see them. | تفاصيل مساحة العمل هذه. يستطيع كل من فيها الاطلاع عليها. | Workspace settings page |
| `workspaceSettings.noAccess.heading` | You do not have access to the workspace settings | ليس لديك وصول إلى إعدادات مساحة العمل | Workspace settings page |
| `workspaceSettings.noAccess.body` | Seeing this workspace's settings needs a permission your roles do not include. Ask somebody who administers this workspace if you need it. | الاطلاع على إعدادات مساحة العمل هذه يحتاج إلى صلاحية لا تتضمنها أدوارك. اطلبها من أحد مسؤولي مساحة العمل إن كنت تحتاج إليها. | Workspace settings page |
| `workspaceSettings.loading` | Loading the workspace settings… | جارٍ تحميل إعدادات مساحة العمل… | Workspace settings page |
| `workspaceSettings.loadFailed` | Could not load the workspace settings | تعذّر تحميل إعدادات مساحة العمل | Workspace settings page |
| `workspaceSettings.tryAgain` | Try again | إعادة المحاولة | Workspace settings page |
| `workspaceSettings.stale.title` | This workspace was renamed somewhere else. | غُيّر اسم مساحة العمل هذه في مكان آخر. | Workspace settings page |
| `workspaceSettings.stale.body` | Your name has not been saved and is still in the field. Reload the latest version to see the saved name; what you typed stays, so you can save it again or change it. | لم يُحفظ الاسم الذي كتبته، وما زال في الحقل. حمّل أحدث نسخة لترى الاسم المحفوظ؛ يبقى ما كتبته، فتستطيع حفظه مرة أخرى أو تغييره. | Workspace settings page |
| `workspaceSettings.stale.reload` | Reload latest version | تحميل أحدث نسخة | Workspace settings page |
| `workspaceSettings.name.heading` | Name | الاسم | Workspace settings page |
| `workspaceSettings.name.lede` | Visitors see this name on the chat page and the widget. | يرى الزوار هذا الاسم في صفحة الدردشة وفي نافذة الدردشة. | Workspace settings page |
| `workspaceSettings.name.label` | Workspace name | اسم مساحة العمل | Workspace settings page |
| `workspaceSettings.name.saved` | Saved name: | الاسم المحفوظ: | Workspace settings page |
| `workspaceSettings.unsaved` | Unsaved changes | تغييرات غير محفوظة | Workspace settings page |
| `workspaceSettings.saving` | Saving… | جارٍ الحفظ… | Workspace settings page |
| `workspaceSettings.saveName` | Save name | حفظ الاسم | Workspace settings page |
| `workspaceSettings.readOnly` | Only people who can manage this workspace can change these. | لا يغيّر هذه الإعدادات إلا من يستطيع إدارة مساحة العمل. | Workspace settings page |
| `workspaceSettings.renamed` | The workspace is now called {$name}. | أصبح اسم مساحة العمل «{$name}». | Workspace settings page |
| `title.workspaceSettings` | Workspace settings | إعدادات مساحة العمل | Tab title |
| `nav.workspaceSettings` | Workspace settings | إعدادات مساحة العمل | Sidebar and command palette |
| `pager.invitations.zero` | of {$count} invitations | من {$count} دعوة | Invitation pager |
| `pager.invitations.one` | of {$count} invitation | من دعوة واحدة | Invitation pager |
| `pager.invitations.two` | of {$count} invitations | من دعوتين | Invitation pager |
| `pager.invitations.few` | of {$count} invitations | من {$count} دعوات | Invitation pager |
| `pager.invitations.many` | of {$count} invitations | من {$count} دعوة | Invitation pager |
| `pager.invitations.other` | of {$count} invitations | من {$count} دعوة | Invitation pager |
| `pager.invitations.label` | Invitation pages | صفحات الدعوات | Invitation pager |

## 9. Risks and open issues

- **Synthetic data left in Northwind** (through the real API):
  - 21 waiting invitations `p38.p4-inv-NN@omniai.example` (no roles, expiring 2026-11-18), kept so page 2 can be seen live;
  - 2 revoked (`p38.p4-inv-00`/`-01`), and 6 revoked `p38.p4-mail-*` invitations from the Mailpit checks;
  - their emails, which are in Mailpit.

  The workspace name was put back to "Northwind Operations". Each e2e run adds its own synthetic account and workspace, as before.
- **Fallback plan and finite limits are proved in specs only** (BA-4.3).
- **Load sensitivity in the unit suite.** Two timing waits were tightened (§4). Others with fixed turns may show up under a heavier machine; this machine also runs other projects' containers.
- **Startup JavaScript is 144,853 B gzip**, 147 B under the 145,000 target noted after 38.3 (4.28 % over 38.0, ceiling 10 %). The new nav item and `countMessage` are the only startup additions.
- **The workspace list in memory follows a rename in this browser only.** Another member's open tab shows the old name in its top bar until it reads the list again, which is today's behaviour for every workspace field.

## 10. Proposed checkpoint commit message

```
feat: rename the workspace safely, page invitations by status, choose the invitation language, and count role holders

- Workspace settings (D1): a new page every member reads and workspace.manage
  edits; the name is saved with If-Match and the returned ETag, a stale save
  keeps what was typed and offers the latest version beside it, and the top
  bar follows the rename. Visitors' chat page shows the new name at once.
- Invitations (D3, D4): Waiting, Accepted, Revoked and Expired lists, filtered
  and counted by the server, with the list and its page in the address; the
  invite form chooses the email's language, starting from the sender's.
- Roles (D2, D5): members per role in the list and the editor, "Saving changes
  access for N members" before a save, areas named by groupKey.
- Billing (D6): the plan's limits beside the fallback plan's, null as
  "Unlimited".
- Refusals (D7): every team, role, invitation, email-address and billing code
  worded in English and Arabic from the shared registry.
- Two e2e journeys (rename with a stale tab; an invitation read in Mailpit),
  100 new Arabic messages, and two load-sensitive specs made deterministic.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1

Arabic only, from the owner's reading: «على هذا التثبيت» means nothing to a customer, so the two
sentences that used it no longer name the installation. Nothing else changed: no English, no code,
no layout. No test asserted the old Arabic.

| Id | English | Arabic (was) | Arabic (now) | Where |
| --- | --- | --- | --- | --- |
| `problem.billing.checkout.offernotpurchasable` | That plan cannot be bought on this installation. Nothing was charged. | لا يمكن شراء هذه الباقة على هذا التثبيت. لم يُخصم أي مبلغ. | لا يمكن شراء هذه الباقة حاليًا. لم يُخصم أي مبلغ. | Billing refusals |
| `problem.roles.permission.unknown` | One of the permissions is not available on this installation. Reload the page and choose again. | إحدى الصلاحيات غير متاحة على هذا التثبيت. أعد تحميل الصفحة واختر مرة أخرى. | إحدى الصلاحيات لم تعد متاحة. أعد تحميل الصفحة واختر مرة أخرى. | Role refusals |
