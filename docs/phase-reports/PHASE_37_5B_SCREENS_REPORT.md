# Phase 37.5b: Screens — team, roles, permissions, billing, and the session check

What the redesigned team, role, permission and billing screens and the signed-out pages asked the backend for (plan rows 23, 24, 26, 27, 28b and 29), plus your decision after 37.3 that a completed password reset lets the person sign in at once. Evidence is in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-5b\`.

## 1. Summary

- **Roles say how many people hold them.** The role list and each role read now carry `memberCount`. The role editor can warn "saving changes access for 9 members" before anybody presses save.
- **Invitations can be filtered by status.** `?status=Pending`, `Accepted`, `Revoked` or `Expired` lists only those, with a matching total; a pending invitation past its expiry counts as expired. The list now reads every page in a fixed number of statements, and its typical response time halved (p50 30.7 → 17.4 ms).
- **Areas, permissions and billing plans have stable keys.** Every permission carries a `groupKey`, so the screens can name each area in English or Arabic. Billing shows the current plan and the plan a workspace would fall back to, each with its limits.
- **Every refusal in roles, invitations, team and billing names its rule** (for example `roles.name.taken`, `invitations.email.already_member`, `team.member.self`), so the screens can explain it in the reader's language.
- **Signed-out pages can ask "am I signed in?" without an error,** through a new `GET /api/auth/session` that never sets a cookie. **After a password reset the person signs in at once,** even if failed guesses had throttled their address. Today's screens work exactly as before.

## 2. Starting point

- Your message's first line named **37.5b**.
- Branch `master`, HEAD `f138d19` ("feat: say what indexing did, which channels an agent answers on, and whose chat it is"), worktree clean (`git status --short` printed nothing). That is the 37.5a checkpoint, as "Starting point" requires.
- The dev stack was running the 37.5a build (`omniai-p36-api` inside `omniai-p37-netns`, Mailpit, `omniai_pg25`, `omniai_redis18`, dev server on :4200). The provider canary passed before any traffic (`<evidence>\live\canary-start.txt`).
- Free memory was 9.6–9.7 GB throughout, so no page-cache drop was needed.

## 3. Changes by area

`git status --short`: 75 entries, 66 modified and 9 new, this report included (the listing before the report is `<evidence>\live\git-status-final.txt`).

| Folder | Files | What changed |
| --- | --- | --- |
| `backend/src/OmniAI.Domain` | 26 | a code on all 94 throw sites in `Authorization`, `Invitations`, `Billing`, `Plans`, `Tenants`, `Users` and `Accounts`; `PermissionDefinition.GroupKey` and `PlatformPermissions.GroupKeyFor` |
| `backend/src/OmniAI.Application` | 14 | `TenantRoleSummary.MemberCount`, `TenantRoleDetailView`; the invitation list (status filter, batched reads, `InvitationStatusFilterInvalidException`); `ProtectedMembershipException.Code`; the billing read (`BillingPlanView`, `BillingPlanLimits`, `FallbackPlanResolver.FindAsync`); new port members: `CountMembersAsync`, `FindNamesAsync`, `FindEmailsAsync`, `ClearSignInFailuresAsync`, `AuthoriseSessionCheckAsync`, `SessionChecksPerNetwork` |
| `backend/src/OmniAI.Infrastructure` | 4 | the role list's correlated member count, the batched role-name and address reads, the invitation status predicate, the two Redis operations |
| `backend/src/OmniAI.Api` | 12 | `GET /api/auth/session` and `SessionProbe` (no cookie renewal, no sign-out on that route), the reset's clear, `RoleDetailResponse`, `memberCount`/`groupKey`/plans on the responses, the `status` query, the area codes in the problem handler, OpenAPI summaries |
| `backend/tests` | 13 | 8 new test files (below) and 5 updated: test doubles for the new port members, the two role-detail unit tests, the anonymous-surface pin (five → six routes), `ProblemCodeTests` (37.5b folders, types and codes) |
| `docs/architecture` | 5 | §3.3 |
| `frontend` | 0 | none |

New files:

```
?? backend/src/OmniAI.Api/Security/SessionProbe.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Accounts/ResetClearsSignInFailuresApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Accounts/SessionCheckApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Billing/BillingPlansApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Team/AdministrationScreensApiTests.cs
?? backend/tests/OmniAI.Application.Tests/Team/InvitationListTests.cs
?? backend/tests/OmniAI.Domain.Tests/Authorization/PermissionGroupKeyTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/AdministrationReadModelTests.cs
?? docs/phase-reports/PHASE_37_5B_SCREENS_REPORT.md
```

No migration. No `deploy/` change. No package added, upgraded or removed. No frontend change.

### 3.1 Items

**Session check (row 29).**
- `GET /api/auth/session`, anonymous. It answers 200 with exactly `{"authenticated":false}` when:
  - the caller is signed out;
  - the session was revoked or has expired;
  - the account no longer exists.

  Signed in, it answers `{"authenticated":true,"user":{"id","email","language"}}`, built by the same projection as `/api/auth/me`. A test compares the two bodies.
- **No cookie, ever.** The route carries the `SessionProbe` marker, which the cookie events read:
  - the sliding expiration (`OnCheckSlidingExpiration`) does not renew the cookie;
  - the per-request stamp check does not renew it either;
  - a revoked cookie is rejected without the sign-out that would write `Set-Cookie`. The next ordinary request clears it as before.
- `Cache-Control: no-store` on every answer, refusals included.
- Rate-limited per client network, for every caller (600 per 10 minutes, `AccountProtection:SessionChecksPerNetwork`). Over the limit: 429 `auth.throttled` with `Retry-After`. Limiter unreachable: 503 `auth.throttle.unavailable` (fail closed).
- `/api/auth/me` is unchanged, 401 included.

**Signing in after a reset (your decision).** A completed reset (204) deletes two counters, both belonging to that address only:
- `signin:fail:address`, its ceiling across every network;
- `signin:fail:address-network`, its count on the network the reset came from.

The per-network counter, which every address on that network shares, is never cleared, and nor is any other address's counter. A refused reset clears nothing. Best effort: if Redis is unreachable, the reset still stands and warning 4342 is logged.

**Members per role (row 23).**
- On the list, `memberCount` is a correlated count in the list's single statement, on `ix_tenant_role_assignments_role`, scoped by the role's own workspace.
- On the detail read, it is one indexed count, taken after the role was found. The detail read uses its own `RoleDetailResponse`, so create, rename and permission writes keep their exact shape.

**Invitation status filter and N+1 (row 24).**
- The filter is checked before membership, so an unknown value is the same 400 `invitations.status.invalid` for anybody. A number or a list is refused too.
- The status is the one the list shows: a stored `Pending` row past `expires_at` is `Expired`, the same rule as `TenantInvitation.HasExpiredAt`, evaluated in SQL.
- One instant decides both the filter and every status shown on the page. `total` counts the filter.
- Each page now costs a fixed number of reads: the count, the page, the role names of every invitation on it, and the inviters' addresses, one statement each. Before, each invitation read each of its roles and its inviter separately.

**Catalogue keys (row 26).**
- `groupKey` on every catalogue entry: `workspace`, `ai-agents`, `roles`, `members`, `conversations`, `customers`, `channels`, `knowledge`, `analytics`, `reports`, `billing`.
- The keys come from a switch that throws for a group without a key.
- A snapshot test fixes every code with its key, so a key cannot change silently.
- The permission `code` remains the stable key for its label and description.

**Billing plans (row 27).**
- `currentPlan: { code, name, limits }`. Its limits are the ones the entitlements were just judged against, so it adds no read and cannot disagree with the usage screen.
- `fallbackPlan: { code, name, limits } | null`. It is `null` whenever the fallback is unset, unusable, missing or unlimited: everything B11 refuses reads as "no fallback", and the read never fails because of it.
- `limits` names the six plan limits as plan-usage does; `null` means unlimited and is always written.
- These are plan definitions only. Usage stays behind `analytics.view`.

**Codes (row 28b).**
- All 94 Domain and Application throw sites in these areas carry a code.
- Six Application refusal types have one code each in `AreaCode`. `ProtectedMembershipException` carries its own code, because one type covers two rules (owner, self).
- The source scan in `ProblemCodeTests` now covers these folders, and every code is in `problem-codes.md`.
- None of these areas returns an `errors` map, so no `errorCodes` map was needed (§4).

### 3.2 Contract table

| Endpoint | Change | New fields or parameters | Permission | Backward compatible |
| --- | --- | --- | --- | --- |
| `GET /api/auth/session` | **New**, anonymous | → `{authenticated}` or `{authenticated, user{id,email,language}}`; `Cache-Control: no-store`; 429/503 coded | none (rate-limited per network) | Yes: new route; nothing calls it today |
| `GET /api/auth/me` | None | none | signed in | Yes: unchanged, 401 included (test) |
| `POST /api/auth/password/reset` | Also clears the address's sign-in failures | none | anonymous | Yes: same statuses and bodies; a person throttled before the reset can now sign in at once |
| `GET /api/tenants/{t}/roles` | Members per role | `memberCount` on each item | `roles.view` (unchanged) | Yes: added field; same order, statuses, refusals |
| `GET /api/tenants/{t}/roles/{id}` | Members holding it | `memberCount` | `roles.view` | Yes: added field; `ETag` unchanged |
| Role writes (create, rename, permissions) | None | none | `roles.manage` | Yes: exact shape kept (test) |
| `GET /api/tenants/{t}/invitations` | Status filter; constant reads per page | `?status=Pending\|Accepted\|Revoked\|Expired`; 400 `invitations.status.invalid` | `members.view` (unchanged) | Yes: without `status` the list is identical (old-field comparison, §5) |
| `GET /api/tenants/{t}/permissions` | Area keys | `groupKey` on each entry | `roles.view` | Yes: added field; `group`, `label`, `description` unchanged |
| `GET /api/tenants/{t}/billing` | Plans and limits | `currentPlan{code,name,limits}`, `fallbackPlan\|null` | `billing.view` (unchanged) | Yes: added fields; `currentPlanName` and the refusals unchanged |
| Role, invitation, team and billing refusals (400, 409, and 410/403 for acceptance) | Specific codes | `code` (§3.1) | — | Yes: status, `title` and `detail` unchanged; `validation.failed`/`state.conflict` replaced by specific codes in these areas only |
| `POST /api/tenants` (slug) | Specific code | `workspace.slug.invalid` (was `validation.failed`) | signed in | Yes: added specificity; same status and detail |

**OpenAPI.** The changed reads and the new route carry `WithSummary` and `Produces`. The new records (`SessionResponse`, `RoleDetailResponse`, `BillingPlanResponse`, `BillingPlanLimitsResponse`) and the new fields are in the generated document (`<evidence>\live\openapi-v1.json`, checked). `status` is read from the query string, like the list's existing `page` and `pageSize`, so it is described in the summary rather than as a declared parameter.

### 3.3 Architecture documents

- `authentication-tenant-membership.md`:
  - D24: eight anonymous endpoints;
  - D30: the session-check limit and the reset's clear;
  - new D34, the session check;
  - new D35, a completed reset clears the address's failures.
- `tenant-rbac.md`: `groupKey`, and `memberCount` on the list and the detail.
- `team-members-invitations.md`: new T21, the status filter and the N+1 fix.
- `saas-subscription-billing.md`: new B25, the plans on the billing read.
- `problem-codes.md`:
  - the scan's new scope, and the note on `errors`;
  - the slug and id codes in the workspace section;
  - new sections for roles, invitations, team, email addresses and billing;
  - the internal consistency codes.

### 3.4 Migrations

None. The plan lists no migration for 37.5b, and none was needed: every new field is computed from existing tables and indexes, and `groupKey` is compile-time. `DatabaseModelAuthorityTests` stays green (§5).

## 4. Decisions

- **The session check never writes a cookie, even for a revoked session.** I skip both renewals and the stamp check's sign-out on that one route, through endpoint metadata, rather than stripping `Set-Cookie` afterwards. Response callbacks run in reverse order, so stripping would not reliably win against the cookie handler. Rejected: removing the header in middleware.
- **No audit row for a throttled session check.** It names no account and changes nothing. A row per refused poll would turn the limit into a way to fill `security_events`. Throttled sign-ins and reset requests are still audited as before.
- **The session limit is generous**: 600 per 10 minutes per network, for offices behind one address. It fired on the dev stack during my latency runs (§5), which shows it works live.
- **A reset never clears the per-network counter.** Your decision named "the counter for the network the reset came from"; I read that as the address's own counter on that network. The network-wide counter belongs to every address there, and clearing it would break "a reset never clears another address's counters". Rule 19: this is at least as safe as the wording. Please confirm the reading.
- **The plan's "404 without permission unchanged" for billing** is in fact 403 for a member without `billing.view` and 404 outside the workspace. That is today's behaviour, which I kept and pinned in a test; I corrected the wording in B25.
- **Writes keep their shape; reads carry the new facts**, as in 37.5a. Only the role detail read carries `memberCount`.
- **Throw sites coded beyond the four areas' own folders**: the value objects their requests are built from (`Tenants` slug and id, `Users` address and id, `Accounts`, `Plans`). An invitation's address refusal, for example, is raised by `EmailAddress`. 67 of the 94 sites are internal invariants that no request can reach, such as an aggregate without its workspace, a provider record without its id, or a plan definition's own rules; they are listed separately in the catalogue. The other 27 are the refusals a person can meet.
- **`email.invalid_characters`, not `email.control_characters`**: the rule refuses spaces too.
- **No `errorCodes` map.** No endpoint in these areas answers with a validation `errors` map, since none uses `ValidationProblem`: each refusal is one rule with one code. `problem-codes.md` says what would happen if one ever did.
- **I reverted an edit to the checkout's `request is null` branch.** That branch cannot be reached through model binding, and I would not catalogue a code that never appears. The reachable refusal for a checkout without an offer is `billing.offer_id.invalid`, which is tested.
- **The `ProblemCodeTests` code scan now matches whole words** (`\bCode`), so `EnglishCode = "en"` is not read as a problem code.
- **I tested the expired case by moving one invitation's dates into the past** inside the test database. Waiting seven days was not an option; the row stays stored-pending, which is exactly the case under test.

## 5. Verification

The backend gates ran in Docker as in the Phase 33.5 report §11:
- `mcr.microsoft.com/dotnet/sdk:10.0` with the repository mounted;
- a throwaway `pgvector/pgvector:pg17` and `redis:7-alpine` through `OMNIAI_TEST_POSTGRES` / `OMNIAI_TEST_REDIS`;
- the test projects one at a time (`-m:1`);
- all removed afterwards.

Every gate ran alone. Free memory before each memory-gated command (`<evidence>\gates\memory.txt`):
- backend gates: 9.6 GB;
- `test:ci`: 9.7 GB;
- `build`: 9.1 GB.

| Check | Result |
| --- | --- |
| `dotnet restore` (NuGet audit on) | PASS |
| `dotnet build OmniAI.slnx -c Release` | PASS, **0 warnings, 0 errors** |
| `dotnet list package --vulnerable --include-transitive` | PASS, no vulnerable packages in any of the 8 projects |
| Tests, `OmniAI.Api.IntegrationTests` | PASS, 1,072 passed, 2 skipped |
| Tests, `OmniAI.Application.Tests` | PASS, 855 passed |
| Tests, `OmniAI.Domain.Tests` | PASS, 854 passed |
| Tests, `OmniAI.Infrastructure.IntegrationTests` | PASS, 1,163 passed, 1 skipped |
| Total | **3,944 passed, 3 skipped, 0 failed** (37.5a: 3,879; 65 new). Exit 0, 0 leftover test databases (`gates\backend-gates-run1.log`) |
| `DatabaseModelAuthorityTests` | PASS (in the run above) |
| Migrations on a fresh database and on a copy of `omniai_p36` | NOT RUN: this step adds no migration (§3.4). `omniai_p36` was not migrated and needed no `pg_dump` |
| Problem-code guarantee (`ProblemCodeTests`) | PASS: every throw site in the 37.5a and 37.5b folders is coded; every published code is in `problem-codes.md`; the codes reach the wire |
| No N+1 (`AdministrationReadModelTests`, `InvitationListTests`) | PASS: the role list with counts is 1 statement; role names and inviter addresses are 1 statement each for any number of ids; a page of 13 invitations reads names once and addresses once |
| Frontend `npm run format:check` | PASS |
| Frontend `npm run test:ci` | PASS, 96 files, 1,169 tests (unchanged; no frontend change) |
| Frontend `npm run build` | PASS (initial total 313.56 kB raw, 73.66 kB transfer, as in 37.5a) |
| Frontend `npm run i18n:check` | PASS, 2,077 messages, 0 errors, 0 warnings |
| API checks against the dev stack (`<evidence>\live\api-checks.json`) | PASS, **43/43**. See the list below |
| OpenAPI (`<evidence>\live\openapi-v1.json`) | PASS: the session route, the four new records, `memberCount`, `groupKey`, `currentPlan` and `fallbackPlan` are present |
| Chrome, English and Arabic (`<evidence>\live\chrome\`) | PASS, 17/17 (Playwright in Chrome), plus the Chrome DevTools MCP signed-out checks. See below |
| p95 latency, before and after | See the tables below |
| Provider canary | PASS at the start, after each of the three API recreations, and at the end (`live\canary-*.txt`): every provider secret, `OMNIAI_LIVE_*` and the SMTP credentials absent; SMTP host loopback |
| Secret scan of the changed files and the evidence folder | PASS: the demo password appears nowhere. The only hits are the `Microsoft.AspNetCore.Identity` namespace, the scripts' throwaway `Password=$PW` connection templates, and the scan's own pattern list |

**The API checks** used the demo owner, support and analyst accounts, a stranger account with its own workspace (created through the API), a fresh reset account, and signed-out requests.

- **Unchanged contracts.** Before the rebuild I saved the 37.5a build's statuses and bodies (`live\snapshot-before.json`) for each account on roles, a role, the catalogue, invitations, billing and `/me`. After the rebuild, every status and every old field is identical: 18/18.
- **`memberCount`** matches who the member list says holds each role: Owner 1, Analyst 9, Support Agent 1. The detail read gives the same number.
- **Invitation filter:**
  - Pending 0, Accepted 10, Revoked 1, Expired 2; they add up to the unfiltered 13, and every item matches its filter;
  - `?status=Open` → 400 `invitations.status.invalid`, for the owner and for the stranger alike.
- **Catalogue:** eleven `groupKey`s, in catalogue order.
- **Billing:** `currentPlan` is `development`, with the same limits as plan-usage. `fallbackPlan` is `null`, because this deployment configures none.
- **Codes:** `roles.name.required`, `roles.name.taken`, `roles.permission.unknown`, `email.invalid`, `invitations.email.already_member`, `team.member.self`, `roles.system.protected`. No role or invitation was created.
- **Support:** roles 403, filtered invitations 200, billing 403. **Analyst:** 403 on all three. Both are unchanged from the 37.5a build.
- **Another workspace:** every Northwind read is 404, including a Northwind role id asked through the stranger's own workspace. Their own Owner role counts 1 member.
- **Session check:**
  - signed out: `{"authenticated":false}`, no cookie, `no-store`, while `/me` is still 401;
  - signed in: the `user` part is identical to `/me`, again with no cookie.
- **Reset:** a fresh account with five failures on one network is throttled even with the right password. After a reset through Mailpit (202, 204), the new password signs in at once on the same network (200).

**Chrome.** The Chrome DevTools MCP was available this time.
- **What ran in the MCP:** the signed-out checks in English (`dir=ltr`, "Sign in") and Arabic (`dir=rtl`, «تسجيل الدخول»), each with the session check answering 200 false and `no-store`.
- **Why not more in the MCP:** signing in there would put the demo password into the tool-call arguments, which rule 10 forbids. The MCP also refuses to save screenshots outside its workspace roots, as in 36.5.
- **The signed-in screens** therefore ran with **Playwright in Google Chrome (channel `chrome`, a temporary profile)**, which keeps the password in process memory. In English and Arabic:
  - roles list, role editor, new-role editor, team, billing and plan & usage all render as before, and their reads carry the new fields;
  - the team screen still reads invitations unfiltered;
  - the signed-in app's own session check answers `authenticated`;
  - signed out, the sign-in page renders and the check answers false.
- **Console errors:** 4 in total, all the sign-in page's known 401 from `/api/auth/me`. They stay until Phase 38 adopts the session check.
- My first run reported the team screen as failing. My check expected the newest invitation to be listed as waiting, but it was an accepted one; the screen was right ("Invitations waiting 0"). I corrected the check, not the product, and both languages passed.

**p95 latency (ms)**, 300 sequential reads after 30 warm-up, three runs each (`<evidence>\latency\`):

| Read | Before (runs 1 / 2 / 3) | Before, median | After (runs 1 / 2 / 3) | After, median |
| --- | --- | --- | --- | --- |
| `GET /roles` | 17.2 / 17.2 / 17.4 | **17.2** | 28.3 / 24.1 / 22.6 | **24.1** |
| `GET /roles/{id}` | 16.9 / 17.0 / 17.8 | **17.0** | 29.2 / 27.5 / 25.6 | **27.5** |
| `GET /invitations` (page 1, 100) | 46.0 / 38.9 / 42.3 | **42.3** | 35.1 / 36.1 / 31.0 | **35.1** |
| `GET /invitations?status=Pending` | — (new) | — | 23.5 / 22.7 / 32.2 | **23.5** |
| `GET /permissions` | 17.0 / 17.5 / 17.1 | **17.1** | 26.5 / 24.6 / 21.5 | **24.6** |
| `GET /billing` | 29.4 / 23.8 / 22.5 | **23.8** | 59.2 / 35.9 / 37.3 | **37.3** |
| `GET /api/auth/me` (unchanged code, reference) | 17.2 / 17.1 / 17.2 | **17.2** | 26.9 / 18.9 / 17.9 | **18.9** |
| `GET /api/auth/session` (new) | — | — | 21.2 / — / — | **21.2** |

How to read it:
- **The invitation list is the real change.** Its p50 halved, from 30.7 to 17.4 ms, and its p95 fell although the machine was noisier.
- **The "after" runs met a noisier machine.** Every read's tail rose, including `/api/auth/me`, whose code did not change. The p50s of the other reads stayed at the same 15 ms loopback floor.
- **`/billing` was checked separately,** because its p50 had risen too, from about 15 to about 26–31 ms. I ran an interleaved A/B on the same machine state (`latency\billing-probe-*.json`): I published the 37.5a commit into the dev API from a temporary git worktree outside the repository, then restored the current build. `/billing` measured:
  - 37.5a build: p50 21.8 / 20.3, p95 26.1 / 24.6;
  - this build: p50 21.6 / 21.2, p95 27.9 / 26.5.

  `/plan-usage`, which this step did not touch, measured the same as `/billing` in both. The billing change therefore costs nothing measurable; the time is the six usage counts both reads share. It adds no statement when no fallback is configured, and one indexed plan read when one is.
- **The session check's runs 2 and 3 stopped at 429** after about 600 checks in 10 minutes: its own limit, working on the live stack.

**What the A/B changed, and how it was put back.** Publishing the 37.5a commit over the dev volume left stale assemblies behind, and the next start of the current build failed (`TypeLoadException`). I emptied that publish output inside the volume (the volume itself was kept), ran the stack's `build` and `api` again, ran the canary, and confirmed `/api/auth/session` answers 200. The worktree was removed and the main worktree is unchanged (74 entries before and after). I pulled a short-lived `alpine:3` image for the emptying and removed it afterwards.

## 6. See it live

The API is on `http://127.0.0.1:5027` and the dev server on `http://localhost:4200`, both left running. Sign in as `p36.owner@omniai.example` (password in `demo-accounts.md`) and choose **Northwind Operations**. Today's screens look as before; Phase 38 draws the new fields.

