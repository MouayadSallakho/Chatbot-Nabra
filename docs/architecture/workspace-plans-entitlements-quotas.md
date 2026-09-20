# Workspace plans, entitlements and quotas

What a workspace is allowed to hold and consume, and how that is enforced.

> **This is entitlement enforcement, not billing.** There is no payment provider,
> no checkout, no invoice, no subscription, no card, no VAT, no wallet, no credit,
> no plan price and no past-due handling. A plan says what may be used; it never
> says what it costs. The commercial machinery — and whatever authority is
> supposed to approve a plan change — is Phase 25's.

Phase 24. Builds on [tenant-rbac.md](tenant-rbac.md),
[team-members-invitations.md](team-members-invitations.md) and
[provider-usage-cost-accounting.md](provider-usage-cost-accounting.md).

---

## 1. A permission is not an entitlement

**Decision.** They are separate questions, asked in that order, and neither
answers the other. Plan limits are never modelled as permissions.

**Why.** `ai-agents.create` says a workspace has trusted this person to create
agents. It says nothing about whether the workspace may hold another one. If the
limit were a permission, granting somebody a role would raise a limit, and a
workspace at its cap could buy its way past it by editing a role — which is
exactly the thing a cap exists to prevent.

**Consequence.** Every enforced mutation runs:

```
authenticate → membership → permission → validate → entitlement → mutate
```

Somebody without the permission gets 403 **before** the plan is consulted, so a
refusal never leaks "5 of 5 agents" to a caller who cannot see the workspace.
Somebody with the permission gets 409 when the workspace is full.

---

## 2. A plan is platform configuration

**Decision.** `plans` has no `tenant_id`. Plans arrive with the migration and
change by deliberate deployment. There is no `POST /plans`, no `PUT /plans/{id}`,
no upgrade route, and `IPlanCatalog` has no write method.

**Why.** A workspace that could edit its own limits would not have limits. The
first write method would eventually acquire a caller, and that caller would be a
workspace raising its own cap.

**Immutability.** `Plan` has no mutator. Changing what "Starter" means underneath
the workspaces already on it would silently rewrite an agreement they never saw
change; a new plan is introduced and workspaces are moved instead. That is
revision-safe without building plan-version history, which belongs with Phase 25.

---

## 3. Unlimited is an absence, never a number

**Decision.** `PlanLimit` wraps `int?`. `null` means unlimited, in the object and
in the column. `-1`, `999999999` and `int.MaxValue` are forbidden. Finite limits
are `>= 0`, enforced by a check constraint per column.

**Why.** A sentinel must be understood identically by the database, every query
and every screen, and will be summed or averaged by the first report that forgets.
A percentage against `int.MaxValue` is always nought; a progress bar against it is
always empty; and somebody eventually prints the number.

**Consequence.** EF will not map a non-nullable struct to a nullable column, so
`Plan` holds private `int?` properties that persistence maps and exposes
`PlanLimit` to everything else. The database keeps the honest type and the domain
keeps the useful one.

**Zero is a real limit.** A plan with `max_whatsapp_connections = 0` is
immediately exhausted, and that is different from unlimited.

---

## 4. Exactly one plan per workspace

`workspace_plan_assignments` is keyed by `tenant_id`. Not a unique index over a
table keyed by something else, and no "is current" flag: with the tenant as the
key there is no second row to exclude, no ordering to get wrong, and no window in
which a workspace has two plans or none.

The tenant FK cascades (the row describes that workspace); the plan FK restricts
(a plan workspaces are on cannot be deleted out from under them).

---

## 5. Where a workspace's plan comes from

**New workspaces.** Onboarding resolves `Plans:DefaultPlanCode` and writes the
assignment in the **same transaction** as the tenant, membership, owner role and
role assignment. A workspace never exists, even momentarily, without a plan.

**It fails rather than guesses.** If the setting is missing, unusable, or names a
plan this deployment does not have, workspace creation stops with a 503. The
tempting fallback would be something permissive, and it is the one mistake nobody
notices: every production workspace created afterwards would silently have no
limits, and the first anybody would hear of it is a provider bill.
`appsettings.json` deliberately does not set it; `appsettings.Development.json`
names `development` explicitly.

