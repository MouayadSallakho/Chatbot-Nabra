# Phase 23 — Provider usage and cost accounting foundation

Written for: the reviewer of this phase.

Baseline: `51a18da` (Phase 22 checkpoint). Nothing is staged or committed.

> **This is accounting, not billing.** No invoice, no subscription, no credit, no
> wallet, no markup, no customer charge, no VAT, no payment provider, no usage
> limit and no automatic suspension was built, and none is implied. "Estimated
> provider cost" is an internal operational figure: what this platform reckons it
> paid OpenAI. A spec sweeps the rendered dashboard for the vocabulary of billing
> and fails if any of it appears.

## Provider scope

**No live OpenAI traffic.** `OMNIAI_LIVE_OPENAI` was never set. Phase 18's
Development guard is untouched and was not weakened for testing: every accounting
test drives a canned transport or a recording ledger, and the browser walkthrough
used synthetic rows written locally into `ai_provider_usages` by SQL. The API
container's log contains no reference to `api.openai.com`.

**No live Meta traffic.** Nothing in this phase touches WhatsApp.

**No VPS, no deployment.** Everything ran on `127.0.0.1` against local Docker
PostgreSQL and Redis.

## What was built

| Layer | Added |
| --- | --- |
| Domain | `AiProviderUsage` aggregate, `ProviderTokenUsage`, `AiModelRates`, `ModelName`, `CurrencyCode`, and the four enums |
| Application | `AiUsageContext`, `IAiProviderUsageLedger`, `IAiProviderPricing`; an attribution parameter added to both provider ports |
| Infrastructure | `AiProviderUsageLedger`, `AiProviderPricingCatalog`, `OpenAiUsageMapping`, accounting inside both OpenAI adapters, one migration |
| Api | `aiUsage` and `models` on the existing dashboard response — no new endpoint |
| Frontend | `ProviderUsageCard`, rendered inside the analytics dashboard |

Full design rationale and the forbidden directions:
[provider-usage-cost-accounting.md](../architecture/provider-usage-cost-accounting.md).

## The decisions worth reviewing

**Provider-reported usage only.** No tokenizer, no character count, no estimate.
Every count is nullable, and an unreported one is stored NULL so `SUM` ignores it.
Absent is never zero.

**Attribution is a required parameter.** `IAiTextGenerator.GenerateAsync` and
`ITextEmbeddingGenerator.GenerateAsync` both take `AiUsageContext`. Four
production call sites declare a workspace and a purpose; a fifth cannot be added
without doing the same, because it would not compile. Nothing is inferred from
the HTTP request, the URL, an `AsyncLocal` or provider metadata.

**Durable before dispatch.** `BeginAsync` commits a `Started` row and its failure
is not caught — if the row cannot be written, the paid call is not made. The
first test in `OpenAiUsageAccountingTests` asserts `transport.Calls == 0` in that
case. The trade is deliberate: a platform that cannot account for spend should not
spend.

**The ledger owns its own scope.** `IServiceScopeFactory` per operation, so the
usage row is independent of the caller's unit of work: no flushing a half-built
conversation, no losing the row to a caller's rollback. This also resolved the
singleton adapter / scoped `DbContext` lifetime mismatch.

**`Unknown` is a first-class outcome.** A timeout may well have been charged.
Recording it as a failure would understate spend; recording it as a success would
invent a token count. Analytics reports `Started` and `Unknown` together as
*unresolved*. No paid call is ever retried — both client factories already set
`ClientRetryPolicy(maxRetries: 0)`.

**Unrated is not free.** `AiModelRates.Rate` returns null when any needed rate is
missing, and the row stays unrated. Unrated calls are counted separately and the
dashboard says so in words. Money is `decimal` throughout and `numeric(18,6)` in
PostgreSQL. Reasoning tokens are not charged twice — the provider already counts
them inside output.

**Rates are configuration, versioned by date, snapshotted onto the row.** A later
price change cannot rewrite a historical cost. A bad rate stops the deployment
from starting.

**Currencies are never added.** Two currencies are two figures with an
explanation. A model billed in two reports no figure at all, and keeps exact call
and token counts.

**`analytics.view`, reused.** No `usage.view`, no `cost.view`, no `billing.view`,
no new endpoint. `aiUsage` is null for a workspace that never called a provider.

## What the tests found

Three real defects, each caught by a test written in this phase rather than by
review:

