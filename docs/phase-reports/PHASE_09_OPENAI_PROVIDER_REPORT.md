# Phase 9 — OpenAI Responses provider integration

## 1. Phase objective

Implement the first real AI provider behind the accepted Phase 8
`IAiTextGenerator` contract: the official OpenAI .NET SDK, the Responses API, in
Infrastructure only, with deterministic offline tests and one authorised live
smoke call — without changing the Domain, the Application contract or the HTTP
surface.

## 2. Final status

**PASS.** Release build 0 warnings, 0 errors. **908 of 908 tests pass**, in both
Debug and Release. 8 of 8 projects report no vulnerable packages. The live smoke
test executed successfully through the real endpoint. Domain unchanged, no
migration, nothing committed.

One incident is reported honestly in §41: before the test host was hardened,
several unintended live OpenAI calls were made by suites that inherited the
machine's `OPENAI_API_KEY`. It was found by a failing test, fixed at source, and
the suite is now provably offline.

## 3. Baseline commit

`c2aae2a25ba8ada266c37f136e3035b99e3ae961` — *feat: establish ai runtime and
agent preview*. Working tree clean at start.

## 4. OpenAI package / version

**`OpenAI` 2.13.0**, referenced by `OmniAI.Infrastructure` only (plus, test-only,
by the API integration project — see §32).

Verified rather than assumed: the NuGet flat container lists 49 published
versions, of which **2.13.0 is the newest stable**. The brief's expectation was
therefore current and no upgrade decision arose.

No unofficial package, no Semantic Kernel, no `Microsoft.Extensions.AI`, no
LangChain, and no hand-rolled `HttpClient` calls to OpenAI.

## 5. Current Responses SDK experimental status

**The entire `OpenAI.Responses` namespace carries `[Experimental("OPENAI001")]`
in 2.13.0.** Established by reflecting over the shipped assembly, not from
documentation: every type the adapter touches — `ResponsesClient`,
`ResponsesClientOptions`, `CreateResponseOptions`, `ResponseItem`,
`ResponseResult` — is marked.

Because the solution builds with `TreatWarningsAsErrors`, using the API requires
an explicit acknowledgement.

`OPENAI002` (Realtime) and `SCME0002` (host-builder helpers) also exist in the
SDK; neither is used and neither is suppressed.

## 6. Suppression scope

`#pragma warning disable OPENAI001` in **three files**, each of which names a
Responses type — and nothing else in the solution does:

| File | Why |
| --- | --- |
| `Infrastructure/Ai/OpenAi/OpenAiTextGenerator.cs` | The adapter |
| `Infrastructure/Ai/OpenAi/OpenAiClientFactory.cs` | Builds the client |
| `tests/…/AiAgents/AiAgentOpenAiPreviewApiTests.cs` | The factory's return type |

**Not** in `Directory.Build.props`, **not** in any `.csproj`, **not** in a
repository-wide `NoWarn` — `grep -rn "NoWarn" ` over the build files finds
nothing. No unrelated warning is suppressed anywhere.

**Why the Responses API despite the evolving .NET surface.** It is the API OpenAI
documents as current for text generation and — the deciding reason — it accepts
**instructions and input items as separate fields**, which is exactly the
privilege boundary this platform's contract is built on. Chat Completions would
have required folding the agent's instructions into a message role, permanently
flattening the distinction the accepted architecture exists to preserve. The
marker warns that the *.NET shape* may change; that shape is confined to the
adapter, which is the whole point of having one.

## 7. `OpenAiTextGenerator` design

One internal sealed class implementing `IAiTextGenerator`. It builds the provider
request, makes one call, and translates the answer. No second interface, no broad
`IAiProvider`, no capability registry.

**No provider type escapes it.** Tests assert the *assembly* of what comes back:
results are `OmniAI.Application` types and so are failures. No `ResponseResult`,
`ClientResult`, `ClientResultException`, response id, usage object, status code or
raw JSON crosses the boundary.

## 8. Responses API mapping

