# Phase 24 — Workspace plans, entitlements and quotas

Written for: the reviewer of this phase.

Baseline: `5b1d545` (Phase 23 checkpoint). Nothing is staged or committed.

> **Entitlement enforcement, not billing.** No payment provider, checkout,
> invoice, subscription, card collection, VAT, wallet, credit, plan price,
> refund, dunning or past-due handling was built, and none is implied. A plan
> says what may be used; it never says what it costs.

## Provider scope

**No live OpenAI traffic.** `OMNIAI_LIVE_OPENAI` was never set and
`OpenAiOptions.cs` is unmodified. The provider-quota suite drives the real
adapter over a canned transport that counts requests, so "the provider received
exactly one request" is asserted rather than assumed. The local API container's
log contains no reference to `api.openai.com`.

**No live Meta traffic.** Nothing in this phase touches WhatsApp delivery; the
WhatsApp entitlement counts connection rows.

**No VPS, no deployment.** Local Docker PostgreSQL 17 + pgvector, local Redis,
`127.0.0.1` only.

## What was built

| Layer | Added |
| --- | --- |
| Domain | `Plan`, `WorkspacePlanAssignment`, `PlanLimit`, `PlanCode`/`PlanDisplayName`/`PlanId`, `EntitlementKind`, `EntitlementState`, `EntitlementUsage` |
| Application | `IWorkspaceEntitlementService`, `IPlanCatalog`, `DefaultPlanResolver`, `EntitlementExceededException`, `GetWorkspacePlanUsageHandler`, `EntitlementPeriod` |
| Infrastructure | `WorkspaceEntitlementService` (counts + advisory-lock reservation), `PlanCatalog`, two EF configurations, one migration with seeds and backfill |
| Api | `GET /api/tenants/{id}/plan-usage`, 409 ProblemDetails with stable `code` and safe extensions |
| Frontend | `/plan-usage` screen, nav entry, `PlanUsageApi`, `code` surfaced on `ApiError` |

Full rationale: [workspace-plans-entitlements-quotas.md](../architecture/workspace-plans-entitlements-quotas.md).

## The decisions worth reviewing

**Permission ≠ entitlement.** Two questions, asked in that order. A member
holding `ai-agents.create` in a full workspace gets 409; a member without the
permission gets 403 **before** the plan is consulted, so a refusal never leaks
"3 of 3 agents" to somebody who cannot see the workspace. Both are tested.

**Unlimited is `null`, never a sentinel.** EF refuses to map a non-nullable
struct to a nullable column, so `Plan` holds private `int?` properties that
persistence maps and exposes `PlanLimit` to everything else. The database keeps
the honest type; the domain keeps the useful one. Check constraints keep finite
limits at or above zero. A limit of zero is a real limit and is exhausted at once.

**Race safety is PostgreSQL's, not this process's.** `ReserveAsync` opens or
joins a transaction, takes `pg_advisory_xact_lock` keyed on a SHA-256 of
(workspace, entitlement), counts, decides and mutates inside it. The key is
hashed rather than `GetHashCode`d because that is randomised per process — two
web servers would otherwise not exclude each other at all.

**The provider quota lives inside `AiProviderUsageLedger.BeginAsync`**, so the
count and the durable `Started` row are one atomic step. Phase 23's rule stands
and gains a sibling: no row → no provider request; quota exhausted → no row *and*
no provider request.

**Onboarding assigns a plan in the same transaction as the workspace**, from
`Plans:DefaultPlanCode`. There is no fallback: a deployment that has not
configured it cannot create workspaces (503). `appsettings.json` deliberately
leaves it unset. A workspace with no assignment is refused, never assumed
unlimited.

**409, not 402/403/429.** Extensions carry `code`, `entitlement`, `current`,
`limit` and the period where one applies — and nothing commercial. Wording is
product-neutral.

## What the work surfaced

Two real integration problems, both found by running the suite rather than by
review:

1. **Nested transactions in invitation acceptance.** The seat reservation opens a
   transaction; `TenantInvitationRepository.AcceptAsync` unconditionally opened
   another. It now joins an ambient one — which is also more correct, because the
   seat count, the invitation's ending and the membership belong in one
   transaction.

2. **Test fixtures produced workspaces the application never produces.** Fixtures
   that insert a `Tenant` aggregate directly bypass onboarding and so had no plan.
   Rather than weaken the service, the shared test database now assigns the legacy
   plan on insert, exactly as onboarding does. The service's refusal to treat a
   missing assignment as unlimited stays fully exercised — its own test deletes
   the assignment and asserts the refusal.

## Migration

One: `20260920131325_AddWorkspacePlansAndEntitlements`. Two tables, one unique
index on plan code, six check constraints, FK integrity both ways, three seeded
plans (`legacy`, `development`, `starter`), and a set-based backfill putting every
existing workspace on `legacy` with `assigned_at` taken from its earliest
membership. No historical migration was edited. No invoice, subscription,
credits, wallet, billing or rollup table. No physical `xmin`, no concurrency
token.

