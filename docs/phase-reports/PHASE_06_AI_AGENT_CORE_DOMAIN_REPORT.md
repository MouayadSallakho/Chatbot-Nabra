# Phase 6 — AI Agent Core Domain

**Date:** 2026-09-04
**Scope:** The provider-independent Domain representation of an AI agent.
**Domain only** — no persistence, no Application use case, no endpoint, no AI
provider, no channel, no frontend.

---

## 1. Phase objective

Introduce the core Domain concept of an AI agent: a tenant-owned business
configuration describing an automated customer-facing assistant. Establish it as
a platform concept that would survive replacing every AI vendor on the market,
and prove that provider independence is enforced rather than merely intended.

## 2. Final status

**PASS**

Every authorised objective is implemented. `0 Warning(s), 0 Error(s)` in Release
across all 8 projects; **405 tests defined, 405 passing, 0 failing, 0 skipped**;
8/8 projects report no vulnerable packages. No package was added. Only the Domain
project and its test project changed.

One decision is flagged for the reviewer's attention because it is a judgement
call rather than a mechanical outcome: **`Activate` contains no configuration
guard, because the condition cannot be false** (§12). The reasoning, and the
alternative design that was rejected, are set out in full there.

No commit was created. Deployment and VPS access were not authorised and did not
occur.

## 3. Baseline commit

```
d2393e3  feat: implement tenant onboarding vertical slice
```

The accepted Phase 5 checkpoint, verified as `HEAD` before any file was touched.
Phases 1–5 were preserved; no accepted decision was reverted.

## 4. Initial Git state

```
$ git status --short          # (no output — clean working tree)
$ git log -5 --oneline
d2393e3 feat: implement tenant onboarding vertical slice
38f7e48 feat: establish postgresql persistence foundation
e714005 feat: establish conversation and message domain
478dd2b feat: establish tenant core domain
b7ab2ea chore: establish omnichannel ai platform foundation
```

Clean, as required. Before writing anything, the following were read: `TenantId`,
`ConversationId` and `MessageId` (the accepted id strategy), `Tenant` and
`Conversation` (aggregate style, immutable `TenantId`, idempotent status
operations), `Message` (text normalisation and the platform-bound maximum),
`TenantName`/`TenantSlug` (value-object style and the recorded reasoning about
uniqueness), `ConversationStatus` (enum style, no zero member),
`DomainValidationException`/`DomainStateException`, the architecture documents,
the Domain test conventions, and the Phase 4 persistence decisions — the last of
these so that Phase 7 mapping is possible without changing anything now.

## 5. Domain types introduced

Five, all in the new namespace `OmniAI.Domain.AiAgents`:

| Type | Kind | Purpose |
| --- | --- | --- |
| `AiAgentId` | `readonly record struct` | Identity |
| `AiAgentName` | `sealed record` | Business-facing display name |
| `AgentInstructions` | `sealed record` | What the agent is for, in the business's words |
| `AiAgentStatus` | `enum` | `Draft`, `Active`, `Disabled` |
| `AiAgent` | `sealed class` | The aggregate |

No other type was added anywhere in the solution.

## 6. `AiAgentId` design

A `readonly record struct` wrapping `Guid`, mirroring `TenantId` and
`ConversationId` exactly:

- `New()` generates `Guid.CreateVersion7()` — time-ordered, so keys insert
  locally in an index rather than scattering across a B-tree.
- `From(Guid)` rejects `Guid.Empty` with `DomainValidationException`.
- `IsEmpty` exposes the `default(struct)` hole, which C# permits and no factory
  can produce.
- Value equality and a stable `GetHashCode` come from the record struct.
- `ToString()` is the invariant dashed form.

**No generic `StronglyTypedId<T>`**, no source generator, no third-party id
package. Four hand-written members per id type is less machinery than a generic
framework, and it keeps each validation message specific to the thing identified.

The `default` limitation is inherited from the already-accepted `TenantId`
decision and is put to work rather than merely documented: `AiAgent.Create`
rejects an empty `TenantId` explicitly, which is the one identity a caller can
fabricate.

10 tests, including an assertion that `New()` really produces a **version 7**
UUID — the ordering property the design depends on, so worth checking rather than
assuming.

## 7. `AiAgent` aggregate design

Seven properties and nothing else:

| Property | Mutability |
| --- | --- |
| `Id` | get-only, no setter |
| `TenantId` | get-only, **no setter at all** (§8) |
| `Name` | `private set`, via `Rename` |
| `Instructions` | `private set`, via `UpdateInstructions` |
| `Status` | `private set`, via `Activate`/`Disable` |
| `CreatedAt` | get-only, no setter |
| `UpdatedAt` | `private set`, high-water mark (§13) |

Four operations: `Rename`, `UpdateInstructions`, `Activate`, `Disable`. One
private constructor, one static `Create` factory.

**No child collections, and the boundary is narrow on purpose.** Knowledge
sources, tools, channel assignments, handoff rules, memory, guardrails and
budgets each have their own rules and, in several cases, their own security and
privacy design. An aggregate that owns concepts it cannot enforce invariants over
is an aggregate in name only, so each of those will decide for itself whether it
belongs inside this boundary — a decision that stays available precisely because
it was not pre-empted.

**Provider independence is enforced by two reflection tests**, not by intent:

- `The_aggregate_carries_no_provider_or_model_configuration` asserts the exact
  seven-property set, so adding a field is a deliberate, visible act that breaks
  a test.
- `The_agent_namespace_references_no_provider_type` asserts no type in the
  namespace is named after a provider, model, prompt, embedding, vector, tool,
  knowledge source or channel.

The Domain project still references nothing at all — the Phase 1 dependency guard
covers that — so no vendor SDK can reach this code even transitively.

## 8. Tenant ownership invariant

**Every agent belongs to exactly one tenant, permanently.**

`TenantId` is get-only with **no setter of any kind, not even a private one**, and
there is no `ChangeTenant`, `TransferTo` or `MoveTo` operation. This is the
identical, already-accepted treatment of `Conversation.TenantId`.

**Structural rather than validated, deliberately.** A private setter plus a rule
saying "never call it" relies on every future author knowing the rule. Removing
the setter entirely means no code path — correct, buggy or malicious — can
reassign ownership, because none exists to call.

**And moving an agent would not be a property update anyway.** An agent's
instructions are the tenant's own confidential business writing; moving the agent
would hand one business's configuration to another. That is a data transfer with
its own authorisation, audit and consent rules.

`AiAgent.Create` also rejects an empty `TenantId` with
`DomainValidationException`, so an agent cannot be created ownerless.

Two tests assert both halves by reflection:
`The_owning_tenant_has_no_setter_at_all_not_even_a_private_one` and
`There_is_no_operation_that_moves_an_agent_between_tenants`.

## 9. Name rules

`AiAgentName`, a value object. Required; non-null; non-empty; whitespace-only
rejected; surrounding whitespace trimmed; interior spacing preserved; at most
**120** characters measured after trimming; **all** control characters rejected;
never truncated.

**Why 120 rather than `TenantName`'s 200.** Different kinds of name. A tenant name
is a real legal or trading name the business already has, including long non-Latin
forms and corporate suffixes. An agent name is a short label the tenant invents to
tell its own agents apart, appearing in lists, pickers and tabs where anything
long is truncated by the interface anyway. 120 sits far above any realistic label
— `Customer Support Assistant` is 26.

**Why control characters are rejected outright**, including tab and line breaks
which `AgentInstructions` allows: a single-line label has no use for interior
formatting, and permitting it would corrupt lists, logs and rendered output.

**No uniqueness rule was invented.** Two tenants may each have a
`Sales Assistant`, and nothing currently forbids one tenant from having two agents
with the same display name. Three reasons: no product requirement asks for it and
guessing the scope (global? per tenant? case-insensitive? including disabled
agents?) would be expensive to relax later; a name is a label while `AiAgentId` is
the identifier; and the Domain could not enforce uniqueness anyway, since that
needs knowledge of every stored agent — the same reasoning already recorded for
`TenantSlug`.

**No silent truncation.** The rejection message says "not truncated" explicitly,
and a test asserts it, because quietly shortening a name would mean a tenant's
agent acquiring a name they never typed.

## 10. Instructions rules

`AgentInstructions`, a value object. Required; non-null; non-empty;
whitespace-only rejected; **surrounding** whitespace trimmed; **interior
formatting preserved exactly**; at most **20,000** characters measured after
trimming; control characters rejected except tab, carriage return and line feed;
never truncated.

**Interior formatting is preserved byte for byte** — line breaks, blank lines,
indentation, list markers, CRLF. An author who writes numbered rules on separate
lines means them to stay on separate lines; collapsing that would change what the
agent was told to do. Six tests cover this specifically, including blank lines,
tabs, CRLF, and trimming only the outside of a multiline value.