To see the new contracts, call them from the signed-in tab's devtools console (same origin). Use `<t>` for the Northwind id from `GET /api/me/tenants`, and send the `X-XSRF-TOKEN` header (from the `XSRF-TOKEN` cookie) on writes.

- **Members per role:** `GET /api/tenants/<t>/roles`. "Analyst (read-only insights)" shows `memberCount: 9`, and the same role at `/roles/<id>` shows 9 too. Open `/team` to count the same nine people.
- **Invitation status:**
  1. `GET /api/tenants/<t>/invitations?status=Accepted` lists 10 invitations with `total: 10`.
  2. `?status=Expired` lists 2, and `?status=Pending` lists 0, matching "Invitations waiting 0" on `/team`.
  3. `?status=Open` answers `400` with `code: "invitations.status.invalid"`.
- **Area keys:** `GET /api/tenants/<t>/permissions`. Each entry has `groupKey` (`ai-agents`, `channels`, …) beside the English `group`.
- **Plans on billing:** `GET /api/tenants/<t>/billing/`. `currentPlan` is `development`, with every limit `null` (unlimited). `fallbackPlan` is `null` on this deployment.
- **Coded refusals:** `POST /api/tenants/<t>/roles` with `{ "name": "support agent" }` answers `409 roles.name.taken`. `POST /api/tenants/<t>/invitations` with `{ "email": "p36.support@omniai.example", "roleIds": [] }` answers `409 invitations.email.already_member`.
- **The session check:**
  - Open `http://localhost:4200/api/auth/session` in a private window: `{"authenticated":false}`. In the network panel there is no `Set-Cookie`, and `Cache-Control` is `no-store`.
  - In the signed-in tab, the same address shows your id, email and language, exactly as `/api/auth/me` does.
