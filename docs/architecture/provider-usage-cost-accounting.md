# Provider usage and cost accounting

How this platform knows what it spent with an AI provider, and why every part of
that answer is deliberately narrow.

> **This is accounting, not billing.** Nothing described here is an invoice, a
> subscription, a credit, a wallet, a customer charge, a markup, a tax, a quota
> or an amount due. There is no payment provider, no usage limit and no automatic
> suspension. "Estimated provider cost" is an internal operational figure: what
> *we* reckon *we* paid OpenAI. Reading it as anything else would be a mistake
> the vocabulary in this codebase is designed to prevent.

Phase 23. Builds on [ai-runtime.md](ai-runtime.md),
[openai-provider.md](openai-provider.md),
[knowledge-base-rag.md](knowledge-base-rag.md) and
[analytics-usage-foundation.md](analytics-usage-foundation.md).

---

## 1. The one rule everything else follows: the provider is the authority

**Decision.** A token count is recorded only when the provider reports it. There
is no estimation, no tokenizer, no character count, no "roughly four characters
per token". When the provider returns no usage, the operation is stored with
every count null.

**Why.** An estimated token count is indistinguishable on a screen from a
measured one, and the difference only surfaces when somebody reconciles a figure
against a provider's own statement and finds it does not match. At that point
every other number on the page becomes suspect. A platform that cannot say what a
call cost should say so.

**Forbids.** Deriving usage from prompt length, completion length, message text,
knowledge excerpt size or embedding input size. Defaulting an unreported count to
zero. "Approximately" anywhere in a stored figure.

**Consequence.** `ProviderTokenUsage` carries five independently nullable counts
(`InputTokens`, `CachedInputTokens`, `OutputTokens`, `ReasoningTokens`,
`TotalTokens`) and a `ProviderTokenUsage.Unreported` for the case where nothing
came back. The columns are nullable in PostgreSQL, so `SUM` ignores them: a call
nobody could quantify contributes nothing to a total rather than dragging it
down. Absent is not zero, at every layer.

The aggregate deliberately does **not** require `Total == Input + Output`.
Providers do not all define the total the same way, and refusing a response
because its arithmetic differs from ours would discard a real measurement in
favour of a rule we invented.

---

## 2. Nothing about the content is stored

**Decision.** The accounting record holds: the workspace, the provider, the kind
of operation, what it was for, the model asked for, the model the provider said
it used, the outcome, two instants, the provider's token counts, and — where a
rate was configured — a cost and the rates it was computed with. Nothing else.

**Why.** An accounting table is read by more people, kept for longer and exported
more often than a conversation table. Anything sensitive that reaches it has
escaped every boundary built to contain it.

**Forbids.** Storing prompts, completions, agent instructions, grounding context,
knowledge excerpts, embedding vectors, customer messages, customer identifiers,
phone numbers, employee emails, provider API keys, raw provider request JSON or
raw provider response JSON.

**Consequence.** `AiProviderUsage` has no `string` property at all — every text
value on it is a validated value object (`ModelName`, `CurrencyCode`). A
reflection test in the Domain suite asserts exactly that, so a future `string
Prompt { get; set; }` fails a test rather than a review. The API contract is
swept for the same vocabulary in the Api suite.

---

## 3. Whose money it is, is a compile-time question

**Decision.** Both provider ports take an explicit attribution as a required
parameter:

```csharp
Task<AiTextGenerationResult> GenerateAsync(
    AiTextGenerationRequest request, AiUsageContext usage, CancellationToken ct = default);

Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
    IReadOnlyList<string> inputs, AiUsageContext usage, CancellationToken ct = default);
```

`AiUsageContext.For(tenantId, purpose)` refuses an empty workspace.

**Why.** Ambient attribution — an `AsyncLocal`, a current-request accessor, a
tenant read back out of provider metadata — fails silently and in the worst
possible direction: a background job or a retry loses the ambient value and the
spend lands on whichever workspace happens to be in scope, or on none. A required
parameter cannot be forgotten, because the code does not compile.

**Forbids.** Inferring the workspace from the current HTTP request, the URL, an
`AsyncLocal`, or anything the provider returns.

**Consequence.** There are four production call sites, and each one declares both
the workspace and the reason:

| Call site | Purpose |
| --- | --- |
| `CustomerTurnRunner` | `ConversationGeneration` |
| `KnowledgeRetriever` | `ConversationRetrievalEmbedding` |
| `IndexKnowledgeDocumentHandler` | `KnowledgeIndexEmbedding` |
| `PreviewAiAgentHandler` | `AgentPreviewGeneration` |

