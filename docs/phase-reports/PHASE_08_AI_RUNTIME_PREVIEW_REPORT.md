# Phase 8 — AI runtime contracts and agent preview

## 1. Phase objective

Establish the platform's first executable AI-oriented flow: provider-independent
text-generation contracts owned by the Application, a focused stateless
`PreviewAiAgent` use case, a tenant-scoped preview endpoint, deterministic test
infrastructure, and explicit behaviour when no AI provider is configured.

Explicitly **not** to integrate a provider. No SDK, no API key, no model, no
network call.

## 2. Final status

**Complete.** Release build 0 warnings, 0 errors. **818 of 818 tests pass**, in
both Debug and Release. 8 of 8 projects report no vulnerable packages. Domain
unchanged, no migration, no packages added, nothing committed.

## 3. Baseline commit

`95fc1ca4e53be8ebc8b8942e7868b03c8c1008f0` — *feat: implement ai agent management
vertical slice*.

## 4. Initial git state

`git status --short` was empty; the working tree was clean. `git log -7 --oneline`
showed the seven commits from `95fc1ca` back to `b7ab2ea`.

## 5. Domain modified

**NO.** `git diff --stat HEAD -- backend/src/OmniAI.Domain` is empty and
`git status --short` lists no Domain path. `AiAgent` gained no provider, no
model, no generation method, no preview state and no token usage. A test resolves
the real contract types and asserts no Domain property, method return or method
parameter uses any of them.

## 6. Application AI contracts

Eight types in `OmniAI.Application.Ai`:

| Type | Purpose |
| --- | --- |
| `IAiTextGenerator` | The capability contract — one method |
| `AiTextGenerationRequest` | Instructions plus ordered turns |
| `AiTextGenerationResult` | The generated text |
| `AiTurn` | One turn of conversation, validated |
| `AiTurnRole` | `User` or `Assistant` |
| `AiTurnValidationException` | Unusable caller text |
| `AiRuntimeUnavailableException` | No runtime configured |
| `AiResponseInvalidException` | Runtime answered with nothing usable |

They are owned by the Application because the use cases are what need text
generated. Implementations belong to Infrastructure because a vendor is a detail.
This mirrors `IAiAgentRepository` exactly.

## 7. AI turn model

`AiTurn` is a sealed record holding a `Role` and `Text`, validated at
construction so an unusable turn cannot exist to be passed around.

`AiTurnRole` has **two** members, `User` and `Assistant`, and the omissions are
the design:

- **No `System` or `Developer`.** The privileged layer is already
  `AiTextGenerationRequest.Instructions`. A system role on a turn would create a
  second, equally privileged channel reachable from the same ordered list that
  carries customer text. `AiTurn` exposes only `FromUser` and `FromAssistant` —
  there is no public factory taking a role, so no call site can construct a
  privileged turn even by accident, and a test asserts no such factory exists.
- **No `Tool`, `Function`, `Image`, `Audio` or `Document`.** Nothing produces
  them yet; adding them would mean guessing at shapes no requirement describes.

It is **not** a Domain `Message`. A stored message is a business record with
identity, channel, direction and ingestion order; a turn is transient input to
one call. A conversation runtime will later project messages into turns rather
than the two being one type. It is also not modelled on any vendor's message
object.

## 8. Generation request

`AiTextGenerationRequest` has exactly two properties — `Instructions` (a plain
string) and `Turns` (an ordered `IReadOnlyList<AiTurn>`) — and never
concatenates them. A test enumerates its properties and asserts there are only
those two, so a model name, temperature, token budget, tool list or response
format cannot appear here first.

`Instructions` is a `string` rather than the Domain's `AgentInstructions`, so the
contract stays usable by anything needing text generated without depending on the
AI agent aggregate.

It is a class rather than a record: it holds a list, and a record's generated
equality would compare that list by reference, reporting two identical requests
as unequal. The turns are copied on construction, so a caller cannot alter a
request after handing it to an adapter that may hold it across an await.

## 9. Generation result

`AiTextGenerationResult` has one property, `Text`. No model name, provider name,
token counts, cost, finish reason, tool calls, raw payload or request id — none
has been observed from a real provider, so each would be a guess baked into a
contract, which is far harder to remove than to add.

