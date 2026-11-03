# Tenant roles and permissions

How OmniAI decides what a member of a workspace may do inside it.

Phase 12 answered one question — *does this person belong to this workspace* —
and treated the answer as total: a membership row meant full access. That was
deliberately temporary. This document records the model that replaced it, the
decisions behind it, and what was left out on purpose.

```
Authenticated user
        |
        v
TenantMembership          "may this person reach this workspace at all"
        |
        v
TenantRoleAssignment(s)   "which of its roles do they hold"
        |
        v
TenantRole                "what does each role permit"
        |
        v
Effective permissions     union, in catalogue order
        |
        v
Authorised use case
```

---

## D1. Why not ASP.NET Core Identity roles

Identity's role system is global to the installation. A user is in a role, or is
not, everywhere. That is the wrong ownership model for this product, and not
marginally so: one authenticated person may legitimately be

- the Owner of workspace A,
- a support agent with three permissions in workspace B,
- and a member of workspace C with nothing granted at all.

`AspNetRoles` and `AspNetUserRoles` cannot express that without a tenant column
bolted onto framework tables this platform does not own — at which point they are
this model with extra steps and a `RoleManager` that no longer means what its
documentation says.

So the split is: **ASP.NET Core Identity owns authentication credentials**
(password hashes, security stamps, lockout) and nothing else. The OmniAI Domain
owns tenant authorisation. `IdentityUserContext<OmniAiUser, Guid>` remains the
base context, which is why there are still no role tables in the schema — and an
executable guard asserts the Domain references no Identity assembly at all.

---

## D2. The aggregate: `TenantRole`

| Field | Notes |
| --- | --- |
| `Id` | `TenantRoleId`, UUIDv7, its own type |
| `TenantId` | Immutable. No setter, no operation, no factory that changes it |
| `Name` | `TenantRoleName`, carrying its own normalised form |
| `Kind` | `Owner` or `Custom`, stored by name |
| `CreatedAt` / `UpdatedAt` | From the caller's injected clock |
| grants | `RolePermissionGrant` rows, for custom roles only |

`TenantId` immutability is enforced three ways: no setter, no instance method
taking a `TenantId`, and — at the database — an assignment whose composite
foreign keys make a role from one tenant unusable in another (D12).

---

## D3. `TenantRoleId`

A `readonly record struct` over a UUIDv7, following `TenantId`, `AiAgentId` and
`UserId`. The substitution it prevents is not hypothetical: a role id sits beside
a tenant id in a route, both are GUIDs, and with bare `Guid` parameters a
transposition would compile. The pair is what scopes a role to its workspace, so
a swap is exactly the shape a cross-tenant modification would take.

---

## D4. `TenantRoleName`

Required, trimmed, whitespace-only rejected, at most **80 characters**, no
control characters, never silently truncated.

It carries **two** values: `Value`, the display form as typed, and `Normalised`,
the upper-invariant form. Case is preserved because it is what a person reads;
only the comparison is case-insensitive.

**Uniqueness is per tenant, on the normalised form, and is enforced by
PostgreSQL** — `ux_tenant_roles_tenant_name` on `(tenant_id, normalized_name)`.
Never global: two businesses may each have a `Support agent`, and a name one
takes must not affect the other.

`ToUpperInvariant`, not `ToUpper`: a name normalised under one culture and
compared under another would let a duplicate through.

---

## D5. The protected Owner role

Exactly one per tenant. It is created with the workspace, in the same
transaction, and it is protected in four independent ways:

1. **Cannot be created by a caller.** `CreateRoleRequest` has no kind field and
   `TenantRole.CreateCustom` cannot produce a system role. A second Owner is not
   refused — it is unrepresentable through the API.
2. **Cannot be renamed.** `Rename` throws `DomainStateException` → `409`.
3. **Cannot have its permissions replaced.** Same.
4. **Cannot be deleted.** No delete exists anywhere in this phase, so this is
   structural rather than a rule somebody could move.

The database holds the count as well: `ux_tenant_roles_owner` is a **partial
unique index** on `tenant_id WHERE kind = 'Owner'`. A workspace may have any
number of custom roles; a second Owner is refused outright. "Nothing currently
creates two" is a property of today's code; this is a property of the data.

The application guarantees *at least* one, in provisioning. A database cannot
express "at least one" as an index, and expressing it as a trigger would put a
business rule where nobody reviewing this codebase would look.

---

## D6. Owner permission semantics — the design that cannot fall behind

**An Owner role stores no permission rows at all.** Asked what it permits, it
answers with `PlatformPermissions.AllCodes` — the catalogue, read at the moment
the question is asked.

```csharp
public IReadOnlyList<PermissionCode> EffectivePermissions =>
    IsSystem ? PlatformPermissions.AllCodes : /* stored grants */;
```

This is the answer to the failure mode the brief named: a capability added in a
future release silently leaving every existing Owner without it. There is no row
to backfill, no migration to remember and no tenant that can be missed — adding
an entry to `PlatformPermissions.All` extends every Owner in every workspace in
the same edit, because the list *is* what Owner means.

