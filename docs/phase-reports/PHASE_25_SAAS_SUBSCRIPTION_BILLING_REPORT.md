# Phase 25 — SaaS subscription and billing foundation

Written for: the reviewer of this phase.

Baseline: `1df6d6c` (Phase 24 checkpoint). Nothing is staged or committed.

> **Chrome verification ran and passed.** The MCP Chrome and Playwright servers
> timed out again on this session, so Chrome was driven through the
> `chrome-devtools` CLI against a real Chrome window instead. §72–79 are
> **satisfied**; §§4–12 of the Chrome plan were executed against the real UI.
> Six defects were found in the browser and fixed. See "Browser verification".

> **No Stripe account, no key, no card, no charge.** `STRIPE_SECRET_KEY` was set
> to the literal `sk_test_local_stub_only`, which reaches a local stub on
> `127.0.0.1:4242` selected by `STRIPE_API_BASE`. Nothing in this phase contacted
> `api.stripe.com`, `api.openai.com` or `graph.facebook.com`.
> `OMNIAI_LIVE_OPENAI` was never set.

> **No VPS, no deployment.** Local Docker PostgreSQL 17 + pgvector, local Redis,
> `127.0.0.1` only.

---

## Provider scope

| Provider | Contacted | How it was stood in for |
| --- | --- | --- |
| Stripe API | No | Local Node stub on `127.0.0.1:4242`, outside the repository |
| Stripe webhooks | No | Signatures generated locally with Stripe.net's own `EventUtility` |
| OpenAI | No | `OPENAI_API_KEY` blank in every suite |
| Meta | No | Untouched by this phase |

Stripe.net **52.4.2**, pinned centrally in `Directory.Packages.props`. The SDK
targets API version **2026-08-26.dahlia**.

---

## What was built

| Layer | Added |
| --- | --- |
| Domain | `BillingPlanOffer`, `BillingProviderPriceMapping`, `WorkspaceSubscription`, `WorkspaceBillingCustomer`, `BillingCheckoutIntent`, `BillingEvent`, `Money`, `BillingCurrency`, `OfferCode`, `ProviderReference`, `SubscriptionStatus`, `BillingInterval`, `BillingProvider` and four strong ids |
| Application | `IBillingProvider` and its snapshot/session/result/request shapes, `IBillingCatalog`, `IBillingSubscriptionService`, `IBillingWebhookVerifier`, `FallbackPlanResolver`, `BillingOptions`, four persistence contracts, and the handlers `GetWorkspaceBilling`, `StartBillingCheckout`, `CreateBillingPortalSession`, `ReceiveBillingWebhook`, `ApplyProviderBillingEvent` |
| Infrastructure | `StripeBillingProvider`, `StripeWebhookVerifier`, `StripeSubscriptionStatus`, `StripeOptions`, `UnconfiguredBillingProvider`, `BillingSubscriptionService`, five stores, `BillingEventWorker`, `PastDueGraceWorker`, EF configurations, one migration |
| Api | `BillingEndpoints` (3 routes), `BillingWebhookEndpoints` (1 anonymous route), Problem Details arms for four new failures |
| Frontend | `features/billing/` — API client, page, styles, spec — plus a nav entry and two permission codes |
| Permissions | `billing.view`, `billing.manage` in a new `Billing` group |

`IPlanCatalog` gained `FindByIdAsync`. Nothing else outside billing changed
behaviourally.

---

## The architecture chain

```
Workspace -> WorkspaceSubscription -> IBillingProvider -> StripeBillingProvider
  -> verified BillingEvent -> SubscriptionStatus -> WorkspacePlanAssignment
    -> Phase 24 entitlements
```

**Stripe is not the entitlement engine.** Every entitlement check still reads the
Phase 24 assignment. `grep -r "using Stripe" src/` returns two files, both under
`Infrastructure/Billing/Stripe/`. No Stripe type appears in Domain, in any
Application contract, on any API response, or anywhere in the Angular bundle.

---

## Security decisions

| Decision | Why |
| --- | --- |
| Webhook signature verified against the **exact raw bytes**, read once into one array | Verifying a re-serialised copy checks a different document from the one acted on |
| Strict UTF-8 decode | A body that is not valid UTF-8 is refused rather than mangled into something that could never match |
| 512 KiB body ceiling, 413 before the whole body is in memory | An anonymous caller could otherwise stream indefinitely before a signature over the whole body could be checked |
| No webhook secret ⇒ refuse everything | "Unconfigured" must never mean "skip the check" |
| Webhook is anonymous and **not** antiforgery-checked | There is no browser session to defend; the signature is stronger than the token one would carry |
| All three human-facing billing routes **are** antiforgery-checked | They are cookie-authenticated and they spend money |
| Browser sends only an internal `offerId` | A client that could name a price could buy something nobody put on sale |
| Return URLs from `Billing:ReturnBaseUrl` | A browser-supplied return address is an open redirect the provider itself performs |
| No raw payload column on `billing_events` | A verified payload kept forever is payment data this product has no use for |
| Failure text is the platform's own, truncated to 400 chars | Provider messages name internal identifiers and quote the request back |
| Non-member gets 404, not 403 | Somebody outside a workspace must not learn it exists, let alone what it pays |