`Create` trims the ends, preserves the interior, and **refuses blank text** by
raising `AiResponseInvalidException`. Making the type itself the boundary means a
badly behaved adapter fails where the bad value was produced rather than further
up where it is only a symptom.

## 10. `IAiTextGenerator` design

```csharp
Task<AiTextGenerationResult> GenerateAsync(
    AiTextGenerationRequest request,
    CancellationToken cancellationToken = default);
```

One method, named for the capability rather than the vendor. Deliberately not an
`IAiProvider` carrying embeddings, image generation, speech, transcription,
moderation, tools, structured output, streaming and model management: a broad
interface would be implemented mostly as methods that throw, and the platform
would ask "does this provider really support that?" at runtime — the question a
type system exists to answer at compile time. When embeddings are needed they get
their own contract.

## 11. `PreviewAiAgent` use case

`PreviewAiAgentHandler(IAiAgentRepository agents, IAiTextGenerator generator)`.

1. `TenantId.From` / `AiAgentId.From` — empty identifiers rejected
2. `AiTurn.FromUser(command.Message)` — message validated
3. **`agents.FindAsync(tenantId, agentId, ct)`** — tenant-scoped; `null` → not found
4. `AiTextGenerationRequest.Create(stored.Agent.Instructions.Value, [question])`
5. `generator.GenerateAsync(request, ct)`
6. Guard against a null result
7. Return `AiAgentPreview(result.Text)`

**Read-only.** No aggregate method is called, `UpdateAsync` is never reached, and
the handler takes **no `TimeProvider`** — there is no timestamp to stamp because
nothing is written.

**Stateless.** No conversation, no message, no stored request or response, no
history, no summary.

## 12. Instructions handling

Instructions come from the persisted, tenant-scoped agent and from **nowhere
else**.

Neither `AiAgentPreviewRequest` nor `PreviewAiAgentCommand` has an instructions
field, so a caller cannot supply, override, extend or prepend them. A test posts
`instructions` and `system` fields alongside the message and asserts the
generator still received the stored text and a single user turn carrying only the
message.

Without that rule, preview would be an open text-generation endpoint wearing an
agent's name: anyone could run instructions of their choosing on this platform's
AI account, and could make an agent appear to behave in ways its owner never
configured.

They are **never returned** by the preview response, and are **not logged**
anywhere in the pipeline.

## 13. User input validation

Enforced by `AiTurn`, in the Application:

| Rule | Behaviour |
| --- | --- |
| Required | `null`, empty and whitespace-only rejected |
| Surrounding whitespace | Trimmed |
| Interior whitespace | Preserved exactly — line breaks, blank lines, indentation |
| Maximum | `AiTurn.MaxTextLength` = **10,000** characters, measured after trimming |
| Over-length | **Rejected, never truncated** |
| Control characters | Rejected except tab, carriage return, line feed |

**10,000 characters** is roughly 20 KB: far above any realistic customer message,
while bounding the request body, the log line and the memory one call can occupy.
It sits below `AgentInstructions.MaxLength` (20,000) deliberately — instructions
are authored once and read on every turn; a turn is one person's message.

**It is a platform resource bound, not a provider token or context limit.**
Provider limits are counted in tokens, differ per model and change without
notice; encoding one here would put a vendor's current pricing decision into this
platform's validation.

**Not in the Domain,** because no aggregate was consulted. This is a limit the
platform places on a request it will process, in the same family as a maximum
upload size, so it raises the Application's own `AiTurnValidationException`.

## 14. Status / preview decision

**Preview is allowed for `Draft`, `Active` and `Disabled`.** The handler does not
read `AiAgent.Status` at all.

An operator has to be able to test an agent **before** deciding whether to
activate it — that is the main reason to preview — and has to be able to test one
they have **disabled** for repair, which is exactly when checking it matters
most. Requiring activation would invert the workflow preview exists to serve.

Status governs whether an agent serves customers. It does not govern whether its
owner may test it. **The Domain lifecycle is unchanged**: `Draft → Active`,
`Active → Disabled`, `Disabled → Active`, `Draft → Disable` still refused.

## 15. Tenant-isolation behaviour

Preview uses the existing tenant-scoped repository path, so both predicates are
in the SQL and another tenant's agent is never read, materialised or tracked.