**Why 20,000, and what it is not.** A **platform resource-safety bound,
deliberately not any provider's context limit.** Provider limits are measured in
tokens rather than characters, differ per model, and change without notice, so
encoding one here would put a vendor's current pricing decision inside the Domain
and would be wrong within months. 20,000 characters is roughly 40 KB — far above
any realistic hand-written instruction set, while bounding storage, log and render
cost, and bounding the size of a value the platform will read on every agent turn.
Whether an instruction set fits a chosen model's context is a provider-adapter
concern.

`The_maximum_is_a_platform_bound_not_a_provider_context_limit` guards the
decision, so a future "align this with the model's window" change has to argue
with a failing test.

**This is configuration, not a provider payload.** No prompt template, no
role/content array, no system-message object, no request body, no variable
interpolation syntax, no model name, provider id, temperature, top-p or token
limit. How a provider is eventually asked to honour the text — which message role
it lands in, how it combines with retrieved context or tool definitions, how
tokens are budgeted — is that adapter's concern in a later phase.

## 11. Lifecycle / status design

`AiAgentStatus`: `Draft = 1`, `Active = 2`, `Disabled = 3`. No zero member, so an
uninitialised value is detectable rather than silently `Draft`.

| From | Operation | Result |
| --- | --- | --- |
| `Draft` | `Activate` | `Active` |
| `Active` | `Disable` | `Disabled` |
| `Disabled` | `Activate` | `Active` |
| `Active` | `Activate` | no-op, `UpdatedAt` untouched |
| `Disabled` | `Disable` | no-op, `UpdatedAt` untouched |
| `Draft` | `Disable` | **`DomainStateException`** |

**A new agent starts `Draft`** — deciding when an agent begins serving customers
belongs to the tenant, not to the act of saving a record.

**Idempotence where the target state is already reached**, matching the accepted
`Conversation.Close`/`Reopen` and `Tenant.Suspend`/`Activate` behaviour: the
caller's intent is satisfied, and demanding a check-then-act sequence would invite
a race while preventing nothing.

**`Draft` → `Disable` is refused**, and it is the one meaningful exception to
idempotence. A draft has never been switched on, so there is nothing to withhold,
and a no-op would silently accept a call that made no sense. Allowing it would be
worse: there is no path from `Disabled` back to `Draft`, so a draft disabled by
mistake could only be recovered by activating it — precisely what the tenant was
avoiding. A test also confirms the refusal leaves `UpdatedAt` untouched.

**Deliberately absent:** `Deleted` and `Archived` (retention concerns; a status is
the wrong place), `Training` and `Publishing` (asynchronous work that does not
exist), `Error` (would mix transient integration failure into a configuration
lifecycle), `Suspended` and `Trial` (billing concerns belonging to the tenant, not
each of its agents). Two tests fail if any hidden state or a zero member appears.

## 12. Activation invariants

**The requirement:** an agent may only become `Active` when its required Domain
configuration is valid — a valid `Name` and valid `Instructions`.

**The implementation: `Activate` contains no runtime check for this, because the
condition cannot be false.** `Name` and `Instructions` are non-null value objects
that validated themselves at construction, both are required by `Create`, and no
operation can leave either missing or malformed. A configuration check inside
`Activate` would be a check that can never fail — dead code dressed up as an
invariant, which is worse than nothing because it implies a risk that does not
exist and invites the next author to assume other things are checked too.

This is an honest consequence of the aggregate being **always valid**, the style
already accepted for `Tenant`, `Conversation` and `Message`.

**The alternative was considered and rejected.** Making `Instructions` optional
until activation would give `Draft` the meaning "incomplete" and give `Activate`
something real to enforce. It was rejected because `Instructions` would then be
nullable forever, so every future consumer would have to handle "an agent with no
instructions" — a permanent cost paid for an editing convenience that the
interface can solve by holding an unsaved draft. The brief's own state list
includes `Instructions` unqualified and its rules say "reject null", which points
the same way.

**What changes when a real requirement arrives.** A later requirement that *can*
be unmet — a selected model, a connected channel, an approved tool — gets enforced
in `Activate`, and only then does a guard mean something.
`Activation_never_fails_on_domain_configuration_because_it_cannot_be_invalid`
records the present reasoning in executable form so the next author meets it.

**Nothing external is required to activate**, exactly as instructed: no OpenAI
connection, no knowledge base, no channel, no tools, no credentials — none of
those concepts exist.

