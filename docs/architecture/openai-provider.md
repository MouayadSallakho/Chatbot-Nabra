# The OpenAI provider

Phase 9 implements the first real provider behind the Phase 8 contract. Nothing
above Infrastructure changed shape: the Domain, the preview use case, the
endpoint and the HTTP contract are the ones already accepted, and the adapter
slotted in underneath them.

```
PreviewAiAgentHandler          (Application — unchanged)
        |
        v
IAiTextGenerator               (Application — unchanged)
        |
        +-- UnconfiguredAiTextGenerator   no OPENAI_API_KEY  -> refuses, 503
        +-- OpenAiTextGenerator           key configured     -> OpenAI Responses API
```

> **The vendor stops at one project.**
>
> The `OpenAI` package is referenced by `OmniAI.Infrastructure` and by nothing
> else in production. Guard tests assert that `OmniAI.Application` and
> `OmniAI.Api` reference no provider SDK, checked against compiled assembly
> metadata rather than source text.

---

## DECIDED NOW

### D1 — The official SDK, version 2.13.0

**Decision.** `OpenAI` 2.13.0, from nuget.org, referenced only by Infrastructure
through Central Package Management.

**Why this version.** The brief named 2.13.0 and asked for it to be verified
rather than assumed. Querying the NuGet flat container showed 49 published
versions, of which 2.13.0 is the newest stable — so there was no upgrade decision
to make and no release notes to weigh.

**Why the official SDK.** No unofficial wrapper, no Semantic Kernel, no
`Microsoft.Extensions.AI`, no LangChain. A second abstraction layer would sit
between two abstractions that already exist — this platform's
`IAiTextGenerator` above and the vendor's client below — and would have to be
understood, versioned and debugged for no gain. Hand-rolled `HttpClient` calls
were not needed either: the SDK does exactly what this adapter requires.

### D2 — The Responses API, not Chat Completions

**Decision.** `ResponsesClient.CreateResponseAsync`.

**Why.** It is the API OpenAI documents as current for text generation, and — the
reason that actually decided it — **it takes instructions and input items as
separate fields**. That is precisely the privilege boundary this platform's
contract is built on. Chat Completions would have required folding the agent's
instructions into a message role, flattening the very distinction the accepted
architecture exists to preserve.

Not the Assistants API, not threads, not Conversations: all of them make the
provider hold state, and preview is stateless by design.

### D3 — Experimental surface: `OPENAI001`, suppressed in three files

**Decision.** The entire `OpenAI.Responses` namespace in 2.13.0 carries
`[Experimental("OPENAI001")]` — verified by reflecting over the shipped assembly,
not assumed. Because the solution builds with `TreatWarningsAsErrors`, using it
requires an explicit acknowledgement.

It is suppressed with `#pragma warning disable OPENAI001` in exactly three files,
each of which names a Responses type and nothing else does:

| File | Why it needs it |
| --- | --- |
| `Infrastructure/Ai/OpenAi/OpenAiTextGenerator.cs` | The adapter itself |
| `Infrastructure/Ai/OpenAi/OpenAiClientFactory.cs` | Builds the client |
| `tests/…/AiAgentOpenAiPreviewApiTests.cs` | The factory's return type |

**Not** in `Directory.Build.props`, **not** in any `.csproj`, **not** in a
repository-wide `NoWarn`. A future file that needs it must ask again, which is a
decision somebody takes rather than one already made for them. No other
diagnostic is suppressed anywhere.

**Why accept an experimental surface at all.** The marker says the *.NET shape*
may change — not that the API is unreliable. The shape that would change is
confined to the adapter, which is the entire reason the adapter exists. The
alternative, Chat Completions, would have cost the privilege boundary
permanently to avoid a rename.

### D4 — `OpenAiTextGenerator`

**Decision.** One class implementing `IAiTextGenerator`. It translates the
request, makes one call, and translates the answer. No second interface, no
`IAiProvider`, no capability registry.

**No provider type escapes it.** Callers see `AiTextGenerationResult` or one of
the Application's own failure types. No `ResponseResult`, `ClientResult`,
`ClientResultException`, response id, usage object, status code or raw JSON
crosses the boundary — asserted by tests that check the *assembly* of what comes
back.

### D5 — Request mapping: structure only

| This platform | OpenAI Responses |
| --- | --- |
| `request.Instructions` | `instructions` |
| `AiTurn` with `Role = User` | input item, `role: "user"` |
| `AiTurn` with `Role = Assistant` | input item, `role: "assistant"` |
| turn order | input array order, unchanged |