The alternative — one mutable `role_permissions` row per permission for every
Owner in every tenant — was rejected precisely because it makes correctness
depend on remembering. It also makes "Owner" mean whatever those rows currently
say, which is not what the word means.

A test asserts `owner.EffectivePermissions == PlatformPermissions.AllCodes`
against the catalogue rather than a hard-coded list, so a capability added later
is covered by that test the moment it is added.

---

## D7. Custom roles

Belong to one tenant; editable name; explicit permission set; **may grant
nothing**; never a system role.

A role granting nothing is a legitimate thing to create on the way to deciding
what it should do. Refusing it would only invite somebody to grant a placeholder
permission they then forget to remove.

Deliberately absent: description, colour, icon, hierarchy, parent role,
priority, deny permissions. None has a requirement behind it, and each would be a
field nothing fills.

---

## D8. `PermissionCode`

A `readonly record struct` over a canonicalised string.

Grammar: two or more dot-separated segments; lowercase letters, digits and
interior hyphens; at most 64 characters; no leading, trailing or adjacent
separators. Trimmed and lowercased on construction — `Roles.Manage` and
`roles.manage` must never become two different grants.

**A string, not an integer.** An integer permission id would make the meaning of
stored data depend on a declaration order that no database row records:
renumbering or inserting a member would silently reinterpret every grant already
written. A code is self-describing — a row saying `ai-agents.edit` means the same
thing in five years, in a backup, and in a log line.

**Independent of HTTP.** No code is derived from a route, a method, an endpoint
name or a handler type, and nothing is discovered by reflection. Renaming a URL
must not change who may do what.

**Shape is validated by the type; recognition is not.** Whether the platform has
such a capability is `PlatformPermissions.IsRecognised`. Merging the two would
make it impossible to read a code out of the database once a capability was
retired.

---

## D9. The platform permission catalogue

Compile-time, in `OmniAI.Domain.Authorization.PlatformPermissions`. Eight
capabilities, in three groups:

| Group | Code | What it allows |
| --- | --- | --- |
| Workspace | `workspace.view` | See the workspace's name and status |
| AI agents | `ai-agents.view` | See the agents and read their instructions |
| AI agents | `ai-agents.create` | Add a new agent |
| AI agents | `ai-agents.edit` | Change an agent's name and instructions |
| AI agents | `ai-agents.lifecycle` | Activate and disable |
| AI agents | `ai-agents.preview` | Try an agent out — the only code that reaches a paid third party |
| Roles | `roles.view` | See the roles and the catalogue |
| Roles | `roles.manage` | Create and edit custom roles |

**Not discovered.** No reflection scans handlers, no attribute is read, no route
table is walked. A discovered catalogue would change silently when a class was
renamed, which means the set of things a role can grant would change without
anybody deciding to change it.

**Only capabilities that exist.** No `conversations.*`, `knowledge.*`,
`channels.*`, `billing.*`, `integrations.*` or `analytics.*`. Listing them would
let an administrator grant authority over nothing and then wonder why it had no
effect — and would commit the platform to whatever shape the guess took. A test
asserts their absence.

**No `tenant.admin`.** A catch-all would make every fine-grained code beside it
decorative. Full authority is the Owner role, which is protected and cannot be
assembled from checkboxes. A test asserts its absence too.

### Metadata

Each entry carries `Group`, `Label` and `Description` alongside the code, and the
API publishes all four. Authorisation reads **only the code**; nothing in the
decision path touches the metadata, which is what makes the wording safe to
change.

The text lives in the Domain because the capability does — "what can be done in
this product" is a business statement, and a parallel table of strings is one
somebody forgets to update. The cost is that these strings are English;
localisation is a deferred concern (D27).

---

## D10. No implication between permissions

`roles.manage` does **not** confer `roles.view`. `ai-agents.edit` does not confer
`ai-agents.view`. **Union of the member's roles is the only composition rule in
this platform.**

This was evaluated, not defaulted. An implication graph means the permissions a
role actually confers can no longer be read off the role — a reviewer would have
to evaluate a closure in their head — and it is exactly the machinery that
becomes load-bearing before anybody has agreed what it means.

The consequence is visible and intended: the role editor needs both `roles.view`
(to load the catalogue it composes from) and `roles.manage` (to write), and the
admin UI asks for both explicitly. The role list offers "Create role" only when
both are held, because offering it otherwise would be a dead end.

---

## D11. Persistence

Three tables, added by one migration (D14).

### `tenant_roles`

`id` (PK), `tenant_id`, `name`, `normalized_name`, `kind`, `created_at`,
`updated_at`. Concurrency via PostgreSQL's `xmin` system column — no physical
column is created, matching the two accepted migrations before it.

`kind` is stored **by name**, like every other enum here: an integer column would
make the stored meaning depend on declaration order, and here that would mean
reclassifying protected system roles as editable ones.

`normalized_name` is a **shadow property** the repository sets from
`TenantRoleName.Normalised` on every write. A value converter fills one column
and this name needs two.

