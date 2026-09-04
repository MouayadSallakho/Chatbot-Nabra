# The AI runtime

Phase 8 establishes the platform's first executable AI path: a
provider-independent text-generation contract owned by the Application, a
stateless agent preview use case, an HTTP endpoint, and honest behaviour on a
deployment where no provider is configured.

**No AI provider is implemented.** No SDK is installed, no API key is read, no
model is chosen, and no request leaves this machine. What Phase 8 proves is the
shape of the path, so that adding a real provider later changes one registration
line and nothing above it.

```
AiAgent  (Domain: name, instructions, lifecycle)
   |
   |  instructions, read after tenant ownership is resolved
   v
PreviewAiAgentHandler  (Application use case)
   |
   |  AiTextGenerationRequest { Instructions, Turns }
   v
IAiTextGenerator  (Application contract — one capability)
   |
   +-- UnconfiguredAiTextGenerator   (Infrastructure, today: refuses explicitly)
   +-- an OpenAI adapter             (Infrastructure, later)
   +-- an Anthropic adapter          (Infrastructure, later)
```

> **The privilege boundary is structural, not conventional.**
>
> Agent instructions and conversational turns are separate slots of separate
> types. There is no `System` role a turn could carry, no single prompt string in
> which the boundary could be lost, and no `instructions` field on the preview
> request. Caller text cannot become platform instruction because there is
> nowhere for it to go.

> **Ownership is resolved before anything is sent anywhere.**
>
> A request naming another tenant's agent is refused before its instructions are
> read, before a generation request is assembled, and before the generator is
> reached. A test asserts the generator's call count is still zero.

---

## DECIDED NOW

### C1 — A capability contract, not a provider interface

**Decision.** `IAiTextGenerator` has exactly one method:

```csharp
Task<AiTextGenerationResult> GenerateAsync(
    AiTextGenerationRequest request,
    CancellationToken cancellationToken = default);
```

There is no `IAiProvider` carrying embeddings, image generation, speech,
transcription, moderation, tool calling, structured output, streaming or model
management.

**Why.** A broad provider interface has to be implemented in full by every
adapter, and most adapters would implement most of it as methods that throw. The
platform would then be asking "does this provider really support that?" at
runtime — which is the question a type system exists to answer at compile time.
Naming the capability rather than the vendor also means an adapter that can only
generate text is a first-class implementation rather than a partial one.

When the platform needs embeddings it gets its own contract, and an adapter
implements the capabilities it actually has.

### C2 — The abstraction is owned by the Application

**Decision.** The contracts live in `OmniAI.Application.Ai`. Implementations will
live in `OmniAI.Infrastructure`. The Domain knows nothing about any of it.

**Why.** The use cases are what need text generated, so the contract belongs
where the need is; a vendor is a detail, so it belongs outside. This is the same
arrangement as `IAiAgentRepository`, and it is what makes a provider swappable
without a single use case changing.

`AiAgent` gained nothing: no provider, no model, no generation method, no
preview state, no token usage. A test resolves the real types and asserts no
Domain member exposes any of them.

### C3 — Instructions and turns are separate slots

**Decision.** `AiTextGenerationRequest` has exactly two properties:

| | |
| --- | --- |
| `Instructions` | `string` — what the business configured this agent to be |
| `Turns` | `IReadOnlyList<AiTurn>` — what people said, oldest first |

Nothing in the platform concatenates them.

**Why.** This is the whole reason the request is a type rather than a prompt
string. The instruction layer is privileged: it can say what the agent must never
do, what it must never disclose, and how to escalate. Conversational content is
not privileged — it is text somebody typed, and some of those people will type
"ignore your previous instructions".

If the platform flattened these into one string, the boundary would exist only in
the mind of whoever wrote the concatenation. Keeping them apart means an adapter
receives them still separated and can map each onto whatever its API calls the
same idea — a system message and a message array, an instructions field and an
input list, something else again. Each provider does this differently, which is
precisely why flattening here would decide it once, wrongly, for all of them.

### C4 — Turns carry only `User` and `Assistant`