`POST /TenantA/AgentB/preview` where Agent B belongs to Tenant B returns **404**,
and **`IAiTextGenerator` is never called** — asserted directly through the fake's
call count and request list, at both the Application and API levels.

That is the ordering's whole purpose: ownership is resolved at step 3, before any
instruction text is read into a request, so a cross-tenant preview cannot extract
somebody's configuration through a model's answer and cannot spend this
platform's AI budget on their data.

A foreign agent and one that never existed produce the **same status and the same
body** once each response's own id is masked; the per-request `traceId` is
excluded because it differs between two identical requests and so carries no
information about the resource. The target agent is byte-for-byte unchanged
afterwards.

## 16. Unconfigured-provider behaviour

`UnconfiguredAiTextGenerator` is registered by
`InfrastructureServiceCollectionExtensions.AddOmniAiRuntime()` as the **real**
implementation. It throws `AiRuntimeUnavailableException` on every call.

- The application **starts normally**; health checks and the whole agent
  management slice keep working.
- It **never fabricates an answer** — no canned reply, template, apology sentence
  or echo of the caller's message. Anything returned successfully would put text
  of unknown origin in front of somebody evaluating whether their agent works,
  and the failure mode is that they conclude it does.
- It **never falls back** to another provider.
- It **reads no configuration** — no key, no environment variable, no settings
  section, no HTTP client.
- It does not inspect, log or retain the request.
- It is **not a test double.** The deterministic fake lives in the test projects
  and is never registered by the composition root; a test resolves the service
  from the real composition root and asserts it is `UnconfiguredAiTextGenerator`.

Mapped to **503 Service Unavailable**.

## 17. Invalid-provider-response behaviour

Two guards:

1. `AiTextGenerationResult.Create` refuses null, empty and whitespace-only text.
   A provider adapter therefore cannot construct a successful-looking result
   carrying nothing.
2. The handler guards a null result object — the one breach no return type can
   prevent.

Both raise `AiResponseInvalidException`, mapped to **502 Bad Gateway**: this
server acted as a gateway and received an invalid response from upstream. The
caller did nothing wrong and cannot fix it, so a `4xx` would misdirect them.

An empty reply must never reach a person as though the agent had chosen to say
nothing — a broken integration looking like an unhelpful agent is the most
expensive kind of bug to notice.

**Nothing is retried.** A test asserts the generator was called exactly once
after a failure.

## 18. Cancellation strategy

`GenerateAsync` takes a `CancellationToken`. `PreviewAiAgentHandler.HandleAsync`
takes one and passes the caller's token straight through — the API supplies the
request's own token. Two tests: one asserts the exact token instance reaches the
generator, another asserts an already-cancelled request raises
`OperationCanceledException`.

Every real implementation will be a network call to a service that can be slow. A
synchronous signature would block a request thread for the duration and give a
caller that has already given up no way to stop work nobody is waiting for.

## 19. API preview endpoint

`POST /api/tenants/{tenantId:guid}/ai-agents/{aiAgentId:guid}/preview`

Added to the existing `AiAgentEndpoints` group. `POST` rather than `GET` because
the request carries a body and the body is the message: a `GET` would put
somebody's text in the URL, where it lands in access logs, proxy logs and browser
history. The result is not cacheable either.

**No `If-Match` required and no `ETag` returned.** Preview writes nothing, so
there is no precondition to state and no version to be out of date about. Tests
read `updated_at` straight from PostgreSQL before and after and assert it is
unchanged, and assert the entity tag is identical across repeated previews.

The existing six management endpoints and their `ETag` / `If-Match` behaviour are
untouched.

## 20. Request contract

```json
{ "message": "Can you help me choose a product?" }
```

One field. No `instructions`, no `system`, no `model`, no `temperature`, no
history. `Message` is nullable so a missing or explicitly null field reaches the
use case and is rejected with an explanation, rather than being bound to an empty
string and treated as a blank message somebody sent.

## 21. Response contract

```json
{ "reply": "..." }
```

One field. A test enumerates the response's JSON properties and asserts `reply`
is the only one, and separate tests assert the body contains neither instruction
string and none of: `model`, `provider`, `prompt`, `token`, `temperature`,
`system`, `openai`, `anthropic`, `gpt`, `claude`, `npgsql`, `xmin`, `ai_agents`.

No `ETag` header.

## 22. ProblemDetails behaviour