`ResponsesClient.CreateResponseAsync(CreateResponseOptions, CancellationToken)`,
non-streaming. Text is extracted with the SDK's supported
`ResponseResult.GetOutputText()`.

The body the real serialiser produces, captured at the transport:

```json
{"model":"…","max_output_tokens":800,
 "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"…"}]}],
 "store":false,
 "instructions":"…",
 "stream":false}
```

Set deliberately: `model`, `instructions`, `input`, `store`,
`max_output_tokens`, `stream`. Deliberately absent: `tools`, `tool_choice`,
`previous_response_id`, `conversation`, `temperature`, `top_p`, `user`,
`safety_identifier`, `prompt_cache_key`, `metadata`.

## 9. Instructions mapping

`request.Instructions` → the provider's `instructions` field, verbatim.

**Never concatenated, prefixed, wrapped or rewritten.** No system message the
adapter invented, no model-specific preamble, no XML scaffolding. An adapter that
rewrote instructions would make an agent behave in a way its owner never
configured and could not see.

A test asserts the instruction text does not appear anywhere inside `input`, and
the message text does not appear inside `instructions` — checked on the serialised
bytes rather than on the options object.

## 10. Turn mapping

| Application | OpenAI |
| --- | --- |
| `AiTurnRole.User` | input item, `role: "user"` |
| `AiTurnRole.Assistant` | input item, `role: "assistant"` |

Order is preserved exactly; a four-turn test asserts both the role sequence and
the text sequence on the wire.

**No system or developer item can be produced.** The mapping is an exhaustive
switch over the two Application roles with a throwing default. The SDK's
`CreateSystemMessageItem` and `CreateDeveloperMessageItem` are unreachable from
this code, and a test parses the request body to assert every item's role is
`user` or `assistant`.

## 11. Model configuration

`OpenAI:Model`, default **`gpt-5.6-luna`** — the low-cost model this phase was
asked to use, appropriate because preview is staff testing a configuration rather
than customer-facing work. The live smoke confirmed the provider accepts it.

Infrastructure configuration only. **Not** on `AiAgent`, **not** on
`AiTextGenerationRequest`, **not** in the preview DTO. Per-agent model selection
is deferred; it needs a product decision and a Domain change with its own
authorisation.

## 12. API-key configuration

Read as `OPENAI_API_KEY` through `IConfiguration`, which in the hosted
application is supplied by the environment-variable provider. Handed straight to
the client factory: not bound into `OpenAiOptions`, not stored in a field, not
logged, not included in any exception. Only presence is ever tested.

**Going through configuration rather than `Environment.GetEnvironmentVariable` is
a deliberate deviation** — see §44. In production it is the same environment
variable; in tests it is controllable without mutating process-global state.

**Never in settings.** A test walks every `appsettings*.json` and
`launchSettings.json` in the repository and asserts none contains
`OPENAI_API_KEY` or an `sk-` prefixed string. `appsettings.json` is unmodified and
has no `OpenAI` section.

**Never printed.** No command run in this phase echoed the value; only a length
was ever observed, and no value appears in any transcript, file or log.

## 13. `store = false` configuration

`CreateResponseOptions.StoredOutputEnabled = false` on every request. Asserted on
the serialised body in the adapter tests and again on the real endpoint path in
the API tests.

`previous_response_id` and `conversation` are never set, so nothing chains.

> **`store = false` is NOT Zero Data Retention.** It disables the Responses
> application-state storage — the provider does not keep the response as a
> retrievable resource. **Standard OpenAI API retention policies may still
> apply** unless the organisation holds a Zero Data Retention arrangement, which
> is contractual and cannot be conferred by a request flag.

## 14. Max-output configuration

`OpenAI:MaxOutputTokens`, default **800**.

A cost and runaway-output safety bound — **explicitly not a model context limit
and not a Domain rule**. 800 tokens is roughly 600 words: comfortably more than
any customer-service reply needs, far short of runaway generation. Preview is also
the most exposed AI surface here, being unauthenticated, so a bound that keeps a
single call cheap matters more on this endpoint than anywhere else.