---

## Database

One migration: `20260922102508_AddSaasSubscriptionBilling`. Six tables. No
historical migration was edited.

Constraints, all enforced by PostgreSQL rather than by a handler:

| Guarantee | Mechanism |
| --- | --- |
| Offer code unique | `ux_billing_plan_offers_code` |
| Amount non-negative | `ck_billing_plan_offers_amount_not_negative` |
| Only Monthly/Annual | `ck_billing_plan_offers_interval` |
| Trial 0–365 days or none | `ck_billing_plan_offers_trial_days` |
| One provider price per offer | `ux_billing_price_mappings_provider_price` |
| One billing customer per workspace/provider | composite primary key |
| One provider customer per workspace | `ux_workspace_billing_customers_provider_customer` |
| **At most one live subscription per workspace** | `ux_workspace_subscriptions_current`, partial on `status <> 'Canceled'` |
| One provider subscription per local subscription | `ux_workspace_subscriptions_provider_subscription` |
| **Provider event id unique** | `ux_billing_events_provider_event` |
| Checkout idempotency unique | `ux_billing_checkout_intents_idempotency` |
| Workspace relational safety | tenant foreign keys, cascade |

**Seeded:** one `free` plan (2 members, 1 agent, 10 documents, 1 web chat, 0
WhatsApp, 200 monthly provider operations), guarded with `ON CONFLICT DO
NOTHING`. **Not seeded:** any offer, any provider price, any subscription, any
billing customer. A schema test asserts all four are empty on a fresh database.

No `xmin` column. No wallet, credit, transaction, invoice, payment, usage-record,
meter or tax table — asserted by a theory over all eight names.

---

## Two defects found by driving the API, and fixed

Found before Chrome was available, by driving the real API against the stub
over HTTP rather than by the suite. Both have regression tests. Six further
defects were found later in Chrome; see "Browser verification".

**1. `IsManaged` meant the wrong thing.** It was computed as `subscription is not
null`, but its own documentation and the frontend read it as "this deployment
takes payments". A configured deployment with no subscription therefore reported
`isManaged: false` alongside `canPurchase: true`, and the screen would have hidden
the entire purchase path on exactly the deployments that can sell. Now
`_options.IsConfigured`. Covered by `UnbilledDeploymentApiTests`.

**2. Conflict handling was too broad.** `WorkspaceBillingCustomerStore.GetOrAddAsync`
caught *any* unique violation and read back by `(tenant, provider)`. When the
violated constraint was instead the provider-customer index — a reference already
held by a different workspace — that read found nothing and the request became a
500 saying "could not be read back", hiding what had actually happened. All three
stores now match the specific constraint name; the customer store reports the
cross-workspace case as a provider problem and writes nothing. Covered by two
tests in `BillingSchemaTests`.

---

## Testing

Real PostgreSQL 17 throughout. No in-memory provider anywhere.

| Suite | Tests | Result |
| --- | --- | --- |
| `OmniAI.Domain.Tests` | 719 | pass |
| `OmniAI.Application.Tests` | 662 | pass |
| `OmniAI.Infrastructure.IntegrationTests` | 776 | pass |
| `OmniAI.Api.IntegrationTests` | 796 | pass |
| **Total** | **2,953** | **0 failures** |

Phase 25 added 128 tests.

**Webhook security.** Missing signature, wrong secret, body altered after
signing, a correctly signed request from yesterday, and five malformed header
shapes — each refused with 401 and **nothing stored**. Three sequential
redeliveries and six concurrent ones each store exactly one row. An event type
this phase does nothing with is accepted and settled rather than retried forever.

**Checkout.** Permission matrix (`billing.view` reads but cannot buy;
`analytics.view` gets neither; a non-member gets 404). Antiforgery removed ⇒ 400
and no provider call. Two rapid clicks and four concurrent ones ⇒ one provider
session and one intent. A price, amount and currency smuggled into the body are
ignored. Withdrawn and unknown offers refused with their stable codes. Provider
outage ⇒ 502, no subscription, no session recorded, and the provider's own words
not forwarded.

