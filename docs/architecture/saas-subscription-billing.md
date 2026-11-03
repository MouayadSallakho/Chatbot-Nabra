# SaaS subscription and billing

Written for: an engineer changing anything in the billing path, and the reviewer
deciding whether the boundaries hold.

Stripe first, provider-independent by construction. Nothing below assumes Stripe
except the one adapter that names it.

---

## B1. Stripe is not the entitlement engine

**Decision.** The chain is one-directional and every link is explicit:

```
Workspace
  -> Commercial subscription        (WorkspaceSubscription)
    -> Billing provider abstraction (IBillingProvider)
      -> Stripe adapter             (StripeBillingProvider)
        -> verified, durable events (BillingEvent inbox)
          -> normalised state       (SubscriptionStatus)
            -> plan assignment      (WorkspacePlanAssignment, Phase 24)
              -> entitlements       (IWorkspaceEntitlementService, Phase 24)
```

Every entitlement check in the platform reads the Phase 24 assignment and
nothing else. Billing decides what that assignment should be; it never becomes
the thing that is read.

**Why.** A payment provider is a supplier, not a source of truth about what a
customer may do. If a permission check reached Stripe, then a Stripe outage
would be a platform outage, a Stripe schema change would be a permission change,
and anyone able to influence Stripe data could influence access.

**Forbids.** Any query against a provider from an authorization or entitlement
path. Any provider status read at request time to decide what a workspace may
do. Caching a provider answer and treating the cache as authority.

**Consequence.** There is a lag between a provider event and the local state,
bounded by the worker's poll interval. That is accepted deliberately: correctness
under provider unavailability is worth more than immediacy.

---

## B2. Stripe types exist in exactly one file

**Decision.** `src/OmniAI.Infrastructure/Billing/Stripe/` is the only place
`Stripe.*` types appear. Everything above it works with
`ProviderSubscriptionSnapshot`, `ProviderCheckoutSession`,
`ProviderCheckoutResult`, `ProviderReference` and `SubscriptionStatus`.

**Why.** A second provider should mean writing a second adapter, not editing the
subscription domain, the handlers, the entitlement layer and the UI. The test is
mechanical: `grep -r "using Stripe" src/` returns the adapter and the webhook
verifier, and nothing else.

**Forbids.** A Stripe type in Domain, in any Application contract, in Agents,
Knowledge, Conversations, WhatsApp, Analytics or entitlement logic. A Stripe
identifier on any API response. Stripe.js or any payment SDK in the browser.

**Consequence.** The adapter does translation work that would otherwise be spread
out — notably normalising statuses and reading period boundaries from the right
place (see B7).

---

## B3. Plan, offer and subscription are three different things

**Decision.** Four separate concepts, none of which may absorb another:

| Concept | Holds | Owner |
| --- | --- | --- |
| `Plan` (Phase 24) | what may be used | platform configuration |
| `BillingPlanOffer` | what it costs and how often | platform configuration |
| `BillingProviderPriceMapping` | what a provider calls that price | deployment |
| `WorkspaceSubscription` | one workspace's commercial lifecycle | the workspace |

**Why.** "Stripe Price != Plan" is the whole point. One plan is sold monthly and
annually without either plan or subscription changing. A plan can exist with no
offer (the fallback), and an offer can be withdrawn while subscriptions to it
continue.

**Forbids.** A price column on `plans`. A limit column on `billing_plan_offers`.
A provider price stored on either — it lives in the mapping, so a second provider
is another row rather than a schema change.

---

## B4. Money is a whole number of minor units

**Decision.** `Money` is `long` minor units plus an ISO 4217 `BillingCurrency`.
No `double`, no `float`, no implicit division by a hundred.

**Why.** 19.99 has no exact binary floating-point representation. A price that
drifts in the second decimal place is a price somebody is eventually charged
wrongly. Minor units are exact and happen to be what every payment provider
expects on the wire.

**Forbids.** Adding amounts in different currencies. A total across currencies.
Dividing by 100 anywhere — the exponent is a property of the currency, and the
frontend takes it from `Intl.NumberFormat`.

**Consequence.** Formatting is the client's job. The API sends
`amountMinorUnits` and `currency`, never a formatted string, so the server never
guesses a reader's locale.

---

