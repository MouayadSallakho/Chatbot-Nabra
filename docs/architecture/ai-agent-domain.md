# The AI agent domain

**Status:** Phase 6 introduced the provider-independent core Domain
representation of an AI agent. **Domain only** — there is no persistence, no
Application use case, no endpoint and no AI provider integration. Nothing marked
DEFERRED below exists in code.

---

## The one sentence this document exists to protect

> **An `AiAgent` is not an OpenAI Assistant.**

It is also not an Anthropic agent, a WhatsApp bot, an OpenAI thread, an LLM
session, a vector store, a prompt, or a remote object of any kind. It is what a
business means when it says *"our sales assistant"* — a decision the tenant made,
recorded by this platform, which would still be meaningful if every AI provider
on the market were replaced tomorrow.

That distinction is the whole value of the type. The moment a model name, a
sampling parameter or a provider identifier appears on it, the platform's core
concept becomes a vendor's data structure, and swapping or adding a provider
stops being an Infrastructure change and becomes a Domain migration.

---

## DECIDED NOW

### A1 — The agent is provider-independent, and that is enforced

**Decision.** `AiAgent` carries exactly seven properties: `Id`, `TenantId`,
`Name`, `Instructions`, `Status`, `CreatedAt`, `UpdatedAt`. Nothing else.

**Forbids.** Model name, provider id, temperature, top-p, max tokens, reasoning
effort, stop sequences, response format, OpenAI-specific or Anthropic-specific
settings, prompt objects, role/message arrays, JSON configuration blobs,
embedding ids, vector ids, file references, tool definitions, channel
connections and credentials.

**Enforcement, not aspiration.** Two tests fail if this erodes:

- `The_aggregate_carries_no_provider_or_model_configuration` asserts the exact
  property set by reflection, so adding any field is a deliberate, visible act.
- `The_agent_namespace_references_no_provider_type` asserts no type in
  `OmniAI.Domain.AiAgents` is named after a provider, model, prompt, embedding,
  vector, tool, knowledge source or channel.

The Domain project also still references nothing at all — the Phase 1 dependency
guard covers that — so no vendor SDK can reach this code even indirectly.

### A2 — Tenant ownership is structural and permanent

**Decision.** `AiAgent.TenantId` is get-only with **no setter of any kind, not
even a private one**, and there is no `ChangeTenant`, `TransferTo` or `MoveTo`
operation.

**Why structural rather than validated.** Tenant ownership is the platform's
isolation boundary. A private setter plus a rule saying "never call it" relies on
every future author knowing the rule. Removing the setter entirely means there is
no code path — correct, buggy or malicious — that can reassign ownership, because
none exists to call.

**Why moving an agent is not a property update anyway.** An agent's
`Instructions` are the tenant's own business writing. Moving the agent to another
tenant would hand one business's confidential configuration to another; that is a
data transfer with its own authorisation, audit and consent rules, not an
assignment. If such an operation is ever genuinely needed it will be an explicit,
audited migration that creates a new agent under the new owner.

**Enforcement.** `The_owning_tenant_has_no_setter_at_all_not_even_a_private_one`
and `There_is_no_operation_that_moves_an_agent_between_tenants` assert both
halves by reflection. This matches the identical, already-accepted decision on
`Conversation.TenantId`.

### A3 — Aggregate boundary: deliberately narrow

**Decision.** The aggregate is one object with no child collections. It owns its
name, its instructions and its lifecycle state, and nothing else.

**Why.** Every candidate for inclusion — knowledge sources, tools, channel
assignments, handoff rules, memory, guardrails, budgets — has its own rules, its
own lifecycle and, in several cases, its own security and privacy design. Pulling
them in now would mean guessing at those rules, and an aggregate that owns
concepts it cannot enforce invariants over is an aggregate in name only.

**Consequence.** When those concepts arrive, each decides for itself whether it
belongs inside this boundary or references the agent from outside. That decision
stays available precisely because it was not pre-empted here.

### A4 — `AiAgentId`: a distinct type, written out by hand

**Decision.** A `readonly record struct` wrapping `Guid`, generated with
`Guid.CreateVersion7()`, with `From` rejecting `Guid.Empty` and an `IsEmpty`
property making the `default` hole visible.