**Lifecycle, driven end to end by signed webhooks.** Checkout completion creates
the subscription and assigns its plan; the second purchase is refused; arrears
keep the plan and set a grace deadline; cancellation falls back to `free`;
two agents created under `starter` **still exist** after the downgrade and only
the next one is refused, by Phase 24's own 409.

**Ordering.** A late `invoice.payment_failed` delivered after the customer has
paid leaves the subscription `Active` with no grace deadline — because nothing
applies an event's contents.

**Unknown price.** A completed checkout billing an unmapped price creates no
subscription and leaves the workspace on the plan it had. Not a guess, and
emphatically not unlimited.

**Grace, with a supplied clock.** Grace does not extend across six daily arrears
events. Suspension refuses at day 3 and succeeds at day 8. A payment during grace
clears the deadline and the sweeper then declines to suspend.

**Schema.** Every constraint above is asserted by attempting the violation
against real PostgreSQL. A cancelled subscription does not block a new one. No
billing column matches `%card%`, `%cvv%`, `%iban%`, `%payment_method%` and
similar. `billing_events` has no payload, body, raw or json column.

**Isolation.** One workspace's subscription never reaches another; a workspace
that bought nothing has no subscription and keeps its original plan.

**Frontend.** 672 tests pass (50 files). Billing adds 20: the permission split,
the exact request body, the failure path, the in-flight guard, the states in
words, the portal call, the unconfigured deployment, the absence of any provider
identifier, the card footnote, that returning from the provider is not treated as
proof of payment, and — added after Chrome — payment-specific copy on 502 and
503, the scheduled-cancellation notice and its absence, and a distinct
accessible name per Subscribe button.

---

## Browser verification

**Chrome ran.** The MCP Chrome and Playwright servers timed out again on this
session, so Chrome was driven through the `chrome-devtools` CLI against a real,
visible Chrome window. The earlier session's MCP timeout is left recorded above
as execution history; it no longer describes the state of this phase.

### Stack

Everything local, everything on `127.0.0.1`. Angular dev server on `:4200`, the
API in Docker on `:5027`, PostgreSQL 17 + pgvector and Redis in Docker, and the
deterministic Stripe-compatible stub (Node, outside the repository) on `:4242`.
`STRIPE_API_BASE` pointed the adapter at the stub; `STRIPE_SECRET_KEY` was the
literal `sk_test_local_stub_only`. **No Stripe account, no production key, no
card, no charge.**

### Workflows verified through the real UI

| Section | Workflow | Result |
| --- | --- | --- |
| 4 | Sign in as Owner, open Billing from the nav | Renders; nav entry present |
| 4 | Truthful no-subscription state | "This workspace has no active subscription, so it is on the plan above" |
| 4 | Monthly and Annual offers | 29.00 per month, 290.00 per year, 14-day trial shown |
| 4 | **No card field in OmniAI** | 3 inputs on the page, all theme radios; **0 iframes**; no field matching card/cvv/expiry/iban |
| 4 | Start subscription | Redirects to the local hosted checkout |
| 4 | Request contents | Body is `{offerId}` only, no price, amount or currency; smuggled `priceId` and `amountMinorUnits` ignored |
| 4 | Complete synthetic checkout | Signed `checkout.session.completed` delivered, 200 |
| 4 | Worker settles it | Provider re-read (checkout session, then subscription), subscription becomes `Active` |
| 4 | Phase 24 assignment changes | `workspace_plan_assignments` moves to `starter` |
| 4 | Plan and usage reflects it | "Starter", limits 1 of 5 members, 0 of 3 agents |
| 4 | **Rapid double-click** | 1 checkout intent, 1 provider session |
| 5 | Payment failure to PastDue | "Payment failed" and "keeps its plan for now"; plan stays `starter` |
| 5 | Grace shown truthfully | "Plan kept until September 30, 2026" |
| 5 | Nothing deleted, workspace usable | Plan and usage still Starter |
| 5 | Recovery to Active | Warning cleared, `grace_ends_at` null, plan retained |
| 6 | Cancel at period end | "Cancellation is scheduled... will not renew"; label switches to "Access ends"; plan retained |
| 6 | Terminal cancellation | `Canceled`; fallback `free` assigned |
| 6 | **Non-destructive downgrade** | Both agents created under `starter` still listed on `free`; the next one refused with "This workspace has reached its plan limit of 1 AI agents" |
| 7 | Manage billing | Server created a portal session; browser redirected to the local portal |
| 7 | Arbitrary return URL | `returnUrl` and `return_url` in the body ignored; response URL is the configured one |
| 7 | Portal URL not persisted | **No URL column exists** in any billing table |
| 10 | Fabricated success return with `success=true`, `session_id` and `subscribed=1` | Granted nothing; page reports no active subscription |
| 10 | Two tabs | Stale tab kept old server truth, never fabricated; converged on refresh |