| Situation | Status | Title |
| --- | --- | --- |
| Route identifier is not a GUID | **404** | (route constraint; no handler runs) |
| Identifier is the empty GUID | **400** | Invalid request details |
| Message blank, over-length, or with control characters | **400** | Invalid request details |
| Body unreadable | **400** | Malformed request |
| Agent not in this tenant (missing **or** foreign) | **404** | AI agent not found |
| No AI runtime configured | **503** | AI generation unavailable |
| Runtime answered with nothing usable | **502** | AI generation failed |
| Anything unanticipated | **500** | no detail disclosed |

Three arms were added to `DomainProblemDetailsHandler`. The Application contains
no status code — checked by the existing guard that the Application assembly
references no ASP.NET Core.

**`503` rather than `500` or `501`:** the request is good and will succeed once a
provider is configured, without the caller changing anything. Calling a
deliberate configuration state a server fault would send an operator hunting for
a bug in code behaving as intended; `501` would claim the platform does not
implement preview at all.

**No `Retry-After`.** The platform has no idea when a provider will be
configured — a deployment decision, not a queue that drains — so a number would
invite polling against a promise nothing backs. A test asserts the header is
absent.

**Logging levels were split.** `4xx` still logs at Information; the two new `5xx`
outcomes log at Warning, because a provider outage and somebody's typing mistake
are not the same event to an operator. Neither level records the preview message
or the agent's instructions.

## 23. Application tests

**+76** (123 → 199).

| Class | Tests |
| --- | --- |
| `PreviewAiAgentHandlerTests` | 34 |
| `AiRuntimeContractTests` | 38 |
| `AiRuntimeDependencyTests` | 4 |

`PreviewAiAgentHandlerTests` covers: the generated reply returned; the generator
called exactly once; the stored instructions supplied character for character;
the message supplied as a single `User` turn; instructions never inside a turn;
no turn ever privileged; nothing about the agent changed; `Draft`, `Active` and
`Disabled` all preview; a foreign agent not found; **a cross-tenant preview
never reaching the generator**; an unknown agent; foreign and missing answered
identically; blank, over-length and boundary-length messages; multiline content
preserved; surrounding whitespace trimmed; empty identifiers; a null command; an
unavailable runtime; blank and null provider results; the caller's token reaching
the generator; an already-cancelled request abandoned; both dependencies
required.

## 24. API integration tests

**+47** (133 → 180), over real HTTP against real PostgreSQL.

| Class | Tests | Fixture |
| --- | --- | --- |
| `AiAgentPreviewApiTests` | 38 | `AiAgentPreviewApiFixture` — deterministic generator |
| `AiAgentUnconfiguredProviderApiTests` | 9 | `AiAgentApiFixture` — **nothing substituted** |

The second suite is the important one: it runs the composition root exactly as a
deployment would and proves that the application starts with no provider, that
the registered generator is the unconfigured one, that preview returns **503**,
that no answer is fabricated, that the failure names no provider or configuration
key, that no `Retry-After` is advertised, that a bad request is still `400`, that
a cross-tenant preview is still `404` rather than `503`, and that a failed
preview changes nothing.

The first covers: `200` with the exact reply; a body carrying only `reply`; the
instructions never echoed; no provider or persistence detail in the body; the
stored instructions reaching the generator exactly; **a caller-supplied
`instructions` field ignored**; no concatenation; multiline round-trip; no
`If-Match` and no `ETag`; the agent byte-for-byte unchanged; `updated_at`
unchanged in the database; repeated previews not moving the version; `Draft`,
`Active` and `Disabled` previewing; `404` for a foreign agent; **the generator
never called on a cross-tenant path**; the target untouched; foreign and missing
answered identically; blank, missing, over-length and boundary-length messages;
malformed body and malformed identifiers; `503`; `502` for blank and null
results; no provider named in a failure body; nothing retried; a failed preview
changing nothing; `GET` refused.

`AiAgentPreviewApiFixture` replaces exactly one service using
`ServiceCollectionDescriptorExtensions.Replace`, so the production registration
is removed rather than shadowed. Everything else — endpoints, handlers,
repositories, EF Core, PostgreSQL — is real.

**No test calls the internet.** No test reads or holds an API key.

## 25. Architecture / purity tests