- **Signing in after a reset:**
  1. Register a throwaway account at `/create-account`, sign out, and fail sign-in five times: the sixth attempt, even with the right password, is refused.
  2. Use "Forgot password" and open the link in Mailpit (`http://127.0.0.1:8026`).
  3. Set a new password: you can sign in at once.

## 7. Frontend follow-ups (what Phase 38 adopts)

| Adopt | Contract |
| --- | --- |
| "Saving changes access for N members" in the role editor; a count column on the roles list | `memberCount: number` on `GET …/roles` items and `GET …/roles/{id}` |
| Invitation tabs or filters (Pending, Accepted, Revoked, Expired), paged in the URL | `GET …/invitations?status=Pending\|Accepted\|Revoked\|Expired&page&pageSize` → `{ items, page, pageSize, total }` (total of the filter); 400 `invitations.status.invalid` |
| Name areas and permissions by key, in both languages; keep the English as a fallback | `groupKey` on `GET …/permissions` entries; translate labels and descriptions by `code`, areas by `groupKey` |
| Plan limits and the fallback plan on the billing screen | `currentPlan: { code, name, limits }`, `fallbackPlan: {…} \| null` on `GET …/billing`; `limits.{members,aiAgents,knowledgeDocuments,webChatEndpoints,whatsAppConnections,monthlyProviderOperations}: number \| null` (null = unlimited) |
| Word team, role, invitation and billing refusals by code | the role, invitation, team, email-address and billing sections of `problem-codes.md` |
| Use the session check on signed-out pages, so the console 401 disappears | `GET /api/auth/session` → `200 {authenticated:false}` or `{authenticated:true,user:{id,email,language}}`; 429 `auth.throttled` (respect `Retry-After`), 503 `auth.throttle.unavailable`. Keep `/api/auth/me` for signed-in flows |
| Tell a person on the reset page that they can sign in now | no contract change: a completed reset already clears that address's sign-in failures |

