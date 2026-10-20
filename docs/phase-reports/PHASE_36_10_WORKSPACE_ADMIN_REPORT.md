# Phase 36.10: Team, roles, plan & usage, billing and notifications

Date: 2026-10-20. Branch `master`. Not committed (see section 9).
Evidence: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-10\` (written `<evidence>` below).

## 1. Summary

- Administrators now see the consequence of access before they grant it. Roles list their 29 permissions in eleven areas, each with "n of m granted", and the ones that matter carry a hint ("Reads what customers wrote", "Spends the workspace's money", "Cannot be undone"). Saving a member's roles says what changes, and asks before every role is taken away.
- Team shows every member, a page at a time. Waiting invitations show when their link expires and ask before they are revoked. Accepted, revoked and expired ones are kept in a folded history, each with its state in words.
- Plan & usage gives every limit a meter in the server's four states, each a word with a marker of its own ("Approaching the limit", "Limit reached"…), with tabular figures; an unlimited limit says so in words, with no meter.
- Billing says which plan is in effect: during a failed payment, how long the grace period keeps the plan; after it, the fallback plan beside the one subscribed to.
- Notifications are told apart by an "Unread" word, not by weight alone, and are grouped by day. All of these screens are now in natural Arabic (480 new messages).

## 2. Starting point

- Branch `master`, HEAD `783ad75` ("feat: give customers one chat, and channels their real state"), the accepted Phase 36.9 checkpoint. The worktree was clean.
- The stack was up at first (API `/health/ready` 200, dev server 200), and the provider canary passed before any traffic (`<evidence>\canary-start.log`).
- Midway, the connection dropped twice. After the second drop Docker Desktop and the dev server were down (the machine had restarted). Nothing had been edited at that point, and no background run had been started apart from the baseline build, which had finished. I started Docker Desktop, ran `p36-stack.sh up`, ran the canary again before any traffic (PASS, `<evidence>\canary-restart.log`) and ran `npm start` again.
- Baseline production build: no warnings. Startup JavaScript 411,464 B raw, 135,808 B gzip, 120,242 B brotli (`<evidence>\startup-js-baseline.json`).
- What the real API holds (read-only inventory, `<evidence>\api-inventory.json`):
  - The plan is "Development (unlimited)", so every limit is unlimited.
  - Billing is not configured (`isManaged: false`).
  - The 10 existing invitations were all Accepted.
  - The owner had 9 notifications, all unread.
  - The catalogue has 29 permissions in 11 groups, all in English.
  - The support role holds `members.view`; the analyst holds no team permission.

## 3. Changes by area

`git status --short` at the end:

```
 M docs/design/MASTER.md
 M frontend/src/app/core/notifications/notification-item.ts
 M frontend/src/app/features/billing/billing-page/billing-page.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/design-lab/design-lab.html|.ts
 M frontend/src/app/features/notifications/notification-preferences/*.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/notifications/notifications-nav.ts
 M frontend/src/app/features/notifications/notifications-page/*.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/notifications/notifications.routes.ts
 M frontend/src/app/features/plan-usage/plan-usage-api.ts
 M frontend/src/app/features/plan-usage/plan-usage-page/*.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/roles/permission-groups.ts
 M frontend/src/app/features/roles/role-create/*.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/roles/role-editor/*.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/roles/role-list/*.html|.scss|.ts
 M frontend/src/app/features/team/invite-member/*.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/team/member-detail/*.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/team/team-page/*.html|.scss|.spec.ts|.ts
 M frontend/src/app/layout/notification-bell/notification-bell.spec.ts
 M frontend/src/app/shared/charts/meter.ts
 M frontend/src/app/shared/pagination/pagination.ts
 M frontend/src/locale/GLOSSARY.md
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles.scss
 M frontend/src/styles/_charts.scss
?? frontend/src/app/core/notifications/notification-words.ts (+ .spec.ts)
?? frontend/src/app/features/billing/billing-words.ts
?? frontend/src/app/features/billing/subscription-summary/
?? frontend/src/app/features/plan-usage/plan-words.ts
?? frontend/src/app/features/plan-usage/quota-row/
?? frontend/src/app/features/roles/permission-picker/
?? frontend/src/app/features/roles/permission-words.ts
?? frontend/src/app/features/team/team-words.ts
?? frontend/src/styles/_admin.scss
?? docs/phase-reports/PHASE_36_10_WORKSPACE_ADMIN_REPORT.md
```

Nothing in `backend/`, `deploy/`, the database, endpoints, permissions or business rules changed. No package was added.

**Team (`/team`).**

- A facts strip: members and invitations waiting, each counted from the server's totals.
- Members page through the server with the page in the address (`?page=`). Before, only the first 20 were ever read, so a 21st member would silently not appear.
- Each row: the address as a left-to-right island, Owner and You markers, the roles (custom names as written), and when they joined.
- Invitations are read newest first at the server's maximum of 100; the page says so when there are more.
  - Waiting ones show "Expires in 7 days" with the date, and a Revoke that asks first. The confirmation, the toast and focus moving to the section are unchanged.
  - Accepted, revoked and expired ones are folded into "Past invitations" (a disclosure, closed by default). Each carries its state as a word with its own marker ("i", tick, dot, ring), who sent it and when.
  - Before, concluded invitations were not shown at all.
- If only the invitations fail to load, the members stay on screen and the section says "Not checked" with Try again; it never shows zero for something that was not read.

**Invite member.** Two numbered sections:

1. Who: the address, typed left to right.
2. Access: role cards with their permission counts, and "No roles selected: joins with no access".

The token handling is unchanged: the link lives in component memory only, its token is in the fragment, and it is shown once. The result names the address and the roles the person will join with. If the browser refuses the clipboard, the page now says so and the link stays on screen to select by hand; before, the failure was silent.

**Member detail.**

- Facts: when they joined, when their access last changed, and how many permissions they hold.
- "What they can do now": the member's access by area ("Conversations 4 of 4"), drawn from the permissions the server published for them against the catalogue. It appears only when the reader may see the catalogue (`roles.view`), and a failed catalogue read costs only this section, never the page.
- Its impact hints, summed.
- Roles as cards. Saving says what changes ("Removes Support.") beside "Unsaved changes". Taking every role away now asks first, though it remains allowed. A toast confirms the save.
- The four read-only reasons, the stale-version notice, the unsaved guard and the delegation ceiling are unchanged.

**Roles & permissions.**

- **Areas.** `permission-groups.ts` keeps the server's order and now adds the area's translated name and a stable key. A shared `PermissionPicker` draws each area as a fieldset with "n of m granted", in two columns from 80rem.
- **Each permission:** its translated label and sentence, plus impact hints where the server's own description says so: reads what customers wrote, customers see the result, costs money, spends money, cannot be undone, changes who has access. The checkbox is named by its label; the sentence and hints are its description.
- **The role as a whole:** "Grants 10 of 29 permissions", in every Arabic plural form, and the impact of everything ticked, updating as boxes are ticked.
- **The authority ceiling is unchanged.** Only permissions the reader holds are offered, and the note now says how many are withheld. Owner and roles out of reach stay read-only records, not disabled forms.
- **The editor** has two numbered sections (name, permissions), each saving on its own with "Unsaved changes" beside it. The stale-version notice, the guard and the If-Match saves are unchanged.
- **The list:** Protected and Custom markers, counts in words, and the system role said in the reader's language (المالك). Custom role names are never translated.

**Plan & usage.**

- The plan's name, when it took effect, and a link to Billing for whoever may see it. There is still no Upgrade button and no price.
- A threshold legend shows the four states once.
- Every capped limit gets a `QuotaRow`:
  - its state as a word with a marker of its own and its status colour: Within the limit (neutral dot), Approaching the limit (warning triangle, from 70%), Nearly at the limit (danger octagon, from 90%), Limit reached (a new solid `badge-exhausted` with a "no entry" disc);
  - "6 of 10" in tabular figures, a real `meter`, and "60% used · 4 left";
  - one sentence on what the state means for the next one.
- Unlimited is the word "Unlimited", with no meter.
- The state is always the server's, never re-derived from the percentage. The shared meter gained the `exhausted` state (full, outlined).
- A failed refresh keeps the figures and says they may be out of date.

**Billing.**

- `SubscriptionSummary` shows the status as a word with its own marker (ring awaiting first payment, "i" trial, tick active, triangle payment failed, octagon suspended, dot ended) and one sentence.
- PastDue shows "Plan kept until 25 October 2026 · 5 days left", and what happens after.
- Whenever the fallback applies (suspended, ended, awaiting first payment), "In effect now" sits beside "Subscribed plan (not in effect)". A renewal date appears only while the subscription keeps its plan; a suspended one used to show "Renews".
- Buy and Manage work exactly as before (the double-click guard, the full navigation to the provider, an internal offer id only). The page never reads return parameters, and the footnote now says the page changes only when the provider confirms the payment to Nabra.

**Notifications and the bell.**

- **Words.** Titles and bodies are translated on the client by kind. A report's name is taken from the server's sentence and kept as written; a kind this build doesn't know keeps the server's words.
- **Each item:** unread is a dot, a heavier title, an ink edge on the reading start and the word "Unread". The category is named.
- **The history** groups a page by day (Today, Yesterday, the date) and gives each row its time of day; summary and announcements are translated.
- **Email preferences:** categories named by the shared words. They gained the unsaved-changes guard (route and tab close) and "Unsaved changes". Changes are compared by value, so a switch turned back leaves the page clean. Every category stays off by default.

**Shared.**

- `_admin.scss` holds the rules several pages share (facts strip, permission areas, impact hints, role options, quotas, subscription callouts, folded history). It stays out of the component budgets: one would have gone over, see section 5.
- The pager gained `members` with all six Arabic forms.
- The Design Lab gained `#lab-admin` (labelled sample): every quota state and six subscription states. This local stack can't produce them.

**Language.** 480 new messages in English and Arabic (2,047 in total). The glossary gained grace period (مهلة السماح), fallback plan (الباقة البديلة) and plan (الباقة).

**Screenshots.**

- Before: `<evidence>\before\` (114 cells, `before.json`): owner en light and dark plus ar light at 1440 and 390 for 13 views (the bell panel included); support and the analyst in en and ar.
- After: `<evidence>\after\` (the same names, same script), `<evidence>\matrix\` (full matrix), `<evidence>\peek1–3\` (build-time looks) and `<evidence>\peek2\lab-admin-en-light.png` (the sample states).

## 4. Decisions

1. **Areas, not a roles-by-permissions grid.** A role is decided one at a time, and 29 rows in 11 short lists read faster than a spreadsheet. Rejected: a grid of roles against permissions, which would need a member-impact column the API cannot give (backend ask 1).
2. **Impact hints restate the server's own descriptions** and nothing else; they are neutral chips (icon plus word), not warnings, because none of these permissions is wrong to grant. Rejected: dependency hints ("Edit needs View"), which the API does not state.
3. **The authority ceiling stays a filter.** Withheld permissions are counted, not listed; a disabled list of what the reader cannot grant would be controls nobody may use.
4. **Concluded invitations come back, folded.** Before they were dropped, which hid what happened to an invitation. They are history now: closed by default, never above the waiting ones.
5. **Invitations are read 100 at a time, newest first, with a "newest N of M" note.** The API has no status filter, so a page of waiting ones is impossible honestly (backend ask 2).
6. **Removing every role asks first,** the one member change that leaves somebody unable to do anything. It is still allowed, as before.
7. **"Limit reached" is the only solid badge,** because it is the only state that refuses something; Warning and Critical are informational, as the server says.
8. **The plan in effect is the server's `currentPlanName`,** shown beside the subscribed plan only when the status does not keep the plan (`GrantsPlan`, B12). The browser never infers either.
9. **Notification text is translated by kind, report names kept as sent.** Rejected: matching the English report title back to a type, which would be parsing a sentence the server is free to reword (backend ask 3).
10. **The system role Owner is translated; custom role names never are.** Owner is a platform word with a fixed name; the others are the workspace's own.
11. **Deferred:** the overview's attention cards keep their own limit names (unchanged). The 36.8 agent preview still draws its own customer view (36.9 open item).

## 5. Verification

| Check                                                                             | Result                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `npm run format:check`                                                            | **PASS**: all matched files use Prettier style (`<evidence>\final-format.log`).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `npm run test:ci`                                                                 | **PASS**: 91 files, 1,081 tests, 0 failures (`<evidence>\final-test.log`; 90 / 1,064 at 36.9). The first run after the redesign had 33 failures in the six specs of these areas, all asserting old copy, markup or request sizes; each was updated to the new behaviour without weakening it. New cases: permissions by area with live counts; impact hints beside a permission (in its description, name kept to the label) and summed for the role; the ceiling note; the member pager with the page in the address; waiting versus folded past invitations with their states; a failed invitation read keeping the members; removing every role asks first, and declining sends nothing; what saving changes, and the saved toast; access by area; all four quota states as word and marker class; the meter's values; consequences; unlimited without meter or legend; a failed refresh keeping figures; the fallback plan in effect and no renewal date while suspended; the grace deadline; preferences dirty by value; day groups, times and the unread word; clipboard refusal; notification words by kind, report name, plain sentence and unknown kind. |
| `npm run build`                                                                   | **PASS**: no warnings, no budget raised. Initial total 313.31 kB raw / 73.16 kB transfer (baseline 304.37 / 72.32), the new global admin layer. One component (`design-lab.scss`) went 137 B over the 4 kB warning during the phase; its rule moved to `_admin.scss` instead.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Startup JavaScript (loader + application chunk, production)                       | **PASS**: 135,808 → 136,853 B gzip (+0.77%), brotli 120,242 → 121,094 B (+0.71%), raw 411,464 → 415,973 B (`<evidence>\startup-js-*.json`). The growth is the notification words, which the bell loads at startup.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `npm run i18n:check`                                                              | **PASS**: 2,047 messages, 0 errors, 0 warnings. One glossary warning during the phase («ووكلاؤها» for AI agents) was fixed. The three duplicate-ID notices from 36.7/36.8 remain; this phase's own was fixed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Chrome matrix (`<evidence>\matrix\matrix.json`, `matrix.log`)                     | **PASS**: 336 cells, run in full after the last code change, with no edits while it ran. <br>• Owner (288): Team, Invite, a member, own access, Roles, Create role, the Owner role, a custom role, Plan & usage, Billing, Notifications, Email preferences, at 1920/1440/1280/1024/768/390 × light/dark × en/ar. <br>• Support (24): Team read-only, an Owner member, and the no-access Roles, Plan & usage and Billing, plus Notifications, at 1440/390 × en/ar. <br>• Analyst (24): no-access Team, Invite and Billing, plus Plan & usage, Notifications and preferences, at 1440/390 × en/ar. <br>• Per cell: page overflow 0, clipped content 0, console errors 0, exactly one `h1`, unnamed controls 0, shell targets under 24 px 0. <br>• The after gallery (114 cells, the before gallery's names) is clean too (`<evidence>afterafter.json`).                                                                                                                                                                                                                                                                                                             |
| Keyboard (`<evidence>\keyboard\keyboard.json`)                                    | **PASS** in en and ar, real key events. <br>• **Role editor:** Tab reaches the first permission (21 stops through the shell); every stop had a visible ring. The checkbox is named "View workspace" / «عرض مساحة العمل» and described by its sentence. Space ticks it, the summary goes from 9 to 10 of 29, and "Unsaved changes" appears with Save enabled. Leaving asked "You have unsaved changes on this page. Leave and discard them?" and "stay" kept the page and the tick. <br>• **Team:** Tab reaches "Past invitations" and Enter opens it. Revoke is named for the address; the dialog asks "Revoke the invitation for …?" with focus on "Keep invitation"; Escape closes it, focus returns to that Revoke, and both invitations remain. <br>• **Preferences:** Space turns a switch on, "Unsaved changes" appears, and leaving asks first. <br>• **Bell:** Enter opens the panel (focus stays on the bell, by design: Tab moves into the panel; only the command palette moves focus to its heading). Each unread item shows "Unread", and Escape closes the panel and returns focus to the bell.                                                     |
| Contrast (`contrast_check.py`, `<evidence>\contrast\`)                            | **PASS**: 36 new pairs × light, dark, light + more, dark + more: 35 pass, 0 fail, 1 informational (a chip's decorative edge) in each mode. Tightest text 4.68:1 (Active badge, light); tightest UI 3.08:1 (a ticked role card's edge, dark). The Limit reached badge label is 5.43:1 light and 6.99:1 dark.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Reduced motion and RTL mirroring (`<evidence>\flow\rtl-motion.json`, screenshots) | **PASS**, measured in Chrome. <br>• **Arabic** (`dir="rtl"`): each area's name sits at the reading start with its count at the end, and checkboxes come before their labels. The closed history chevron points to the reading end (rotated +90°; −90° in English). The unread edge is on the reading start (−3 px inset; 3 px in English). Addresses stay `direction: ltr` inside Arabic rows. Lists, facts, the role cards, the meters (they fill from the right), the two-column areas and the back links mirror (`<evidence>\peek3\`, `matrix\*-ar-*`). Custom role names and English report names keep their own direction (`dir="auto"`, `<bdi>`). <br>• **Reduced motion:** `--duration-sm` collapses to 0.01 ms, and the chevron, the quota refresh dim and the switch go from 0.15 s to 1e-05 s / 0 s. State changes stay visible as content (the open history, the dimmed figures, the switch position). Nothing else in these screens moves.                                                                                                                                                                                                            |
| Lints (`<evidence>\lint\`)                                                        | **PASS** on every touched folder (staged copy): `rtl_logical_lint.py` 0; `token_lint.py` 0 after annotating two 2 px optical offsets that predate this phase (notification icon, switch); `security_scan.py` 0 at medium or above; `erp_screen_lint.py` 0 HIGH / 0 MEDIUM / 0 LOW.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| Security                                                                          | **PASS** (static reading + scan): no `innerHTML`, bypass or `eval`. Server text is interpolated as text, and a kind this build doesn't know still shows the server's `<script>` as characters (spec). Notification links are still only internal routes. The invitation token is still in memory only and in the fragment. Billing still sends an internal offer id only and reads no return parameter.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Provider canary                                                                   | **PASS** at start, after the restart and at the end: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_*` and `Email__Smtp__*` are absent (names only; `<evidence>\canary-*.log`). No provider was called.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Quota states and billing states on real data                                      | **NOT RUN on real data**: the local plan is unlimited and billing is not configured, so these states cannot come from the API here. They are covered by specs and shown in the Design Lab as labelled sample (`<evidence>\peek2\lab-admin-en-light.png`).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |

## 6. See it live

- http://localhost:4200/team (members, waiting invitations, open "Past invitations")
- http://localhost:4200/team/invite
- http://localhost:4200/roles, then "Support Agent" (areas, counts, impact hints; tick something and try to leave), and "Owner" (the protected readout)
- http://localhost:4200/roles/new
- http://localhost:4200/plan-usage (every limit is unlimited on this stack)
- http://localhost:4200/billing (not configured on this stack)
- http://localhost:4200/notifications and http://localhost:4200/notifications/preferences, and the bell
- http://localhost:4200/design-lab#lab-admin (the four quota states and six subscription states, labelled sample)
- Switch the language to Arabic from the profile menu for the Arabic pages.
- Accounts and synthetic records: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-10\demo-accounts-p36-10.md`. It points to `p36-01\demo-accounts.md`, which holds the password. Use the owner for everything, support for a read-only Team, and the analyst for the no-access states.

The dev server (`npm start`, port 4200) and the API (`omniai-p36-api`, 127.0.0.1:5027) are running.

## 7. Backend asks

1. **Who holds a role:** a member count, or the members, on the role read, so the editor could say "Saving changes access for 3 members".
2. **A status filter on invitations** (`?status=Pending`), so waiting ones can be paged on their own instead of reading the newest 100.
3. **The report type on report notifications** (and a stable code per kind's parameters), so the report's name can be translated instead of quoted as sent.
4. **Translated permission labels, descriptions and groups**, or a stable key per group, so the client is not the only place the catalogue has Arabic.
5. **The fallback plan's name and the subscribed plan's limits** on the billing read, so the page could say what the workspace will lose when the grace period ends.
6. **Notification email subjects still begin "OmniAI:"** (`NotificationCatalog.Subject`). That is the code name, which users must never see; it should say Nabra (and be translated).
7. Still open from earlier phases: the widget appearance on the hosted read, a translated loader frame title, a workspace display name on public reads, coded origin refusals (36.9); a persisted indexing outcome, source file size, channels per agent, coded file refusals (36.8); purpose breakdown, comparison period, localised report catalogue, first-activity date (36.7); the NuGet-audit transitive package (36.1).

## 8. Risks and open issues

- **No real quota state or subscription state was seen on the API.** The local plan is unlimited and billing is not configured. The states are covered by specs and the labelled Lab samples, which use the same components the pages use.
- **Impact hints are client prose.** They restate the server's descriptions today; a server change to a permission's meaning would need this list changed too. An unknown code shows no hint and keeps the server's words.
- **Report names in Arabic notifications stay in English** inside «…», as the server wrote them (backend ask 3).
- **Invitations beyond the newest 100** are not read; the page says so when that happens.
- **Arabic copy has not had a native review**, as in earlier phases. Plurals and the glossary are checked by script.
- **Synthetic data:** three invitations were added in Northwind (two waiting, expiring on 2026-10-27, one revoked); see the accounts file.

## 9. Proposed checkpoint commit message (not committed)

```
feat: show what access means before it is given

Roles list the permission catalogue by area (eleven areas, "n of m
granted", two columns on wide screens), each permission with its own words
in English and Arabic and, where the server's description says so, what it
can lead to: reads what customers wrote, customers see the result, costs
money, spends money, cannot be undone, changes who has access. The role as
a whole says how much it grants and what it adds up to. The authority
ceiling is unchanged; the note now counts what is withheld.

Team pages through every member with the page in the address, shows when
each waiting invitation expires, and keeps accepted, revoked and expired
ones in a folded history with their state. Member detail shows what a
member can do now by area, says what saving changes, and asks before every
role is removed.

Plan & usage gives every limit a meter in the server's four states, each a
word with its own marker (a new solid "Limit reached" badge), tabular
figures, and one sentence on what it means; unlimited is a word, never a
bar. Billing shows the grace period's deadline while a payment has failed
and which plan is in effect once the fallback applies, and never reads the
browser's return as proof of payment.

Notifications are translated by kind, show "Unread" as a word, and group by
day; email preferences stay off by default and gained the unsaved-changes
guard. 480 new messages in English and Arabic. Frontend only.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
