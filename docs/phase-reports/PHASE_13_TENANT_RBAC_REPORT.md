# Phase 13 — Tenant roles and permissions authorization

## 1. Phase objective

Replace Phase 12's all-or-nothing membership with tenant-scoped role-based
authorization, enforced in the Application layer rather than by hiding Angular
buttons: a protected Owner role per workspace, custom roles composed from a
platform permission catalogue, a permission check inside every tenant-scoped use
case, role administration APIs with `ETag` concurrency, and an admin UI for it.

---

## 2. Final status

**IMPLEMENTED AND DOCUMENTED. PARTIALLY VERIFIED — and the gap is stated plainly
below rather than papered over.**

Everything asked for is written, builds with zero warnings, and is covered by
tests. Two categories of verification **could not be executed on this machine**:

- the PostgreSQL integration tests and the API integration tests, and
- the required browser workflow.

**Cause: Windows Smart App Control, enforced on this machine, is blocking the
freshly built `OmniAI.Infrastructure.dll`, `OmniAI.Api.dll` and the two
integration-test assemblies.** The phase brief forbids disabling Smart App
Control or changing Windows security policy, so I did not. The evidence, what I
tried, and exactly which claims are therefore unverified are in §55 and §80.

What **was** executed: 387 Domain tests, 319 Application tests, 261 frontend
tests, a zero-warning Release build of all eight projects, a clean vulnerability
scan, `npm ci`, `npm run build`, `npm run test:ci`, `npm run format:check`, and
`npm audit`. The Phase 13 migration was applied to real PostgreSQL and its
schema read back — that ran before the block appeared, and is reported as a
partial result in §23.

---

## 3. Baseline commit

`83328bf1c9939859c87303cc993b9bbebef7aa05` — *feat: secure admin authentication
and tenant membership*.

---

## 4. Initial Git state

`git status --short` was empty. `git log -13 --oneline` showed twelve commits,
`83328bf` at HEAD. No remotes configured. Nothing was reset, cleaned, restored or
discarded.

---

## 5. Why ASP.NET Core Identity roles were not used

Identity's role system is global to the installation. In this product one
authenticated person may be Owner of workspace A, a three-permission support
agent in workspace B, and a member of workspace C with nothing granted.
`AspNetRoles` cannot express that without a tenant column bolted onto framework
tables this platform does not own — at which point they are this model with extra
steps and a `RoleManager` that no longer means what its documentation says.

Identity stays confined to authentication credentials. No `IdentityRole`, no
`RoleManager`, no `AspNetRoles`, no `AspNetUserRoles`; the context is still
`IdentityUserContext<OmniAiUser, Guid>` and the schema still has four Identity
tables. A guard test asserts the Application references no Identity assembly.

---

## 6. Domain types introduced

`OmniAI.Domain.Authorization`: `TenantRoleId`, `TenantRoleName`,
`TenantRoleKind`, `PermissionCode`, `PermissionDefinition`, `PlatformPermissions`,
`RolePermissionGrant`, `TenantRole`, `TenantRoleAssignment`.

`OmniAI.Domain.Tenants`: `NewWorkspace`.

---

## 7. `TenantRoleId`

`readonly record struct` over a UUIDv7, private constructor, `From` rejects
`Guid.Empty`, `IsEmpty` for the `default` struct. The substitution it prevents is
concrete: a role id sits beside a tenant id in a route, both are GUIDs, and with
bare `Guid` parameters a transposition would compile — which is exactly the shape
a cross-tenant modification takes.

---

## 8. `TenantRole` design

`Id`, `TenantId` (immutable), `Name`, `Kind`, `CreatedAt`, `UpdatedAt`, and a
private grants collection. Operations: `CreateOwner`, `CreateCustom`, `Rename`,
`ReplacePermissions`. `EffectivePermissions` is the single question the
authorisation path asks.

`TenantId` immutability is asserted by reflection in a test: no setter, and no
instance method takes a `TenantId`.

---

## 9. `TenantRoleName` rules

Required; trimmed; whitespace-only rejected; ≤ 80 characters measured after
trimming; control characters rejected; never truncated. Carries `Value` (display,
case preserved) and `Normalised` (`ToUpperInvariant`, matching the
`normalized_email` convention already in this schema).

Uniqueness is **per tenant**, on the normalised form, decided by
`ux_tenant_roles_tenant_name`. Never global.

---

## 10. Owner role design

One per tenant, created with the workspace, named by the constant
`TenantRole.OwnerRoleName`. It is a `TenantRoleKind.Owner`, which is what makes
it Owner — not its name. A custom role may even be *called* "Owner"; it is still
Custom, and a test asserts that.

---

## 11. Owner protection

| Attempt | Result |
| --- | --- |
| Create a second Owner via the API | Unrepresentable — no kind field, and `CreateCustom` cannot produce one |
| Create a second Owner in code | `DomainStateException`, and `ux_tenant_roles_owner` refuses the row |
| Rename Owner | `DomainStateException` → `409` |
| Replace Owner permissions | `DomainStateException` → `409` |
| Delete Owner | No delete exists anywhere in this phase |
| Convert Owner ↔ Custom | No operation changes `Kind` |

---

## 12. Owner permission semantics

**An Owner stores no permission rows.** `EffectivePermissions` returns
`PlatformPermissions.AllCodes`, read at the moment the question is asked.

This is the answer to the failure mode the brief named. There is no row to
backfill, no migration to remember and no tenant that can be missed — adding an
entry to the catalogue extends every Owner in every workspace in the same edit,
because the list *is* what Owner means.

Proved rather than asserted: `An_owner_grants_every_permission_the_platform_defines`
compares against `PlatformPermissions.AllCodes` rather than a hard-coded list, so
a capability added later is covered by that test the moment it is added.
`An_owner_stores_no_permission_rows` asserts `Grants` is empty while
`EffectivePermissions` is not — which is what makes the property structural.

---

## 13. Custom role design

One tenant; editable name; explicit permission set; **may grant nothing**; never
a system role. No description, colour, icon, hierarchy, parent, priority or deny
permissions — none has a requirement behind it.

---

## 14. `PermissionCode` design

`readonly record struct` over a canonicalised string. Grammar: two or more
dot-separated segments of lowercase letters, digits and interior hyphens; ≤ 64
characters; no leading, trailing or adjacent separators. Trimmed and lowercased
with `ToLowerInvariant`.

A string rather than an integer id, so the meaning of a stored row does not
depend on a declaration order no row records. Independent of HTTP: no code is
derived from a route or discovered by reflection.

---

## 15. Permission catalogue

`PlatformPermissions`, compile-time, in the Domain. Explicit list, no reflection,
no attribute scanning, no route walking.

It lives in the Domain because `TenantRole.EffectivePermissions` needs it: an
Owner's authority *is* the catalogue, so putting the list in the Application
would have forced the Owner special case into several call sites — exactly the
fragility the brief warns about.

Tests assert: unique codes; every entry has a group, label and description;
`AllCodes` matches `All` exactly; no code for an unimplemented product area; no
`tenant.admin`-style catch-all; groups are contiguous for rendering.

---

## 16. Current permission list

```
workspace.view          Workspace              View workspace
ai-agents.view          AI agents              View AI agents
ai-agents.create        AI agents              Create AI agents
ai-agents.edit          AI agents              Edit AI agents
ai-agents.lifecycle     AI agents              Activate and disable AI agents
ai-agents.preview       AI agents              Preview AI agents
roles.view              Roles and permissions  View roles
roles.manage            Roles and permissions  Manage roles
```

Eight. Nothing for Conversations, Knowledge/RAG, Channels, Billing, Integrations
or Analytics, because none is implemented.

`ai-agents.lifecycle` is separate from `edit` because the risks differ: editing
changes what an agent *would* say, activating decides whether it says anything to
customers. `ai-agents.preview` is separate because it is the only capability that
reaches a paid third party.

---

## 17. Role permission persistence

Table `role_permissions`, primary key `(tenant_role_id, permission)`. The
composite key **is** the "no duplicate grant" rule — structural rather than an
extra constraint a later migration could drop.

`ON DELETE CASCADE`, uniquely in this schema, and right here: a grant has no
meaning without its role.