**Existing workspaces.** The migration backfills every row in `tenants` onto a
`legacy` plan with no limits, stamping `assigned_at` from the workspace's earliest
membership. They were built under a platform that had no limits; imposing one
retrospectively would break working workspaces — including every development and
test database — for a commercial decision nobody has taken.

**Three seeded plans**: `legacy` (unlimited, for the backfill), `development`
(unlimited, for dev and test deployments), `starter` (finite, so a production
deployment has something honest to name and the limits are exercised by something
other than a test).

**A workspace with no plan is refused, not assumed unlimited.** It means a bug in
onboarding or backfill, and the convenient guess is the dangerous one.

---

## 6. What consumes each entitlement

| Entitlement | Counted as |
| --- | --- |
| `Members` | Rows in `tenant_memberships`. Pending invitations do **not** take a seat. |
| `AiAgents` | Every agent row, whatever its status. |
| `KnowledgeDocuments` | Every document row. Manual and file-backed count identically. |
| `WebChatEndpoints` | Every endpoint row, whatever its status. |
| `WhatsAppConnections` | Every connection row, whatever its status. |
| `MonthlyProviderOperations` | Phase 23 `ai_provider_usages` rows started in the current UTC month. |

**Counts are the resources themselves.** No counter column, no rollup table. A
cached counter is a second source of truth that drifts the first time a delete
path forgets it, and the drift is invisible until somebody is wrongly refused —
or wrongly allowed.

**There are no soft deletes in this schema**, which is why counting rows is
exactly right: disabling something does not free a slot, and only genuine deletion
does. **Replacing a document's file** edits the existing row and consumes nothing
further.

**A pending invitation is not a seat**, so several invitations can outlive one
free seat. They may all exist; exactly one acceptance gets in. Invitation
*creation* refuses when the workspace is already full — a courtesy, so an
administrator does not send something nobody can use — but **acceptance is the
authority**.

**Security bounds are not entitlements.** The 10 MB upload limit, parser bounds,
rate limits and session lifetimes protect the platform whatever a workspace pays,
and folding them in would let a commercial decision weaken a defence.

---

## 7. The monthly AI quota is operations, not tokens

**Decision.** `MaxMonthlyProviderOperations` counts Phase 23 ledger rows whose
`started_at` falls in the UTC calendar month. All four statuses count — `Started`,
`Succeeded`, `Failed`, `Unknown` — because each means a request reached the
paid-dispatch boundary or may have.

**Why not tokens.** Exact token counts are only known *after* the provider
answers. Enforcing a token cap before dispatch would require estimating usage,
and refusing to estimate usage is the foundation Phase 23 is built on.

**Nothing extra is counted**, because Phase 23 already writes no row for a
rate-limited turn, a duplicate message, Human handling mode, an agent with no
eligible knowledge, an unconfigured provider, or a validation or authorization
failure.

**One authority.** There is no second quota-consumption table. The entitlement
system queries `ai_provider_usages` and protects the creation of the real
`Started` row — no dual-write counter to disagree with it.

**UTC calendar months**, half-open `[first day 00:00, next first day 00:00)`. Not
a rolling thirty days: a rolling window recovers a little every day, which nobody
can predict; a calendar month resets on a date somebody can put in a diary.
Nothing historical is deleted when it resets.

---

## 8. Both quotas are race-safe, in PostgreSQL

**Decision.** `IWorkspaceEntitlementService.ReserveAsync` opens (or joins) a
transaction, takes `pg_advisory_xact_lock` on a key derived from (workspace,
entitlement), counts, decides, and runs the mutation — all inside it.

**Why not a process lock.** A `lock` statement protects one web server and
nothing else. The advisory lock is held by the database for the life of the
transaction and released when it ends, including when it ends by crashing.

**Why not check-then-act.** Counting, returning, and inserting afterwards is the
classic race: two requests both see four of five agents and both create one. Here
the second waits, counts five, and is refused.

The key is SHA-256 of a namespaced `(tenant, entitlement)` string rather than
`GetHashCode`, which is randomised per process — two web servers would otherwise
compute different keys for the same workspace and not exclude each other at all.