A response cut off at the bound still returns what was generated; refusing it
would spend the money and discard the result.

## 15. Timeout configuration

`OpenAI:TimeoutSeconds`, default **30**, applied as the client pipeline's
`NetworkTimeout`.

Preview is interactive — somebody is watching a spinner, and a longer wait is
indistinguishable from a hang. Finite because an unbounded wait is how a slow
dependency becomes an outage.

The caller's `CancellationToken` still propagates and still wins: a caller who
gives up after two seconds is not made to wait thirty.

## 16. SDK retry configuration

**`RetryPolicy = new ClientRetryPolicy(maxRetries: 0)`.**

The official SDK retries **408, 429, 500, 502, 503 and 504 by default**. Phase 8
authorised no retry policy, so accepting that default would have meant a hidden
one: one preview quietly becoming several billed calls, an "unavailable" answer
arriving after four timeouts instead of one, and load multiplying during exactly
the incidents where that does most harm.

Proven, not assumed: a parameterised test drives each of those five statuses
through the real client and asserts the transport recorded **exactly one** call.

No Polly, no resilience pipeline, no application retry loop, no provider
fallback.

## 17. Provider error mapping

| Provider outcome | Application type | HTTP |
| --- | --- | --- |
| 401, 403 | `AiRuntimeUnavailableException` | **503** |
| 429 | `AiRuntimeUnavailableException` | **503** |
| 5xx | `AiRuntimeUnavailableException` | **503** |
| 400, 404, 422 | `AiRuntimeUnavailableException` | **503** |
| 408, 504 | `AiRuntimeTimeoutException` | **504** |
| transport deadline expired | `AiRuntimeTimeoutException` | **504** |
| null / empty / whitespace reply | `AiResponseInvalidException` | **502** |
| caller cancelled | `OperationCanceledException`, rethrown | — |

**Four flat types, no hierarchy, no per-status classes.** Every case collapses
into the two questions a caller has — can I try again, and is it my fault?
Everything answerable "try later, and no" is 503; a timeout is separated because
it says the work probably started; a garbage reply is 502 because this server
acted as a gateway and upstream sent something invalid.

**401 and 429 are reported identically to clients**, deliberately: a rejected key
is an operator's problem, and distinguishing it would tell an unauthenticated
caller something true about this deployment's configuration. Operators get the
distinction from a Warning-level log carrying a category and a status code, and
nothing else.

**A provider 400 is not the caller's fault**: this platform validated the message
already, so the provider disagreeing means the adapter or the configured model is
wrong.

**Nothing leaks.** Canned error bodies plant an `sk-`-shaped string,
`api.openai.com`, a request id and an OpenAI error code; tests assert none of it
reaches the exception message or the HTTP body — including the status code
itself.

## 18. Cancellation

The caller's token reaches `CreateResponseAsync`. If it fires, the
`OperationCanceledException` is rethrown unchanged and never converted into a
provider failure — a closed browser tab is not an incident, and treating it as one
would corrupt any latency signal built on these failures.

**An already-cancelled request never reaches the provider.** The adapter checks
the token before dispatching. This was added because a test caught the SDK
pipeline *not* short-circuiting a pre-cancelled token — it reached the transport.
With a paid provider behind this, a caller who has already given up must not still
be able to spend money. See §45.

## 19. Unconfigured behaviour

Unchanged and still accepted. No key — or a blank one — and `IAiTextGenerator`
resolves to `UnconfiguredAiTextGenerator`: preview answers **503**, the
application starts normally, health checks answer, and the whole agent management
slice works.

A blank key counts as no key: treating an empty string as a credential would
produce 401s from the provider instead of the clear "not configured on this
deployment" answer.

Startup never fails for want of a provider. It *does* fail for a malformed
setting — a non-numeric or non-positive bound — because a deployment that meant to
cap spending at 200 tokens and mistyped it must not silently run at whatever this
code prefers.

## 20. Deterministic transport tests