> A PostgreSQL generated column — `GENERATED ALWAYS AS (upper(name)) STORED` —
> would need no application code, and was rejected: `upper()` is
> collation-dependent, so for a non-ASCII name the database's normalised form and
> `ToUpperInvariant`'s could differ. This platform would then have two
> definitions of when two role names are the same, and the pre-check and the
> index would disagree in exactly the cases nobody tests. One definition, in the
> Domain, is worth one assignment in the repository — and an integration test
> asserts a case-variant name is actually refused by the index.

### `role_permissions`

Primary key `(tenant_role_id, permission)`. **That composite key is where "a role
cannot grant the same permission twice" lives** — structural, rather than a
unique constraint beside a surrogate key that a later migration could drop
without anything obviously breaking.

`ON DELETE CASCADE` — uniquely in this schema, and right here: a grant has no
meaning without its role and is not business content anybody would want to
recover.

**No foreign key to a permissions table, because there is no permissions table.**
The catalogue is compiled in. A tenant-writable one would let a workspace invent
authority over capabilities that do not exist. `TenantRole` checks every code
against the catalogue before it becomes a row, and drops any that a later release
retires.

### `tenant_role_assignments`

`user_id`, `tenant_id`, `tenant_role_id`, `created_at`. All three ids are the
primary key.

---

## D12. Cross-tenant assignment is unrepresentable

This is the part worth reading twice.

The assignment row carries `tenant_id` even though the user and the role would
each imply one. That redundancy is the mechanism:

```sql
FOREIGN KEY (user_id, tenant_id)
    REFERENCES tenant_memberships (user_id, tenant_id)      -- fk_..._membership

FOREIGN KEY (tenant_role_id, tenant_id)
    REFERENCES tenant_roles (id, tenant_id)                 -- fk_..._role
```

Both read **the same `tenant_id` column of this row**. PostgreSQL therefore
accepts the row only when the membership and the role agree about which workspace
they are in. A row pairing a membership in tenant A with a role from tenant B is
not rejected by application code — it cannot be written at all.

The second key requires a unique constraint on the columns it references, which
is why `tenant_roles` carries `ak_tenant_roles_id_tenant` — `UNIQUE (id,
tenant_id)`. On its own that looks redundant beside the primary key. It is not
there to prevent duplicates; it is there so this foreign key can exist.

Both are `RESTRICT`. Integration tests write both invalid shapes directly and
assert PostgreSQL refuses them.

---

## D13. Provisioning a workspace

Phase 12 wrote two rows atomically. Phase 13 writes **four**:

```
Tenant  +  TenantMembership  +  Owner TenantRole  +  TenantRoleAssignment
```

All four or none. A workspace missing any one is broken in a way no endpoint can
repair:

- no membership → the creator cannot reach the workspace at all;
- no Owner role → no full authority in it, and no way to make one, because no
  caller can mint a system role;
- no assignment → the creator is a member who may do nothing, in a workspace
  where nobody can grant them anything.

`NewWorkspace.Create(name, slug, creator, now)` is the only way to obtain the
four, and it derives the last three from the first two. Four loose repository
parameters would have compiled just as happily with a membership for one person
and an assignment for another; here **a mismatched set cannot be constructed**,
which is why the run-time pairing check Phase 12 needed is gone.

One clock reading for all four, so they carry the instant of the single act.

`ITenantRepository.AddAsync(NewWorkspace)` commits them in one explicit
transaction. On a slug conflict it rolls back and detaches all four, leaving the
unit of work exactly as it was.

### Still no `IUnitOfWork`

Re-examined this phase, because four rows is the shape that usually argues for
one. It does not here: the count is not what matters. An `IUnitOfWork` earns its
cost when *many* use cases must compose commits the repositories cannot see, and
there is still exactly one. Introducing it would mean reworking every accepted
repository so none of them committed, to serve a single call site.

---

## D14. Migration and backfill

**`20260906101730_AddTenantRolesAndPermissions`** — one migration, no accepted
migration modified.

It creates the three tables, the two unique indexes, the alternate key, the
foreign-key index, and then backfills:

```sql
-- One Owner per existing tenant.
INSERT INTO tenant_roles (id, tenant_id, name, normalized_name, kind, created_at, updated_at)
SELECT <uuidv7>, tenant.id, 'Owner', 'OWNER', 'Owner', now(), now()
FROM tenants AS tenant;

-- Every existing member holds it.
INSERT INTO tenant_role_assignments (user_id, tenant_id, tenant_role_id, created_at)
SELECT membership.user_id, membership.tenant_id, owner.id, now()
FROM tenant_memberships AS membership
JOIN tenant_roles AS owner
  ON owner.tenant_id = membership.tenant_id AND owner.kind = 'Owner';
```

**This preserves the accepted Phase 12 meaning of the data exactly.** Under Phase
12 a membership row granted full access with no gradation; under Phase 13 full
access is what Owner means. "Every existing member becomes an Owner" is not a
generous default — it is the same authority those rows already carried, written
down in the new model.