**Why it mirrors `TenantId` and `ConversationId` exactly.** Consistency here has
a practical payoff: any reader who has understood one id type has understood all
of them, and every id in the platform has the same ordering property and the same
empty-value semantics.

**Forbids.** A generic `StronglyTypedId<T>` base type, source generators, and any
third-party id package. Four hand-written members per id type is less machinery
than a generic framework, and it keeps each validation message specific to the
thing being identified.

**The known limitation is inherited and accepted.** C# permits
`default(AiAgentId)`, which holds `Guid.Empty`. No factory can produce it, but the
language hole cannot be closed, so `IsEmpty` exists for boundaries accepting an
identity from outside the Domain — and `AiAgent.Create` uses exactly that to
reject an empty `TenantId`.

### A5 — `AiAgentName`: a display label, no uniqueness rule

**Decision.** A value object: required, non-blank, surrounding whitespace
trimmed, at most **120** characters, no control characters at all.

**Why 120 rather than `TenantName`'s 200.** They are different kinds of name. A
tenant name is a real legal or trading name the business already has, including
long non-Latin forms and corporate suffixes. An agent name is a short label the
tenant invents to tell its own agents apart, and it appears in lists, pickers and
tabs where anything long is truncated by the interface anyway. 120 sits far above
any realistic label — `Customer Support Assistant` is 26 — while bounding display
and storage cost.

**Why control characters are rejected outright**, including tab and line breaks,
unlike `AgentInstructions`: a single-line label has no use for interior
formatting, and allowing it would corrupt lists, logs and rendered output.

**No uniqueness rule was invented, and this is deliberate.** Two tenants may each
have a `Sales Assistant`. Nothing currently forbids one tenant from having two
agents with the same display name. Three reasons:

1. **No product requirement asks for it.** Inventing one would mean guessing the
   scope (global? per tenant? case-insensitive? including disabled agents?), and
   a uniqueness rule is far more expensive to relax later than to add.
2. **A name is a label, not an identifier.** `AiAgentId` is the identifier.
3. **The Domain could not enforce it anyway.** Knowing whether a name is taken
   requires knowledge of every stored agent — the same reasoning already recorded
   for `TenantSlug`, where uniqueness became a persistence guarantee.

**No silent truncation.** An over-long name is rejected with a message that says
so explicitly, because quietly shortening it would mean a tenant's agent
acquiring a different name from the one they typed.

### A6 — `AgentInstructions`: business configuration, not a prompt

**Decision.** A value object: required, non-blank, **surrounding** whitespace
trimmed and **interior formatting preserved exactly**, at most **20,000**
characters, control characters rejected except tab, carriage return and line
feed.

**Why interior formatting is preserved.** An author who writes numbered rules on
separate lines means them to stay on separate lines. Collapsing blank lines or
indentation would change what the agent was told to do — so trimming the outside
is the only normalisation, and everything inside is kept byte for byte. This
matches the accepted `Message` text rule for the same reason.

**Why 20,000, and what it is not.** It is a **platform resource-safety bound,
deliberately not any provider's context limit.** Provider limits are measured in
tokens rather than characters, differ per model, and change without notice, so
encoding one here would put a vendor's current pricing decision inside the
Domain and would be wrong within months. 20,000 characters is roughly 40 KB — far
above any realistic hand-written instruction set, while bounding storage, log and
render cost, and bounding the size of a value the platform will read on every
agent turn. Whether a given instruction set actually fits a chosen model's
context, and what to do when it does not, is a provider-adapter concern.

`The_maximum_is_a_platform_bound_not_a_provider_context_limit` guards the
decision, so a future "let's align this with the model's window" change has to
argue with a failing test.

**Forbids.** Prompt templates, role/content arrays, system-message objects,
provider request bodies, variable interpolation syntax and any structured AI
configuration. It is one piece of text a business author wrote.

### A7 — Lifecycle: three states, one refusal

**Decision.** `Draft` → `Active` → `Disabled`, with `Draft = 1` and no zero
member.