**Nothing is concatenated, prefixed, wrapped or invented.** No system message the
adapter made up, no model-specific preamble, no XML scaffolding, no magic prefix.
An adapter that rewrote instructions would make an agent behave in a way its
owner never configured and could not see.

**No system or developer item can be produced.** The Application has only two
roles, and the mapping is an exhaustive switch over them. The SDK offers
`CreateSystemMessageItem` and `CreateDeveloperMessageItem`; neither is reachable
from this code, which is what carries the privilege boundary all the way to the
wire. A test parses the serialised body and asserts every input item's role is
`user` or `assistant`.

The observed request body, captured from the real SDK serialiser:

```json
{"model":"…","max_output_tokens":800,
 "input":[{"type":"message","role":"user","content":[{"type":"input_text","text":"…"}]}],
 "store":false,
 "instructions":"…",
 "stream":false}
```

Instructions and input are visibly separate. That is the assertion, made against
what OpenAI would actually receive rather than against the options object this
code happened to fill in.

### D6 — Model is Infrastructure configuration

**Decision.** `OpenAI:Model`, defaulting to **`gpt-5.6-luna`** — the low-cost
model this phase was asked to use, appropriate because preview is staff testing a
configuration rather than customer-facing work.

**Not** on `AiAgent`, **not** on `AiTextGenerationRequest`, **not** in the preview
request DTO. Per-agent model selection is a product feature needing its own
design, and putting a vendor's catalogue into the Domain would make one
supplier's naming part of this platform's business vocabulary.

### D7 — The API key

**Decision.** Read as `OPENAI_API_KEY` through `IConfiguration`, which in the
hosted application is supplied by the environment-variable provider. It is handed
straight to the client and never stored, logged, bound into options, or included
in any exception.

**Why through configuration rather than `Environment.GetEnvironmentVariable`.**
In production these are the same thing — the environment provider is where the
value comes from. Going through configuration is what makes both registration
branches testable without mutating the process environment, which is global state
shared with every parallel test and would make results depend on whether the
developer running them happens to have a key.

**Never in `appsettings`.** A test walks every `appsettings*.json` and
`launchSettings.json` in the repository and asserts none contains
`OPENAI_API_KEY` or an `sk-` prefixed string.

**Never printed.** No command in this phase echoed the value; only its presence
was ever tested.

### D8 — Client lifetime: one singleton

**Decision.** The `ResponsesClient` and the adapter are both singletons.

**Why.** The client owns an HTTP stack. Building one per request exhausts sockets
under load — the classic `HttpClient` failure — and the SDK's clients are
documented as reusable and thread-safe.

### D9 — `store = false`

**Decision.** `CreateResponseOptions.StoredOutputEnabled = false` on every
request, asserted on the serialised body and again on the real endpoint path.

**Why.** Preview is stateless. A platform that deliberately keeps no record of an
exchange should not quietly leave a retrievable copy with a third party either.
It also keeps `previous_response_id` meaningless here, which is the point: there
is nothing to chain to.

> ### `store = false` is NOT Zero Data Retention
>
> It disables the **Responses application-state storage** — the provider does not
> keep the response as a retrievable resource. It says nothing about the
> provider's own retention of API traffic. **Standard OpenAI API retention
> policies still apply** unless the organisation holds a Zero Data Retention
> arrangement, which is a contractual matter and not something a request flag can
> confer. Anyone reading this as ZDR would be wrong, and would be wrong in the
> direction that matters.

### D10 — Output bound: 800 tokens

**Decision.** `OpenAI:MaxOutputTokens`, default **800**.

**A cost and runaway-output safety bound. Not a model context limit, and not a
Domain rule.** It says nothing about what the model can read; it says what this
platform will pay for in one preview.

800 tokens is roughly 600 words — several paragraphs, comfortably more than any
customer-service reply needs, and far short of the runaway generation that turns
one stuck request into a real bill. Preview is also the most exposed AI surface
here: it is unauthenticated today, so a bound that keeps a single call cheap
matters more on this endpoint than anywhere else.

A response cut off at the bound still returns what was generated. Refusing it
would spend the money and then discard the result.

### D11 — Timeout: 30 seconds

**Decision.** `OpenAI:TimeoutSeconds`, default **30**, applied as the client
pipeline's `NetworkTimeout`.

**Why 30.** Preview is interactive — somebody is watching a spinner, and a longer
wait is indistinguishable from a hang. It is generous for a completion at the
configured output bound and short enough that a stalled upstream does not hold a
request thread, and a person, indefinitely.

**Why finite at all.** An unbounded wait is how a slow dependency becomes an
outage: requests accumulate faster than they retire until nothing is served.