No permissions table and no foreign key to one. The catalogue is compiled in;
`TenantRole` validates every code against it before it becomes a row.

---

## 18. Membership-role persistence

Table `tenant_role_assignments`: `user_id`, `tenant_id`, `tenant_role_id`,
`created_at`. All three ids are the primary key. Index
`ix_tenant_role_assignments_role` on `(tenant_role_id, tenant_id)` for the
foreign key's referencing side.

---

## 19. Cross-tenant relational constraints

```sql
FOREIGN KEY (user_id, tenant_id)        REFERENCES tenant_memberships (user_id, tenant_id)
FOREIGN KEY (tenant_role_id, tenant_id) REFERENCES tenant_roles (id, tenant_id)
```

Both read **the same `tenant_id` column of the same row**, so PostgreSQL accepts
the row only when the membership and the role agree about the workspace. A row
pairing a tenant-A membership with a tenant-B role **cannot be written**.

The second key needs a unique constraint on what it references, hence
`ak_tenant_roles_id_tenant` — `UNIQUE (id, tenant_id)`. It looks redundant beside
the primary key; it is not there to prevent duplicates, it is there so that
foreign key can exist.

Both `RESTRICT`.

---

## 20. Provisioning changes

`ITenantRepository.AddAsync` now takes a single `NewWorkspace` and commits
**four** rows in one transaction: tenant, membership, Owner role, Owner
assignment.

`NewWorkspace.Create(name, slug, creator, now)` is the only way to obtain one and
derives the last three from the first two, so a mismatched set is not rejected —
it cannot be constructed. That is why the run-time pairing check Phase 12 needed
is gone from the repository.

On a slug conflict: roll back, detach all four, report
`TenantSlugAlreadyExistsException`.

---

## 21. Existing-membership migration and backfill

Every existing tenant gets an Owner role; every existing membership gets that
role assigned. **This preserves the accepted Phase 12 meaning exactly** — under
Phase 12 a membership granted full access with no gradation, and under Phase 13
full access is what Owner means.

Doing nothing would have locked every existing member out of every existing
workspace: a member with no assignments may do nothing, and no endpoint grants an
assignment. No production database exists, so this touches development data only,
but a migration that silently revoked everyone's access would be wrong regardless.

Backfilled role ids are **UUIDv7**, assembled in SQL because PostgreSQL 17 has no
`uuidv7()`: `gen_random_uuid()` supplies entropy and correct variant bits, the
first 48 bits are overwritten with Unix milliseconds, and the version nibble is
set to 7. A backfilled row is indistinguishable in shape from an
application-written one.

---

## 22. Migration name

`20260906101730_AddTenantRolesAndPermissions`. One migration. No accepted
migration was modified.

---

## 23. Migration inspection

Inspected by hand after scaffolding. Verified in the file:

- `tenant_roles`, `role_permissions`, `tenant_role_assignments` created
- Primary keys: `id`; `(tenant_role_id, permission)`; `(user_id, tenant_id, tenant_role_id)`
- `ak_tenant_roles_id_tenant` — `UNIQUE (id, tenant_id)`
- `ux_tenant_roles_tenant_name` — unique on `(tenant_id, normalized_name)`
- `ux_tenant_roles_owner` — unique on `tenant_id` with filter `kind = 'Owner'`
- `ix_tenant_role_assignments_role`
- Both composite cross-tenant foreign keys, `RESTRICT`
- `fk_role_permissions_role`, `CASCADE`
- **The scaffolder's `xmin` column line removed by hand**, matching the two
  accepted migrations before it, with the same explanatory comment
- Backfill SQL, set-based, inside the migration's transaction

**Executed against real PostgreSQL 17.6** before the Smart App Control block
appeared: the migration applied cleanly, including the backfill, and
`MigrationSchemaTests` passed six of seven cases — the seventh failed only on its
`Assert.Equal(3, applied.Length)` count, which I then updated to 4 and which has
**not been re-run**. `TenantRoleSchemaTests`, written afterwards, has never been
executed (§55).

---

## 24. Application authorisation abstraction

`TenantAuthorizer` — a plain class, matching the accepted `TenantAccessGuard`.
Not an interface: nothing substitutes it, and an interface would make a
permissive registration possible by accident, which for this type is a
platform-wide authorization bypass in one line.

No `HttpContext`, `ClaimsPrincipal`, ASP.NET policy, status code, EF Core or
Npgsql. Existing guard tests already assert the Application references no web
framework and no data-access library.

Composes `TenantAccessGuard` (membership) with `ITenantRoleRepository` (roles).
Nothing is cached.

---

## 25. Membership decision

`TenantAccessGuard.IsCurrentUserMemberAsync` — unchanged from Phase 12, answered
by a primary-key probe on every request. Asked **first**, so a non-member never
reaches a permission check and nothing about a workspace's roles is consulted on
their behalf.

---

## 26. Permission decision

The union of the member's assigned roles' `EffectivePermissions`. `Any`, not
`All` — one role granting the permission is enough. A member with no roles falls
through to denied, which is correct: they belong to the workspace and have been
given nothing to do in it.

---

## 27. `401` behaviour

No session → `401`, from the pipeline. `NotAuthenticatedException` is the second
layer inside the use cases, for a route mapped without its requirement. Thrown
rather than returned as a decision: the caller has not said who they are, and
doing so may change the answer entirely.

---

## 28. `404` behaviour

Authenticated non-member → `404`, byte-identical to a workspace that never
existed. Verified against `/api/tenants/{id}`, `/roles`, `/permissions`,
`/ai-agents` and `/api/me/tenants/{id}/permissions`, with a test comparing a real
workspace against an invented id.

---

## 29. `403` behaviour

Member without the permission → `403`, with a detail naming the permission code
and nothing else: no role name, no member list, no tenant name, no hint about
which record was addressed.

This is the one refusal that admits the resource exists, and it is safe because
the caller already proved membership. It buys a person who can act.

---

## 30. Resource-existence protection

**Authorisation runs before the resource is loaded in every tenant-scoped use
case.** A member without `ai-agents.view` receives the same `403` for a real
agent id, another workspace's agent id and an invented one — asserted with
identical response *bodies*, not merely identical statuses.

Checking after the lookup would answer `403` for a record that exists and `404`
for one that does not, turning a permission somebody lacks into a way to
enumerate the ids they cannot see.

---

## 31. Existing-use-case permission mapping

| Use case | Requires |
| --- | --- |
| `GetTenantById` | `workspace.view` |
| `ListAiAgents`, `GetAiAgentById` | `ai-agents.view` |
| `CreateAiAgent` | `ai-agents.create` |
| `RenameAiAgent`, `UpdateAiAgentInstructions` | `ai-agents.edit` |
| `ActivateAiAgent`, `DisableAiAgent` | `ai-agents.lifecycle` |
| `PreviewAiAgent` | `ai-agents.preview` |
| `ListMyTenants` | membership only |
| `CreateTenant` | authentication only |

No `tenant.admin`.

---

## 32. Effective-permissions endpoint

`GET /api/me/tenants/{tenantId:guid}/permissions`

Authenticated; membership required; non-member → `404`; returns codes in
catalogue order, deduplicated; Owner receives all known codes; a member with no
roles receives `[]`, which is a real answer and not a `404`.

**No user parameter**, so there is no shape of request that asks about somebody
else. It exposes no other member's permissions, no role assignments and no
persistence metadata.

No permission is required to read one's own — requiring one would be circular.

---

## 33. Permission catalogue endpoint

`GET /api/tenants/{tenantId:guid}/permissions` — requires `roles.view`.

Under the tenant rather than at the root, because it is published only to
somebody entitled to see that workspace's roles; a rootless route would have
nothing to authorise against.

Returns `code`, `group`, `label`, `description`. A test asserts the body contains
no internal type names.

---

## 34. Role endpoints

```
GET  /api/tenants/{tenantId}/roles                       roles.view
POST /api/tenants/{tenantId}/roles                       roles.manage
GET  /api/tenants/{tenantId}/roles/{roleId}              roles.view
PUT  /api/tenants/{tenantId}/roles/{roleId}/name         roles.manage + If-Match
PUT  /api/tenants/{tenantId}/roles/{roleId}/permissions  roles.manage + If-Match
```