| From | Operation | To |
| --- | --- | --- |
| `Draft` | `Activate` | `Active` |
| `Active` | `Disable` | `Disabled` |
| `Disabled` | `Activate` | `Active` |
| `Active` | `Activate` | `Active` — no-op |
| `Disabled` | `Disable` | `Disabled` — no-op |
| `Draft` | `Disable` | **refused** — `DomainStateException` |

**A new agent starts `Draft`.** Deciding when an agent begins serving customers
belongs to the tenant; it must not be a side effect of saving a record.

**Activate and Disable are idempotent** where the target state is already
reached, for the reason already accepted for `Conversation.Close`/`Reopen` and
`Tenant.Suspend`/`Activate`: the caller's intent is satisfied, and demanding a
check-then-act sequence would invite a race while preventing nothing.

**`Draft` → `Disable` is refused, and that is the one meaningful exception.** A
draft has never been switched on, so there is nothing to withhold, and treating
the request as a no-op would silently accept a call that made no sense. Allowing
it would be worse: there is no path back from `Disabled` to `Draft`, so a draft
disabled by mistake could only be recovered by activating it — precisely what the
tenant was trying to avoid.

**Deliberately absent states.** `Deleted` and `Archived` are retention concerns
and a status is the wrong place to record them. `Training` and `Publishing`
describe asynchronous work that does not exist. `Error` would mix a transient
integration failure into a configuration lifecycle. `Suspended` and `Trial` are
billing concerns belonging to the tenant, not to each of its agents.
`The_status_enum_exposes_exactly_the_three_agreed_states` fails if any of them
appears without a decision.

### A8 — Activation invariants: satisfied by construction, not by a guard

**Decision.** An agent may only become `Active` when its required Domain
configuration is valid — a valid `Name` and valid `Instructions`. **`Activate`
contains no runtime check for this, because the condition cannot be false.**

**Why there is no guard.** `Name` and `Instructions` are non-null value objects
that validated themselves at construction, they are required by `Create`, and no
operation can leave either missing or malformed. A configuration check inside
`Activate` would therefore be a check that can never fail — dead code dressed up
as an invariant, which is worse than no code because it implies a risk that does
not exist and invites the next author to assume other things are checked too.

This is an honest consequence of the aggregate being **always valid**, which is
the style already accepted for `Tenant`, `Conversation` and `Message`. The
alternative design — instructions optional until activation, so `Draft` means
"incomplete" and activation has something real to enforce — was considered and
rejected: it would make `Instructions` nullable forever, so every future consumer
would have to handle "an agent with no instructions", a permanent cost paid for a
convenience the editing interface can solve by holding an unsaved draft.

**What changes when a real requirement arrives.** When a later phase adds a
requirement that *can* be unmet — a selected model, a connected channel, an
approved tool — that requirement is enforced in `Activate`, and only then does a
guard mean something. `Activation_never_fails_on_domain_configuration_because_it_cannot_be_invalid`
records the current reasoning in executable form.

**Readiness is not validity.** Whether an active agent is additionally *ready* to
serve a particular channel depends on integrations and credentials that do not
exist yet, and it belongs to the Application layer. Mixing it into Domain state
would make the aggregate's validity depend on the health of external systems —
so an agent could become invalid because a third party had an outage.

### A9 — Time: supplied, UTC, and a high-water mark

**Decision.** Every state-changing operation takes the effective instant as a
`DateTimeOffset` parameter, normalised to UTC on the way in. `default` is
rejected. No Domain code reads the clock.

- **`CreatedAt`** — immutable, set once from the supplied creation instant.
- **`UpdatedAt`** — starts at the creation instant; advances only when something
  actually changed; **never moves backwards.**

**The exact rule for out-of-order instants, which the brief asked to be settled
explicitly:**

> A mutation supplied with an instant earlier than the current `UpdatedAt` **is
> still applied**. Only the timestamp is left alone.

Both halves matter. Applying the edit is required because the Domain records what
it is told and does not silently discard a caller's change — the same principle
that the Phase 3 remediation established when it removed the forward-time
invariant from `Conversation`. Protecting the mark is required because a
`UpdatedAt` that regresses is a lie to anything sorting, paging or caching on
"last changed".