### Authorization, with real users and roles

Roles were created through the Roles UI and granted through an invitation
accepted by a second real account. No testing bypass was added.

| Member | Nav | Billing page | GET billing | POST checkout | POST portal |
| --- | --- | --- | --- | --- | --- |
| No `billing.view` (holds `analytics.view`) | Billing **absent**; Analytics and Plan and usage present | Honest refusal, no data | **403** | 403 | 403 |
| `billing.view` only | Billing present | Reads fully | **200** | **403** | **403** |
| Owner | Billing present | Full access | 200 | 200 | 200 |

The first row is also the proof that `analytics.view` was not reused for
billing. Owner now holds **24** permissions (22 plus the two billing codes), and
the role editor renders a `BILLING` group with both.

### Security inspection

| Checked | Result |
| --- | --- |
| Requests from the Billing page | 52, **all to `localhost:4200`**; **zero external hosts** |
| `api.stripe.com`, `api.openai.com`, `graph.facebook.com` | **Never contacted** |
| `localStorage` | One key: the selected workspace id |
| `sessionStorage` | Empty |
| Cookies readable to script | `XSRF-TOKEN` only; the session cookie is HttpOnly |
| Page HTML, storage and cookies scanned for `sk_`, `whsec_`, `price_`, `cus_`, `sub_`, `cs_`, `cvv`, `cvc`, `iban`, `openai`, `graph.facebook`, network key, HMAC | **No match** |
| `GET /billing` response body | No provider price, customer, subscription or session id |
| Console on the Billing page | **Zero messages** |

### Responsive and themes

Eight states (no subscription, offer selection, Active, Trialing, PastDue,
cancellation scheduled, Canceled/fallback, no offers available) plus a
75-character plan name, checked at **390 / 768 / 1440 / 1920** and in light and
dark at 390 / 768 / 1440.

**No horizontal page overflow in any combination**, and no element extending past
the viewport. Offer cards stack below 540px so the price and the Subscribe button
stop sharing a line. Currency renders from `Intl.NumberFormat` using the
currency's own exponent.

Measured contrast after the token fix (defect 5 below):

| Element | Light | Dark |
| --- | --- | --- |
| Status badge | 5.75:1 | 8.17:1 |
| Scheduled-cancellation note | 6.33:1 | 9.11:1 |
| Plan name | 17.77:1 | 14.81:1 |

### Accessibility

| Checked | Result |
| --- | --- |
| Heading order | h1 Billing, h2 Current subscription, h2 Available plans, h3 Starter; no skips |
| Visible focus | 1.6px solid outline with 1.6px offset on every control |
| Keyboard operation | Tab reaches Subscribe; **Enter starts the checkout** and navigates to the hosted page |
| Offer controls labelled | Each Subscribe is named for what it buys (defect 6 below) |
| Monthly and Annual textual | "per month" and "per year" on screen, "Monthly" and "Yearly" in the facts list |
| State not colour-only | Every state carries a word: Active, Trial, Payment failed, Suspended, Cancelled, each with a sentence |
| PastDue and cancellation understandable without colour | Yes, both are full sentences |
| Errors announced | The failure card is `role="alert"`, rendered assertive |
| Images without alt | 0 |

**Pre-existing, not a Phase 25 regression:** after signing out, the first sign-in
attempt answers 400 until the page is reloaded, because the antiforgery token the
SPA holds is stale. It affects every screen equally and predates this phase. It
is recorded here only because it was met during this run.

### Defects found in Chrome and fixed

All six were found in the browser, not by the suite. Each has a regression test.

1. **Billing failures spoke of the AI provider.** 502 and 503 are shared with the
   AI provider and are not in the client's "show the server's detail" list, so a
   payment failure rendered "The AI provider returned a response this platform
   could not use." Billing now supplies its own copy for those two kinds, the way
   the hosted chat already overrides them.
2. **Plan and usage denied that plans can change.** It still read "Plan changes
   are not available from this workspace yet" - true before this phase, false the
   moment billing shipped. It now points at Billing without offering the change,
   which belongs behind `billing.view`.
3. **After cancelling, the page credited the plan to an administrator.** "...which
   whoever runs this installation assigned to it" is false for a workspace that
   just landed on the fallback. Now: "This workspace has no active subscription,
   so it is on the plan above."