Every OpenAI test except the gated smoke uses an `HttpClientPipelineTransport`
over a canned `HttpMessageHandler`, with a placeholder credential and no network.

The substitution is at the **socket**, deliberately: the real adapter, the real
client factory, the real SDK, the real request serialiser and the real response
deserialiser all run. A test that substituted `IAiTextGenerator` would prove
nothing about the adapter; one that substituted the client would prove nothing
about what this platform puts on the wire.

Canned response bodies are raw JSON in the real wire format, taken from an actual
round-trip through the installed SDK — not built from the SDK's own model types,
which would make the test agree with itself and prove nothing about
deserialisation.

## 21. Request-shape tests

**`OpenAiRequestShapeTests` — 20 tests.** Asserted against the serialised body:

configured model sent; default model is the documented one; instructions in the
provider's instruction field; **instructions never concatenated into input**; user
turn → user item; assistant turn → assistant item; four-turn order preserved
exactly; **no system or developer item ever produced**; `store: false`; configured
and default output bounds; `stream: false`; **no tool of any kind** (no
`web_search`, `file_search`, `computer_use`, `code_interpreter`, `function`,
`mcp`, `tool_choice`); no `previous_response_id` or `conversation`; no `user`,
`safety_identifier`, `prompt_cache_key` or `metadata`; no `temperature` or
`top_p`; **credential in the header and never in the body**; no platform metadata
(tenant, agent id, `omniai`, `xmin`, `postgres`); exactly one HTTP call per
generation; multiline text survives serialisation.

## 22. Response tests

**`OpenAiResponseTests` — 10 tests.** Valid text becomes a result; interior
formatting preserved; **multiple output text parts handled per the installed
SDK's own `GetOutputText` semantics**, with both parts present and in order; empty
`output` array refused; blank, whitespace-only and empty text refused; ends
trimmed; only `AiTextGenerationResult` comes back, from the `OmniAI.Application`
assembly; no response id or usage reaches the caller.

## 23. Provider failure tests

**`OpenAiFailureTests` — 23 tests.** 401 and 403; 429; 500, 502 and 503; 408 and
504; transport-deadline timeout; provider 400; **no provider exception type ever
escapes** (asserted by assembly); **no provider detail in the failure message**
across four statuses; **SDK retries disabled** across all five default-retried
statuses; nothing retried after an unusable response; caller cancellation stays
cancellation; a pre-cancelled request never reaches the provider.

**`OpenAiRegistrationTests` — 18 tests.** Both registration branches from
in-memory configuration; blank key counts as none; singleton lifetime; model and
bound defaults and overrides; unparseable and non-positive settings fail at
startup; the key is never bound into the options; **no settings file in the
repository carries a key**.

## 24. API integration tests

**`AiAgentOpenAiPreviewApiTests` — 17 tests**, with the real adapter installed
over a canned socket against real PostgreSQL. Proves the endpoint resolves to
`OpenAiTextGenerator`; 200 with the generated reply; stored instructions reach
OpenAI and the message stays a user turn; **a client-supplied `instructions` field
never reaches OpenAI**; **a cross-tenant preview never reaches OpenAI** (call count
zero, body empty); a missing agent never reaches it; an invalid message never
reaches it; **preview persists nothing** — `updated_at` unchanged in the database,
entity tag unchanged, zero conversation and zero message rows, no `ETag` header;
`store: false` on the real path; five provider statuses map to their HTTP
statuses with exactly one attempt; an unusable reply is 502; failure bodies leak
nothing; successful bodies carry only `reply`.

**`ApiProviderIsolationTests` — 2 tests.** The production `OmniAI.Api` assembly
references no provider SDK and not even `System.ClientModel`.

The Phase 8 suites are all preserved and still pass.

## 25. Live OpenAI smoke executed

**YES.** One call, through `AiAgentLiveOpenAiSmokeTests`, gated on
`OMNIAI_LIVE_OPENAI=1` **and** a configured credential. Path exercised:

```
HTTP -> PreviewAiAgent -> IAiTextGenerator -> OpenAiTextGenerator
     -> OpenAI Responses API -> reply
```

## 26. Live smoke safe result

**PASSED.** The agent was instructed to reply with exactly `OMNIAI_OPENAI_OK`; the
reply came back non-empty and containing that token. The response body carried
only `reply` and no entity tag, confirming the contract holds on the live path.

The configured model `gpt-5.6-luna` was **accepted by the provider** — no
model-not-found, no quota or credit failure, no authentication failure. One
request only.

No credential value was read, printed or logged by the test, and no Authorization
header was recorded anywhere.

## 27. Usage metadata observed

Available on `ResponseResult.Usage` in 2.13.0:

| Member | Type |
| --- | --- |
| `Usage.InputTokenCount` | `int` |
| `Usage.OutputTokenCount` | `int` |
| `Usage.TotalTokenCount` | `int` |
| `Usage.InputTokenDetails.CachedTokenCount` | `int` |
| `Usage.OutputTokenDetails.ReasoningTokenCount` | `int` |

Also present: `Id`, `Status`, `Model`, `CreatedAt`, `Error`,
`IncompleteStatusDetails.Reason`.

**`AiTextGenerationResult` was deliberately not widened.** No consumer needs any
of it yet, and a published contract is far harder to shrink than to grow. This is
recorded so the phase that designs usage and cost can design against what a real
provider actually reports, rather than against a guess.

## 28. Domain modified

**NO.** `git status --short backend/src/OmniAI.Domain` is empty. No provider,
model, API key, token limit, OpenAI id or response id entered `AiAgent`.

## 29. Application contract modified

**Effectively no — the contract is unchanged; one failure type was added.**

`IAiTextGenerator`, `AiTextGenerationRequest`, `AiTextGenerationResult`, `AiTurn`
and `AiTurnRole` are all byte-for-byte as accepted in Phase 8. No OpenAI type
appears in the Application, and the existing guard tests confirm the Application
assembly references no provider SDK.

**Added: `AiRuntimeTimeoutException`.** A real provider introduced a state Phase 8
could not reach — a request accepted, never refused, that simply did not come
back. The recommended mapping asks for **504** for a provider timeout, which needs
a type distinct from "unavailable". It is provider-independent, transport-neutral
and flat, with no base class. This is the smallest change that expresses the new
state.

## 30. Migration changed

**NO.** `backend/src/OmniAI.Infrastructure/Persistence/Migrations` is unchanged.
Preview is read-only and stores nothing; no AI table, no preview history, no usage
persistence. An API test asserts zero conversation and zero message rows after a
preview.

## 31. Frontend modified

**NO.** `git status --short -- ':!backend' ':!docs'` is empty.

## 32. Packages added

**Two, both justified.**

| Package | Version | Where | Why |
| --- | --- | --- | --- |
| `OpenAI` | 2.13.0 | `OmniAI.Infrastructure` (production) + `OmniAI.Api.IntegrationTests` (test-only) | The provider SDK this phase exists to integrate |
| `Microsoft.Extensions.Configuration` | 10.0.11 | `OmniAI.Infrastructure.IntegrationTests` only | In-memory `IConfiguration` so both registration branches are provable without mutating the process environment |

The second is first-party, already present transitively, and test-only; declaring
it explicitly is preferable to relying on another package's dependency graph. It
avoided adding `Microsoft.Extensions.Configuration.Binder` to production — the
adapter reads its three settings explicitly instead.

The test-only `OpenAI` reference in the API test project exists to build the
canned transport. The **production** `OmniAI.Api` assembly references no provider
SDK, and a guard test asserts it.

## 33. Tests before

**818** — Domain 288, Application 199, Infrastructure 151, Api 180.

## 34. Tests after

**908**, all passing, 0 failed, 0 skipped, in Debug and Release.

| Suite | Before | After | Added |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | 288 | **288** | 0 |
| `OmniAI.Application.Tests` | 199 | **199** | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 151 | **222** | +71 |
| `OmniAI.Api.IntegrationTests` | 180 | **200** | +20 |
| **Total** | **818** | **908** | **+90** |