**Readiness is not validity.** Whether an active agent is *ready* to serve a
particular channel depends on integrations and credentials that do not exist yet
and belongs to the Application layer. Mixing it into Domain state would make the
aggregate's validity depend on the health of external systems, so an agent could
become invalid because a third party had an outage.

## 13. Time strategy

Consistent with `Conversation`: every state-changing operation takes the effective
instant as a `DateTimeOffset` parameter, normalised with `ToUniversalTime()`, and
`default` is rejected as "not a real point in time". **No Domain code calls
`DateTimeOffset.UtcNow`** — no clock abstraction, no fake clock, no injected time
provider; one parameter pushed down instead, per the accepted decision 11.

- **`CreatedAt`** — immutable, set once. A test mutates the agent three ways and
  asserts it is unchanged.
- **`UpdatedAt`** — starts at the creation instant; advances only on a real
  change; **never regresses.**

**The exact out-of-order rule, which the brief asked to be settled and
documented:**

> A mutation supplied with an instant earlier than the current `UpdatedAt` **is
> still applied.** Only the timestamp is left alone.

Both halves are deliberate. Applying the edit is required because the Domain
records what it is told and does not silently discard a caller's change — the
principle the Phase 3 remediation established when it removed the forward-time
invariant from `Conversation`. Protecting the mark is required because an
`UpdatedAt` that regresses is a lie to anything sorting, paging or caching on
"last changed". Two tests cover the two halves separately.

**High-water-mark semantics were chosen, consistent with the accepted
`Conversation.LastActivityAt` decision**, and no strong Domain reason to deviate
was found.

**It is not a version token and cannot be used as one** — two edits can share an
instant. Losing a concurrent update is prevented by the persistence layer's
optimistic concurrency, already in place since Phase 4.

**A no-op does not advance it.** Renaming to the current name, updating to
identical instructions, or activating an already active agent all return without
touching `UpdatedAt`, because nothing about the agent changed. Value-object
equality makes that comparison exact, and three tests assert it.

## 14. Immutability strategy

| Property | Protection |
| --- | --- |
| `Id`, `CreatedAt` | no setter at all |
| `TenantId` | no setter at all, and no operation that could reassign it |
| `Name`, `Instructions`, `Status`, `UpdatedAt` | `private set`, reachable only through the four named operations |

Asserted, not assumed — five reflection tests:
`No_property_exposes_a_public_setter`,
`The_owning_tenant_has_no_setter_at_all_not_even_a_private_one`,
`The_identity_and_creation_instant_have_no_setters_either`,
`There_is_no_operation_that_moves_an_agent_between_tenants`, and
`The_aggregate_exposes_only_the_agreed_operations` (which asserts the operation
set is exactly `Activate`, `Disable`, `Rename`, `UpdateInstructions`).

Both value objects are `sealed record` types with a private constructor and a
get-only `Value`, so an invalid or mutated instance cannot exist.

## 15. Error strategy

**Reused unchanged. No new exception type was introduced.**

| Failure | Type |
| --- | --- |
| Invalid name, instructions, tenant id or instant | `DomainValidationException` |
| `Draft` → `Disable` | `DomainStateException` |
| Null value object passed to the aggregate | `ArgumentNullException` |

The last of these follows the accepted `Tenant.Create`/`Rename` convention:
passing null where a value object is required is a programming defect, not a
business rule violation, so a BCL argument exception is correct.

**No AI-specific exception hierarchy, no third-party result package, no
`Result<T>` framework, and no HTTP vocabulary** — no status codes, no ASP.NET
types, no `ProblemDetails`, no transport mapping. How these surface to a client
remains a future Application/API concern, exactly as recorded in decision 9.

## 16. Tests added

**116 new Domain tests**, all in `OmniAI.Domain.Tests/AiAgents/`. No mocking
framework — none was needed, and none is installed anywhere in the solution.

| Class | Tests | Coverage |
| --- | --- | --- |
| `AiAgentIdTests` | 10 | `From` keeps the value; empty GUID rejected; `New` usable, distinct, and a real **version 7** UUID; equality, inequality, hash code; `default` reports `IsEmpty`; created ids never empty; invariant `ToString` |
| `AiAgentNameTests` | 27 | Six realistic names including Arabic and accented forms; trimming; interior spacing preserved; seven blank/whitespace forms rejected; exactly-at-maximum accepted; one-over rejected; maximum measured after trimming; **not truncated** message; four control-character forms rejected including tab and newline; equality, inequality, case sensitivity; `ToString` |
| `AgentInstructionsTests` | 26 | Realistic instructions; trimming; **interior line breaks, blank lines, indentation, tabs and CRLF preserved**; trims only the outside of a multiline value; non-Latin preserved; seven blank forms rejected; boundary at and one over the maximum; maximum after trimming; **not truncated** message; the maximum is a platform bound not a provider limit; three control-character forms rejected; equality including formatting-sensitivity; `ToString` |
| `AiAgentTests` | 53 | Creation, time, rename, instruction update, lifecycle, immutability and provider independence — broken down below |