`AiRuntimeDependencyTests` (4) and one guard in
`UnconfiguredAiTextGeneratorTests`, plus the three pre-existing
`ApplicationDependencyTests`.

Two complementary techniques, and **neither scans type names for substrings** — a
test searching for "openai" would pass for a class called `GptClient` and fail
for a business term containing those letters:

1. **Assembly references,** read from compiled metadata without loading code. The
   Application assembly is asserted to reference none of `OpenAI`,
   `Azure.AI.OpenAI`, `Anthropic`, `Anthropic.SDK`, `Betalgo.OpenAI`,
   `OpenAI_API`, `Microsoft.Extensions.AI`, `Microsoft.SemanticKernel`,
   `LangChain`, `Facebook` or `Meta`. The Infrastructure assembly is checked the
   same way. The existing guards already cover EF Core, Npgsql and ASP.NET Core.
2. **Type signatures,** resolved as real `Type` objects. Every type the eight
   contracts expose — property types, return types, parameter types, and generic
   arguments unwrapped so `Task<T>` is not judged on `Task` alone — must come from
   `OmniAI.*` or the .NET base libraries. EF Core, Npgsql and ASP.NET Core all
   ship under `Microsoft.*` and are deliberately not on the allowed list, so a
   contract returning a `DbSet`, an `NpgsqlParameter` or an `IResult` fails.

Two further guards assert the contracts live in the Application assembly, and
that no Domain property, method return or method parameter uses any of them.

## 26. Backend tests before

**688** — Domain 288, Application 123, Infrastructure 144, Api 133.

## 27. Backend tests after

**818**, all passing, 0 failed, 0 skipped.

| Suite | Before | After | Added |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | 288 | **288** | 0 |
| `OmniAI.Application.Tests` | 123 | **199** | +76 |
| `OmniAI.Infrastructure.IntegrationTests` | 144 | **151** | +7 |
| `OmniAI.Api.IntegrationTests` | 133 | **180** | +47 |
| **Total** | **688** | **818** | **+130** |

## 28. Restore result

`dotnet restore OmniAI.slnx` — "All projects are up-to-date for restore." No
package was added, so nothing was downloaded.

## 29. Release build

`dotnet build OmniAI.slnx -c Release` — **Build succeeded, 0 Warning(s), 0
Error(s)** across all 8 projects. Nothing suppressed;
`TreatWarningsAsErrors` remains on.

## 30. Test result

**818 of 818 passing in both configurations.**

```
Debug     Domain 288 | Application 199 | Infrastructure 151 | Api 180
Release   Domain 288 | Application 199 | Infrastructure 151 | Api 180
```

**The Smart App Control blocker from Phases 6 and 7 no longer occurs.** Those
phases could only run the suite in Debug because SAC refused to load the Release
assemblies (`0x800711C7`). This phase's Release run completed normally.

Smart App Control was **not disabled and not modified**. A read-only check of
`HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState`
returns **`1`** — still enabled, the same value recorded in the Phase 7 report.
The change is in what SAC's reputation check permits, not in the policy.

The standing requirement is unaffected: **a Linux CI environment must execute the
complete suite in Release before any production release.** A local Release run
passing on one Windows machine is evidence, not a substitute.

## 31. Vulnerability result

`dotnet list OmniAI.slnx package --vulnerable --include-transitive` — **all 8
projects report "no vulnerable packages given the current sources."**

## 32. Warnings

**Zero.** No compiler warning, no analyzer warning, nothing suppressed, no
`#pragma warning disable`, no `NoWarn` added.

Five analyzer errors were raised during development and fixed at source rather
than suppressed: three `xUnit2029` (`Assert.Empty` over a filtered collection →
`Assert.DoesNotContain` with a predicate) and two `CA1861` (constant array
arguments → `static readonly` fields).

## 33. Packages added

**NONE.** `backend/Directory.Packages.props` is unchanged and
`git diff HEAD -- '*.csproj'` shows no added `PackageReference`. The only project
file edits are two `<Compile Include>` links for the shared test generator.

No AI SDK, no HTTP client library, no resilience package, no mocking framework.

## 34. Migration changed

**NO.** `backend/src/OmniAI.Infrastructure/Persistence/Migrations` is unchanged —
still `InitialPersistence`, `AddAiAgents` and the model snapshot. Preview is
read-only, stores nothing, and needs no schema. No AI runtime table exists.