New classes: `OpenAiRequestShapeTests` (20), `OpenAiFailureTests` (23),
`OpenAiRegistrationTests` (18), `OpenAiResponseTests` (10),
`AiAgentOpenAiPreviewApiTests` (17), `ApiProviderIsolationTests` (2),
`AiAgentLiveOpenAiSmokeTests` (1, gated).

**89 of the 90 run offline.** The live smoke does nothing unless explicitly gated
on.

## 35. Restore

`dotnet restore OmniAI.slnx` — succeeded, resolving the new `OpenAI` and
`Microsoft.Extensions.Configuration` references.

## 36. Release build

`dotnet build OmniAI.slnx -c Release` — **Build succeeded, 0 Warning(s), 0
Error(s)** across all 8 projects, with `TreatWarningsAsErrors` on and only the
three file-scoped `OPENAI001` suppressions.

## 37. Test result

**908 of 908 passing in both configurations.**

```
Debug     Domain 288 | Application 199 | Infrastructure 222 | Api 200
Release   Domain 288 | Application 199 | Infrastructure 222 | Api 200
```

Plus one gated live run: 1 of 1 passing.

## 38. Vulnerability result

`dotnet list OmniAI.slnx package --vulnerable --include-transitive` — **all 8
projects report "no vulnerable packages given the current sources"**, including
the newly added `OpenAI` 2.13.0 and its transitive `System.ClientModel` 1.14.0.

## 39. Warnings

**Zero.** No compiler or analyzer warning; nothing suppressed beyond the three
scoped `OPENAI001` pragmas; no `NoWarn` anywhere in the repository.

Three analyzer errors were raised during development and fixed at source rather
than suppressed: `CA1859` (return type widened to the concrete `Task<T>`),
`CA1861` twice (constant array arguments → `static readonly` fields), and
`CA1001` (a fixture owning a disposable field made `IDisposable`).

## 40. Secret review

**No credential of any kind is in the repository or in any output.**

- Scanned every created and modified file for `sk-`-shaped strings, `sk-proj-`,
  `sk-ant-`, private key blocks, bearer tokens, the VPS address and the VPS
  username. **No match.**
- `appsettings.json` is unmodified, has no `OpenAI` section and no credential. No
  `appsettings.Development.json` or `launchSettings.json` carries one — asserted
  by a test that walks the repository.
- The key was **never printed**. The only observation ever made of it was a
  boolean presence check and a character count; no value appears in any file,
  log, transcript or shell command.
- The environment was never enumerated or dumped.
- No key previously present in chat history was read or reused; the adapter reads
  only the process-supplied `OPENAI_API_KEY`.
- Every deterministic test uses the literal placeholder
  `test-key-not-a-real-credential`.
- Failure messages and log lines carry a category and a status code only — never
  a key, header, body, request id or endpoint.
- No build artefact, `bin`, `obj`, `node_modules` or IDE file is included.

## 41. External network calls: count and purpose

| Purpose | Calls | Host |
| --- | --- | --- |
| Verify the current stable SDK version | 1 | `api.nuget.org` |
| NuGet package restore | package downloads | `nuget.org` |
| **Authorised live smoke** | **1** | `api.openai.com` |
| **Unintended live calls — see below** | **~5** | `api.openai.com` |

**The incident, reported plainly.** After registration became key-dependent, the
Phase 8 API suites — which use a fixture that substitutes nothing — inherited this
machine's real `OPENAI_API_KEY` from the process environment and registered the
real adapter. Running the suite therefore made roughly five real, billed calls to
OpenAI before I noticed.

It surfaced as four test failures (`A_preview_returns_503` returning `200`, and
three like it), which is how it was caught. The cost is negligible at the
configured 800-token bound, but the principle is not: automated tests must never
depend on a developer's environment and must never be able to reach a paid API.