**`AiAgentTests` by group:**

| Group | Covers |
| --- | --- |
| Creation (10) | Supplied name and instructions; non-empty and distinct identity; tenant recorded; starts `Draft`; values normalised; empty `TenantId` rejected; null name and null instructions rejected; unset creation instant rejected |
| Time (8) | Creation instant recorded; `UpdatedAt` starts equal to `CreatedAt`; instants normalised to UTC; determinism (no clock read); `CreatedAt` immutable through three mutations; **`UpdatedAt` does not regress**; **an earlier-instant mutation is still applied** |
| Rename (8) | Name changes; mark advances; rename to the same name changes nothing; null rejected; unset instant rejected; a rejected name has no effect; tenant, identity and status unchanged |
| `UpdateInstructions` (8) | Text replaced; mark advances; multiline preserved; identical value changes nothing; null rejected; unset instant rejected; **allowed while `Active`**; tenant unchanged |
| Lifecycle (12) | All three transitions; repeated `Activate` and repeated `Disable` are no-ops that do not move the mark; **`Draft` → `Disable` throws** and leaves the mark untouched; unset instants rejected; activation cannot fail on Domain configuration; a full round trip; the enum has exactly three states and no zero member |
| Immutability (5) | No public setter; `TenantId` has no setter at all; `Id`/`CreatedAt` have none; no tenant-reassignment operation; the operation set is exactly the four agreed |
| Provider independence (2) | The exact seven-property set; no provider-shaped type name in the namespace |

**One test of mine was wrong and was fixed on its merits.**
`There_is_no_operation_that_moves_an_agent_between_tenants` initially failed
because `GetMethods` includes property accessors, and `get_TenantId` contains
"Tenant" — a false positive from my filter, not a design problem. Fixed by
excluding `IsSpecialName` methods, which is what the test always meant.

**Raw control bytes were removed from a test file.** Two `[InlineData]` cases
initially contained literal BEL and ESC bytes rather than escapes. They compile
and pass either way, but invisible control characters in source break diffs and
editors and can be silently normalised by tooling, so they were replaced with
`\u0007` and `\u001b`, and the file was verified to contain no raw control
characters other than tab, CR and LF.

## 17. Backend tests before Phase 6

| Project | Tests |
| --- | --- |
| `OmniAI.Domain.Tests` | 172 |
| `OmniAI.Application.Tests` | 31 |
| `OmniAI.Infrastructure.IntegrationTests` | 53 |
| `OmniAI.Api.IntegrationTests` | 33 |
| **Total** | **289** |

## 18. Backend tests after Phase 6

| Project | Before | After | New |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | 172 | **288** | **+116** |
| `OmniAI.Application.Tests` | 31 | 31 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 53 | 53 | 0 |
| `OmniAI.Api.IntegrationTests` | 33 | 33 | 0 |
| **Total** | **289** | **405** | **+116** |

## 19. Restore

`dotnet restore OmniAI.slnx` — success, all 8 projects, no warnings, nothing new
to restore because no package was added (§24).

## 20. Release build

`dotnet build OmniAI.slnx -c Release`:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

With `TreatWarningsAsErrors`, analyzers at `latest-recommended` and
`EnforceCodeStyleInBuild`, across all 8 projects.

## 21. Test result

**405 passed, 0 failed, 0 skipped.**

| Project | Passed | Failed | Skipped |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | 288 | 0 | 0 |
| `OmniAI.Application.Tests` | 31 | 0 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 53 | 0 | 0 |
| `OmniAI.Api.IntegrationTests` | 33 | 0 | 0 |
| **Total** | **405** | **0** | **0** |

The Infrastructure and API suites run against the same disposable real
PostgreSQL 17.6 instance established in Phase 4, located through
`OMNIAI_TEST_POSTGRES`. Phase 6 changed nothing about them; they were run to
confirm the new Domain code broke nothing.

`OmniAI.Domain.Tests` executed normally, as in Phase 5. Smart App Control remains
enabled and unmodified; its per-file-hash verdict currently permits the assembly,
which is not something to rely on (§33).