**Decision.** `AiTurnRole` has two members. There is no `System`, `Developer`,
`Tool`, `Function`, `Image`, `Audio` or `Document`.

**Why.** Two different reasons, worth separating.

`System` and `Developer` are absent **on purpose and permanently in this shape**.
The privileged layer is already `Instructions`. A system role on a turn would
create a second, equally privileged channel — one reachable from the same ordered
list that carries customer text. `AiTurn` has no public factory taking a role, so
no call site can construct a privileged turn even by accident; a test asserts
that no such factory exists.

The rest are absent **because nothing produces them yet**. Adding them now would
mean guessing at shapes no requirement has described, and every adapter would
have to translate cases that never occur.

### C5 — `AiTurn` is not a Domain `Message`

**Decision.** The Application's turn type is its own, and the Application does
not project Domain `Message` into it.

**Why.** A stored message is a business record with an identity, a channel, a
direction and an ingestion order. A turn is transient input to one generation
call. Reusing the aggregate would drag persistence identity into a request that
stores nothing, and would tie the AI contract to the conversation model's shape,
so that changing one would force the other.

When a conversation runtime arrives it will *project* messages into turns. That
is a mapping, and mappings are cheap; merging the two types is not reversible.

### C6 — The result carries only text

**Decision.** `AiTextGenerationResult` has one property, `Text`. No model name,
provider name, token counts, cost, finish reason, tool calls, raw payload or
request id.

**Why.** None of those has been observed coming out of a real provider yet, so
every one would be a guess at a shape — and a guess baked into a contract is far
harder to remove than to add. The phase that integrates a real provider will see
which of them exist and are stable, and can add exactly those.

### C7 — Blank output is a failure, never a result

**Decision.** `AiTextGenerationResult.Create` refuses null, empty and
whitespace-only text, raising `AiResponseInvalidException`. The preview handler
additionally guards against an adapter returning a null result object.

**Why.** An empty reply must never reach a person as though the agent had chosen
to say nothing. That is the most expensive kind of bug to notice, because nothing
appears to be wrong: a broken integration looks like an unhelpful agent.

Making the type itself the boundary means a badly behaved adapter fails where the
bad value was produced, rather than further up where it is only a symptom. The
handler's null check exists because a null return is the one breach no return
type can prevent.

### C8 — Preview is stateless and single-turn

**Decision.** `PreviewAiAgentHandler` sends exactly one user turn. No
conversation is created, no message is stored, and neither the question nor the
answer is written anywhere.

**Why.** A preview is somebody testing a configuration, not a customer
interaction. Recording it would put staff experiments into the same history as
real conversations, and would silently accumulate other people's text in a store
nobody asked to have one.

`PreviewAiAgentCommand` also cannot accept a conversation history, even though
the underlying request type can carry one. Accepting a caller-supplied history
would mean accepting caller-written *assistant* turns — a way to put words in the
agent's mouth and steer what it says next.

### C9 — Preview works for `Draft`, `Active` and `Disabled`

**Decision.** The handler does not consult `AiAgent.Status`. Every lifecycle
state previews.

**Why.** Preview is an administrative capability, and requiring activation would
invert the workflow it exists to serve. An operator has to be able to test an
agent **before** deciding whether to activate it — that is the main reason to
preview at all — and has to be able to test one they have **disabled** for
repair, which is exactly when checking it matters most.

Status governs whether an agent serves customers. It does not govern whether its
owner may try it. The Domain lifecycle is unchanged: `Draft → Active`,
`Active → Disabled`, `Disabled → Active`, with `Draft → Disable` still refused.

### C10 — The client cannot supply instructions

**Decision.** The request body is `{ "message": "..." }`. There is no
`instructions` field on `AiAgentPreviewRequest` or on
`PreviewAiAgentCommand`. Instructions come from the persisted, tenant-scoped
agent and from nowhere else.

**Why.** Without this rule, preview would be an open text-generation endpoint
wearing an agent's name. Anyone could run instructions of their choosing on this
platform's AI account, and could make an agent appear to behave in ways its owner
never configured — which is worse than the cost, because a screenshot of that
output would look exactly like the real agent misbehaving.