**Fixed at source.** `AiAgentApiFixture` now blanks `OPENAI_API_KEY` in the test
host's configuration, so every suite built on it is deterministically offline
whatever the machine has configured. A `DisableAiProvider` hook, defaulting to
"off", is what the single gated live fixture overrides — so a new fixture is
offline unless somebody deliberately says otherwise. The full suite was re-run
afterwards and passes with the provider forced off.

No other host was contacted. No VPS access, no deployment.

## 42. Architectural decisions

Recorded in full as D1–D17 in `docs/architecture/openai-provider.md`:

1. Official SDK 2.13.0, verified as current stable, Infrastructure only.
2. Responses API, chosen because it keeps instructions and input separate.
3. `OPENAI001` suppressed in three files, never repository-wide.
4. One adapter class; no provider type escapes it.
5. Structure-only mapping — no concatenation, no invented instructions, no
   reachable system or developer role.
6. Model is Infrastructure configuration, never Domain state.
7. The key comes from `OPENAI_API_KEY` through configuration and is never stored,
   logged or bound into options.
8. One singleton client, to avoid socket exhaustion.
9. `store = false`, and it is explicitly not Zero Data Retention.
10. 800-token output bound as cost safety, not a context limit.
11. A finite 30-second timeout that caller cancellation still overrides.
12. Zero SDK retries, proven by call count against every default-retried status.
13. Four flat failure types; 401 and 429 indistinguishable to clients.
14. Cancellation stays cancellation, and a pre-cancelled request costs nothing.
15. Unconfigured behaviour unchanged; startup never fails for want of a provider.
16. Tests substitute the socket, not the abstraction.
17. The live smoke is gated twice.

## 43. Deferred concerns

Tabulated with triggers in the architecture document. Deferred: per-agent model
and provider selection; Anthropic or any second provider; fallback; retries and
resilience; rate-limit and quota taxonomy; streaming; tools, functions and MCP;
RAG; structured output; multimodal input; token and cost persistence; quotas and
metering; safety identifiers and end-user tracking; prompt caching; evaluations;
authentication.

Usage metadata is available today and deliberately not surfaced (§27).

## 44. Deviations

Four, each small and each stated with its reason.

1. **The key is read through `IConfiguration` rather than
   `Environment.GetEnvironmentVariable`.** In the hosted application these are the
   same value — the environment provider supplies it — but going through
   configuration is what makes both registration branches testable without
   mutating process-global state that every parallel test shares. A test asserts
   no settings file in the repository can supply it instead.

2. **One Application exception type was added** (`AiRuntimeTimeoutException`), to
   express the 504 the recommended mapping asks for. See §29.

3. **`Microsoft.Extensions.Configuration` was added to one test project.** Needed
   for in-memory configuration; first-party, already transitively present,
   test-only. It let production avoid the configuration binder entirely.

4. **The API integration test project references the OpenAI package.** Needed to
   build the canned transport so the real adapter and real SDK serialisation run
   end to end. Test-only; the production API assembly references no provider SDK,
   and a guard test asserts it.

## 45. Known issues

Two findings from this phase, both fixed, plus two standing limits.

- **The SDK pipeline does not short-circuit a pre-cancelled token.** A test found
  the request reaching the transport. Fixed by checking the token before
  dispatching, so a caller who has already given up cannot spend money.
- **The test host inherited the machine's real key.** See §41 — found by a failing
  test, fixed at source.
- **Model, output bound and timeout are platform-wide.** No per-tenant or
  per-agent override exists. Adding one needs a product decision.
- **`AddOmniAiRuntime` requires `ILogger<>` to be registered.** Always true inside
  a host; a bare `ServiceCollection` must add it. The tests do so explicitly
  rather than production silently calling `AddLogging`.

## 46. Blockers

None for Phase 10.

Two standing constraints, both now sharper:

- **Preview is not authorised for production exposure.** Authentication still does
  not exist, so the tenant id is supplied by the caller and believed — and a real,
  billable provider now sits behind the endpoint. An unauthenticated public
  preview would let anyone spend this platform's API credit.