## 22. Vulnerability result

`dotnet list package --vulnerable --include-transitive` — **no vulnerable packages
in any of the 8 projects.**

## 23. Warnings

**Zero compiler and analyzer warnings.** No diagnostic was suppressed, no
`.editorconfig` was changed, and no `#pragma` was added. Two build errors occurred
during development — both missing-`using` compile errors — and were fixed by
adding the directives.

## 24. Packages added

**None.** `Directory.Packages.props` is unmodified and no `.csproj` changed. No
OpenAI package, no Anthropic package, no AI SDK, no id-generation package, no
mocking framework, no result library. `Guid.CreateVersion7()` is BCL.

## 25. Domain files created / modified

**Created — Domain (5):**
`backend/src/OmniAI.Domain/AiAgents/{AiAgentId, AiAgentName, AgentInstructions,
AiAgentStatus, AiAgent}.cs`

**Created — Domain tests (4):**
`backend/tests/OmniAI.Domain.Tests/AiAgents/{AiAgentIdTests, AiAgentNameTests,
AgentInstructionsTests, AiAgentTests}.cs`

**Created — documentation (2):** `docs/architecture/ai-agent-domain.md`, this
report.

**Modified (1):** `docs/architecture/README.md` — the document index, the phase
summary, and decision 3 recording that Phase 6 was the first place the
provider-independence rule had teeth.

**No existing Domain file was modified.** `Tenant`, `TenantId`, `TenantName`,
`TenantSlug`, `TenantStatus`, `Conversation`, `Message`, both id types, all three
conversation enums and both exception types are byte-identical to the Phase 5
checkpoint. `AiAgent` references `TenantId` and nothing else outside its own
namespace.

## 26. Application modified

**NO.** `git status --short -- backend/src/OmniAI.Application
backend/tests/OmniAI.Application.Tests` returns nothing and
`git diff --stat HEAD` for those paths is empty. No command, query, handler, DTO,
repository contract or use case for agents was created.

## 27. Infrastructure modified

**NO.** Verified the same two ways. **No `DbContext` change, no entity
configuration, no migration, no schema change, no repository.** `OmniAiDbContext`,
all three existing entity configurations, `PersistenceSchema`,
`PersistenceConverters`, `ConversationLoading`, `TenantRepository`, the
design-time factory, the DI extension and the `InitialPersistence` migration are
untouched. `AiAgent` is currently a Domain type with no persistence at all, which
is exactly the authorised scope — its mapping is Phase 7.

## 28. API modified

**NO.** No endpoint, no DTO, no route, no Problem Details mapping. `Program.cs`,
`CompositionRoot`, `TenantEndpoints`, `TenantProblemDetailsHandler` and both API
DTOs are untouched.

## 29. Frontend modified

**NO.** `git status --short -- frontend/` returns nothing — zero files added,
changed or deleted. No npm command was run.

## 30. Architecture documentation summary

`docs/architecture/ai-agent-domain.md`, structured as **DECIDED NOW** A1–A11 and
**DEFERRED**:

| # | Decision |
| --- | --- |
| A1 | Provider independence, enforced by two reflection tests |
| A2 | Tenant ownership is structural and permanent |
| A3 | A deliberately narrow aggregate boundary |
| A4 | `AiAgentId` mirrors the accepted id strategy, hand-written |
| A5 | `AiAgentName`: a display label, with **no invented uniqueness rule** |
| A6 | `AgentInstructions`: business configuration, not a prompt |
| A7 | Three lifecycle states and one deliberate refusal |
| A8 | Activation invariants satisfied by construction, not by a guard |
| A9 | Supplied UTC instants and a high-water `UpdatedAt` |
| A10 | Behaviour, not setters |
| A11 | No instruction history, and the consequence stated plainly |

It opens with the sentence it exists to protect — **an `AiAgent` is not an OpenAI
Assistant** — and closes with ten things a reviewer can verify independently.
Deferred concerns are listed as deferred, never as implemented.

## 31. Deferred concerns

Nothing on the exclusion list was implemented. Deferred and documented as such:

| Area | Deferred |
| --- | --- |
| Persistence | `AiAgent` mapping, migration, schema, repository — **Phase 7** |
| Application / API | Use cases, handlers, DTOs, endpoints for agents |
| AI providers | OpenAI, Anthropic, any SDK or client, any API key, provider selection, model routing, fallback |
| Model configuration | Model name, temperature, top-p, max tokens, reasoning effort, response format |
| Prompt lifecycle | Instruction versioning, history, drafts, publishing, rollback |
| Knowledge / RAG | Knowledge sources, documents, chunking, embeddings, vector stores, retrieval |
| Tools | Tool and function definitions, business actions, tool authorisation |
| Channels | WhatsApp, Instagram, Messenger, web chat, agent-to-channel assignment |
| Runtime | Human handoff, memory, profile facts, summaries, guardrails, evaluation, cost and budget controls, multilingual configuration |

**Memory and profile facts are called out separately** because they are not merely
unbuilt: they need a privacy and security design — what may be retained, for how
long, under whose consent, how it is deleted — before any type is written.

Also still open from earlier phases and unchanged by Phase 6: authentication,
authorisation, request tenant context, query scoping, RLS, and the tenancy
invariant that an unscoped tenant-owned query is currently incorrect rather than
impossible.

## 32. Deviations

1. **`Activate` has no configuration guard** (§12). The brief specified
   activation invariants; the honest implementation is that they are satisfied by
   construction and a runtime check could not fail. The reasoning, the rejected
   alternative, and the trigger for adding a real guard are all recorded, and a
   test captures the current reasoning. **This is the one item worth the
   reviewer's attention.**
2. **`Draft` → `Disable` throws instead of being idempotent.** The brief listed
   three supported transitions and allowed non-idempotence where a meaningful
   Domain rule requires it; this is that case (§11).
3. **Renaming to the same value, or updating to identical instructions, does not
   advance `UpdatedAt`.** A small refinement of "advances when a meaningful
   mutation occurs", taken literally so the timestamp stays truthful.
4. **`AiAgentName.MaxLength` is 120, not `TenantName`'s 200.** A deliberate,
   justified difference rather than an inconsistency (§9).
5. **Six reflection-based guard tests were added** beyond the listed coverage, to
   make provider independence, immutability and tenant permanence executable
   rather than reviewable-once.
6. **`docs/architecture/README.md` was modified** — index and phase summary — so
   the accepted document set does not silently omit the new area.

No other deviation. No Domain file from an earlier phase was changed, no
persistence, Application, API or frontend file was touched, no package was added,
and no commit was created.

## 33. Known issues

1. **Instruction history does not exist** (§A11). Once instructions are updated,
   the previous text is gone. Recorded as a real limitation rather than papered
   over; versioning is a product feature with undecided requirements.
2. **`AiAgent` has no persistence**, so an agent cannot yet be stored, loaded or
   listed. Authorised and intended — Phase 7 — but it means nothing in the running
   application can create one today.
3. **`RequireInstant` is duplicated** between `Conversation` and `AiAgent` (six
   lines). Two copies is acceptable; a third would justify extracting a shared
   internal Domain helper, and that is the point at which it should be done rather
   than now.
4. **No display-name uniqueness anywhere** (§9). Deliberate, but it means a tenant
   can create two agents called `Sales Assistant` and will have to tell them apart
   by id. If the product later wants per-tenant uniqueness, it becomes a
   persistence constraint plus an Application check, exactly as `TenantSlug` did.
5. **The Domain suite's executability still depends on Smart App Control's
   per-file-hash verdict.** It passes now, the policy is enabled and unmodified,
   and a rebuild could be refused again. A Linux CI environment remains mandatory
   before any production release.
6. Earlier phases' open items are unchanged: no CI pipeline, no frontend linter,
   no toolchain pins, `InvariantGlobalization` revisit, and the migration `xmin`
   hand-edit.

## 34. Blockers

**None.** The authorised Phase 6 scope is complete, the whole 405-test suite is
green, and nothing external was required. Phase 7 (AI agent persistence) can
proceed on the Phase 4 patterns without any change to what was built here.

## 35. `git status --short`

```
?? backend/src/OmniAI.Domain/AiAgents/AgentInstructions.cs
?? backend/src/OmniAI.Domain/AiAgents/AiAgent.cs
?? backend/src/OmniAI.Domain/AiAgents/AiAgentId.cs
?? backend/src/OmniAI.Domain/AiAgents/AiAgentName.cs
?? backend/src/OmniAI.Domain/AiAgents/AiAgentStatus.cs
?? backend/tests/OmniAI.Domain.Tests/AiAgents/AgentInstructionsTests.cs
?? backend/tests/OmniAI.Domain.Tests/AiAgents/AiAgentIdTests.cs
?? backend/tests/OmniAI.Domain.Tests/AiAgents/AiAgentNameTests.cs
?? backend/tests/OmniAI.Domain.Tests/AiAgents/AiAgentTests.cs
 M docs/architecture/README.md
```