No `DELETE`, no member-assignment endpoint, no invitation endpoint.

**Create contract:** `name` + `permissions` only. No id, tenant id, kind, system
flag, timestamp or version — absent, not validated away. Unknown code → `400`;
malformed code → `400` (same answer, so the catalogue cannot be probed);
**duplicates deduplicated**, documented; taken name → `409`.

Authorisation runs before validation in this use case, deliberately: the
catalogue is published only to `roles.view`, so validating a permission list
first would let a member without `roles.manage` learn which codes exist by
watching which came back invalid.

**List contract:** id, name, type, isSystem, permissionCount, timestamps.
Owner's count is the catalogue size, not a row count. Order: **Owner first, then
custom roles by normalised name**, tie-broken by id.

**Detail contract:** adds tenantId and the **effective** permission codes. No
version in the body — that is the `ETag`.

---

## 35. Role `ETag` behaviour

Strong tags, never weak. `GET` detail and `POST` create both return one. Both
mutations return the new tag, so consecutive edits need no re-read. The shared
`EntityTags` helper is the same one the AI agent endpoints use.

---

## 36. Role `428` behaviour

`If-Match` absent or empty → `428 Precondition Required`, before the role is
looked up, so it reveals nothing about whether the role exists.

---

## 37. Role `412` behaviour

Stale version → `412`. A well-formed tag this platform never issued is also
`412`, not `400`: the header is syntactically valid and simply does not match,
and `412` is the only answer that describes an action which recovers.

**A permission replacement moves the version.** Without care, replacing a set
would write child rows and leave `tenant_roles` untouched — no `UPDATE`, no
concurrency predicate, and two administrators would both succeed with one
silently overwritten. The repository marks `UpdatedAt` modified explicitly so the
guarantee does not depend on two clock readings differing.

---

## 38. Role `409` behaviour

Two distinct conflicts, deliberately not merged with staleness:

- **Protected Owner mutation** → `409`, `DomainStateException`. Never `500`, and
  never silently ignored.
- **Role name already taken** → `409`, `TenantRoleNameAlreadyExistsException`.

**A stale version against Owner is reported as `412` first.** The caller's
precondition genuinely failed; answering the protection rule first would describe
a role they have never seen and would confirm to somebody holding an out-of-date
version exactly which role they had hold of.

---

## 39. Role name uniqueness and race

Database-authoritative: `ux_tenant_roles_tenant_name` on `(tenant_id,
normalized_name)`. The handler's `NameExistsAsync` pre-check is a courtesy that
two concurrent creations both pass; the index decides, and the loser's
`DbUpdateException` is translated into `TenantRoleNameAlreadyExistsException` →
`409`.

Tests assert the message leaks no constraint name, SQLSTATE, `duplicate key`,
`INSERT`, `Npgsql` or `pg_` text.

---

## 40. Permission replacement atomicity

`TenantRole.ReplacePermissions` materialises and validates the **whole** set
before removing a single grant, so a request naming one unrecognised code leaves
the role exactly as it was — rather than emptying it and then throwing, which
would be the most disruptive possible partial state.

At the database, delete-and-insert happen in one `SaveChangesAsync`.

---

## 41. Owner provisioning tests

- `NewWorkspaceTests` (11 cases): all four rows present; all name the same
  tenant; membership and assignment name the same person; the assignment names
  the role that was created; the role is a protected Owner granting everything;
  one instant across all four.
- `TenantRoleHandlerTests` and `CreateTenantHandlerTests` at the Application
  level.
- `TenantRoleRepositoryTests` at the PostgreSQL level — **written, not executed**
  (§55).

---

## 42. PostgreSQL constraint tests

`TenantRoleSchemaTests` (17 executed cases) and `TenantRoleRepositoryTests` (20).
**Both written and compiling; neither executed** (§55). Between them they cover:
custom role round-trip; role id is `uuid`; tenant ownership; name uniqueness;
case-variant name refused by the index; the same name free in another workspace;
one Owner per tenant; role-permission uniqueness; membership-role same-tenant
enforcement; cross-tenant assignment refused; assignment for a non-member
refused; Owner created and assigned during provisioning; role `xmin` concurrency;
version moves on permission replacement; atomic replacement; listing order; no
physical `xmin` column; enum stored by name; `timestamptz`.

---

## 43. Authorization matrix tests

`AuthorizationMatrixTests` (11 cases) — **written, not executed**. Four controlled
callers, all created through the real HTTP pipeline:

- **Owner** — every current capability succeeds; effective permissions equal the
  catalogue.
- **Limited** — granted capabilities succeed; ungranted return `403`.
- **No roles** — can read their own (empty) permissions; everything else `403`;
  still sees the workspace in `/api/me/tenants`.
- **Other tenant** — `404` everywhere, identical to an invented id.
- **Anonymous** — `401`.

Restricted members are arranged by writing rows through `RoleApiFixture`, which
is test infrastructure. **No production endpoint was added to make testing
easier** (§76).

---

## 44. Permission escalation tests

`PermissionEscalationApiTests` (8 cases) — **written, not executed** — plus
Application-level coverage in `AiAgentPermissionTests` and
`TenantRoleHandlerTests`, **which did execute**.

The central case: a member with `roles.view` + `roles.manage` creates a role
granting every AI agent capability, then receives `403` from every AI agent
endpoint, and their own effective permissions are byte-identical before and
after. Managing roles is not holding what they can grant, because holding
requires an assignment and no surface creates one.

`Holding_one_agent_permission_never_implies_another` (executed) states it as a
whole-catalogue property: a member granted exactly one capability holds exactly
that one, for every code in the catalogue.

---

## 45. Provider-not-called test

`A_member_without_preview_never_reaches_the_generator` and
`A_non_member_never_reaches_the_generator_either` — **both executed**, both
asserting `generator.Calls == 0`.

At the API level, `A_member_without_preview_is_refused_before_the_provider_is_reached`
asserts `403` rather than `503`: `503` would mean the request reached the runtime
and found none configured, and a member without the permission must not get that
far. **Written, not executed.**

---

## 46. Angular permission architecture

`PermissionService` holds the caller's codes for the currently selected workspace
**in memory only** — never `localStorage`, `sessionStorage` or a cookie. Loaded
when a workspace is resolved, selected or adopted; cleared on sign-out and on
workspace change. A failed load **grants nothing**.

`PermissionCode` is a typed union, so a typo is a compile error rather than a
silently hidden feature. `has()` / `hasAny()` are the only comparison points —
no string comparisons scattered through templates, and no policy framework.

---

## 47. AI agent permission UX

- Without `ai-agents.view`: a permission-denied state, and **no request is made**.
- Without `ai-agents.create`: "Create agent" hidden in the header and the empty
  state; the create page explains itself if reached by URL.
- Without `ai-agents.edit`: fields become read-only with one explanation for the
  whole editor, rather than a note beside each control — somebody with view
  access came to read them.
- Without `ai-agents.lifecycle`: no lifecycle control at all, plus a sentence
  saying why. A disabled primary button with no reason reads as a fault.
- Without `ai-agents.preview`: the panel is replaced by an explanation. A text box
  that refuses to send is more confusing than no text box.

`ETag` behaviour for people who **can** edit is untouched; the existing agent
concurrency specs still pass.

---

## 48. Roles page

Owner and custom roles in one list. Owner is marked `Protected` and reports
"All N permissions" — shown rather than hidden, because somebody looking for "who
can do everything here" must find it and must see at a glance that it is not
theirs to edit. "Create role" appears only with both `roles.view` and
`roles.manage`; otherwise a line says the reader can see roles but not change
them.

---

## 49. Create Role UX

Name plus permission checkboxes **grouped by catalogue group**, each with a label
and a one-sentence explanation. Codes are not shown to operators — a test asserts
`ai-agents.edit` does not appear on screen while "Edit AI agents" does. A live
count of selected permissions. One request; no precondition.

---

## 50. Edit Role UX

Name and permission set are **separate saves**, each with its own `If-Match`,
because they fail differently. Dirty-state tracking compares the ticked set
against the saved one by content, so ticking and unticking leaves the form clean.
Explicit save buttons, disabled until something changes. Loading, validation and
failure states throughout.