**This is not a version token and cannot be used as one** — two edits can share
an instant. Losing a concurrent update is prevented by the persistence layer's
optimistic concurrency, which is already in place and accepted.

**An operation that changes nothing does not advance the mark.** Renaming to the
current name, updating to identical instructions, or activating an already active
agent all return without touching `UpdatedAt`, because nothing about the agent
changed. Value-object equality is what makes that comparison exact.

**Consistency.** This is the same high-water-mark semantics accepted for
`Conversation.LastActivityAt`, and the same "aggregates supply their own time"
decision recorded in [README.md](README.md) decision 11 — no clock abstraction, no
injected time provider, one parameter pushed down instead.

### A10 — Behaviour, not setters

**Decision.** Four operations — `Rename`, `UpdateInstructions`, `Activate`,
`Disable` — and no public setters. `No_property_exposes_a_public_setter` and
`The_aggregate_exposes_only_the_agreed_operations` assert both by reflection.

**`UpdateInstructions` is allowed while the agent is `Active`.** Correcting a
live agent's behaviour is the normal way a business fixes it, and requiring it to
be disabled first would mean taking the agent off the air to fix a typo. The
previous text is replaced, not kept — see A11.

**Error strategy is unchanged and reused.** `DomainValidationException` for
invalid input, `DomainStateException` for the one invalid transition. No new
exception type, no AI-specific hierarchy, no third-party result package, and no
HTTP vocabulary anywhere — how these surface to a client remains a future
Application/API concern.

### A11 — No instruction history, and the consequence is stated

**Decision.** `UpdateInstructions` replaces the text. There is no
`AgentInstructionVersion`, no history, no draft-versus-published copy and no
publishing step.

**Consequence, stated rather than hidden.** Once instructions are updated, what
they used to say is gone. That is a real limitation and it is recorded here, not
papered over: version history is a product feature with its own requirements —
who may publish, what customers see mid-change, how a rollback interacts with
live conversations — and none of those have been decided. Building a versioning
scheme now would mean guessing all of them.

---

## DEFERRED

Not implemented, and not described anywhere as if it were:

| Area | Deferred |
| --- | --- |
| Persistence | `AiAgent` mapping, migration, schema, repository — Phase 6 touched no `DbContext`, configuration or migration |
| Application / API | Create/read/update use cases, handlers, DTOs, endpoints |
| AI providers | OpenAI, Anthropic, any SDK, any client, any API key, provider selection, model selection and routing, fallback |
| Model configuration | Model name, temperature, top-p, max tokens, reasoning effort, response format |
| Prompt lifecycle | Instruction versioning, history, drafts, publishing, rollback |
| Knowledge / RAG | Knowledge sources, documents, chunking, embeddings, vector stores, retrieval |
| Tools | Tool and function definitions, business actions, tool authorisation |
| Channels | WhatsApp, Instagram, Messenger, web chat, agent-to-channel assignment |
| Runtime | Human handoff, conversation memory, profile facts, summaries, guardrails, evaluation, cost and budget controls, multilingual configuration |

**Memory and profile facts are noted separately** because they are not merely
unbuilt: they need a privacy and security design — what may be retained, for how
long, under whose consent, and how it is deleted — before any type is written.

---

## Things a reviewer can check independently

1. `AiAgent` has exactly seven properties, asserted by reflection.
2. `TenantId` has no setter at all, and no method mentions Tenant, Transfer or
   Move.
3. The status enum has exactly `Draft`, `Active`, `Disabled` and no zero member.
4. `AgentInstructions` preserves interior newlines, blank lines, tabs and CRLF,
   and trims only the outside.
5. `AiAgentName` rejects tabs and line breaks; `AgentInstructions` accepts them.
6. Neither value object truncates; both say "not truncated" when rejecting.
7. `UpdatedAt` does not regress when an earlier instant is supplied, and the edit
   is still applied.
8. A no-op operation leaves `UpdatedAt` untouched.
9. `Draft` → `Disable` throws `DomainStateException`; every other listed
   transition succeeds.
10. `git status` shows changes only under `backend/src/OmniAI.Domain/AiAgents`
    and `backend/tests/OmniAI.Domain.Tests/AiAgents` — no persistence,
    Application, API or frontend file was touched, and no package was added.