Keeping the field out of the contract makes it impossible rather than
discouraged: a caller that sends one is ignored by model binding, and nothing
downstream would read it. A test posts `instructions` and `system` fields
alongside the message and asserts the generator still received the stored text.

### C11 — Message validation is an Application rule

**Decision.** `AiTurn` validates: required, non-blank, trimmed at the ends,
interior preserved, at most `MaxTextLength` = **10,000** characters, and no
control characters other than tab, carriage return and line feed. Failure raises
`AiTurnValidationException`.

**Why it is not in the Domain.** No aggregate was consulted. These are limits the
platform places on a request it will process, in the same family as a maximum
upload size — not business rules about what an AI agent is. Raising
`DomainValidationException` would claim an aggregate rejected something it was
never asked about.

**Why 10,000.** Roughly 20 KB. Far above any realistic customer message — a long,
detailed support enquiry runs to a couple of thousand characters — while bounding
the request body, the log line and the memory one generation call can occupy. It
sits below `AgentInstructions.MaxLength` (20,000) deliberately: instructions are
authored once and read on every turn; a turn is one person's message.

**It is a platform resource bound, not a provider token limit.** Provider limits
are counted in tokens, differ per model, and change without notice. Encoding one
here would put a vendor's current pricing decision into this platform's request
validation and would be wrong within months. What a chosen model can accept, and
what to do when a request exceeds it, is an adapter's concern.

**Over-length text is rejected, never truncated.** Truncating would send the
model a question the person did not ask and return an answer to it as though
nothing had happened.

### C12 — Ordering, and why the generator is the last thing reached

**Decision.** The preview handler runs in this order:

1. Identifier validation → `400`
2. Message validation → `400`
3. **Tenant-scoped lookup → `404`**
4. Build the request from the stored agent's instructions
5. Call `IAiTextGenerator`

**Why step 3 before steps 4 and 5.** This is the security property of the whole
use case. A caller naming another tenant's agent is refused before a single
character of that agent's configuration is read into a request — so a cross-tenant
preview cannot become a way to extract somebody's instructions through a model's
answer, and cannot spend this platform's AI budget on their data.

Steps 1 and 2 run first and disclose nothing: a malformed message is refused
identically whether the agent exists, belongs to somebody else, or was never
real, so the ordering cannot be used to probe. A caller with an unusable message
is also told what is wrong with it rather than being told AI is unavailable,
which would send them to fix the wrong thing.

### C13 — An unconfigured provider fails explicitly

**Decision.** `UnconfiguredAiTextGenerator` is registered as the real
implementation and refuses every request with `AiRuntimeUnavailableException`.
The application starts normally and everything else keeps working.

**Why.** The alternatives are all worse. Leaving `IAiTextGenerator` unregistered
would produce a dependency-injection failure that reads like a bug in the
endpoint. Returning a canned reply, a template, an apology sentence or an echo of
the caller's message would put text of unknown origin in front of somebody
evaluating whether their agent works — and the failure mode is that they conclude
it does.

**It never falls back.** There is no second provider to try and no silent
substitution, because falling back invisibly is how a deployment ends up sending
its data somewhere its operator did not choose.

**It is not a test double.** The deterministic fake that returns configured text
lives in the test projects and is never registered by the composition root. That
is the difference between an application that admits it cannot do something and
one that quietly pretends.

### C14 — Three flat failure types

**Decision.** Three sealed exception types, no base class, no error-code
catalogue:

| Type | Means | Who can fix it |
| --- | --- | --- |
| `AiTurnValidationException` | The message is unusable | The caller |
| `AiRuntimeUnavailableException` | No AI runtime is configured | The operator |
| `AiResponseInvalidException` | The runtime answered with nothing usable | Nobody, yet |

**Why these three and no more.** Each names one outcome that leads to a different
action. There is deliberately no rate-limit type, quota type, moderation type or
provider-specific code, because nothing has yet observed how a real provider
reports those. Inventing the taxonomy first would mean mapping real failures onto
guessed categories — and the mapping would be invisible once written.