---

## 51. Owner read-only UX

Owner detail is **read-only prose plus a plain list** of what it grants — not a
form of disabled checkboxes. A test asserts no `input[type=checkbox]` is rendered
at all. It states: *"Owner always has every permission this platform defines,
including any added in future releases. It cannot be renamed and its permissions
cannot be changed. To give somebody a narrower set of capabilities, create a
custom role."*

---

## 52. Role conflict UX

**`412`:** a banner — *"This role was updated elsewhere."* The typed name and the
ticked boxes are **left exactly as they are**; the only action is a deliberate
"Reload latest version" that says it will replace them. Tests assert the ticked
state survives, that **no automatic retry** is issued, and that reloading only
happens when asked.

**`409`:** handled as its own experience, not as staleness. A test asserts a
protected-role refusal does **not** render the stale banner.

---

## 53. Browser workflow

**NOT PERFORMED.** The local backend cannot start: Smart App Control blocks
`OmniAI.Api.dll` (§55). Without a running API there is no workflow to drive, and
fabricating one would be worse than reporting the gap.

The twelve steps the brief lists remain unverified in a browser. Every one of
them has automated coverage — role list, Owner protection, Owner's full
permission set, create, rename, permission edit, `ETag`/`If-Match`, `412`, `409`,
and Owner's continued access to AI agents — but automated coverage is not the
same claim and I am not presenting it as one.

---

## 54. Browser `ETag` verification

**NOT PERFORMED**, for the same reason. `If-Match` on the wire is asserted in
`RoleConcurrencyApiTests` and in the `RoleEditor` spec (which reads
`request.headers.get('If-Match')`), but not observed in a browser's Network
panel.

---

## 55. Limited-user browser verification — and the blocker in full

**Superseded — see §87–§99.** Written before the post-review verification run; what is described here as blocked or unperformed has since been executed on a Linux host.

**NOT PERFORMED.**

**What happened.** Partway through the phase, every attempt to load
`OmniAI.Infrastructure.dll`, `OmniAI.Api.dll` or the two integration-test
assemblies began failing with:

```
System.IO.FileLoadException: Could not load file or assembly '...OmniAI.Api.dll'.
An Application Control policy has blocked this file. (0x800711C7)
```

**Diagnosis.** `HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy` →
`VerifiedAndReputablePolicyState = 1`, i.e. Smart App Control **On (enforced)**.
The `Microsoft-Windows-CodeIntegrity/Operational` log confirms it: event 3077,
*"did not meet the Enterprise signing level requirements or violated code
integrity policy"*, alongside event 3118 *"Smart App Control Block Details"*.

**What I tried, without changing security policy.**

1. Retried repeatedly — a background watcher probed every two minutes for over
   forty minutes; blocked on all 19 attempts.
2. Rebuilt, and clean-rebuilt after deleting `bin` and `obj`.
3. Tried the Debug configuration — also blocked, on `OmniAI.Infrastructure.dll`.
4. Tested whether the migration's embedded SQL was the trigger by temporarily
   stripping it — still blocked, so it is not the content I added.
5. Checked for a Mark-of-the-Web zone stream on the blocked files — none, so it
   is not a provenance flag I could legitimately clear.

**What I did not do.** I did not disable Smart App Control, did not switch it to
evaluation mode, did not add an exclusion, and did not change any Windows
security setting. The brief forbids it and I am not going to work around a
machine's security policy to make a test suite green.

**What this means.** `OmniAI.Domain.Tests` and `OmniAI.Application.Tests` are
unaffected and ran normally. The two integration suites and the local API cannot
run here. They compile with zero warnings and are ready to execute in an
environment where unsigned local builds are permitted — CI, or this machine with
the policy in whatever state its owner chooses.

---

## 56. Responsive verification

**NOT PERFORMED in a browser**, for the same reason. The Roles & permissions
screens were written against the accepted Phase 10 design system and follow the
patterns already verified at 390 / 768 / 1440 px in Phase 11:

- the role list uses the agent list's one-markup/two-layout approach — stacked
  cards below `md`, an aligned grid above, so no horizontal overflow;
- permission groups are `display: grid` with `min-inline-size: 0`, and long
  labels use `overflow-wrap: anywhere`;
- colours are design tokens only, so light and dark follow the existing
  `data-theme` switch with no new declarations.

None of that is a substitute for looking, and it is not reported as one.

---

## 57. Accessibility verification

**Superseded — see §87–§99.** Written before the post-review verification run; what is described here as blocked or unperformed has since been executed on a Linux host.

Built in, not verified in a browser:

- one `<h1>` per page via `app-page-header`;
- `<fieldset>` + `<legend>` for each permission group, so a screen reader
  announces the group before each checkbox;
- every checkbox inside its `<label>`, giving a large target and an accessible
  name;
- `role="alert"` on failures and the conflict banner; `aria-live="polite"` on the
  selected-permission count and the unsaved-changes note;
- `aria-busy` plus a visually hidden `role="status"` on loading states;
- read-only fields use the `readonly` attribute rather than `disabled`, so they
  stay focusable and readable.

No axe or Lighthouse run was possible without a running application.

---

## 58. Backend tests before

**Superseded — see §87–§99.** Written before the post-review verification run; what is described here as blocked or unperformed has since been executed on a Linux host.

**1117** — Domain 291, Application 252, Infrastructure 252, Api 322.

---

## 59. Backend tests after

**Superseded — see §87–§99.** Written before the post-review verification run; what is described here as blocked or unperformed has since been executed on a Linux host.

| Suite | Before | After | Status |
| --- | --- | --- | --- |
| Domain | 291 | **387** | ✅ executed, all passing |
| Application | 252 | **319** | ✅ executed, all passing |
| Infrastructure | 252 | 252 + 37 written | ⛔ blocked (§55) |
| Api | 322 | 322 + 61 written | ⛔ blocked (§55) |

**706 executed, 0 failing.** 98 new integration cases written and compiling but
not executed: `TenantRoleSchemaTests` 17, `TenantRoleRepositoryTests` 20,
`AuthorizationMatrixTests` 11, `RoleEndpointsTests` 19, `RoleConcurrencyApiTests`
15, `PermissionEscalationApiTests` 8, `AiAgentPermissionApiTests` 8.

Projected total once they run: **1215**.

---

## 60. Frontend tests before

231, across 17 files.

---

## 61. Frontend tests after

**261, across 21 files. All passing.**

New: `permission-service.spec.ts` (8), `role-list.spec.ts` (6),
`role-editor.spec.ts` (10), `role-create.spec.ts` (6).

---

## 62. Backend build

`dotnet build OmniAI.slnx -c Release` — **0 errors, 0 warnings**, eight projects.
`TreatWarningsAsErrors` is on and no `NoWarn` was added anywhere.

---

## 63. Frontend build

`npm run build` — succeeds. Initial bundle **381.06 kB raw / 92.39 kB transfer**.

---

## 64. Vulnerability scan

`dotnet list package --vulnerable --include-transitive` — all eight projects
report no vulnerable packages.

---

## 65. `npm audit`

**0 vulnerabilities.** `npm audit fix` was not run.

---

## 66. `npm ci`

**Succeeded**, from a clean `node_modules`-deleted state, on the first attempt.
This is a change from Phase 12, where it failed four times with Windows `EPERM`
file locks — the environmental warning review accepted then did not recur.

`package.json` and `package-lock.json` are both **unmodified**. The subsequent
`npm run build` produced a byte-identical bundle (381.06 kB / 92.39 kB) and all
261 tests passed afterwards. The lockfile was not touched.

---

## 67. Bundle before and after

| | Phase 12 | Phase 13 | Δ |
| --- | --- | --- | --- |
| Initial raw | 379.52 kB | **381.06 kB** | +1.54 kB |
| Initial transfer | 92.04 kB | **92.39 kB** | +0.35 kB |

The growth is the permission service, the typed codes and the navigation filter —
the only Phase 13 code that must be present before the first route renders.

The role feature is **lazily loaded**, as three chunks nobody downloads unless
they open it: `role-editor` 11.51 kB, `role-create` 7.58 kB, `role-list` 6.34 kB.

---

## 68. Packages added