## B5. The browser never submits a provider price

**Decision.** `POST /billing/checkout` accepts exactly one field: an internal
`offerId`. The provider's price comes from the mapping, the return addresses from
`Billing:ReturnBaseUrl`, and the idempotency key from the durable checkout
intent.

**Why.** A client that could name a price could buy something nobody put on sale,
or buy it at a number it chose. A client that could supply a return URL would
have an open redirect that a payment provider is willing to send a customer to.

**Forbids.** A price, amount, currency, plan or provider identifier in any
request body. A `returnUrl` parameter. Any client-supplied value reaching the
provider as authority.

---

## B6. This platform never sees a card

**Decision.** Checkout and every payment-method change happen on the provider's
own hosted pages. `WorkspaceBillingCustomer` stores an opaque provider reference
and a creation timestamp, and nothing else.

**Why.** Card data this platform never receives is card data it can never leak,
log, back up or be audited on. The PCI boundary holds because there is no code
here that could receive a card number.

**Forbids.** Card numbers, CVV, expiry, payment-method secrets, bank details or
IBANs in any table, log, response or exception. Using an email address as the
billing identity — identity is the workspace, and email is neither unique over
time nor stable.

**Consequence.** Invoices, receipts, payment methods and cancellation are reached
through `POST /billing/portal`, which returns a short-lived provider URL that is
used once and never stored. A schema test asserts no billing column matches
`%card%`, `%cvv%`, `%iban%`, `%payment_method%` or similar.

---

## B7. An event is a signal to look, never a fact to apply

**Decision.** No code path applies an event's contents. A verified event is
stored, and the worker then asks the provider what is true now and applies that.

**Why.** Provider events arrive out of order and are redelivered. A late
`invoice.payment_failed` must not drag a subscription that has since recovered
back into arrears. Reading the current snapshot makes ordering irrelevant by
construction rather than by a sequence number nobody can trust.

**Forbids.** Deriving a status from an event body. Replaying a checkout or a
charge to "repair" ordering — that moves money to fix a bookkeeping problem.
Sequencing events by `created` and rejecting older ones.

**Consequence.** The adapter must read fields from where they actually live.
`current_period_start` and `current_period_end` moved from the subscription onto
its **items** in current API versions; reading them from the subscription
produces silent nulls on every period. Stripe.net **52.4.2**, API version
**2026-08-26.dahlia**.

---

## B8. The webhook is verified against the exact raw bytes

**Decision.** The endpoint reads the body once into one array, verifies the
`Stripe-Signature` header against those bytes, and only then interprets them.
Verification uses Stripe's own `EventUtility.ConstructEvent`, which applies the
300-second timestamp tolerance, ignores schemes other than `v1` and compares in
constant time. Bytes are decoded with a strict UTF-8 decoder that throws on
invalid input.

**Why.** Verifying a re-serialised copy checks a different document from the one
acted on, which is the standard way a signed-webhook route is defeated.
Reimplementing the HMAC by hand is how a timing leak or an accepted replay gets
introduced.

**Forbids.** Parsing before verifying. Re-serialising for verification. A body
larger than 512 KiB (refused with 413 before the whole of it is in memory).
Treating "no webhook secret configured" as "skip the check" — that would make
every unconfigured environment a way to grant plans.

**Consequence.** The route is anonymous and carries **no antiforgery check**,
which is correct: antiforgery defends a browser session against a cross-site
request, and there is no session here. The signature is stronger than the token a
browser would have carried. Every human-facing billing write **is** antiforgery
checked.

---

## B9. The inbox is durable, deduplicated by the provider's own id, and payload-free

**Decision.** A verified event is written to `billing_events` and the endpoint
returns. `(provider, provider_event_id)` is unique. A worker claims rows with
`FOR UPDATE SKIP LOCKED`. **No raw payload is stored.**

**Why.** Providers expect an acknowledgement within seconds and redeliver
anything slower; taking a workspace lock and calling back to the provider does
not belong inside that window. The unique index is the whole of duplicate
protection — it holds for concurrent redeliveries and across web servers, which
an in-memory guard does not. Storing the payload would make every redelivery
permanently retained payment data this product has no use for.