A fifth call site cannot be added without saying whose money it spends.

---

## 4. Accounting happens at the provider boundary, once

**Decision.** `AiProviderUsageLedger` is called from the two OpenAI adapters and
nowhere else. Handlers, runners and endpoints know nothing about it.

**Why.** Accounting scattered across handlers drifts: one forgets the failure
path, another double-counts a retry, a third accounts for a call it did not make.
The only place that knows for certain whether a paid request was dispatched is
the code that dispatches it.

**Consequence.** One accounted operation is exactly one provider request. That
matters most for embeddings, where a document is chunked and sent in batches: a
row per batch, never a row per chunk, which would overstate the call count by an
order of magnitude. The batching rule is asserted from both ends — against a
canned transport in the Infrastructure suite, and against the indexing handler in
the Application suite.

---

## 5. The record is durable before the money is spent

**Decision.** `BeginAsync` commits a `Started` row before the provider request is
made, and its failure is not caught. If the row cannot be written, the call does
not happen.

**Why.** The alternative — account afterwards — loses precisely the calls worth
knowing about: the ones during an incident, where the process died between paying
and recording. Money spent with no record of it is the one outcome this design
refuses.

**Forbids.** Dispatching a paid provider request before its operation is durably
recorded. Swallowing a ledger failure to "let the customer's answer through".

**Consequence.** A database outage stops AI replies. That is a deliberate trade:
a platform that cannot account for spend should not spend. The first test in
`OpenAiUsageAccountingTests` asserts `transport.Calls == 0` when the ledger
refuses.

The ledger opens **its own `IServiceScopeFactory` scope per operation**, so the
usage row is independent of the caller's unit of work. Sharing the caller's
`DbContext` would mean a `SaveChanges` flushing a half-built conversation, and a
caller rollback taking the usage row with it.

---

## 6. Four outcomes, and one of them is honest ignorance

| Status | Meaning |
| --- | --- |
| `Started` | Dispatched. Nothing came back yet, or the process died. |
| `Succeeded` | The provider answered. Usage is whatever it reported. |
| `Failed` | A definitive refusal. Nothing was produced. |
| `Unknown` | A timeout or cancellation. The provider may well have been paid. |

**Why `Unknown` exists.** A request that timed out was very likely served and
charged. Recording it as a failure would understate spend; recording it as a
success would invent a token count. It is recorded as what it is.

**Forbids.** Retrying a paid provider call to resolve an `Unknown`. Both client
factories set `ClientRetryPolicy(maxRetries: 0)`, so the SDK does not retry
either — one dispatch is one charge.

Analytics reports `Started` and `Unknown` together as **unresolved**, because
both mean "dispatched, outcome not established".

---

## 7. No call means no row

A usage row is evidence that a provider request was made. These produce none,
because none is made:

- a rate-limited turn
- a duplicate customer message
- a conversation in Human handling mode
- an agent with no eligible knowledge (the query is never embedded)
- re-indexing unchanged text
- a provider that is not configured
- the Phase 18 Development OpenAI guard refusing to compose a live adapter
- an authorization failure, a validation failure or a stale ETag
- reading analytics

The negative cases are tested as carefully as the positive ones: "spent nothing"
has to mean no call, not a cheap one.

---

## 8. Money is decimal, and rated is not the same as free

**Decision.** Every money value is `decimal`, in code and in PostgreSQL
(`numeric(18,6)`). `AiModelRates.Rate` returns `null` — not zero — when any rate
it needs is missing, and the aggregate then leaves `EstimatedCost` null.

**Why.** Binary floating point cannot represent a tenth exactly, and an
accounting figure that drifts in the sixth decimal place is an accounting figure
nobody will trust. Separately, a model with no configured price did not cost
nothing; showing it at nought would make a total quietly wrong in the direction
that flatters us.

**Forbids.** `double` or `float` for money. Falling back to a similar model's
rate. Treating a missing rate as zero.

**Consequence.** Rating is all-or-nothing per operation: if cached input tokens
were reported but no cached rate is configured, the whole operation is unrated
rather than priced as though the cache did not exist. Unrated calls are counted
and shown separately, and the dashboard says in words that unrated does not mean
free.

Reasoning tokens are **not** added again — the provider already includes them in
the output count, and charging them twice would overstate every reasoning call.

---

## 9. Rates are configuration, versioned by date, and snapshotted onto the record