1. **The model and currency groupings did not translate.** EF Core will not
   translate `resolved ?? requested` over converted value objects, nor reaching
   inside a converted value object in a `GroupBy` key, nor ordering a
   constructor-projected member after a grouping. Every one of those fell back to
   client evaluation and threw. As shipped, the dashboard would have returned 500
   for any workspace with a provider call. Fixed by grouping on the stored
   columns, ordering and taking before the projection, and merging in memory over
   a bounded number of rows.

2. **`TimeProvider` was not registered by `AddInfrastructure`.** The API host
   registers its own, so the Api suite passed; composing Infrastructure on its own
   threw. Fixed with `TryAddSingleton(TimeProvider.System)`, which yields to a
   host that chose one.

3. **The usage card sat behind the conversation-activity empty state.** Indexing
   a document and previewing an agent both cost money and produce no conversation,
   so a quiet workspace — exactly the one whose owner wants to know about spend —
   would have seen nothing. Found in the browser; fixed and pinned by a spec.

Two smaller corrections came out of the browser check: costs were rounding
`$0.038436` to `$0.04` (now two decimal places minimum, six maximum), and the
unrated caveat said "outside the figure above" when there was no figure.

## Migration

One: `20260915100358_AddAiProviderUsageAccounting`. One table, one index on
`(tenant_id, started_at)`. No historical migration was edited. No rollup table, no
billing table, no pricing warehouse, no physical `xmin`, no concurrency token —
nobody edits an accounting record, and the idempotent settle already makes a
racing finalisation safe. The tenant foreign key is `Restrict`.

## Verification

**Backend** — `dotnet build -c Release` clean, zero warnings; `dotnet test`
**2,752 passed, 0 failed** (Domain 670, Application 656, Infrastructure 691, Api
735). `dotnet list package --vulnerable --include-transitive`: none.

New suites: `AiProviderUsageTests` and `ProviderTokenUsageTests` (Domain),
`ProviderUsageAttributionTests` (Application), `OpenAiUsageAccountingTests`,
`OpenAiEmbeddingAccountingTests`, `AiProviderUsageLedgerTests`,
`AiProviderPricingCatalogTests`, `WorkspaceUsageAnalyticsTests` (Infrastructure),
`UsageAnalyticsApiTests` (Api). Every accounting and analytics query runs against
real PostgreSQL.

`AiProviderUsageTests` includes a structural privacy test: the aggregate has no
`string` property at all, so a `Prompt` or `Completion` field fails a test rather
than a review.

**Frontend** — `npm run build` clean (one pre-existing `embed-chat.scss` budget
warning, unrelated and unchanged); `npm run test:ci` **632 passed, 0 failed**;
`npx prettier --check` clean; `npm audit --omit=dev` 0 vulnerabilities. There is
no `lint` script in this project.

The analytics stylesheet was already at its 4 kB budget before this phase, so the
usage section became its own component with its own stylesheet rather than being
squeezed in. That is also the better boundary: it is a self-contained subject with
its own vocabulary.

**Chrome** — signed in as a real user in a real workspace, against synthetic local
accounting rows covering every state: rated, unrated, mixed currency, unreported
usage, failed, unknown and never-settled. Checked at 390 / 768 / 1440 / 1920 in
light and dark. Verified: exact figures (`$0.038436`, not `$0.04`), both
currencies side by side with the explanation, a model billed in two currencies
showing "Not priced" with exact counts intact, no `$0.00` anywhere in the
fully-unrated state, no card at all for a workspace with no calls, no page-level
horizontal scroll at any width, the model table scrolling inside its own
container, no console messages, one dashboard request with no polling, and no
billing vocabulary anywhere on the page. Contrast of the unrated warning measured
6.33:1 in light and 9.11:1 in dark.

## Known gap, not introduced here

`.table-scroll` containers carry no `tabindex`, so a horizontally scrolling table
is not keyboard-scrollable (WCAG 2.1.1). This is the app-wide convention — the
agents and daily-activity tables do the same — and the usage table follows it for
consistency. Fixing one instance would create an inconsistent tab order between
two adjacent tables on the same page. It belongs to a dedicated accessibility
pass across every table, not to this phase.

## Not done, deliberately

Invoices, plans, credits, wallets, customer charges, markup, VAT, payment
providers, usage limits, quotas, workspace suspension, token estimation, rollup
tables, a pricing warehouse, FX conversion, a second permission, a second
endpoint, and any billing screen.