Doing nothing was the alternative, and it would have locked every existing member
out of every existing workspace: a member with no assignments may do nothing, and
no endpoint grants an assignment. There is no production database yet, so in
practice this touches development data only — but a migration that silently
revoked everyone's access would be wrong whether or not anybody was there to
notice.

Both statements are set-based and run inside the migration's own transaction.

### UUIDv7 in SQL

PostgreSQL 17 has no `uuidv7()`. Rather than fall back to `gen_random_uuid()`'s
version 4 and break the schema-wide convention, the value is assembled: the
random UUID supplies the entropy and the correct variant bits, the first 48 bits
are overwritten with the current Unix time in milliseconds, and the version
nibble is set to 7. A backfilled row is indistinguishable in shape from one the
application would have written, and the time-ordered insert locality the indexes
rely on is preserved.

---

## D15. The Application authorisation boundary

`TenantAuthorizer`, in `OmniAI.Application.Authorization`. A plain class, not an
interface — matching `TenantAccessGuard`, and for a sharper reason: nothing
substitutes it, and an interface would make it possible to register a permissive
implementation by accident. For this type that is a platform-wide authorisation
bypass in one line.

It knows nothing about HTTP (`HttpContext`, `ClaimsPrincipal`, ASP.NET policies,
status codes) and nothing about data access (EF Core, Npgsql, PostgreSQL).
Executable guards assert the Application assembly references neither.

Two layers, asked in order: **membership first**, through the accepted
`TenantAccessGuard`; **permissions second**, from the roles the member holds. The
order is the security property — a non-member never reaches a permission check,
so nothing about a workspace's roles or contents is consulted on their behalf.

### Nothing is cached

No permission set is held between requests, baked into the session cookie, or
memoised in a static. A cached decision would keep granting authority after a
role changed — turning "we removed that permission" into "we removed that
permission, eventually". One indexed read per authorised request; correctness
first. Caching, if it is ever measured to be needed, arrives with explicit
invalidation semantics (D26).

---

## D16. Three outcomes, never two

```csharp
public enum TenantAccessDecision { NotAMember, PermissionDenied, Granted }
```

`AuthorizeAsync` returns all three. `RequireAsync` is a thin wrapper that returns
`false` for `NotAMember` (each use case answers with its own ordinary not-found)
and **throws** `PermissionDeniedException` for `PermissionDenied`.

The two failures leave by different doors on purpose: absence is an outcome every
caller already handles, and answering a stranger any other way would tell them
which workspaces exist; lacking a permission is not absence, and the caller is
entitled to know they need something they have not got.

Not authenticated is not a fourth member. Nobody has said who they are, so there
is nothing to decide — it throws, exactly as it did before roles existed.

---

## D17. Status mapping

| Situation | Status | Why |
| --- | --- | --- |
| No session | `401` | The caller has not said who they are; signing in may change the answer |
| Signed in, not a member | `404` | Identical to a workspace that never existed — the route cannot be probed |
| Member, permission missing | `403` | They have proved they belong; telling them their own workspace vanished would be a lie |
| Member, permitted | continue | |

**This is the one refusal in the platform that admits the resource exists**, and
it is safe precisely because the caller already proved membership. What it buys
is a person who can act — they can see they need a permission and ask for it,
instead of staring at a resource that appears to have disappeared.

Getting the pair the wrong way round fails in one direction or the other: `403`
to a stranger confirms somebody else's workspace is real; `404` to a member says
their workspace is gone.

The `403` detail names the permission code and nothing else — no role name, no
member list, no tenant name, no hint about which record was addressed. The code
is already public: it is in the catalogue every role editor reads.

---

## D18. Resource-existence protection

**Authorisation runs before the resource is loaded, in every tenant-scoped use
case.** A member without `ai-agents.view` is refused identically whether the id
they named is real, belongs to another workspace, or was invented — because
nothing is looked up before the refusal.

Checking the permission *after* the lookup would answer `403` for a record that
exists and `404` for one that does not, turning a permission somebody lacks into
a way to enumerate the ids they cannot see. An API test asserts the two responses
are byte-identical, not merely the same status.

For non-members the tenant-hiding `404` from Phase 12 is unchanged.

---

## D19. Permission mapping for the existing use cases

| Use case | Requires |
| --- | --- |
| `GetTenantById` | `workspace.view` |
| `ListMyTenants` | membership only — see below |
| `CreateTenant` | authentication only (not inside a tenant) |
| `ListAiAgents`, `GetAiAgentById` | `ai-agents.view` |
| `CreateAiAgent` | `ai-agents.create` |
| `RenameAiAgent`, `UpdateAiAgentInstructions` | `ai-agents.edit` |
| `ActivateAiAgent`, `DisableAiAgent` | `ai-agents.lifecycle` |
| `PreviewAiAgent` | `ai-agents.preview` |
| `ListTenantRoles`, `GetTenantRoleById`, `GetPermissionCatalog` | `roles.view` |
| `CreateTenantRole`, `RenameTenantRole`, `ReplaceTenantRolePermissions` | `roles.manage` |
| `GetMyTenantPermissions` | membership only — see below |