**Decision.** `AiPricing:Rates` is read and validated once, at startup. A rate
change is a new entry with a later `EffectiveFrom`, never an edit. When an
operation is rated, the rates used are copied onto the row.

**Why.** Nothing in this repository knows what a provider charges, and nothing
should pretend to: it holds what an operator wrote down. And a call made in March
must still cost March's price in June — otherwise a published figure changes
retroactively when somebody updates a config file, and no report ever reconciles
twice.

**Forbids.** Fetching prices from a provider or a web page. Guessing from a
similar model. Recomputing historical costs from current rates. A migration or
job that rewrites `estimated_cost`.

**Consequence.** A misspelled model, an unknown operation, a negative rate, a bad
currency, a missing effective date or two rates taking effect at the same instant
all stop the deployment from starting. That is the right time to find out.

---

## 10. Currencies are grouped, never added

**Decision.** There is no exchange rate anywhere in this platform and there must
not be one. Two currencies produce two totals, side by side, with a note saying
why. A single model billed in two currencies reports **no** cost figure — its
call and token counts stay exact.

**Why.** A converted figure is a fiction: it depends on a rate, a date and a
source, none of which this platform has. Showing one would be worse than showing
nothing, because it would look authoritative.

---

## 11. Finalisation is idempotent

`Succeed`, `Fail` and `MarkUnknown` all return `false` when the operation is
already settled, and change nothing. A finalisation arriving twice — a retry in
the calling code, two racing paths through a `catch` — cannot double a cost. The
Infrastructure suite proves it against real PostgreSQL, including eight
concurrent settles of one operation.

Settling an operation that is not there is not an error. The row is the
authority; if it is gone there is nothing to correct, and failing a caller who
has already paid the provider helps nobody.

---

## 12. It is part of analytics, not a new thing to authorise

**Decision.** Usage reads require `analytics.view`. There is no `usage.view`, no
`cost.view` and no `billing.view`. There is no new endpoint: the figures ride on
the existing dashboard response.

**Why.** A second permission is a second thing for an administrator to get right,
and getting it wrong exposes the same numbers. A second endpoint would let two
panels on one screen describe two different instants.

**Consequence.** `aiUsage` is `null` when the workspace has never called a
provider — absent rather than a panel of noughts, because "no calls" and "calls
that cost nothing" are different facts. The model breakdown is bounded to the ten
busiest.

---

## 13. The queries run in PostgreSQL

Every usage figure is a `COUNT` or a `SUM` computed server-side, over the
`(tenant_id, started_at)` index. The reader issues a fixed number of statements
whatever the workspace holds.

One wrinkle is worth recording, because it is not obvious and it was found by a
test rather than by review: **EF Core will not translate a choice between two
converted value objects** — neither `resolved ?? requested` nor the equivalent
conditional — and will not translate reaching inside a converted value object in
a `GroupBy` key. Both fall back to client evaluation, which on this table means
reading every row a workspace ever made.

So the model breakdown groups on the two stored columns as they are, takes the
busiest of those pairs, and coalesces them in memory over a few dozen rows. The
same applies to currency, which is grouped as the stored value object and
unwrapped afterwards. `WorkspaceUsageAnalyticsTests` asserts that every statement
touching `ai_provider_usages` counts or sums.

---

## 14. One migration, one table

`20260915100358_AddAiProviderUsageAccounting` adds `ai_provider_usages` and one
index. There is no rollup table, no daily aggregate, no pricing warehouse and no
billing table — those would be a second source of truth that has to be kept
consistent with the first, and the first is small and indexed.

There is no concurrency token on the table. Optimistic concurrency exists so two
people editing one record do not silently overwrite each other; nobody edits an
accounting record, and the idempotent settle above already makes a racing
finalisation safe.

The tenant foreign key is `DeleteBehavior.Restrict`. Spend history is not
something to cascade away.

---

## 15. What the screen says

The dashboard's usage section (`ProviderUsageCard`) is held to the same standard
as the data:

- the heading is **Estimated provider cost**, and the copy says it is an internal
  estimate of what this platform paid its providers
- an amount smaller than a penny is shown to six places rather than rounded to
  `$0.00`
- unrated calls get their own line saying *unrated does not mean free*
- a workspace with no calls gets no card at all
- two currencies appear side by side with an explanation that they are not added
- a model with no single currency reads **Not priced**, never a dash

A spec sweeps the rendered page for the vocabulary of billing — invoice, credit,
wallet, subscription, amount due, balance, quota, billing, payment, VAT — and
fails if any of it appears.