**For provider operations** this happens inside `AiProviderUsageLedger.BeginAsync`,
so the quota check and the durable `Started` row are one atomic step. Phase 23's
rule is unchanged and now has a sibling:

- if the `Started` row cannot be committed → **zero** provider requests;
- if the quota is exhausted → **zero** provider requests **and zero rows**.

Proved against real PostgreSQL with a canned transport: nine of ten used, two
concurrent dispatches, one `Started` row added, `transport.Calls == 1`.

---

## 9. Limit refusals are 409 with a stable code

**Decision.** `EntitlementExceededException` → `409 Conflict`, with
ProblemDetails extensions: `code`, `entitlement`, `current`, `limit`, and
`periodFrom`/`periodTo` where a period applies.

**Why 409.** Not 401 or 403 — the caller is authenticated and permitted. Not 429 —
waiting and retrying will not help. Not 402 — nothing in this platform takes
payment, and telling somebody "payment required" would send them looking for a
screen that does not exist.

`code` is `entitlement.<kind>.exhausted`. A client branches on it rather than on
the sentence, which is free to be reworded or translated. Nothing commercial
travels: no price, no plan to upgrade to, no payment link.

**Wording is product-neutral** — "This workspace has reached its monthly AI
operation limit", never "payment required", "card declined" or "subscription
expired".

---

## 10. One read endpoint, behind `analytics.view`

`GET /api/tenants/{tenantId}/plan-usage` returns the plan's code and name, and
every entitlement with its usage, limit (or `unlimited: true`), percentage, state
and — for the monthly one — its UTC period.

No `plan.view`, `quota.view` or `billing.view` was added: this is the same kind of
fact the analytics dashboard already shows, and a second permission is a second
thing for an administrator to get right, exposing the same numbers when they get
it wrong. A non-member gets 404 and learns nothing; a member without the
permission gets 403 before any count is taken.

---

## 11. Warning states are informational

`Normal` (< 70%), `Warning` (>= 70%), `Critical` (>= 90%), `Exhausted` (>= 100%).
Integer arithmetic, floored — 69.9% must not round up into a warning. Unlimited is
never anything but `Normal`; a limit of zero is `Exhausted` at once; a workspace
above a tightened limit reads 100% rather than more.

**Nothing is blocked at 70% or 90%.** Only the hard limit refuses, and the server
is what refuses it.

---

## 12. What the screen says

Settings-level route `/plan-usage`, behind the same permission, read-only.

- the plan's **name**, and nothing else about it
- each resource entitlement with "3 of 5" or "12 used · Unlimited"
- the monthly AI allowance with the month spelled out in UTC
- **no progress bar at all when unlimited** — a bar needs something to be a
  proportion of, and drawing one implies a limit off to the right
- warning, critical and exhausted are **words beside the colour**, never colour
  alone; the bars are `aria-hidden` because the same fact is in the text
- no Upgrade, Buy, Subscribe, Pay or Downgrade. One truthful sentence instead:
  *"Plan changes are not available from this workspace yet."*

A spec sweeps the rendered page for commercial vocabulary and for the three
sentinels, and asserts that Refresh is the only button.

**Backend enforcement is authoritative.** The screen reports; it never decides. A
race is resolved by the server's 409.

---

## 13. Tenant isolation

Usage is counted per `tenant_id` in every query, and the advisory key includes the
workspace, so two workspaces never wait on each other. Many workspaces may share
one plan; their usage of it is entirely separate. A `tenantId` from the browser
selects nothing: membership is checked first, and a non-member is told the
workspace is not there.

---

## 14. Migration

One: `20260920131325_AddWorkspacePlansAndEntitlements`. Two tables, one unique
index on plan code, check constraints keeping finite limits at or above zero, FK
integrity both ways, three seeded plans, and the backfill.

No invoice table, no subscription-payment table, no credits, no wallet, no billing
transactions, no daily quota rollup. No historical migration was edited. No
physical `xmin`, and no concurrency token on either table — nobody edits a plan,
and the reservation lock already makes a racing assignment safe.