`ai-agents.lifecycle` is separate from `ai-agents.edit` because the two are
different risks: editing instructions changes what an agent would say;
activating one decides whether it says anything to customers at all.

`ai-agents.preview` is separate because it is the one capability that reaches a
paid third party. A workspace that wants to bound provider spend needs to be able
to withhold exactly this without also withholding the ability to read an agent.

### `GET /api/me/tenants` requires membership only

Discovering that you belong to a workspace must not require `workspace.view`.
Somebody may belong to a tenant while holding no product permissions at all, and
they still need to select it and be told why it is empty. The workspace switcher
would otherwise hide workspaces the person genuinely belongs to.

### `GET /api/me/tenants/{id}/permissions` requires membership only

Requiring a permission would be circular — a member with nothing granted could
not be told they have nothing granted — and every fact in the answer is about the
caller.

---

## D20. The role API

```
GET    /api/tenants/{tenantId}/permissions                 roles.view
GET    /api/tenants/{tenantId}/roles                       roles.view
POST   /api/tenants/{tenantId}/roles                       roles.manage
GET    /api/tenants/{tenantId}/roles/{roleId}              roles.view
PUT    /api/tenants/{tenantId}/roles/{roleId}/name         roles.manage, If-Match
PUT    /api/tenants/{tenantId}/roles/{roleId}/permissions  roles.manage, If-Match
GET    /api/me/tenants/{tenantId}/permissions              membership
```

**No `DELETE`.** Role deletion has a real unanswered question behind it — what
happens to the members holding the role — and answering it in passing would be
worse than leaving it.

**No member-assignment endpoint.** Half of member administration, with a grant and
no revoke and no audit, is a liability rather than a head start.

Two mutation endpoints rather than one, because the two edits fail differently: a
name can collide with another role's and a permission set cannot. Merging them
would mean somebody fixing a duplicate name had to resend a permission set they
were not changing.

### Create contract

Accepts `name` and `permissions` only. There is no id, tenant id, kind, system
flag, timestamp or version field — **not validated away, simply absent**. A
caller cannot ask for an Owner because there is nowhere to ask.

- Unknown permission code → `400`.
- Malformed permission code → `400`, the same answer, so the catalogue cannot be
  enumerated by probing.
- **Duplicate codes are deduplicated**, deterministically. Asking for the same
  capability twice expresses the same wish as asking once; refusing it would be
  pedantry a client has to write code to avoid. Documented, so a client can rely
  on it.
- Name already taken in this workspace (case-insensitive) → `409`.

Authorisation runs **before** the request is validated in this use case, unlike
the agent slice. The difference is deliberate: an agent's name rules are already
public, but the permission catalogue is published only to `roles.view`.
Validating a permission list first would let a member without `roles.manage` learn
which codes the platform recognises by watching which came back invalid.

### List contract

`id`, `name`, `type` (`System` / `Custom`), `isSystem`, `permissionCount`,
`createdAt`, `updatedAt`. A count, not the set: the permissions are on the detail
screen, one request away.

**For an Owner the count is the catalogue's size, not a row count.** Counting rows
would report zero — true of the table and false about the role.

Order: **Owner first, then custom roles by normalised name**, tie-broken by id.
Published as a contract so the list does not reshuffle between page loads.

### Detail contract

Adds `tenantId` and `permissions` — the **effective** set, so an Owner returns the
complete current catalogue rather than its empty stored set. No version field in
the body: the version is the `ETag`.

---

## D21. Role concurrency

`TenantRole` is the platform's **second** client-edited resource with
client-think-time concurrency, and a permission editor is exactly the screen
somebody leaves open while they think.

Same accepted mechanism as AI agents: PostgreSQL `xmin`, an opaque
`ConcurrencyToken`, a strong `ETag`, `If-Match` required on both mutations.

| Situation | Status |
| --- | --- |
| `If-Match` absent | `428` |
| `If-Match` malformed, `*`, weak, or a list | `400` |
| Well-formed tag this platform never issued | `412` |
| Stale version | `412` |
| Owner mutation, current version | `409` |
| Owner mutation, stale version | `412` — staleness first |

**Staleness is reported before the protected-role rule.** The caller's
precondition genuinely failed; answering the protection rule first would describe
a role they have never seen, and would confirm to somebody holding an
out-of-date version exactly which role they had hold of.

### The parent row is forced into the UPDATE

Replacing a permission set writes child rows. Without care, `tenant_roles` would
be untouched, EF would issue no `UPDATE` against it, there would be **no
concurrency predicate to fail**, and two administrators replacing permissions
inside the same window would both succeed with one silently overwritten. The
repository marks `UpdatedAt` modified explicitly, so the guarantee does not depend
on two clock readings differing.

### What was extracted, and what was not

The brief asked whether the focused concurrency infrastructure should now be
generalised. It was, minimally:

- `Versioned<T>` replaced the hand-written `VersionedAiAgent` and
  `VersionedAiAgentView`. One record, no constraint, no hierarchy.