- **A Linux CI environment must run the complete suite in Release before any
  production release.** It must also run with no `OPENAI_API_KEY`, or the live
  smoke gate unset, so CI never makes billed calls.

## 47. `git status --short`

```
 M backend/Directory.Packages.props
 M backend/src/OmniAI.Api/Composition/CompositionRoot.cs
 M backend/src/OmniAI.Api/ErrorHandling/DomainProblemDetailsHandler.cs
 M backend/src/OmniAI.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs
 M backend/src/OmniAI.Infrastructure/OmniAI.Infrastructure.csproj
 M backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentApiFixture.cs
 M backend/tests/OmniAI.Api.IntegrationTests/OmniAI.Api.IntegrationTests.csproj
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Ai/UnconfiguredAiTextGeneratorTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/OmniAI.Infrastructure.IntegrationTests.csproj
 M docs/architecture/README.md
?? backend/src/OmniAI.Application/Ai/AiRuntimeTimeoutException.cs
?? backend/src/OmniAI.Infrastructure/Ai/OpenAi/
?? backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentLiveOpenAiSmokeTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentOpenAiPreviewApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/ApiProviderIsolationTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Ai/OpenAi/
?? docs/architecture/openai-provider.md
```

Nothing staged. `HEAD` is still `c2aae2a`. No remotes.
*(This report is a further untracked file, written after that listing.)*

## 48. `git diff --stat`

```
 backend/Directory.Packages.props                   |  17 +++
 .../src/OmniAI.Api/Composition/CompositionRoot.cs  |  14 +--
 .../ErrorHandling/DomainProblemDetailsHandler.cs   |  17 +++
 .../InfrastructureServiceCollectionExtensions.cs   | 125 ++++++++++++++++++---
 .../OmniAI.Infrastructure.csproj                   |  13 +++
 .../AiAgents/AiAgentApiFixture.cs                  |  39 +++++++
 .../OmniAI.Api.IntegrationTests.csproj             |   9 ++
 .../Ai/UnconfiguredAiTextGeneratorTests.cs         |  51 ++++++---
 .../OmniAI.Infrastructure.IntegrationTests.csproj  |   2 +
 docs/architecture/README.md                        |  12 +-
 10 files changed, 253 insertions(+), 46 deletions(-)
```

`git diff --check` is clean. 25 files touched: 15 created, 10 modified.

## 49. No API key appears anywhere in repository or output

**Confirmed.** See §40. The key was never printed, never written to a file, never
logged, never included in an exception, never placed in configuration committed
to the repository, and never enumerated from the environment. A test walks every
settings file in the repository to keep it that way. Every deterministic test uses
an obvious placeholder.

## 50. VPS not accessed

**Confirmed.** No SSH connection was attempted or made to the production server.
No address, credential or key for it appears in any file changed by this phase.
Nothing was deployed, no remote command was run, and no remote configuration was
read or written. The only processes started locally were the .NET build and test
host, the project's disposable PostgreSQL test cluster, and a throwaway SDK
reflection probe in the scratchpad directory.

## 51. Readiness recommendation for Phase 10

**Ready.** The abstraction held: a real provider was added without touching the
Domain, the use case, the endpoint or the HTTP contract, and the only Application
change was one new failure type for a state that previously could not occur. That
is the strongest available evidence that Phase 8's design was right.

Worth deciding at the start of Phase 10:

1. **Authentication is now the highest-value next step.** It was already the gate
   on production; a billable provider behind an unauthenticated endpoint makes it
   urgent rather than merely pending.
2. **Whether usage should be captured**, now that real token counts are available
   (§27) — and if so, whether `AiTextGenerationResult` grows or a separate
   observation path is used.
3. **Which provider failures are actually transient**, observed rather than
   guessed, before any retry policy is designed.
4. **Whether per-agent model selection is wanted**, which is a product decision
   with a Domain consequence.
5. **How CI is configured** so the live smoke gate is never set and no key is
   present, keeping continuous integration free of billed calls.