**None**, backend or frontend. `Directory.Packages.props` is unmodified and so
are both npm manifests. Nothing in this phase needed an authorization library:
the decision logic is a union over a small set, and a policy framework would have
been more machinery than the thing it replaced.

---

## 69. Domain modified

**YES.** New `Authorization` namespace (nine types) and `NewWorkspace` in
`Tenants`. No existing Domain type changed behaviour.

## 70. Application modified

**YES.** New `Authorization` namespace: the authorizer, decision enum, two
exceptions, the repository contract, two read models, seven use cases.
`ITenantRepository.AddAsync` re-shaped. Nine existing handlers now require
permissions. `Versioned<T>` extracted.

## 71. Infrastructure modified

**YES.** Three entity configurations, `TenantRoleRepository`, `TenantRoles`
helper, three converters, two `DbSet`s, one schema constant, `TenantRepository`
provisioning, one registration, one migration.

## 72. API modified

**YES.** `RoleEndpoints`, `RoleContracts`, the effective-permissions endpoint,
three new Problem Details arms, composition-root registrations, route mapping,
`EntityTags` moved to `Api/Http`. A stale Phase 10 comment claiming the AI agent
endpoints were unauthenticated was replaced — it was already wrong after Phase 12
and would have been read as current.

## 73. Frontend modified

**YES.** `core/authorization` (four files), `core/api/versioned.ts`, the roles
feature (ten files), permission wiring in the workspace service, navigation
gating, AI agent screen gating, `403` copy, test doubles.

---

## 74. Files created and modified

**156 files:** 85 created, 68 modified, 2 deleted, 1 renamed.

Deleted: `VersionedAiAgent.cs`, `VersionedAiAgentView.cs` — replaced by
`Versioned<T>`.
Renamed: `Api/AiAgents/EntityTags.cs` → `Api/Http/EntityTags.cs`.

By type: 108 `.cs`, 36 `.ts`, 7 `.html`, 3 `.scss`, 2 `.md`.

---

## 75. Architectural decisions

Recorded in full in `docs/architecture/tenant-rbac.md` (D1–D26). The ones most
worth review:

1. **Owner permissions are intrinsic, never rows** (D6) — the only design where a
   capability added later cannot leave an Owner behind.
2. **Cross-tenant assignment is unrepresentable** (D12) — two composite foreign
   keys reading the same `tenant_id` column, not an application check.
3. **No permission implication** (D10) — evaluated and rejected; union is the only
   composition rule, so what a role confers is readable off the role.
4. **`NewWorkspace` rather than four repository parameters** (D13) — a mismatched
   set cannot be constructed, which is why the old pairing check could be deleted.
5. **Authorisation before resource loading** (D18) — the disclosure rule.
6. **Managing roles ≠ holding what they grant** (D22) — no assignment surface
   exists, so role management is not an escalation path.
7. **Concurrency extraction, minimally** (D21) — `Versioned<T>` and `EntityTags`
   generalised; per-slice preconditions and exceptions deliberately not.
8. **Normalisation in .NET, not `upper()`** (D11) — one definition of when two
   role names are the same.

---

## 76. Security review

- **No production authorization backdoor.** No `/api/dev/*`, no `/api/test/*`, no
  grant endpoint. Restricted members are arranged by writing rows through
  `RoleApiFixture`, which lives in the test project.
- **No global Identity role usage.**
- **No browser token storage.** Permissions are in memory; the session is still an
  `HttpOnly` cookie.
- **CSRF unchanged.** Every role mutation is behind the accepted antiforgery
  filter; a test asserts a mutation without the token is refused. `GET`s are not
  affected.
- **Cookies and forwarded headers untouched.** No JWT, no token storage, no
  reverse-proxy change.
- **OpenAI untouched.** `OpenAiTextGenerator`, model selection, retry behaviour,
  `store=false` and key handling are all unmodified. No live OpenAI call was made
  and no key was configured.
- **No historical migration edited.**
- **Escalation surface reviewed** — enumerated in D23.
- **No secrets committed** (§83).

---

## 77. Deferred concerns

Member invitations; member administration; assigning and revoking roles from the
UI; ownership transfer; role deletion and orphan-assignment semantics; deny
permissions; role hierarchy; permission implication; permission caching and any
Redis authorization cache; service accounts and API tokens; global platform-admin
authorization; audit and security-event pipeline; localisation of catalogue
labels.

**One deferred requirement worth naming again:** when member administration
arrives, editing a role the current user holds must refresh their in-memory
effective permissions. Not reachable through current workflows, and no
speculative polling was built.

---

## 78. Deviations

1. **The Settings placeholder was replaced, not nested into.** The brief said to
   replace the appropriate Settings placeholder; I removed the `Settings`
   navigation entry and put `Roles & permissions` at `/roles` in its place, rather
   than creating a Settings hub whose only content is one link. Documented in D24.
2. **Authorisation runs before validation in the role-create use case**, which
   inverts the order the AI agent slice uses. Deliberate and explained in §34: the
   permission catalogue is not public, and validating first would leak it.
3. **`GetPermissionCatalogHandler` takes no repository.** It exists only to place
   an authorisation check in front of a compile-time constant. Stated in its own
   documentation rather than left to be noticed.

---

## 79. Known issues

**Superseded — see §87–§99.** Written before the post-review verification run; what is described here as blocked or unperformed has since been executed on a Linux host.

1. **The PostgreSQL and API integration suites have not been executed** (§55).
   They compile; they are unproven. Everything they assert about database
   constraints, HTTP statuses and the escalation matrix should be treated as
   designed-and-tested-but-unrun until an environment executes them.
2. **The browser workflow has not been performed** (§53–§56), including the
   responsive and accessibility passes.
3. `MigrationSchemaTests`' updated migration count assertion (3 → 4) has not been
   re-run.
4. The catalogue's labels and descriptions are English strings in the Domain.
   Fine today, and a localisation seam that will need cutting.

---

## 80. Blockers

**Superseded — see §87–§99.** Written before the post-review verification run; what is described here as blocked or unperformed has since been executed on a Linux host.

**One, and it is environmental rather than a defect in this phase.**

Windows Smart App Control, enforced on this machine, blocks the freshly built
`OmniAI.Infrastructure.dll`, `OmniAI.Api.dll` and both integration-test
assemblies. It prevents running the two integration suites and starting the local
API, and therefore prevents the browser workflow.

I did not disable it, weaken it or work around it. Evidence, five diagnostic
attempts, and the exact scope of what is unverified are in §55.

**To clear it:** run `dotnet test OmniAI.slnx -c Release` and the browser workflow
in an environment where unsigned local builds may load — a CI runner, or this
machine with whatever policy state its owner decides. Nothing in the code needs to
change for that to work.

---

## 81. `git status --short`

156 changed paths: 85 created, 68 modified, 2 deleted, 1 renamed. No untracked
build artefacts, no `node_modules`, no `dist`, no `bin`, no `obj`, no IDE files.

---

## 82. `git diff --stat`

Tracked files: **71 changed, 1587 insertions, 772 deletions**. The deletions are
mostly the `VersionedAiAgent` → `Versioned<T>` migration and the permission
rewiring of nine handlers. `git diff --check` reports no whitespace errors.

---

## 83. Secret review

Scanned all 156 changed files for: OpenAI and Anthropic key shapes, Meta, Slack,
GitHub and AWS token shapes, private key headers, `CfDJ8` cookie/antiforgery
values, the production VPS address, `sshpass`, local PostgreSQL paths
(`pgtest`, `pw.txt`, `postmaster`), machine paths, database passwords,
connection strings, and personal identifiers.

**Nothing found.** The change set is 108 `.cs`, 36 `.ts`, 7 `.html`, 3 `.scss`
and 2 `.md` files — no binaries, no build output, no environment files.

---

## 84. VPS not accessed

**Confirmed.** No SSH, no network call to `186.240.145.196`, no Nginx
configuration read or written, no VPS credential used, referenced or stored. The
address does not appear anywhere in the change set.

---

## 85. Deployment not performed

**Confirmed.** Nothing was deployed. The only processes started were a local
PostgreSQL cluster on port 5433 (my own throwaway instance at `C:\pgtest`, chosen
because another service already held 5432 and must not be disturbed) and local
`dotnet` and `npm` commands.