- `EntityTags` moved from `Api/AiAgents/` to `Api/Http/`. Not one line of parsing
  changed — what was always generic behaviour stopped living under a name that
  implied otherwise.
- On the frontend, `Versioned<T>` and `withEntityTag` in `core/api/versioned.ts`
  replaced the AI agent client's private pair.

**Deliberately not extracted:** the per-slice precondition check and the
per-slice version-mismatch exception. Each carries the identity of the thing that
moved and explains it in that thing's own words. A shared exception would have to
describe an agent and a role in one sentence; a shared checker would need an
exception factory passed into it — more machinery than the two lines it saves.

No reflection version framework, no generic versioned entity hierarchy, no global
ETag middleware.

---

## D22. The delegation authority ceiling

**The invariant.** A non-Owner actor may create, rename or re-permission a role
only within the authority they themselves currently hold.

```
ActorCeiling            = union of the effective permissions of every role the actor holds

CREATE ROLE             requires  DesiredPermissions      ⊆ ActorCeiling
UPDATE ROLE PERMISSIONS requires  CurrentRolePermissions  ⊆ ActorCeiling
                          and     DesiredPermissions      ⊆ ActorCeiling
RENAME ROLE             requires  CurrentRolePermissions  ⊆ ActorCeiling
```

**What this replaces, and why.** Phase 13 originally argued that role management
could not be an escalation path because holding a role requires an assignment,
and no endpoint creates one outside workspace provisioning — so an administrator
could compose a role granting everything and still be unable to do any of it.

That argument is true of a role the actor does **not** hold, and false of one they
do. A role can be assigned to the person editing it. Adding a permission there
grants it to them the moment the row is written, with no assignment changing
anywhere and nothing to notice. The security review demonstrated it end to end:

```
Bob holds one custom role, "Role manager", granting roles.manage
  before  = [roles.manage]
  PUT /roles/{his own role}/permissions  ["roles.manage", "ai-agents.edit"]
  outcome = SUCCEEDED
  after   = [ai-agents.edit, roles.manage]
  Bob can now edit AI agents = true
```

`roles.manage` was therefore equivalent to every permission in the catalogue. It
is now what it says: permission to perform role administration, not permission to
mint capabilities.

**`roles.manage` is not superuser authority.** It says the actor may operate the
role machinery. What they may put *into* a role is bounded separately, by what
they can already do. The two questions are independent and are asked
independently.

**Owner needs no special case.** An Owner's effective permissions are the whole
catalogue (D6), so the subset rule admits everything they could ask for — and it
will keep doing so when a capability is added in a later release, because D6
derives Owner's set from the catalogue rather than storing rows. There is
deliberately **no** `if (owner) bypass` branch: that would be a second way to be
authorised, sitting beside the first and free to drift from it.

**The union, not the role that carried `roles.manage`.** Authority is the union
of every role the actor holds (D3). Somebody whose administrative permission and
whose editing permission live in two different roles may delegate both, because
they genuinely have both. Inspecting only the role containing `roles.manage`
would refuse a legitimate delegation.

**The current set matters, not only the desired one.** A role carrying authority
the actor does not hold is not theirs to administer at all — even to *narrow*.
Being able to rewrite a role that outranks you is how you take it over: strip it
to something harmless, or rename it to something unremarkable, and it stops being
the thing anybody is watching. So both the incoming set and the set already
stored are checked, and rename is checked against the stored set too.

**Unknown code and ungrantable code are different answers.** A code the platform
does not define is a malformed request; a code it does define that the actor may
not hand out is a refusal.

```
unrecognised PermissionCode      -> 400
recognised, outside the ceiling  -> 403
```

Validation therefore runs **before** the ceiling: asking the ceiling first would
report every invented code as a refusal, which misdescribes the problem and would
let somebody map the catalogue by watching which answer came back.

**Where it is enforced.** `TenantAuthorizer.RequireDelegatableAsync`, in the
Application layer, beside every other authorisation decision — not in an
endpoint, not in a controller filter, and not in Angular. The Application tests
prove it with no HTTP involved at all.

**Order of decisions**, chosen deliberately and unchanged from D21 where the two
overlap:

```
1. authentication                     -> 401
2. tenant membership                  -> 404   (cross-tenant stays absent)
3. base roles.manage permission       -> 403
4. resource lookup                    -> 404
5. precondition (If-Match)            -> 428 / 412
6. delegation ceiling                 -> 403
7. Domain rules (Owner protection)    -> 409
8. persistence
```

The precondition stays ahead of the ceiling because a caller working from a
version somebody else has already replaced is being judged against permissions
they were never shown; the stale answer is the accurate one, and D21's accepted
contract keeps `412` ahead of protected-role semantics for the same reason.

**Atomicity.** A refusal changes nothing. The ceiling is evaluated before the
aggregate is mutated, so a rejected replacement leaves the stored set exactly as
it was, and a rejected creation writes no role row and no grant rows.

**No cache.** The ceiling is read per request from the same store every other
authorisation question uses (D26), so a permission removed from an actor's role
narrows what they may delegate on their very next request.