**Forbids.** A `payload`, `body`, `raw` or `jsonb` column on `billing_events`
(asserted by a schema test). Logging a raw webhook body anywhere. An in-process
set of seen ids. RabbitMQ, Kafka or any broker — PostgreSQL is already the
durable store and a second one would be a second thing that can be out of sync.

**Consequence.** `failure` holds the platform's own sentence about why something
could not be applied, truncated to 400 characters. It never holds the provider's
words, which can name internal identifiers and quote the request back.

---

## B10. An unknown price grants nothing

**Decision.** When a provider reports a price with no mapping, the outcome is
`BillingApplyOutcome.UnknownPrice`. For a new subscription, nothing is created at
all. For an existing one, the lifecycle status is applied and the plan is left
where it was; the event is recorded as `Unprocessable` with the reason.

**Why.** Guessing a plan from an unrecognised price, or trusting provider
metadata to name one, would make entitlements settable by anybody who can create
a price. Falling back to unlimited would make it profitable.

**Forbids.** Inferring a plan from a price's amount, nickname, product or
metadata. Any unlimited fallback on an anomaly. Silently ignoring it.

---

## B11. The fallback plan must be finite

**Decision.** `Billing:FallbackPlanCode` names the plan a suspended or cancelled
workspace lands on. `FallbackPlanResolver` refuses a blank code, a code naming no
plan, and **a plan whose every limit is unlimited**. The Phase 25 migration seeds
a `free` plan with small, real limits.

**Why.** A fallback that granted everything would make not paying an upgrade, and
nobody would notice until the provider bill arrived. `legacy` and `development`
are both unlimited and are therefore not acceptable commercial fallbacks, however
convenient they look.

**Forbids.** Naming `legacy` or `development` as the fallback. A deployment
suspending anybody without a configured fallback — `PastDueGraceWorker` returns
immediately when billing is unconfigured.

---

## B12. Entitlement policy, in one expression

**Decision.**

```csharp
public bool GrantsPlan => Status
    is SubscriptionStatus.Trialing
    or SubscriptionStatus.Active
    or SubscriptionStatus.PastDue;
```

| Normalised state | Stripe statuses | Plan |
| --- | --- | --- |
| `PendingActivation` | `incomplete` | fallback |
| `Trialing` | `trialing` | subscribed |
| `Active` | `active` | subscribed |
| `PastDue` | `past_due` | subscribed, until grace ends |
| `Suspended` | `unpaid`, `paused` | fallback |
| `Canceled` | `canceled`, `incomplete_expired` | fallback |

An unrecognised status maps to `PendingActivation`, which grants nothing: the
safe reading of "I do not know what this means" is not "grant them everything".

**Consequence.** `PastDue` grants deliberately. Cutting a business off the
instant a card expires punishes the commonest and most innocent failure there is.

---

## B13. Grace is this platform's clock, and it starts once

**Decision.** Entering `PastDue` sets `GraceEndsAt` to
`now + Billing:PastDueGracePeriod` (default 7 days). Staying in `PastDue` does
not move it. Recovering clears it. `PastDueGraceWorker` finds expired deadlines
and calls `SuspendAsync`, which **re-checks under the workspace's billing lock**.

**Why.** A provider may retry a failed payment daily; restarting the clock on each
attempt would extend grace indefinitely and a workspace could sit unpaid forever.
Waiting for the provider to declare the subscription dead would put this
platform's commercial policy in somebody else's hands.

**Forbids.** Reading the system clock in domain code — the instant is a
parameter. Suspending from the sweep's own selection without re-checking, which
would override a payment that landed in between.

---

## B14. Subscription and plan assignment move together, under one lock

**Decision.** `BillingSubscriptionService` opens a transaction, takes
`pg_advisory_xact_lock` on a SHA-256-derived key in its own namespace
(`billing:{tenantId}`), writes both the subscription and the plan assignment, and
commits.

**Why.** A window in which the subscription says Active and the assignment still
says fallback is a window in which a workspace is refused something it is paying
for. Two events for one workspace arriving at once would otherwise both read the
old state and one would silently win.

**Forbids.** `GetHashCode` for the lock key — it is randomised per process, so
two web servers would take different locks. Session-scoped advisory locks, which
leak on a failed path. Sharing the entitlement lock's namespace.

---

## B15. A downgrade destroys nothing