4. **Scheduled cancellation was signalled only by a changed field label.** The
   badge still read "Active - Payments are up to date" and only "Renews" became
   "Access ends". An explicit sentence now says the subscription will not renew.
5. **The status badge rendered as plain text.** The stylesheet used token names
   this design system does not define (`--text-warning`, `--surface-*-subtle`,
   `--radius-3`, `--border-danger`), so every fallback chained through to
   transparent and PastDue looked identical to Active. It now uses the real
   tokens (`--warning-fg/bg/border`, `--danger-*`, `--accent-*`, `--radius-pill`,
   `--radius-lg`, `--surface-raised`), and every token used is asserted to exist.
6. **Both Subscribe buttons were named "Subscribe".** Two offers sit side by side
   under identical "Starter" headings, so a reader moving between buttons had
   nothing to choose between. Each is now named for what it buys, for example
   "Subscribe to Starter, 29.00 per month".

### Observations, not defects

- After a subscription ends, the same offer cannot be bought again until the hour
  rolls over: the checkout idempotency key is (workspace, offer, hour) and the
  completed intent still holds. The platform says so plainly rather than starting
  a second purchase, and another offer remains buyable. Deliberate, and the safe
  direction, but worth knowing.
- Restarting the stub resets its identifier counter, which re-mints a customer
  reference another workspace already holds. The platform refused it correctly,
  which confirmed the previous session's conflict-handling fix in the browser.
  The stub now seeds its counter per run.

---

## Quality gates

| Gate | Result |
| --- | --- |
| `dotnet restore` | clean |
| `dotnet build -c Release` | **0 errors, 0 warnings** |
| `dotnet test -c Release` | 2,953 passed, 0 failed |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages, all 8 projects |
| `npm ci` | clean |
| `npm run build` | succeeds; `billing-page` is its own lazy chunk (14.43 kB raw / 3.98 kB transfer) |
| `npm run test:ci` | **672 passed, 0 failed** |
| `npm run format:check` | all files match |
| `npm audit` | 0 vulnerabilities |
| Lint | **no lint script exists in this project.** `package.json` has `ng`, `start`, `build`, `watch`, `test`, `test:ci`, `format`, `format:check` and nothing else. `npm audit fix` was not run. |

The one build warning is pre-existing and unrelated: `embed-chat.scss` exceeds
its 4 kB budget by 72 bytes.

---

## Regression surface re-run

Onboarding, roles and the authority ceiling, invitations, Phase 24 entitlements
and quotas, AI agents, Knowledge, website chat, WhatsApp, conversations, Phase 23
accounting, analytics and Plan & usage all pass unchanged. The only test edits
outside billing were mechanical: `billing.manage` had been the codebase's
canonical example of an *unrecognised* permission code in six test files, and now
that it exists those cases moved to `integrations.manage`, which is still
unimplemented.

---

## Remaining risks

1. **The stub is not Stripe.** It implements the shapes this adapter reads. A
   field Stripe returns differently would not be caught until a real test-mode
   account is used. The period-on-item change was caught by reading current
   documentation, not by the stub.
2. **Trial handling is written but only shallowly exercised.** An offer with
   `trialDays` sends `TrialPeriodDays` and `Trialing` grants the plan, but no
   test drives a trial through to its expiry.
3. **`invoice.paid` and `invoice.payment_succeeded` are treated as prompts to
   re-read**, like every other actionable event. That is correct but means a
   partial payment or a credited invoice is not distinguished — nothing in this
   phase needs to distinguish them.
4. **Re-subscribing to the same offer waits for the hour boundary.** The checkout
   idempotency key is (workspace, offer, hour), so a workspace that cancels
   cannot buy that same offer again until the hour rolls over. It is refused
   plainly rather than silently duplicated, and other offers stay buyable, but it
   is a rough edge somebody will meet.

## Assumptions

1. The commercial fallback is a new `free` plan with small finite limits, seeded
   by the migration. Its numbers are a starting point for this platform to
   adjust, not a commercial commitment.
2. Offers and provider price mappings are configured per deployment by whoever
   runs it. No endpoint and no screen creates one.
3. One Stripe account per deployment. There is no per-workspace key.
4. `PastDue` grants the paid plan during grace. The alternative — cutting a
   workspace off the moment a card fails — was rejected as punishing the
   commonest and most innocent failure.

## Next recommended step

Phase 25 is complete, including Chrome. The reviewer's call is whether to
checkpoint it. If a real Stripe test-mode account is ever attached, the one thing
worth re-checking against it is the subscription snapshot's shape — the stub
implements what this adapter reads, not what Stripe returns in full.