---

## D23. Permission-escalation protections, enumerated

| Attempt | Outcome |
| --- | --- |
| Member with `roles.view` only creates a role | `403` |
| Member with `roles.view` only renames or re-permissions a role | `403` |
| **Manager adds a capability they lack to a role they hold** | **`403`** — the self-escalation D22 exists for; role and authority both unchanged |
| **Manager creates a role granting more than they hold** | **`403`**, no role row and no grant rows |
| **Manager renames or re-permissions a role that outranks them** | **`403`**, target untouched |
| Manager delegates within what they hold | Succeeds — the rule is a ceiling, not a prohibition |
| Manager whose two capabilities live in two roles delegates both | Succeeds — authority is the union |
| Forged code (`tenant.admin`, `roles.*`, `*`, `billing.manage`) | `400`, nothing stored |
| Valid + forged code together | `400`, **neither** stored — the set is validated whole |
| Role id from another workspace, through own route | `404` |
| Role id from another workspace, through their route | `404` (not a member) |
| Assignment pairing tenant A membership with tenant B role | Refused by PostgreSQL |
| Request body carrying `id`, `tenantId`, `kind`, `isSystem` | Ignored — no such fields |
| Any role mutation without the antiforgery token | `400` (Phase 12 CSRF unchanged) |

---

## D24. Frontend permission awareness

`PermissionService` holds the caller's effective codes for the **currently
selected workspace**, in memory only.

- Never written to `localStorage`, `sessionStorage` or a cookie. Permissions are
  not credentials, so there is nothing to protect — but a persisted copy would be
  a stale copy, and a client showing a button because of what was true last week
  is worse than one that asks.
- Loaded when a workspace is selected, adopted or resolved; cleared on sign-out
  and when the selection changes. Authority is per tenant, so a map of every
  workspace's permissions would be a cache with no invalidation story.
- **A failed load grants nothing.** The admin renders as though the person may do
  nothing rather than as though they may do everything — the only safe direction.
- `PermissionCode` is a typed union, so `has('ai-agents.edt')` is a compile error
  rather than a silently hidden feature.
- **The delegation ceiling is mirrored, not enforced, here.** `canDelegate` and
  `canDelegateAll` narrow the permission checkboxes to what the reader holds, and
  present a role that outranks them as a read-only record with an explanation.
  Both screens say plainly that the list was narrowed and why, so a shortened list
  does not read as a page that failed to load. An Owner holds the whole catalogue,
  so the same filter withholds nothing from them and needs no notion of what an
  Owner is. The Owner role's own detail is rendered from the **unfiltered**
  catalogue, because it describes what that role permits rather than what the
  reader may hand out.

**This is UX, not security.** Anybody can edit the JavaScript running in their own
browser. The service could be made to say anything without a byte of extra data
becoming reachable, because every request is authorised again on the server.

### What the UI does with it

- **Sidebar**: an entry is hidden when the person holds none of its permissions.
  Entries with none listed (the unbuilt areas) are shown to everyone.
- **AI agents list**: "Create agent" hidden without `ai-agents.create`; a
  permission-denied state without `ai-agents.view`, and no request is made.
- **AI agent editor**: read-only fields plus one explanation without
  `ai-agents.edit`; lifecycle controls absent without `ai-agents.lifecycle`;
  preview panel replaced by an explanation without `ai-agents.preview`.
- **Roles**: list, create and edit screens each explain a missing permission.
- **403 copy** never shows the status code, never names a permission code, and
  never implies the account is broken.

Hidden rather than disabled for primary actions: a disabled button with no reason
beside it reads as a fault rather than a rule. Read-only rather than hidden for
fields somebody with view access came to read.

### Settings → Roles & permissions

The Phase 10 `Settings` placeholder was **replaced** by a `Roles & permissions`
entry at `/roles`, not nested inside it. There are no other settings yet, so a
Settings hub would be a page with one link on it. When a second settings area
arrives, this becomes a child of one.

---

## D25. Role administration UX

**Roles page.** Owner and custom roles in one list. Owner is marked `Protected`
and reports "All N permissions" — shown rather than hidden, because somebody
looking for "who can do everything here" must be able to find it and must be able
to see at a glance that it is not theirs to edit.

**Create role.** Name plus grouped permission checkboxes, each with a label and a
one-sentence explanation. Codes are not shown to operators. One request, no
precondition — there is nothing to be out of date about yet.

**Edit role.** Name and permission set are **separate saves**, each with its own
`If-Match`, because they fail differently.

**Owner detail is read-only prose**, not a form of disabled checkboxes. Showing
somebody a control and then refusing it reads as a fault rather than a rule.

**`412` conflict UX.** A banner: *"This role was updated elsewhere."* The typed
name and the ticked boxes are **left exactly as they are**, and the only action is
a deliberate "Reload latest version" that says it will replace them. No automatic
retry and no merge — replaying an edit against state nobody has seen is the lost
update the precondition exists to prevent, and with permissions it would quietly
restore authority somebody deliberately removed.