## 8. Risks and open issues

- **Your reading of "the counter for the network the reset came from"** (§4): I cleared the address's own count on that network, not the network's shared counter. If you meant the shared counter too, it is a one-line change, but it would let one person's reset clear failures caused by others.
- **Polling the session check counts against a network's 600 per 10 minutes.** A Phase 38 client should ask on page load and on focus, not on a timer.
- **The session check does not sign out a revoked cookie.** The browser keeps it until the next ordinary request clears it. This is harmless, since the cookie authenticates nothing, and it is what keeps the check free of side effects.
- **Other areas still answer generic codes** for their domain rules (customers, conversations, notifications, reports), as planned; 37.6 and later steps name theirs.
- **`team.member.protected`** is catalogued as the general form, but no route raises it today.
- **Latency on this machine moves with its load.** The A/B shows no billing regression, but the absolute numbers in §5 are a laptop under other projects' containers.

## 9. Proposed checkpoint commit message

```
feat: say who holds each role, filter invitations, and answer "am I signed in" without a 401

Phase 37.5b: what the redesigned team, role, permission and billing screens
and the signed-out pages asked the backend for (plan rows 23, 24, 26, 27,
28b, 29), and the owner's decision that a reset lets a person sign in at once.

- Roles: memberCount on the list (one statement) and the detail read; the
  writes keep their shape.
- Invitations: ?status=Pending|Accepted|Revoked|Expired, a stale pending one
  counted as expired, total of the filter, unknown status 400
  invitations.status.invalid before membership; the page reads role names and
  inviter addresses once each instead of per row.
- Catalogue: a stable groupKey on every permission, held by a snapshot test.
- Billing: currentPlan and fallbackPlan (null when none is usable) with their
  six limits; plan definitions only.
- Codes: every throw site in roles, invitations, team and billing names its
  rule (94 sites), with the refusal types' own codes; catalogue in
  problem-codes.md, held by the source scan.
- GET /api/auth/session: anonymous, 200 {authenticated:false} or the same
  user facts as /me, never a cookie (no renewal, no sign-out on that route),
  no-store, rate-limited per network; /api/auth/me unchanged.
- A completed password reset clears that address's sign-in failures: its
  ceiling and its count on the reset's network, never another address's or
  the network's own.

Every change is additive; no migration, no package, no deploy/ or frontend
change.

Gates: backend 0 warnings, 3,944 passed, 3 skipped, no vulnerable packages;
frontend format, 1,169 tests, build and i18n green; API checks 43/43;
Chrome en and ar 17/17.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