**Decision.** Moving to a smaller plan changes what may be **created**. Members,
agents, documents and channels all remain. Phase 24 then refuses new creation
while usage sits above the new limit, with its existing 409 and stable code.

**Why.** Deleting somebody's work because their card expired is indefensible, and
irreversible in a way the billing state is not.

**Forbids.** Any delete, disable or archive on a downgrade. Retroactive quota
resets — the monthly provider-operation count is still read from Phase 23's usage
rows, which a plan change does not touch.

---

## B16. One live subscription per workspace, enforced by the database

**Decision.** A partial unique index on `workspace_subscriptions (tenant_id)`
filtered to `status <> 'Canceled'`.

**Why.** An application check is passed by two concurrent checkouts that both
read "none yet", and the workspace ends up paying twice. The filter is what lets
a cancelled workspace subscribe again while keeping its history.

**Forbids.** A plain unique index (which would make "cancel and come back"
impossible). Deleting cancelled rows to make room.

---

## B17. Checkout idempotency is durable, not a disabled button

**Decision.** `BillingCheckoutIntent` is claimed under a unique key derived from
`(tenant, offer, hour)` **before** the provider is called, and the same key is
sent to the provider as its idempotency key.

**Why.** A disabled button stops a second click in one tab and nothing else — not
a second tab, not a refresh, not two requests reaching two web servers. The
unique index and the provider's own idempotency together make two clicks one
purchase and one subscription.

**Forbids.** A client-supplied idempotency key, which a client could vary.
Relying on UI state for correctness. Storing the checkout URL — it is a
short-lived way into a payment page.

---

## B18. Reaching the success URL proves nothing

**Decision.** The success and cancel URLs are built from
`Billing:ReturnBaseUrl`. The billing screen does not read `?success=true`, a
session id, or any other browser state as evidence of payment; it re-reads the
workspace from the server.

**Why.** A query parameter is written by whoever holds the address bar. A
subscription becomes real when a verified provider event says so, which is
deliberately a different code path from the redirect.

**Forbids.** Branching on a return query parameter. Accepting a browser-supplied
return address, which would be an open redirect the provider itself performs.

**Consequence.** There can be a few seconds between paying and the screen
updating. The page says so rather than pretending.

---

## B19. Provider secrets are platform secrets

**Decision.** `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` and the local-only
`STRIPE_API_BASE` are read from the environment and handed straight to the
adapter that needs them, exactly as the OpenAI key and the Meta app secret are.

**Forbids.** Storing them in a tenant table, in source control, in
`appsettings.json`, in any API response, in the Angular bundle, or in a log line.
Exposing a per-workspace key — one deployment, one Stripe account.

---

## B20. Billing has its own permissions

**Decision.** `billing.view` and `billing.manage`, in a `Billing` group. Neither
is implied by `analytics.view`, and viewing does not imply managing.

**Why.** What a workspace spends is a different kind of fact from how many
conversations it handled, and the people trusted with one are not automatically
trusted with the other. Phase 13's authority ceiling is unchanged: a role can
still only grant what its holder already has.

**Forbids.** Reusing `analytics.view` for financial management. A catch-all
administration permission.

**Consequence.** A non-member gets 404, not 403 — somebody outside a workspace
must not learn that it exists, let alone what it pays.

---

## B21. Refusals are 409 with stable codes, never 402

**Decision.**

| Situation | Status | `code` |
| --- | --- | --- |
| Unknown offer | 409 | `billing.checkout.unknownoffer` |
| Withdrawn offer | 409 | `billing.checkout.offernotavailable` |
| No provider price mapped | 409 | `billing.checkout.offernotpurchasable` |
| Already subscribed | 409 | `billing.checkout.alreadysubscribed` |
| Nothing to manage | 409 | `billing.portal.nocustomer` |
| Deployment cannot sell | 503 | — |
| Provider unreachable | 502 | — |

**Why.** 402 means "payment required". Sending it for an unknown offer would have
a client showing a payment prompt for a request no payment could satisfy. The
code is stable across wording changes; a frontend matching on English would break
the first time somebody improved the sentence.

**Forbids.** 402 as a generic billing error. Forwarding a provider's own message
to a client.

---

## B22. Nothing was fabricated for existing workspaces