**`409` is a different experience.** A protected-role refusal and a name conflict
are conflicts, not staleness, and telling somebody to refresh would be advice that
cannot work.

**No permission matrix.** Eight capabilities do not justify a spreadsheet, and the
thing a matrix is actually for — assigning roles to members — does not exist yet.

---

## D26. Permission changes take effect on the next check

There is no cache and no Redis, so a permission change is live the next time a
request is authorised.

The frontend's in-memory copy can become stale if a role the current user holds is
edited while they are on screen. Through current product workflows this is not
normally reachable: assignment editing is not exposed, and Owner permissions are
intrinsic rather than stored. A reload or a workspace switch re-resolves it.

**Deferred requirement, recorded rather than solved:** when member administration
arrives, editing a role that the current user holds must refresh their effective
permissions. No speculative polling was built.

---

## DECIDED NOW

- Tenant-scoped roles, owned by the OmniAI Domain
- ASP.NET Core Identity confined to authentication credentials
- `TenantRole` aggregate with immutable `TenantId`
- Protected Owner role, one per tenant, enforced by a partial unique index
- Owner permissions are intrinsic — the catalogue, never rows
- Custom roles with editable names and explicit grants, possibly empty
- `PermissionCode` value type, string-based, transport-independent
- Compile-time platform permission catalogue with safe UI metadata
- Relational role grants with a composite primary key
- Membership-role assignments with composite cross-tenant foreign keys
- Effective permissions as the union of assigned roles
- Owner role and assignment provisioned atomically with the workspace
- Existing memberships backfilled to Owner
- `TenantAuthorizer` as the Application authorisation boundary
- Membership and permission as separate decisions
- `401` / `404` / `403` distinctions
- Authorisation before resource loading
- Permission requirements on every existing tenant-scoped use case
- Five role endpoints plus two permission endpoints
- Role `ETag` concurrency, `428` / `412` / `409`
- Roles & permissions admin UI
- Frontend permission awareness as UX only
- Permission-escalation protections
- **The delegation authority ceiling: an actor may create, rename or
  re-permission a role only within their own current effective permissions —
  including the role they themselves hold**
- **`400` for an unrecognised permission code, `403` for a recognised one outside
  the actor's ceiling**

## CONTRACT FOR PHASE 14 — MEMBER ASSIGNMENT

Binding on whoever implements member invitation and role assignment.

**Assignment must reuse the same authority ceiling as D22.** A non-Owner actor
must never assign a role whose effective permissions exceed their own current
ceiling. Granting somebody else a capability you do not hold is the same
escalation as granting it to yourself, one step removed — and once assignment
exists, that step is available to anybody with `roles.manage`.

```
ASSIGN ROLE TO MEMBER requires  RolePermissions ⊆ ActorCeiling
```

Three further obligations follow from this phase's design:

1. **Owner assignment is a separate, protected ownership concern.** It must not
   fall out of ordinary member-role assignment. Handing somebody Owner is handing
   over the workspace, and it needs its own deliberate operation — plausibly with
   its own confirmation, and certainly not a checkbox in a list.
2. **Revocation is the mirror of assignment.** Removing a role from somebody must
   be bounded the same way, or a delegated administrator could strip authority
   from people who outrank them.
3. **The ceiling must be re-read, never passed in.** Assignment will be a longer
   interaction than a role edit, and an actor's authority may change during it.
   `TenantAuthorizer` deliberately caches nothing (D26); assignment must not be
   the feature that introduces the first cached permission set.

Deleting roles is still not implemented, so orphan-assignment semantics remain
undefined and must be decided before, not during, that work.

---

## DEFERRED

- Member invitations
- Member administration (add, remove, list members)
- Assigning and revoking roles from the UI
- Ownership transfer
- Role deletion, and orphan-assignment semantics
- Deny permissions
- Role hierarchy and inheritance
- Permission implication
- Permission caching, and any Redis authorisation cache
- Service accounts and API tokens
- Global platform-admin authorisation
- Audit and security-event pipeline
- Localisation of catalogue labels and descriptions

## Phase 37.5a: `workspace.manage`

A new capability in the Workspace group: **Manage workspace** — "Change the
workspace's settings, such as its name, which visitors see on its public chat."
It guards `PUT /api/tenants/{id}/name` (owner decision D-3) and, from Phase 37.6,
the workspace time zone.

- **The Owner holds it with no data step**, because the Owner's authority is
  `PlatformPermissions.AllCodes`, read when the question is asked.
- **It implies nothing and nothing implies it**, like every other code:
  `workspace.view` does not confer it, and it does not confer `workspace.view`.
- **A custom role can be granted it** through the role editor like any other code;
  the delegation rule applies (you can only give what you hold).
- The role editor words it by code in both languages
  (`frontend/src/app/features/roles/permission-words.ts`):
  «إدارة مساحة العمل» — «تغيير إعدادات مساحة العمل، مثل اسمها الذي يراه الزوار في الدردشة العامة.»

| Use case | Requires |
| --- | --- |
| `RenameTenant` | `workspace.manage` |