Plus `docs/architecture/ai-agent-domain.md` and this report.

**11 new files, 1 modified.** Every code path is under
`backend/src/OmniAI.Domain/AiAgents` or
`backend/tests/OmniAI.Domain.Tests/AiAgents`; the only other changes are
documentation.

**No commit was created, nothing was staged, and no remote exists.**
`git diff --check` reports no whitespace errors.

## 36. `git diff --stat`

```
 docs/architecture/README.md | 17 +++++++++++++----
 1 file changed, 13 insertions(+), 4 deletions(-)
```

`git diff` covers tracked files only, and the ten new files are untracked. The
single tracked change is documentation: the phase summary, one new index entry,
and a paragraph under decision 3 recording that Phase 6 was the first place the
provider-independence rule was exercised.

Diff review found no provider leakage, no HTTP leakage, no persistence leakage, no
unnecessary abstraction, no secret material, no frontend change and no scope
creep.

## 37. Secret review confirmation

**Confirmed — no secrets.**

- **No API key of any kind was added, read or referenced.** No OpenAI key, no
  Anthropic key, no provider credential. No environment variable for one was
  introduced, and no configuration file was touched.
- **Content scan** of every Phase 6 file for passwords, `pwd=`, API keys, secrets,
  bearer values, `sk-`-style keys, tokens, connection strings and the VPS address:
  no match. The only occurrences of such words anywhere in the change set are in
  this report's own prose.
- **No configuration file was modified** — `appsettings.json`,
  `appsettings.Development.json` and `Directory.Packages.props` are all untouched.
- **No connection string exists in the new code.** The Domain project has no
  persistence, no configuration and no dependencies.
- **Path scan:** nothing under `node_modules`, `bin/`, `obj/`, `dist/`,
  `.angular/` or `.vs/`; no `.env`; no PostgreSQL data file, data directory or
  `postmaster.pid`.

## 38. VPS NOT accessed confirmation

**Confirmed.** No SSH connection was attempted or made to `186.240.145.196` or any
other host. Nothing was deployed. No Nginx configuration, systemd service, server
directory, production database or production application was touched or inspected.
No SSH configuration or credential was read, written, printed, logged or changed.

**No external AI API was called.** No OpenAI, Anthropic or other provider endpoint
was contacted, no client was constructed, and no such package was installed.

All work was local. The only network use was the local PostgreSQL instance, needed
to re-run the pre-existing Infrastructure and API suites and confirm Phase 6 broke
nothing.

## 39. Readiness recommendation for Phase 7

**Ready to proceed.**

The agent Domain is complete, provider-independent by test rather than by
intention, and it required no change to any accepted type — which is the strongest
available evidence that the Tenant and Conversation models were shaped correctly.

**Phase 7 should be `AiAgent` persistence**, and it maps cleanly onto the Phase 4
patterns already accepted:

- `AiAgentId` → native `uuid`, `ValueGeneratedNever`, via a hand-written converter
  alongside the existing three.
- `AiAgentName` and `AgentInstructions` → scalar `character varying(120)` and
  `character varying(20000)`, not JSON, matching decision P5.
- `AiAgentStatus` → stored **by name** in `character varying(32)`, matching P6, so
  ordinals never become a contract.
- `CreatedAt`/`UpdatedAt` → `timestamptz`, matching P7.
- `TenantId` → a non-null foreign key with `OnDelete(Restrict)`, matching the
  `conversations` decision, satisfying tenancy decision D4 for a third aggregate.
- An `xmin` shadow concurrency token, matching P12 — and note that the migration
  `xmin` hand-edit will be needed again, which `MigrationSchemaTests` now guards.

Two questions Phase 7 will have to answer explicitly, flagged now so they are
decided rather than defaulted:

1. **Whether `(tenant_id, name)` gets a unique index.** §9 deliberately declined to
   invent a uniqueness rule; if the product wants one, persistence is where it
   becomes real and the Application layer would surface the conflict, exactly as
   `TenantSlug` did in Phase 5.
2. **Whether `agents` needs a tenant-scoped index for listing**, which the first
   list use case will make clear.

Authentication remains the higher-priority product gap — the tenant endpoints are
still unauthenticated and must not be exposed — and a Linux CI pipeline remains
mandatory before any production release.