**No commit was created.** No push, no remote added or modified.

---

## 86. Readiness recommendation for Phase 14

**Superseded — see §87–§99.** Written before the post-review verification run; what is described here as blocked or unperformed has since been executed on a Linux host.

**Conditional.** The RBAC foundation is complete and coherent, and the parts that
could be executed here are green. Before Phase 14 is authorised I would want the
98 unrun integration cases and the browser workflow executed somewhere the
security policy permits it — they are the tests that prove the database
constraints and the `401`/`404`/`403` contract, which is the part of this phase
most worth having proven rather than argued.

If that run is clean, the natural next phase is **member administration and
invitations**: it is the one thing this phase deliberately stopped short of, it
is what makes roles usable by anybody other than a workspace's creator, and D12's
composite keys and D22's escalation property were both designed with it in mind.

---

# Post-review verification (Docker Linux host)

Sections 87–99 record the verification run performed after the Phase 13 review,
in a Linux environment obtained without altering this machine's security policy.
Where they contradict an earlier section, **these supersede it**: sections 55,
57, 58, 59, 79, 80 and 86 were written while the integration suites could not be
executed, and their "not performed" statements are now historical.

## 87. The original Windows blocker

Unchanged in cause and unchanged in response. Smart App Control
(`VerifiedAndReputablePolicyState = 1`) blocks loading the locally built
`OmniAI.Infrastructure.dll`, `OmniAI.Api.dll` and both integration-test
assemblies with `0x800711C7`. Smart App Control was **not** disabled, not put in
evaluation mode, not given an exclusion, and no Windows security policy was
changed. The verification environment was changed instead.

## 88. WSL discovery

`wsl --status` and `wsl -l -v` were run read-only. Exactly one distribution
exists:

```
Default Distribution: docker-desktop
Default Version: 2

  NAME             STATE     VERSION
* docker-desktop   Running   2
```

`docker-desktop` is Docker Desktop's own utility VM, not a general-purpose
distribution — it carries no .NET, no Node and no PostgreSQL, and it is not a
supported place to install them. **No WSL distribution was installed, no Windows
optional feature was enabled, and the machine was not rebooted.**

## 89. Verification environment selected

The Docker Desktop Linux engine already present on the machine (Docker Desktop
4.71.0, engine 29.4.1, `linux/amd64`). Containers are Linux, so Smart App Control
— a Windows code-integrity policy — does not apply to them, and using them
required installing nothing on the host.

| Component | What was used |
| --- | --- |
| Build/test host | `mcr.microsoft.com/dotnet/sdk:10.0`, SDK **10.0.400** |
| Database | `postgres:17` → **PostgreSQL 17.11** |
| Network | private Docker network `omniai-verify`; the database publishes **no** host port |
| Source | copied into a container-local volume, `.git` included, excluding `node_modules`, `bin`, `obj`, `.angular`, `dist` |

**No Windows build artefact was reused**: the container built from a clean copy
into its own `bin`/`obj`, and nothing was written back into the working tree.

The SDK is feature band 10.0.400 against 10.0.203 on Windows. There is no
`global.json` pinning the SDK, so this is permitted by the repository's own
configuration; the build is warning-clean on both.

**Two false starts, recorded because they touched the tree.** `--artifacts-path`
was tried first so that output would land outside the repository. Git Bash's MSYS
path translation rewrote `/artifacts` into a Windows path, and the restore
created a literal `C:` directory inside `backend/`. It was removed immediately
and `git status` confirmed nothing was tracked; the approach was then abandoned
in favour of the container-local copy, which cannot touch the working tree at
all.

## 90. Backend build and dependency scan

```
dotnet restore OmniAI.slnx            → 8 projects restored
dotnet build   OmniAI.slnx -c Release → Build succeeded. 0 Warning(s) 0 Error(s)
dotnet list package --vulnerable --include-transitive
                                      → no vulnerable packages, all 8 projects
```

`TreatWarningsAsErrors` is on and no `NoWarn` was added, so zero warnings is
zero warnings.

## 91. Actual backend test totals

Every test in the solution executed against real PostgreSQL. Counts are read from
the TRX `<Counters>` element, not from a summary line.

| Assembly | Total | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| `OmniAI.Domain.Tests` | 387 | 387 | 0 | 0 |
| `OmniAI.Application.Tests` | 319 | 319 | 0 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | **289** | **289** | 0 | 0 |
| `OmniAI.Api.IntegrationTests` | **383** | **383** | 0 | 0 |
| **Total** | **1378** | **1378** | **0** | **0** |

The 98 cases that Phase 13 could only compile all executed and passed:
`TenantRoleSchemaTests` 17, `TenantRoleRepositoryTests` 20,
`AuthorizationMatrixTests` 11, `RoleEndpointsTests` 19,
`RoleConcurrencyApiTests` 15, `PermissionEscalationApiTests` 8,
`AiAgentPermissionApiTests` 8.

## 92. Migration and schema, on a clean database

`20260906101730_AddTenantRolesAndPermissions` was applied to a disposable
database and the result inspected with `psql`. No accepted migration was edited.

```
tenant_roles
  PK  (id)
  UNIQUE CONSTRAINT ak_tenant_roles_id_tenant  (id, tenant_id)
  UNIQUE           ux_tenant_roles_owner       (tenant_id) WHERE kind = 'Owner'
  UNIQUE           ux_tenant_roles_tenant_name (tenant_id, normalized_name)
  FK               fk_tenant_roles_tenant → tenants(id) ON DELETE RESTRICT

role_permissions
  PK  (tenant_role_id, permission)          -- duplicates structurally impossible
  FK  fk_role_permissions_role → tenant_roles(id) ON DELETE CASCADE

tenant_role_assignments
  PK  (user_id, tenant_id, tenant_role_id)
  IX  ix_tenant_role_assignments_role (tenant_role_id, tenant_id)
  FK  fk_tenant_role_assignments_membership (user_id, tenant_id)
        → tenant_memberships(user_id, tenant_id) ON DELETE RESTRICT
  FK  fk_tenant_role_assignments_role       (tenant_role_id, tenant_id)
        → tenant_roles(id, tenant_id)       ON DELETE RESTRICT
```

Both assignment foreign keys read the **same** `tenant_id` column, so a row
pairing one tenant's membership with another tenant's role cannot exist. There is
**no physical `xmin` column** on any table — confirmed by inspection and by
`Tenant_roles_has_no_physical_xmin_column`.

**Backfill, proven on Phase 12-era data.** A second database was migrated only as
far as `20260905235628_AddIdentityAndTenantMemberships`, seeded with 2 users, 2
tenants and 3 memberships, and then migrated forward:

| Check | Result |
| --- | --- |
| Owner roles created | 2 — exactly one per existing tenant |
| Owner role ids | UUID version nibble `7` on both |
| Memberships before / assignments after | 3 / 3 — every existing member kept full access |
| `role_permissions` rows for Owner | 0 — Owner derives from the catalogue, so it cannot fall behind |

No existing member was locked out, which was the migration's whole obligation.

## 93. Authorization matrix — executed

| Caller | Result |
| --- | --- |
| Anonymous | **401** on roles, agents and own-permissions |
| Non-member (real other workspace) | **404** |
| Tenant that never existed | **404**, same shape as the above |
| Member lacking the permission | **403**, naming only the required permission |
| Member holding it | **200** |
| Member with no roles at all | can still read their own (empty) permissions; refused everything else |
| Membership alone | enough to see the workspace in the switcher |

Confirmed twice over: by `AuthorizationMatrixTests` (11 cases) and independently
in Chrome against the running API (§96).

Each AI-agent permission was proven not to imply any other —
`ai-agents.view`, `.create`, `.edit`, `.lifecycle`, `.preview` each grant their
own operation and are refused the neighbouring ones. Without `ai-agents.preview`
the request is refused **403 with `IAiTextGenerator` call count 0**, and a
non-member gets **404** with the same zero call count.

## 94. Permission escalation — executed
**Superseded — see §100–§110.** The security review found that this section's central claim was wrong: a role can be assigned to the person editing it, so `roles.manage` was an escalation path. Fixed and re-proven.