All three are transport-neutral: no status code, no `ProblemDetails`, no HTTP
vocabulary. The API decides how each is reported.

None of their messages carries the preview text, the agent's instructions, a
provider name, a configuration key or an upstream response body. Those messages
reach clients.

### C15 — HTTP contract

`POST /api/tenants/{tenantId:guid}/ai-agents/{aiAgentId:guid}/preview`

```
Request:   { "message": "Can you help me choose a product?" }
Success:   200 OK
           { "reply": "..." }
```

| Situation | Status |
| --- | --- |
| Route identifier is not a GUID | **404** (route constraint; no handler runs) |
| Identifier is the empty GUID | **400** |
| Message missing, blank, over-length, or carrying control characters | **400** |
| Body is not readable JSON | **400** |
| Agent not in this tenant — missing **or** owned by somebody else | **404** |
| No AI runtime configured | **503** |
| Runtime answered with nothing usable | **502** |
| Anything unanticipated | **500**, body carrying no detail |

**Why `POST` for something that changes nothing.** The request carries a body,
and the body is the message. A `GET` would have to put somebody's text in the
URL, where it lands in access logs, proxy logs and browser history. Nor is the
result cacheable: the same message may legitimately produce a different answer.

**Why `503` rather than `500` or `501`.** The request is perfectly good and will
succeed once a provider is configured, without the caller changing anything.
Calling a known, deliberate configuration state a server fault would send an
operator hunting for a bug in code behaving exactly as intended; `501` would
claim the platform does not implement preview at all, rather than that this
deployment cannot currently serve it.

**No `Retry-After`.** The platform has no idea when a provider will be
configured — that is a deployment decision, not a queue that drains — and
inventing a number would invite clients to poll against a promise nothing backs.

**Why `502` for an unusable answer.** It is exactly what it describes: this
server acted as a gateway to an upstream service and received an invalid response
from it. The caller did nothing wrong and cannot fix it, so a `4xx` would
misdirect them entirely.

### C16 — No `If-Match`, no `ETag`

**Decision.** Preview requires no precondition and returns no entity tag. The
agent management endpoints are unchanged.

**Why.** Preview reads the agent and writes nothing: no property changes,
`updatedAt` does not move, and the agent's version does not change — which is also
why the handler takes no `TimeProvider`. A precondition protects a caller from
overwriting somebody else's edit, and there is no write here to protect.
Requiring one would be ceremony that taught clients the wrong thing about what
this endpoint does.

Tests read the agent straight from PostgreSQL before and after a preview and
assert `updated_at` and the entity tag are identical.

### C17 — Cancellation, and no retries

**Decision.** `GenerateAsync` takes a `CancellationToken`, the handler passes the
caller's token through, and a cancelled request abandons the call. Nothing is
retried.

**Why cancellable.** Every real implementation will be a network call to a
service that can be slow. A synchronous signature would block a request thread
for the duration and would give a caller that has already given up — a closed tab,
a client timeout — no way to stop work nobody is waiting for.

**Why no retries.** No `Polly`, no resilience pipeline, no backoff. Nothing has
yet observed which of a real provider's failures are transient. Retrying on
guesses multiplies load during exactly the incidents where that is most harmful,
and an unconfigured runtime cannot succeed on a second attempt however long the
loop waits.

### C18 — Logging

**Decision.** `4xx` outcomes log at Information; `503` and `502` log at Warning.
The recorded text is the exception's own message.

**Why.** A caller's typing mistake and a provider outage are not the same event
to an operator. Logging both at Information would mean an outage left no more
trace than a rejected name; logging both at Error would bury real faults under
other people's typing.

Neither level records the preview message or the agent's instructions, so turning
logging up cannot start writing customers' text or a tenant's configuration into
the log.

---

## DEFERRED

Nothing below exists in Phase 8. Each is listed with what would trigger it.