## 35. Frontend modified

**NO.** `git status --short -- ':!backend' ':!docs'` is empty. Nothing outside
`backend/` and `docs/` changed. A future admin preview UI will consume this
endpoint; it is not implemented.

## 36. External AI network calls

**NO.** No HTTP client, socket or web request exists anywhere in
`OmniAI.Application/Ai` or `OmniAI.Infrastructure/Ai` — verified by search. The
only implementation throws before doing anything. No test contacts the internet.

## 37. OpenAI configuration / key read

**NO.** No `OPENAI_API_KEY` or any provider credential is read, stored, logged,
configured or referenced. `appsettings.json` is unchanged and has no AI section.
No OpenAI package is installed, no OpenAI client exists, no model was selected,
and `platform.openai.com` was not contacted. The previously shared key was not
used and appears nowhere.

Searches for `using OpenAI`, `using Anthropic`, `OpenAIClient`, `OPENAI_API_KEY`,
`api.openai.com`, `Azure.AI`, `SemanticKernel`, `IAiProvider`, `temperature =`,
`max_tokens`, `gpt-N` and `claude-N` return only documentation comments
explaining what is deliberately absent, and test lists of forbidden strings.

## 38. Files created / modified

**31 files: 23 created, 8 modified** (32 with this report).

**Created — Application (11)**

- `Ai/IAiTextGenerator.cs`, `Ai/AiTextGenerationRequest.cs`,
  `Ai/AiTextGenerationResult.cs`, `Ai/AiTurn.cs`, `Ai/AiTurnRole.cs`
- `Ai/AiTurnValidationException.cs`, `Ai/AiRuntimeUnavailableException.cs`,
  `Ai/AiResponseInvalidException.cs`
- `AiAgents/PreviewAiAgent/PreviewAiAgentCommand.cs`,
  `PreviewAiAgentHandler.cs`, `AiAgentPreview.cs`

**Created — Infrastructure (1)**

- `Ai/UnconfiguredAiTextGenerator.cs`

**Created — Api (2)**

- `AiAgents/AiAgentPreviewRequest.cs`, `AiAgents/AiAgentPreviewResponse.cs`

**Created — tests (8)**

- `Shared/RecordingAiTextGenerator.cs`
- `Application.Tests/Ai/AiRuntimeContractTests.cs`,
  `Ai/AiRuntimeDependencyTests.cs`,
  `AiAgents/PreviewAiAgentHandlerTests.cs`
- `Infrastructure.IntegrationTests/Ai/UnconfiguredAiTextGeneratorTests.cs`
- `Api.IntegrationTests/AiAgents/AiAgentPreviewApiFixture.cs`,
  `AiAgentPreviewApiTests.cs`, `AiAgentUnconfiguredProviderApiTests.cs`

**Created — docs (1)**

- `docs/architecture/ai-runtime.md`

**Modified (8)**