All 8 cases passed, including: `roles.view` alone cannot create, rename or
re-permission a role; `roles.manage` does **not** hand the editor the permissions
they grant; editing a role does not alter the editor's own assignments; a forged
permission code is refused rather than stored; a role id from another workspace is
not an alternate modification path; role mutations still require the antiforgery
token.

## 95. Role concurrency and ETag — executed

All 15 cases passed, and the same contract was watched live in Chrome:

```
GET  /roles/{id}                            → 200, ETag: "AAAfag"
PUT  /roles/{id}/name         If-Match "AAAfag" → 200, ETag: "AAAfaw"
PUT  /roles/{id}/permissions  If-Match "AAAfag" (now stale) → 412
```

Missing `If-Match` → **428**; malformed (`*`, weak, list) → **400**; protected
Owner mutation with a current version → **409**; protected Owner mutation with a
*stale* version → **412 first**. Exactly one request was sent per attempt: **no
automatic retry**.

## 96. Browser workflow — performed

Windows Chrome → Angular dev server on `localhost:4200` → `/api` proxy →
containerised API on `127.0.0.1:5027` → isolated PostgreSQL reachable only on the
private Docker network. No VPS, nothing exposed publicly, and `OPENAI_API_KEY` was
never set.

Owner path, all steps: registered; created workspace *Acme Trading*; opened Roles
and permissions; **Owner present, badged "Protected", "All 8 permissions"**; Owner
detail is genuinely read-only — an explanation of why, and a plain grouped list,
**not** a form of disabled checkboxes; created a custom role from grouped
checkboxes showing a label and a sentence each and **no permission codes**;
renamed it; changed its permission set; observed the ETag advance; produced a
real two-tab **412** whose banner reads *"This role was updated elsewhere."* with
the typed name and ticked boxes **still on screen** and a deliberate *"Reload
latest version"*; confirmed Owner offers no mutation control at all; returned to
AI Agents and confirmed the Owner still creates, edits, activates and previews.

A duplicate name differing only in case (`support SPECIALIST`) produced a **409**
that talks about names — *"Role names must be unique within a workspace, ignoring
capitalisation"* — and never about stale versions, constraint names, SQLSTATE or
PostgreSQL.

**Limited user — performed, without any backdoor.** A second account was
registered through the ordinary UI and then made a restricted member by inserting
a membership and a role assignment **directly into the disposable test database**
with `psql`. No product endpoint, no dev/test endpoint and no code of any kind was
added to make this possible. Holding only `ai-agents.view`, that user: sees no
Roles entry in the sidebar; can list and read agents; gets read-only fields, a
disabled save with a reason, and prose instead of the lifecycle and preview
controls; and on `/roles` gets the permission-denied state — which says what to do
about it and never shows "403", a permission code, or anything implying a broken
account. The denied page issues **no** roles request at all.

Direct HTTP from that session, bypassing the UI entirely:

| Request | Status |
| --- | --- |
| `GET /api/me/tenants/{acme}/permissions` | 200 `["ai-agents.view"]` |
| `GET /api/tenants/{acme}/roles` | 403 |
| `GET /api/tenants/{acme}/permissions` | 403 |
| `GET /api/tenants/{acme}/ai-agents` | 200 |
| `POST /api/tenants/{acme}/ai-agents` | 403 |
| `POST /api/tenants/{acme}/roles` | 403 |
| `GET /api/tenants/{globex}/ai-agents` | 404 |
| `POST /api/tenants/{globex}/roles` | 404 |

## 97. Responsive, theme, console and keyboard

No horizontal overflow at **390 / 768 / 1440 / 1920**, measured as
`scrollWidth > clientWidth` on the role list and the role editor, with zero
elements extending past the viewport. At 390 the sidebar becomes a hamburger and
the role table becomes stacked cards; permission groups stay grouped and legible.
Checked in **Light** and **Dark**; the palette resolves in both and the disabled
save is legibly disabled rather than merely dim.

Console across both contexts: three messages, all of them the Vite dev-server
handshake and Angular's development-mode notice. **No errors, no warnings.**
Network: same-origin `/api` only, permissions fetched once per session load, no
duplicates, no loops, and **no request to any OpenAI host**.

Keyboard: focus order follows the DOM, no negative `tabindex`, every input
labelled (0 unlabelled), and pressing Space on a permission checkbox toggles it
and enables the save.

## 98. Real defects found and fixed

**1 — `RoleEditor` name save never enabled (frontend, real, user-visible).**
`nameDirty` was a `computed()` reading `this.name.value`. A `FormControl`'s value
is not a signal, so the computed had no reactive dependency on it and never
recomputed while somebody typed: under zoneless change detection the *"Save name"*
button stayed disabled forever, and a custom role could not be renamed through the
UI at all. Found by clicking it in Chrome.

Fixed by mirroring the control into a signal from `valueChanges` — the idiom
`AgentEditor` already uses — and a regression test was added that asserts through
the **button's disabled state**. That is the assertion that was missing: the
existing specs submitted the `<form>` directly, and a submit event fires whatever
the button's state, so a permanently disabled control was invisible to them.

**2 — `A_denied_member_is_refused_identically_for_a_real_and_an_invented_agent`
compared per-request trace ids (test defect, not production).** The test asserted
byte equality of two `ProblemDetails` bodies including `traceId`, which differs
between any two requests by design. Production behaviour was correct — both
refusals are `403` and identical in every field that could disclose whether the
agent exists. The test now compares every field *except* `traceId` and asserts
that both bodies still carry one, following the convention `AiAgentPreviewApiTests`
already established.

No production code was weakened to make the environment pass, and nothing was
changed merely because the build moved to Linux.

## 99. Frontend after the fix, and remaining blockers

```
npm run test:ci      → 21 files, 262 passed (261 + the new regression)
npm run build        → initial 381.06 kB raw / 92.40 kB transfer (unchanged)
                       role-list 6.34 kB, role-create 7.58 kB, role-editor 11.60 kB
                       — the roles feature stays lazy
npm run format:check → clean
npm audit            → 0 vulnerabilities
```

**Remaining blockers: none for Phase 13's verification.** Every required suite
executed and passed against real PostgreSQL, and the browser workflow was
completed including the limited-user path.

Two standing environment notes, neither a defect in this work: Smart App Control
still prevents running the API and the integration suites **natively on Windows**,
so this project needs a Linux runner — the Docker path used here, or CI — for its
integration tests; and the verification containers, database and dev server were
all shut down afterwards, so re-running requires re-provisioning them.

---

# Post-review security remediation — the delegation ceiling

Sections 100–110 record the second review round. It found a real
privilege-escalation defect, and this is what it was, how it was proven, and what
changed. Where these contradict §94 or D22 as originally written, **these
supersede them**.

## 100. The finding

`roles.manage` was effectively equivalent to every permission in the catalogue.

Phase 13 argued that role management could not be an escalation path because
holding a role requires an assignment, and no endpoint creates one outside
workspace provisioning — so an administrator could compose a role granting
everything and still be unable to do any of it. §94 and the original D22 both
rested on that argument.

It is true of a role the actor does **not** hold. It is false of one they do. A
role can be assigned to the person editing it, and a permission added there lands
on them the instant the row is written, with no assignment changing anywhere and
nothing to notice. `Editing_a_role_does_not_change_the_editors_own_permissions`
passed throughout, and was answering a different question: assignment rows had
indeed not moved, while effective authority had.

## 101. Current behaviour, proven before any code changed

A probe was written against the unmodified implementation and executed on the
Linux verification host. Bob holds one custom role, "Role manager", granting only
`roles.manage`, and that role is assigned to him:

```
PROBE before=[roles.manage]
      outcome=SUCCEEDED
      after=[ai-agents.edit,roles.manage]
      canEditAgents=True
```

`PUT /api/tenants/{t}/roles/{his own role}/permissions` with
`["roles.manage","ai-agents.edit"]` succeeded, and Bob could then edit AI agents.
A real privilege escalation, reachable by any holder of `roles.manage`.

The regression suite written for this was run first in the same state: **8 of its
11 cases failed**, every one of them reporting "No exception was thrown" — the
escalations all succeeding. The probe was deleted once recorded; the suite stays.