| Deferred | Trigger |
| --- | --- |
| OpenAI adapter | Phase 9 — the first real provider |
| Anthropic or any second provider | A requirement for one; the contract already allows it |
| Model configuration and selection | The first real adapter, where a live API says what exists |
| Model routing per tenant or agent | A product decision, with its own design |
| Temperature, top-p, output limits, reasoning effort | A real adapter demonstrating which are stable |
| Streaming responses | A UI that renders partial output |
| Conversation runtime and history | The channel phase; turns already carry an ordered list |
| Memory and summarisation | A conversation runtime that outgrows its context |
| RAG and knowledge sources | A knowledge requirement |
| Tools and function calling | A requirement for the agent to act |
| Structured output | A caller needing a schema rather than prose |
| Multimodal input (image, audio, document) | A channel that carries them |
| Token usage and cost tracking | A real provider response showing what is reported |
| Metering, quotas and billing | Usage data that exists |
| Retry and resilience policy | Observed provider failure semantics |
| Rate-limit and quota failure taxonomy | Observed provider error responses |
| Guardrails and moderation | A policy requirement |
| Evaluations | A quality baseline worth regressing against |
| Provider fallback | A second provider, plus a decision about what silent substitution means |
| Preview history or saved transcripts | A requirement to keep them, and a retention decision |
| Authentication and authorisation | The authentication phase |

**The Phase 8 AI runtime abstraction is deliberately TEXT-ONLY and
capability-specific. It is not intended to predict every future AI capability.**
It is meant to be the smallest thing that lets a real provider be added behind
it, and to be joined by sibling contracts — not extended into a universal one.

---

## Security status

**These endpoints are not authorised for production exposure.** Authentication
and authorisation still do not exist, so the tenant id in the route is supplied
by the caller and believed. Tenant scoping is a correctness boundary — it stops
one tenant's data being reached through another tenant's route — but it is not
yet a security boundary, because nothing stops a caller from claiming to be
either tenant. No placeholder API key, bearer token or admin secret has been
added, because a fake control would only hide that.

What Phase 8 does establish, and what carries forward unchanged when
authentication arrives:

- **Ownership is resolved before any provider call.** The generator's call count
  is asserted to be zero on every cross-tenant path.
- **Instructions are never returned by preview.** They are business configuration
  and can describe internal policy; a reply body is the most quotable, most
  forwarded, most logged part of any response.
- **Instructions cannot be supplied by a caller.** The contract has no field for
  them.
- **No secret exists to leak.** No API key is read, stored, logged or configured
  anywhere in this phase.
- **Failure bodies name no vendor, no configuration key and no upstream
  endpoint.**

---

## Things a reviewer can check independently

1. `IAiTextGenerator` has one method. — `backend/src/OmniAI.Application/Ai/IAiTextGenerator.cs`
2. `AiTurnRole` has two members, neither privileged. — `AiTurnRole.cs`
3. `AiTurn` has no public factory taking a role. — `AiTurn.cs`
4. `AiTextGenerationRequest` has two properties and never concatenates. — `AiTextGenerationRequest.cs`
5. `AiTextGenerationResult` has one property and refuses blank text. — `AiTextGenerationResult.cs`
6. Neither `AiAgentPreviewRequest` nor `PreviewAiAgentCommand` has an instructions field.
7. The preview handler looks the agent up before building a request. — `PreviewAiAgentHandler.cs`
8. The preview handler has no `TimeProvider` and calls no aggregate method.
9. `UnconfiguredAiTextGenerator` returns nothing and reads no configuration.
10. The composition root registers only that implementation. — `CompositionRoot.cs`
11. `grep -rn "OPENAI_API_KEY\|using OpenAI\|HttpClient" backend/src/OmniAI.Application/Ai backend/src/OmniAI.Infrastructure/Ai` finds nothing.
12. `backend/Directory.Packages.props` is unchanged; no `PackageReference` was added.
13. `git diff --stat HEAD -- backend/src/OmniAI.Domain` is empty.
14. `backend/src/OmniAI.Infrastructure/Persistence/Migrations` is unchanged.
15. `appsettings.json` has no AI section.