**Decision.** The Phase 25 migration creates six tables and seeds one plan. It
creates no subscription, no billing customer, and **no offers or prices**.

**Why.** Nobody who has not bought anything gets a row saying they did. A price
is a commercial decision and a provider identifier is deployment-specific;
seeding either would put a fictional price in every production database and a
provider id that resolves to nothing.

**Consequence.** A deployment with no offers shows none and refuses checkout
safely. That is the correct behaviour, not a gap.

---

## B23. What this phase deliberately is not

No usage-based billing. No per-token or per-message charging, markup, overage,
credits, wallets, meters or consumption invoices. No tax or VAT handling. No
payment-transaction table. No dunning emails. No customer-facing plan or price
editor. V1 is a fixed recurring subscription to a plan, and each of the above
would be a second product's worth of obligations arriving through the back door.

A schema test asserts that `billing_wallets`, `billing_credits`,
`billing_transactions`, `billing_invoices`, `billing_payments`,
`billing_usage_records`, `billing_meters` and `billing_tax_rates` do not exist.

---

## B24. Verified without a Stripe account

**Decision.** Every mandatory check for this phase runs with no Stripe account,
no production key, no webhook secret from Stripe, no card and no charge. Outbound
calls go to a local deterministic stub selected by `STRIPE_API_BASE`; webhook
signatures are generated locally with the same library that verifies them.

**Why.** A test suite that needs a payment account is a suite that does not run.
Generating signatures with one implementation and verifying with another would
test the suite's idea of the scheme rather than the scheme.

**Forbids.** Any request to `api.stripe.com` during verification. A real key or
webhook secret in the repository. A test that needs a card.

---

## B25. The billing read names the plans and their limits (Phase 37.5b)

**Decision.** `GET …/billing` (`billing.view`, as before) gains:
- `currentPlan: { code, name, limits }`, the plan the workspace is on. Its limits are
  the ones the entitlements were just judged against, so they cost no further read
  and cannot disagree with the plan-usage screen. `currentPlanName` stays.
- `fallbackPlan: { code, name, limits } | null`, what `Billing:FallbackPlanCode` would
  suspend the workspace to. `null` when none is configured, or when the code is
  unusable, names no plan, or names an unlimited plan: everything B11 refuses reads
  as "no fallback" here, and never fails the read
  (`FallbackPlanResolver.FindAsync`).

`limits` holds the six plan limits, named as the plan-usage read names its
entitlements (`members`, `aiAgents`, `knowledgeDocuments`, `webChatEndpoints`,
`whatsAppConnections`, `monthlyProviderOperations`); `null` means unlimited and is
always written.

**Plan definitions, not usage.** How much of each limit is used stays on
`GET …/plan-usage`, behind `analytics.view`. The refusals are unchanged: `403` for a
member without `billing.view`, `404` outside the workspace.

---

## Configuration

| Setting | Meaning |
| --- | --- |
| `Billing:FallbackPlanCode` | The finite plan a lapsed workspace lands on. Required to sell. |
| `Billing:ReturnBaseUrl` | Server-owned base for checkout return addresses. Required to sell. |
| `Billing:PastDueGracePeriod` | How long arrears are tolerated. Default 7 days. |
| `Billing:Workers:IdleInterval` | Event worker poll interval when idle. |
| `Billing:Workers:BatchSize` | Events claimed per pass. |
| `Billing:Workers:MaxAttempts` | Attempts before an event is left alone. |
| `Billing:Workers:GraceSweepInterval` | How often expired grace is swept. |
| `STRIPE_SECRET_KEY` | Platform secret. Absent means the deployment cannot sell. |
| `STRIPE_WEBHOOK_SECRET` | Platform secret. Absent means every webhook is refused. |
| `STRIPE_API_BASE` | Local verification only. Empty in every real deployment. |

## Related

- [workspace-plans-entitlements-quotas.md](workspace-plans-entitlements-quotas.md)
  — what a plan allows, and the assignment this phase writes to.
- [provider-usage-cost-accounting.md](provider-usage-cost-accounting.md) — what a
  provider call cost this platform. Internal accounting, and not what a customer
  is charged.
- [tenant-rbac.md](tenant-rbac.md) — the permission model `billing.view` and
  `billing.manage` join.