| File | Change |
| --- | --- |
| `Api/AiAgents/AiAgentEndpoints.cs` | Preview route and handler method |
| `Api/Composition/CompositionRoot.cs` | `AddOmniAiRuntime()`, `PreviewAiAgentHandler` |
| `Api/ErrorHandling/DomainProblemDetailsHandler.cs` | Three map arms; Warning level for `5xx` |
| `Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | `AddOmniAiRuntime()` |
| `tests/Api.IntegrationTests/AiAgents/AiAgentApiFixture.cs` | `Services` property |
| `tests/Api.IntegrationTests/*.csproj` | Link the shared generator |
| `tests/Application.Tests/*.csproj` | Link the shared generator |
| `docs/architecture/README.md` | Phase 8 sentence, seventh document |

`git diff --stat`: 8 files changed, 245 insertions(+), 13 deletions(-).

## 39. Architectural decisions

Recorded in full in `docs/architecture/ai-runtime.md` as C1–C18. In brief:

1. **A capability contract, not a provider interface.** One method, named for
   what it does rather than who does it.
2. **The Application owns the abstraction; Infrastructure owns providers.**
3. **Instructions and turns are separate slots**, never concatenated. The
   privilege boundary is structural: there is nowhere for caller text to become
   platform instruction.
4. **Two turn roles.** `System` and `Developer` are excluded permanently in this
   shape; the multimodal and tool roles merely do not exist yet.
5. **The turn type is not the Domain `Message`.**
6. **The result carries only text.**
7. **Blank output is a failure, enforced by the type.**
8. **Preview is stateless and single-turn**, and cannot accept a caller-supplied
   history.
9. **Every lifecycle state previews.**
10. **The client cannot supply instructions.**
11. **Message validation is an Application rule**, with a documented platform
    bound that is explicitly not a token limit.
12. **Ownership is resolved before the generator is reached.**
13. **An unconfigured provider fails explicitly**, never fabricating or falling
    back.
14. **Three flat failure types**, no hierarchy, no guessed taxonomy.
15. **`400` / `404` / `502` / `503`**, each chosen for what it tells the caller
    to do.
16. **No `If-Match`, no `ETag`** on a read-only operation.
17. **Cancellable, and nothing retried.**
18. **`5xx` outcomes log at Warning**, without customer text or instructions.

## 40. Deferred concerns

Tabulated with triggers in `ai-runtime.md`. Deferred: OpenAI; any second
provider; model configuration, selection and routing; temperature, top-p, output
limits, reasoning effort; streaming; conversation runtime and history; memory and
summarisation; RAG; tools and function calling; structured output; multimodal
input; token usage and cost; metering, quotas and billing; retry and resilience;
rate-limit and quota taxonomy; guardrails and moderation; evaluations; provider
fallback; preview history; authentication and authorisation.

**The Phase 8 AI runtime abstraction is deliberately TEXT-ONLY and
capability-specific. It is not intended to predict every future AI capability.**

## 41. Deviations

Three, all small and all additive.

1. **A third exception type.** The brief named two AI failure types (runtime
   unavailable, invalid response). A third, `AiTurnValidationException`, was
   needed for unusable caller text, which must map to `400`. The alternative was
   raising `DomainValidationException` from the Application — which the brief also
   forbade, since preview input is not a Domain rule. Three flat sealed types, no
   base class, no codes.

2. **The `503` / `502` logging level was raised to Warning.** The existing handler
   logged every mapped outcome at Information, which was right when every mapped
   outcome was a caller's mistake. Leaving a provider outage at Information would
   have meant it left no more trace than a rejected name. The `4xx` behaviour is
   unchanged.

3. **A `Services` property on `AiAgentApiFixture`.** Needed to assert that a real
   deployment resolves `UnconfiguredAiTextGenerator` rather than a test double.
   Test-only.

## 42. Known issues

None functional. Three limits worth recording:

- **`AiTextGenerationRequest.Create` raises `ArgumentException`,** not an
  Application failure type, for blank instructions or an empty turn list. These
  are programming errors in a caller rather than anything an end user can cause:
  blank instructions cannot come out of the Domain, and a request with nothing to
  answer cannot be built from any endpoint. If a future caller could trigger
  them, they would need a mapped type.
- **`AiTurn.MaxTextLength` is a single platform-wide bound.** No per-tenant or
  per-agent limit exists. Adding one needs a place to configure it, which is a
  product decision.
- **The preview contract has exactly one user.** `AiTurn`,
  `AiTextGenerationRequest` and `IAiTextGenerator` are written to be reusable but
  have one caller today. That is deliberate — the alternative is designing for
  callers nobody has described.

## 43. Blockers

None for Phase 9.

Two standing constraints, unchanged:

- **Preview is not authorised for production exposure.** Authentication and
  authorisation still do not exist, so the tenant id in the route is supplied by
  the caller and believed. Tenant scoping is a correctness boundary, not yet a
  security boundary. No fake security was added.
- **A Linux CI environment must run the complete suite in Release before any
  production release.** This phase's local Release run passing does not discharge
  that.

## 44. `git status --short`

```
 M backend/src/OmniAI.Api/AiAgents/AiAgentEndpoints.cs
 M backend/src/OmniAI.Api/Composition/CompositionRoot.cs
 M backend/src/OmniAI.Api/ErrorHandling/DomainProblemDetailsHandler.cs
 M backend/src/OmniAI.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs
 M backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentApiFixture.cs
 M backend/tests/OmniAI.Api.IntegrationTests/OmniAI.Api.IntegrationTests.csproj
 M backend/tests/OmniAI.Application.Tests/OmniAI.Application.Tests.csproj
 M docs/architecture/README.md
?? backend/src/OmniAI.Api/AiAgents/AiAgentPreviewRequest.cs
?? backend/src/OmniAI.Api/AiAgents/AiAgentPreviewResponse.cs
?? backend/src/OmniAI.Application/Ai/
?? backend/src/OmniAI.Application/AiAgents/PreviewAiAgent/
?? backend/src/OmniAI.Infrastructure/Ai/
?? backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentPreviewApiFixture.cs
?? backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentPreviewApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentUnconfiguredProviderApiTests.cs
?? backend/tests/OmniAI.Application.Tests/Ai/
?? backend/tests/OmniAI.Application.Tests/AiAgents/PreviewAiAgentHandlerTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Ai/
?? backend/tests/Shared/RecordingAiTextGenerator.cs
?? docs/architecture/ai-runtime.md
```

Nothing is staged. `HEAD` is still `95fc1ca`. There are no remotes.
*(This report is written after that listing and is a further untracked file.)*

## 45. `git diff --stat`

```
 .../src/OmniAI.Api/AiAgents/AiAgentEndpoints.cs    |  58 +++++++++++
 .../src/OmniAI.Api/Composition/CompositionRoot.cs  |  12 ++-
 .../ErrorHandling/DomainProblemDetailsHandler.cs   | 109 +++++++++++++++++++--
 .../InfrastructureServiceCollectionExtensions.cs   |  49 ++++++++-
 .../AiAgents/AiAgentApiFixture.cs                  |  12 +++
 .../OmniAI.Api.IntegrationTests.csproj             |   6 ++
 .../OmniAI.Application.Tests.csproj                |   2 +
 docs/architecture/README.md                        |  10 +-
 8 files changed, 245 insertions(+), 13 deletions(-)
```

`git diff --check` is clean — no whitespace error, no conflict marker.

## 46. Secret review

**No secret of any kind was introduced.**

Scanned every created and modified file for OpenAI-style keys (`sk-`, `sk-ant-`),
GitHub tokens, private key blocks, `Password=` / `Pwd=`, bearer tokens, the VPS
address and the VPS username. **No match.** No build artefact, `bin`, `obj`,
`node_modules`, `.env`, IDE file or PostgreSQL data file is included.

No AI provider credential exists to leak: none is read, stored, logged,
configured or referenced anywhere in this phase. `appsettings.json` is unchanged
and contains no AI section and no connection string value.

The local test PostgreSQL connection string was supplied only through the
`OMNIAI_TEST_POSTGRES` environment variable on the command line and **appears in
no file**.

## 47. VPS not accessed

**Confirmed.** No SSH connection was attempted or made to the production server,
and no address, credential or key for it appears in any file changed by this
phase. Nothing was deployed, no remote command was run, and no remote
configuration was read or written. The only processes started locally were the
.NET build and test host, and the project's own disposable PostgreSQL test
cluster.

## 48. Readiness recommendation for Phase 9

**Ready.** The abstraction the next phase needs is in place and proven end to
end without a provider.

Adding OpenAI should require changing **one registration line**, plus a new
adapter in `OmniAI.Infrastructure/Ai`. Nothing in the Domain, the Application, the
preview use case, the endpoint or the HTTP contract needs to change — that
invariant is what this phase exists to establish, and the two API suites
demonstrate it by exercising the same endpoint against two different
implementations.

Points worth deciding at the start of Phase 9:

1. **Where the model name lives.** Configuration, per tenant, or per agent. This
   phase deliberately put it nowhere; the answer is a product decision, and adding
   a field to `AiAgent` would be a Domain change requiring its own authorisation.
2. **Which result fields the real API actually returns** — token usage, finish
   reason, request id — and which of them are stable enough to publish.
3. **Which provider failures are transient**, which is the prerequisite for any
   retry policy. Nothing should be retried until that is observed rather than
   guessed.
4. **How the API key reaches the adapter.** It must come from deployment
   configuration and must never be committed, logged, or included in any failure
   message.
5. **Whether `AiRuntimeUnavailableException` should also cover a configured
   provider that is unreachable,** or whether that deserves its own type. The
   current answer is that `503` and `502` already distinguish "not configured"
   from "answered badly"; a live provider may reveal a third case.

The security position is unchanged and still gates production: **these endpoints
remain development-only until authentication binds an authenticated principal to
the tenant ids it may use.**