The caller's own `CancellationToken` still propagates and still wins. A caller
who gives up after two seconds is not made to wait thirty.

### D12 — Zero automatic retries

**Decision.** `RetryPolicy = new ClientRetryPolicy(maxRetries: 0)`.

**Why this is load-bearing.** The official SDK retries **408, 429, 500, 502, 503
and 504 by default**. Phase 8 deliberately authorised no retry policy, so
accepting that default would have meant a hidden one: a single preview quietly
becoming several billed calls, an "unavailable" answer arriving after four
timeouts instead of one, and load multiplying during exactly the incidents where
that does the most harm.

A test drives each of those statuses through the real client and asserts the
transport saw **exactly one** call. Without the policy it would see several.

No Polly, no resilience pipeline, no application retry loop, no provider
fallback. Resilience will be designed once real provider failure behaviour has
been observed rather than guessed.

### D13 — Failure translation

Four flat Application types, three of them from Phase 8.

| Provider outcome | Application type | HTTP |
| --- | --- | --- |
| 401, 403 — credentials rejected | `AiRuntimeUnavailableException` | **503** |
| 429 — rate or capacity limit | `AiRuntimeUnavailableException` | **503** |
| 5xx — provider service failure | `AiRuntimeUnavailableException` | **503** |
| 400, 404, 422 — provider rejected the request | `AiRuntimeUnavailableException` | **503** |
| 408, 504 — upstream timeout | `AiRuntimeTimeoutException` | **504** |
| transport deadline expired | `AiRuntimeTimeoutException` | **504** |
| null, empty or whitespace reply | `AiResponseInvalidException` | **502** |
| caller cancelled | `OperationCanceledException` (rethrown) | — |

**Why so few types.** A class per OpenAI status code would be a catalogue nobody
reads. Every case above collapses into the two questions a caller actually has:
can I try again, and is it my fault? Everything answerable with "try later, and
no" is 503; a timeout is separated because it tells the caller the work probably
started; a garbage reply is 502 because this server was a gateway and upstream
sent something invalid.

**Authentication and rate limiting are reported identically to clients**, and
that is deliberate: a rejected key is an operator's problem, and distinguishing
it would tell an unauthenticated caller something true about this deployment's
configuration. The distinction operators need is logged instead, at Warning, as a
category plus a status code.

**A provider 400 is not the caller's fault.** This platform already validated the
message, so the provider disagreeing means the adapter or the configured model is
wrong. Blaming whoever typed the message would send them to fix the one thing
that was not broken.

**Nothing about the provider reaches the client.** No vendor name, exception
type, response body, request id, endpoint, header or status code appears in any
message. Tests plant an `sk-`-shaped string, a request id and `api.openai.com` in
a canned error body and assert none of it surfaces — through the adapter and
again through the HTTP response.

### D14 — Cancellation

**Decision.** The caller's token reaches the SDK call. If it fires, an
`OperationCanceledException` is rethrown unchanged — never converted into a
provider failure.

**Why the distinction matters.** A closed browser tab is not an incident.
Reporting it as one would make an ordinary event look like a fault and would
corrupt any latency signal built on these failures.

**An already-cancelled request never reaches the provider.** The adapter checks
the token before dispatching. This was added because a test found the SDK
pipeline does *not* short-circuit a pre-cancelled token — it reached the
transport. With a paid provider behind this, a caller who has already given up
must not still be able to spend money.

### D15 — Unconfigured behaviour is unchanged

**Decision.** No key, or a blank one, and `IAiTextGenerator` still resolves to
`UnconfiguredAiTextGenerator`: preview answers **503** and the application starts
normally.

A blank key counts as no key. Treating an empty string as a credential would
produce 401s from the provider instead of the clear "not configured on this
deployment" answer.

**Startup never fails for want of a provider.** A developer who has not
configured OpenAI must not be met with an application that refuses to run. What
*does* fail at startup is a malformed setting — a non-numeric or non-positive
bound — because a deployment that meant to cap spending at 200 tokens and
mistyped it must not silently run at whatever this code prefers.

### D16 — Test transport

**Decision.** Deterministic tests inject an `HttpClientPipelineTransport` over a
canned `HttpMessageHandler`.

**Why at the socket.** The real adapter, the real client factory, the real SDK,
the real request serialiser and the real response deserialiser all run. A test
that substituted `IAiTextGenerator` would prove nothing about the adapter; one
that substituted the client would prove nothing about what this platform puts on
the wire. Only here can "instructions are not concatenated" and "no tool is
enabled" be asserted against the bytes OpenAI would have received.