## Verification

**Backend** — Release build clean, zero warnings. `dotnet test`:

| Suite | Result |
| --- | --- |
| Domain | 688 passed |
| Application | 656 passed |
| Infrastructure | 722 passed |
| Api | 759 passed |
| **Total** | **2,825 passed, 0 failed** |

`dotnet list package --vulnerable --include-transitive`: none.

Application and Domain totals for pre-existing suites are unchanged from Phase
23 apart from the 18 new Domain tests — the "unlimited changes nothing"
guarantee, held by giving every pre-existing suite an unlimited double.

New suites: `PlanLimitTests`/`EntitlementUsageTests` (Domain, 18),
`WorkspaceEntitlementServiceTests` (19), `MemberSeatTests` (5),
`ProviderQuotaDispatchTests` (7), `PlanUsageApiTests` (16),
`EntitlementEnforcementApiTests` (8). Every entitlement and concurrency claim
runs against real PostgreSQL; no InMemory substitute is used for any of them.

One flake was observed and investigated:
`AiAgentOpenAiPreviewApiTests.A_failure_body_leaks_nothing_about_the_provider`
failed once under full-solution parallel load and passed 17/17 in isolation and
759/759 on a full re-run of its suite. It is unrelated to this phase's changes.

**Frontend** — `npm ci` clean (after stopping the dev server that held
`esbuild.exe`); `npm run build` clean apart from the pre-existing, unrelated
`embed-chat.scss` budget warning; `npm run test:ci` **652 passed, 0 failed**;
`npm run format:check` clean; `npm audit` 0 vulnerabilities. There is **no `lint`
script** in this project — the scripts are `ng, start, build, watch, test,
test:ci, format, format:check`. `npm audit --omit=dev` returned a transient
registry error twice; the unscoped `npm audit`, which covers strictly more,
succeeded with 0 vulnerabilities.

**Chrome** — signed in as a real owner in a real workspace moved onto a synthetic
local plan (members 5, agents 3, documents 10, web chat 1, WhatsApp 0, AI
operations unlimited), with usage seeded directly in the development database.
Verified on the real screens:

- the plan's name, and the truthful "Plan changes are not available from this
  workspace yet"
- every state on one page, each matching PostgreSQL exactly: members 4 of 5
  (80%, Warning), agents 3 of 3 (100%, Limit reached), documents 9 of 10 (90%,
  Nearly at the limit), website chat 0 of 1 (Normal), WhatsApp 0 of 0 (Limit
  reached — a zero limit, not unlimited), AI operations "78 used · Unlimited"
- the UTC month rendered from the half-open window as "September 2026 · Sep 1 to
  Sep 30 UTC" — never "1 Oct"
- creating a fourth agent through the real form is refused, the page stays put,
  and the alert reads "This workspace has reached its plan limit of 3 AI agents."
  The same request over HTTP returns 409 with
  `code: entitlement.aiagents.exhausted`, `entitlement`, `current`, `limit`
- the three existing agents remain listed, readable and editable
- no commercial vocabulary anywhere (`upgrade`, `buy`, `subscribe`, `price`,
  `invoice`, `payment`, `card`, `billing`, `checkout`, `$`, `€`) and none of the
  three sentinels; Refresh is the only action in `main`
- 390 / 768 / 1440 / 1920 with no page-level horizontal scroll; light and dark at
  390 / 768 / 1440; clean console
- headings nest h1 → h2 → h3; progress bars are `aria-hidden` because the same
  fact is in the text beside them; every state is a word as well as a colour.
  Contrast measured 6.33:1 (warning) and 6.29:1 (critical) in light, 9.11:1 and
  7.75:1 in dark

## Known limitations

- **The `analytics.view` denial path was verified by API test, not in Chrome.**
  `PlanUsageApiTests` covers 401, non-member 404, member-without-permission 403
  with no counts in the body, and the 403-before-entitlement ordering. Creating a
  second restricted member in the browser would have exercised the invitation
  flow rather than this phase's code.
- **`.table-scroll` still carries no `tabindex`** (WCAG 2.1.1). Recorded in Phase
  23; this phase adds no table and does not touch that component.
- **A plan's limits cannot be changed from anywhere in the running application.**
  That is deliberate for Phase 24, and it means a plan transition currently
  requires a database change.

## Phase 25 handoff

The commercial layer: prices, plan transitions with an authority that approves
them, and whatever payment machinery follows. Everything it needs is in place —
a plan model with no price column to repurpose, an assignment table keyed by
workspace, and a refusal vocabulary that says nothing about money.

## Not done, deliberately

Payment providers, checkout, invoices, subscription billing, card collection,
VAT, wallets, credits, plan prices, upgrade/downgrade flows, refunds, dunning,
past-due handling, token hard limits, usage estimation, rollup tables, a
plan-administration endpoint, and any customer-facing plan editor.