## 102. The rule now enforced

```
ActorCeiling            = union of effective permissions of every role the actor holds

CREATE ROLE             requires  DesiredPermissions      ⊆ ActorCeiling
UPDATE ROLE PERMISSIONS requires  CurrentRolePermissions  ⊆ ActorCeiling
                          and     DesiredPermissions      ⊆ ActorCeiling
RENAME ROLE             requires  CurrentRolePermissions  ⊆ ActorCeiling
```

`TenantAuthorizer.RequireDelegatableAsync`, in the Application layer beside every
other authorisation decision. Full reasoning in `docs/architecture/tenant-rbac.md`
D22.

**No Owner branch.** An Owner's effective permissions are the whole catalogue, so
the subset rule admits everything they can ask for and keeps doing so when a
capability is added later. An `if (owner) bypass` would be a second way to be
authorised, able to drift from the first.

**The union, not the role carrying `roles.manage`.** An actor whose administrative
and editing permissions live in different roles may delegate both, because they
hold both.

**The current set as well as the desired one.** A role that outranks the actor is
not theirs to administer even to *narrow*: being able to rewrite it is how it gets
taken over. Rename is bounded by the same rule — retitling a powerful role is how
it stops being the one anybody watches.

**`400` and `403` stay distinct.** An unrecognised code is malformed; a recognised
one outside the ceiling is a refusal. Validation runs first, so an invented code
is never reported as a refusal — which would both misdescribe the problem and let
somebody map the catalogue by watching which answer came back.

**Order of decisions**, unchanged where it overlaps the accepted D21 contract:
authentication → membership → `roles.manage` → lookup → precondition → ceiling →
Domain rules → persistence. The precondition stays ahead of the ceiling, so a
caller working from a version somebody else replaced is told that rather than
being judged against permissions they were never shown.

## 103. Files changed

| File | Change |
| --- | --- |
| `Domain/Authorization/PlatformPermissions.cs` | `RequireRecognised` — one definition of "not a capability", shared by the aggregate and the use cases |
| `Domain/Authorization/TenantRole.cs` | `SetPermissions` calls it instead of carrying its own copy |
| `Application/Authorization/TenantAuthorizer.cs` | `RequireDelegatableAsync` — the ceiling |
| `Application/.../CreateTenantRoleHandler.cs` | validate, then ceiling; class comment corrected |
| `Application/.../ReplaceTenantRolePermissionsHandler.cs` | ceiling over the stored **and** requested sets, after the precondition |
| `Application/.../RenameTenantRoleHandler.cs` | ceiling over the stored set |
| `frontend/.../permission-service.ts` | `canDelegate`, `canDelegateAll` |
| `frontend/.../role-create.ts,.html` | checkboxes narrowed to the composer's authority, and it says so |
| `frontend/.../role-editor.ts,.html` | same, plus a role that outranks the reader is a read-only record; Owner readout uses the unfiltered catalogue |

No migration. No schema change. No new package. No new endpoint. **No member
assignment surface was added.**

## 104. Self-assigned-role escalation

The mandatory regression. Bob holds `roles.view` + `roles.manage` and nothing
else; the role is his own. Proven at Application level, over HTTP, and in Chrome.

| Step | Result |
| --- | --- |
| Bob's effective permissions before | `["roles.view","roles.manage"]` |
| `GET` his role | `200`, `ETag: "AAASbg"` — a current version, so the refusal is not a concurrency accident |
| `PUT .../permissions` adding `ai-agents.edit` | **`403`**, detail names `ai-agents.edit` |
| Bob's effective permissions after | `["roles.view","roles.manage"]` — unchanged |
| The role's permissions after | `["roles.manage","roles.view"]` — unchanged |
| `GET .../ai-agents` | `403` |
| `PUT .../ai-agents/{id}/name` | `403` |

The refusal names the capability he lacks, not `roles.manage`, which he has.

## 105. Create-then-escalate

A `roles.view` + `roles.manage` holder creating a role granting
`ai-agents.lifecycle` (or the whole product set) receives **`403`**. No role row
and no grant rows are written — asserted through the store's own record of what
was added, and by re-listing the workspace's roles.

## 106. Higher-authority roles

Owner composes "Senior" granting `ai-agents.edit` + `ai-agents.lifecycle`. Bob
holds `roles.view`, `roles.manage`, `ai-agents.view`.

| Attempt | Result |
| --- | --- |
| Rename "Senior" | `403` |
| Replace its permissions — even *narrowing* it | `403` |
| Its name and permissions afterwards | unchanged |

## 107. Valid delegation, the union, and Owner

The rule is a ceiling, not a prohibition:

- Bob holding `ai-agents.view` + `ai-agents.preview` creates a role granting both,
  then narrows it to `ai-agents.view`, then renames it. All succeed.
- A role granting nothing is within everyone's authority — the empty set is a
  subset of every set.
- **Union:** an actor whose `roles.manage` and `ai-agents.edit` come from two
  different roles may compose a role granting both.
- **Owner:** creates a role carrying all 8 catalogue permissions and edits it
  afterwards. Verified both in tests and in the browser (`201`, 8 permissions
  granted). No Owner protection regressed: rename and re-permission of Owner are
  still `409`, and a stale version against Owner is still `412` first.

## 108. Concurrency preserved

For an otherwise-authorised actor: missing `If-Match` → **`428`**, stale
`If-Match` → **`412`**, both still answered ahead of the ceiling. Cross-tenant
role ids remain **`404`**, so the ceiling never becomes a way to learn that
somebody else's role exists. No automatic retry anywhere.

## 109. Browser verification

Same Docker/Linux stack, Chrome, isolated database, no VPS, no OpenAI key. Bob
was made a Role Manager by inserting rows into the disposable test database —
**no product or test endpoint was added**.

- His role editor offers **only** "View roles" and "Manage roles". There is no AI
  agent checkbox to tick, and the screen says: *"Only capabilities your own roles
  include are listed. You cannot grant something you cannot do yourself."*
- Opening "Senior" shows **"You cannot change this role"** with the explanation,
  no form, no save controls — and still lists what it grants, including the
  capabilities that put it out of his reach.
- Direct requests bypassing the UI: self-escalation `403`, rename of a higher
  role `403`, re-permission `403`, create-beyond-ceiling `403`, editing an agent
  `403`. Every target verified unchanged afterwards.
- Owner, in the same browser session, still composes a role with the full
  catalogue.
- Console carried only the Vite and Angular development notices plus Chrome's own
  logging of the `403`s the probe deliberately provoked.

## 110. Totals after remediation

| Suite | Total | Passed | Failed |
| --- | ---: | ---: | ---: |
| `OmniAI.Domain.Tests` | 387 | 387 | 0 |
| `OmniAI.Application.Tests` | 330 | 330 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 289 | 289 | 0 |
| `OmniAI.Api.IntegrationTests` | 393 | 393 | 0 |
| **Backend total** | **1399** | **1399** | **0** |
| Frontend (Vitest, 21 files) | 271 | 271 | 0 |

Backend up 21 (11 Application delegation cases, 10 API); frontend up 9.

**Two existing tests were rewritten rather than deleted**, because they asserted
the property that turned out to be false:

- `Managing_roles_does_not_give_the_editor_the_permissions_they_grant` asserted
  the over-powered role was *created*. It now asserts the creation is refused
  `403`, nothing is stored, and the manager still cannot act.
- `Editing_a_role_does_not_change_the_editors_own_permissions` widened a role to a
  capability the editor lacked and expected success. The editor now also holds
  `ai-agents.create`, so the in-ceiling creation still succeeds; widening beyond
  the ceiling is refused, and both the role and the editor's authority are
  asserted unchanged.

Nine frontend specs that ticked AI agent permissions now grant the reader those
capabilities, because a composer cannot be offered what they do not hold. That is
the rule working, not a workaround.

`dotnet build -c Release`: 0 warnings, 0 errors. `dotnet list package
--vulnerable --include-transitive`: clean. `npm run build`: 381.16 kB raw /
92.43 kB transfer, roles feature still lazy (`role-editor` 13.18 kB,
`role-create` 7.95 kB, `role-list` 6.34 kB). `format:check` clean. `npm audit`:
0 vulnerabilities.