Every deterministic test uses an obvious placeholder credential and touches no
network.

### D17 — Live smoke: gated twice

**Decision.** One live test, which runs only when `OMNIAI_LIVE_OPENAI=1` **and** a
credential is present. It goes through the real endpoint:

```
HTTP -> PreviewAiAgent -> IAiTextGenerator -> OpenAiTextGenerator
     -> OpenAI Responses API -> reply
```

It asks the agent to reply with a fixed token and asserts that token comes back,
that the body carries only `reply`, and that no entity tag is present.

**Why gated twice.** It is the only test in the solution that can reach the
internet or cost money, so it has to be asked for deliberately. Every other suite
forces the provider off regardless of what the machine has configured — see the
deviation note in the phase report about why that override exists.

---

## DEFERRED

| Deferred | Trigger |
| --- | --- |
| Model selection per agent | A product decision, plus a Domain change needing its own authorisation |
| Provider selection per agent | The second provider |
| Anthropic or any second provider | A requirement; the contract already allows it |
| Provider fallback | A second provider, plus a decision about what silent substitution means |
| Retries and resilience | Observed provider failure behaviour — which failures are actually transient |
| Rate-limit and quota taxonomy | Observed provider error responses over time |
| Streaming | A UI that renders partial output |
| Tools, function calling, MCP | A requirement for the agent to act |
| RAG and file search | A knowledge requirement |
| Structured output | A caller needing a schema rather than prose |
| Multimodal input | A channel that carries it |
| Token and cost persistence | A usage phase; the data now exists (see below) |
| Quotas and metering | Persisted usage to meter against |
| Safety identifiers, end-user tracking | A policy decision about what identifies a person to a vendor |
| Prompt caching policy | A workload where it would pay |
| Evaluations | A quality baseline worth regressing against |
| Authentication | The authentication phase — still the gate on production exposure |

**Usage metadata is available and deliberately not surfaced.** The Responses
result carries `Usage.InputTokenCount`, `Usage.OutputTokenCount`,
`Usage.TotalTokenCount`, `Usage.InputTokenDetails.CachedTokenCount` and
`Usage.OutputTokenDetails.ReasoningTokenCount`. `AiTextGenerationResult` was not
widened to carry any of it, because no consumer needs it yet and a contract is
harder to shrink than to grow. It is recorded here so the phase that designs
usage and cost can design against what a real provider actually reports.

---

## Security status

**Unchanged and still the gate on production.** Authentication does not exist, so
the tenant id in the route is supplied by the caller and believed. The preview
endpoint remains **development-only and is not authorised for public exposure** —
and a real, billable provider now sits behind it, so an unauthenticated public
preview endpoint would let anyone spend this platform's API credit. That makes
the existing restriction more important, not less.

What this phase establishes:

- **Tenant ownership is resolved before the provider is invoked.** Asserted at
  the Application level and again over HTTP with a real adapter: a cross-tenant
  preview leaves the provider call count at zero, so no agent's instructions
  leave the process and no credit is spent.
- **Instructions cannot be supplied by a caller**, proven now against the bytes
  sent to OpenAI.
- **No credential is committed, printed, logged, or placed in any settings file**,
  with a test walking the repository to keep it that way.
- **No provider detail reaches a client** — vendor, endpoint, request id, status
  code and body are all withheld.
- **Automated tests cannot reach the internet**, whatever the developer's machine
  has configured.

---

## Things a reviewer can check independently

1. `OpenAI` appears in `Directory.Packages.props` once and in
   `OmniAI.Infrastructure.csproj` — plus, test-only, in the API test project.
2. `grep -rn "OPENAI001" backend/` finds three files, no `.csproj` and no
   `Directory.Build.props`.
3. `OpenAiTextGenerator` maps only `User` and `Assistant`, with a throwing
   default arm.
4. `StoredOutputEnabled = false` in `OpenAiTextGenerator.BuildOptions`.
5. `RetryPolicy = new ClientRetryPolicy(maxRetries: 0)` in `OpenAiClientFactory`.
6. `NetworkTimeout` is set from configuration in the same file.
7. No `Tools`, `PreviousResponseId`, `Temperature`, `TopP`, `EndUserId`,
   `SafetyIdentifier` or `Metadata` is set anywhere.
8. `Environment.GetEnvironmentVariable` appears in no production file.
9. `git diff --stat HEAD -- backend/src/OmniAI.Domain` is empty.
10. `backend/src/OmniAI.Infrastructure/Persistence/Migrations` is unchanged.
11. `appsettings.json` has no `OpenAI` section and no credential.
12. The API and Application assemblies reference no provider SDK — guard tests.
