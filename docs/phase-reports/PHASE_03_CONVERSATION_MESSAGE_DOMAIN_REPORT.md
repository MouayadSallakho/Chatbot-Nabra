# Phase 3 — Conversation & Message Core Domain

**Date:** 2026-09-04
**Scope:** Core domain model for tenant-owned conversations and their messages.
No persistence, no AI, no channel integration, no application layer.

**Revision (post-review remediation).** The first submission required conversation
history to move strictly forward in time and rejected a message whose timestamp
preceded `LastActivityAt`. Review found that wrong for a distributed omnichannel
system, and it has been removed. Sections 2, 19, 22, 23, 24, 28, 33, 35, 36 and 42
reflect the corrected behaviour; the rest of the report stands as accepted.

---

## 1. Phase objective

Model a tenant-owned conversation, its lifecycle, and the messages belonging to
it — provider-independent, deterministic and fully invariant-enforced — plus
focused behaviour tests and an architecture document separating what is decided
from what is deferred.

## 2. Final status

**PASS**

Every authorised objective was completed, and the one correction required at
review has been applied. Clean build with warnings as errors, 175/175 tests pass,
no vulnerable packages, no dependency added, frontend untouched, nothing outside
scope implemented.

## 3. Baseline commit

```
478dd2b  feat: establish tenant core domain
```

Phase 1 and Phase 2 were preserved intact. No accepted file was recreated or
restructured; the single pre-existing file touched is
`docs/architecture/README.md`, extended additively.

## 4. Initial git state

Working tree **clean** — `git status --short` produced no output;
`git log -2 --oneline` showed `478dd2b` then `b7ab2ea`; 2 commits, 0 remotes. No
stop condition applied.

Inspected before writing anything: the Domain and Domain.Tests trees, `TenantId`
(as the reference identifier design), `TenantStatus` (enum convention),
`Tenant` (aggregate convention), `DomainValidationException` (error convention),
`Directory.Build.props` and `backend/tests/.editorconfig`.

## 5. Files created

**Domain source (9)**

| File | Purpose |
| --- | --- |
| `Conversations/ConversationId.cs` | Conversation identity |
| `Conversations/MessageId.cs` | Message identity |
| `Conversations/ChannelKind.cs` | Business-level channel |
| `Conversations/ConversationStatus.cs` | `Open` / `Closed` |
| `Conversations/MessageSender.cs` | `Customer` / `AiAgent` / `HumanAgent` / `System` |
| `Conversations/MessageContentType.cs` | `Text` / `System` |
| `Conversations/Message.cs` | Immutable message entity |
| `Conversations/Conversation.cs` | Aggregate root |
| `DomainStateException.cs` | State-violation exception |

**Tests (5)** — `Conversations/{ConversationIdTests, MessageIdTests, ChannelKindTests, ConversationTests, MessageTests}.cs`

**Documentation (2)** — `docs/architecture/conversation-domain.md`, this report.

## 6. Files modified

One:

| File | Change |
| --- | --- |
| `docs/architecture/README.md` | +37 / −2. Intro now lists both area documents; decision 9 extended with the second exception type; decision 11 (time) appended. |

No Phase 1 or Phase 2 source, project file, build property or test-infrastructure
file was modified. `Directory.Packages.props` is unchanged.

## 7. Domain types introduced

Eight in `OmniAI.Domain.Conversations`, one in `OmniAI.Domain`:

| Type | Kind |
| --- | --- |
| `ConversationId` | `readonly record struct` |
| `MessageId` | `readonly record struct` |
| `ChannelKind` | `enum` |
| `ConversationStatus` | `enum` |
| `MessageSender` | `enum` |
| `MessageContentType` | `enum` |
| `Message` | `sealed class` (entity) |
| `Conversation` | `sealed class` (aggregate root) |
| `DomainStateException` | `sealed class : Exception` |

No interfaces, no base classes, no generics beyond one private guard helper. No
`StronglyTypedId<T>` framework, no `Entity`/`AggregateRoot`/`ValueObject` base
types, no domain-event abstraction.

## 8. ConversationId design

`readonly record struct` wrapping `Guid`, private constructor, two factories:

- `New()` — `Guid.CreateVersion7()`, time-ordered so generated keys insert
  locally in an index.
- `From(Guid)` — throws `DomainValidationException` on `Guid.Empty`.

Immutable, value equality, `IsEmpty`, `ToString()` in `"D"` form with
`CultureInfo.InvariantCulture`. No package added.

Identical in shape to the accepted `TenantId`, deliberately: consistency is worth
more than micro-variation, and `default(ConversationId)` exposes `IsEmpty` for
exactly the reason recorded in Phase 2 — C# always permits `default` for a
struct, so the uninitialised state must be visible rather than silent.

## 9. MessageId design

Identical design and rationale. UUIDv7 fits messages particularly well: they are
generated in volume and, being time-ordered, sort in creation order without a
separate sequence column — which is why no sequence-allocation mechanism was
introduced.

## 10. Conversation aggregate design

```csharp
public sealed class Conversation
{
    public ConversationId      Id             { get; }          // no setter
    public TenantId            TenantId       { get; }          // no setter
    public ChannelKind         Channel        { get; }          // no setter
    public ConversationStatus  Status         { get; private set; }
    public DateTimeOffset      StartedAt      { get; }          // no setter
    public DateTimeOffset      LastActivityAt { get; private set; }
    public IReadOnlyList<Message> Messages    { get; }          // read-only view

    public static Conversation Start(TenantId, ChannelKind, DateTimeOffset);
    public Message AddTextMessage(MessageSender, string?, DateTimeOffset);
    public Message AddSystemMessage(string?, DateTimeOffset);
    public void Close(DateTimeOffset);
    public void Reopen(DateTimeOffset);
}
```

`sealed`, private constructor, no public setters (asserted by test). Six
properties and five operations. `Start` validates that the tenant id is not
empty, that the channel is a defined enum value, and that the instant is real.

Deliberately absent, per the brief and the guardrails: AI model fields, OpenAI
thread ids, token counts, costs, sentiment, tags, lead scoring, CRM ids, assigned
employee ids, customer entities, phone numbers, emails, billing.

## 11. Tenant ownership invariant

`TenantId` is a get-only auto-property with **no set accessor at all** — not
public, not private. A conversation cannot be moved between tenants because there
is no code path that could move it.

This is a security decision, not a modelling preference: tenant ownership is the
platform's isolation boundary, and a reassignable owner is a cross-tenant leak
waiting for a bug to find it. Two tests assert it — one structural
(`GetProperty(...).SetMethod is null`) and one behavioural (the tenant survives
close, reopen and message activity).

`TenantId` is **not** duplicated onto `Message`. A message's tenant is its
conversation's tenant, and no Phase 3 invariant needs it locally. Whether to
denormalise it into the message table for constraint or query reasons is a
persistence decision, explicitly deferred.

## 12. Channel representation

A single enum, `ChannelKind`: `WebChat`, `WhatsApp`, `Instagram`, `Messenger`.
No zero member, so an uninitialised value is detectable, and `Start` rejects both
`default` and any undefined cast such as `(ChannelKind)99`.

The Domain knows *that* a conversation is a WhatsApp conversation, because that
changes how the business reasons about it. It knows nothing about *how* WhatsApp
works. Verified absent from the whole Domain by grep: no API URL, endpoint,
`HttpClient`, phone number id, page id, thread id, access token, webhook secret,
provider DTO or SDK type. The only occurrences of provider names anywhere in the
Domain are the enum member `WhatsApp` and a doc comment stating that a
conversation is *not* an OpenAI thread.

One enum rather than an aggregate per channel: the channels differ in transport,
not in what a conversation is.

## 13. Conversation lifecycle

Two states. A conversation starts `Open`.

```
Start ─────────────►  Open
Open   ── Close ───►  Closed
Closed ── Reopen ──►  Open
```

`Close` and `Reopen` are **idempotent**: repeating one is a no-op rather than an
error, matching the accepted Phase 2 reasoning — the caller's intent is already
satisfied, and demanding check-then-act would invite a race while preventing
nothing. A no-op records **no** activity, since nothing happened;
`LastActivityAt` is left untouched. Tested both ways.

Not introduced: `WaitingForAgent`, `AssignedToAgent`, `Escalated`, `BotHandling`,
`HumanHandling`, `Pending`, `Archived`. Those encode routing rules that do not
exist; human handoff is its own phase.

## 14. Message design

`Message` is a sealed entity inside the `Conversation` boundary, carrying
`Id`, `ConversationId`, `Sender`, `ContentType`, `Text` and `SentAt` (UTC).

Its factory is `internal` and it has **no public constructor**, so a message
cannot be created outside the aggregate that owns it — which is what guarantees
every message belongs to exactly one conversation, and makes it impossible to
construct one with a mismatched parent id.

`ConversationId` *is* carried on the message (unlike `TenantId`): it makes a
message self-describing when handed out through the read-only view, and it is the
aggregate's own identifier rather than a denormalisation of an outer concern.

## 15. Sender-role design

`MessageSender`: `Customer`, `AiAgent`, `HumanAgent`, `System`. No zero member;
undefined values rejected.

This describes authorship at business level. It is **not** a foreign key, and no
AI agent or human agent entity exists or is implied. Which specific agent or
human wrote a message arrives with those aggregates. No provider-specific sender
types.

## 16. Message content strategy

`MessageContentType`: `Text` and `System` only.

The invariant "System content and System sender always go together" is enforced
structurally rather than by a rule callers must remember: there are two methods.
`AddTextMessage` rejects `MessageSender.System`, and `AddSystemMessage` sets both
to System. That makes the pairing unrepresentable rather than merely validated.

No polymorphic content hierarchy was built. Image, video, audio, document,
location, contact, interactive and template content each bring their own
validation rules and channel constraints; an abstraction designed before those
rules are known would be fitted to imagined requirements.

## 17. Message immutability strategy

Every property is get-only and set once in the constructor. There is no edit
operation, no setter — public **or** private — and no public constructor. Two
tests assert this structurally, and one asserts it behaviourally by driving
further activity through the aggregate and re-checking an earlier message.

A message records something that was said; if it can be rewritten, the transcript
stops being evidence. A correction is a new message.

## 18. Conversation message collection encapsulation

```csharp
private readonly List<Message> _messages = [];
private readonly ReadOnlyCollection<Message> _messagesView;   // built once
public IReadOnlyList<Message> Messages => _messagesView;
```

A genuine read-only wrapper, **not** the internal list typed as
`IReadOnlyList<Message>` — that would let a caller cast back to `List<Message>`
and edit history. The wrapper is created once in the constructor rather than per
access, so the property allocates nothing.

Three tests: the view is not assignable to `List<Message>`; reaching it through
`ICollection<Message>` reports `IsReadOnly` and throws `NotSupportedException` on
mutation; and the view is live, reflecting messages added afterwards.

## 19. Time strategy

**Every state-changing operation takes the effective instant as a parameter.**
The Domain never calls `DateTime.UtcNow` or `DateTimeOffset.UtcNow` — verified by
grep across the whole Domain.

- `DateTimeOffset`, because these instants are externally meaningful.
- Normalised to **UTC** on the way in, so storage and comparison are unambiguous.
  Tested with a `+03:00` input.
- `default` is rejected: it is not a real point in time.
- **No ordering requirement.** An instant earlier than `LastActivityAt` is
  accepted, recorded exactly as supplied, and appended last.

This gives determinism with no clock abstraction, no fake, and no injected time
provider: one parameter replaces a framework. It also keeps the decision of *what
time it is* at the edge, which is correct — an ingested channel message carries
its own timestamp, and the Domain should record that instant rather than the
moment the process happened to handle it.

`LastActivityAt` is a **high-water mark**: the latest instant anything is known to
have happened here — message, close or reopen. Every successful operation applies
`LastActivityAt = max(LastActivityAt, instant)`, so it never moves backwards, and
a late-arriving older message leaves it untouched. Recorded as architecture
decision 11.

**Ordering semantics.** `Messages` is in **ingestion order**, which is explicitly
*not* a claim about external chronological order. Out-of-order ingestion is
normal in a distributed omnichannel system — webhook delay and retries, provider
redelivery, queue retries, concurrent processing, provider-side ordering, imports
— and the aggregate accepts it. It never rewrites a timestamp, never reorders and
never sorts. Channel adapters are **not** required to reconstruct global
chronology before calling the Domain; a caller needing chronological order sorts
by `Message.SentAt` itself.

## 20. Text validation rules and selected length limit

Applied identically to `Text` and `System` content (both are textual):

| Rule | Behaviour |
| --- | --- |
| Required | `null`, empty or whitespace-only → `DomainValidationException` |
| Trimmed | Surrounding whitespace removed |
| Interior preserved | Spacing, tabs and line breaks kept exactly |
| Bounded | > `MaxTextLength` → rejected, **never truncated** |
| Printable | Control characters rejected **except** tab, CR and LF |

**`Message.MaxTextLength = 32,000`** UTF-16 code units, measured after trimming.

This is a **platform resource-safety bound, deliberately not any channel's
limit**. It sits roughly eight times above the largest current channel cap
(WhatsApp's is 4,096), so the Domain never rejects text a channel would have
accepted, while still bounding memory, storage, log and render cost at about
64 KB per message. Per-channel *outbound* limits differ per provider and change
without notice; enforcing them is the job of the channel adapters, where provider
knowledge belongs.

Note the deliberate difference from `TenantName`, which rejects **all** control
characters: newlines and tabs are ordinary message content but meaningless in a
display name.

## 21. Closed-conversation message behaviour

**A closed conversation accepts no messages at all** — `DomainStateException`
from both `AddTextMessage` and `AddSystemMessage`. Reopening restores both.

System messages were considered for an exemption and deliberately **not** given
one. An exemption would be a hidden special case in the single invariant that
defines what "closed" means, and it is not needed: a note explaining why a
conversation is being closed can be added before closing it. The brief asked for
the simplest consistent invariant, and one rule with no exceptions is it.

A rejected message leaves the conversation completely unchanged — message count
and `LastActivityAt` both untouched — because the message is appended only after
it has been constructed successfully. Tested.

## 22. Tests added

**85 new tests**, all in `OmniAI.Domain.Tests`, no mocking framework and no
system clock. (First submission: 80. Remediation removed the 2 tests that
required rejection solely because a message predated `LastActivityAt`, and added
7 covering out-of-order ingestion.)

| Class | Tests | Coverage |
| --- | --- | --- |
| `ConversationIdTests` | 8 | Valid wrap; `Guid.Empty` rejected; `New` non-empty and distinct; equality, inequality, hash, `==`/`!=`; default reports empty; `ToString` |
| `MessageIdTests` | 8 | Same concerns |
| `ChannelKindTests` | 2 | Exactly the four launch channels are defined; no uninitialised default member |
| `ConversationTests` | 63 | Creation (tenant, channel, identity, Open, timestamps, empty history, empty-tenant/undefined-channel rejection); time (unset instant, equal instant accepted, UTC normalisation, no-change-on-rejection); **out-of-order ingestion (delayed message accepted, keeps its own timestamp, appended in ingestion order rather than sorted, does not move `LastActivityAt` backwards, a later message still moves it forward, delayed system message accepted, closing with an earlier instant does not regress `LastActivityAt`)**; lifecycle (close, reopen, both idempotency cases, repeated cycles, other fields untouched); messages (3 senders, system message, System-sender rejection, undefined sender, parent id, distinct ids, insertion order, `LastActivityAt`, earlier message unaffected); text rules (5 blank cases, system blank, trimming, interior whitespace, at-limit, over-limit, no truncation, length after trim, control characters, no-change-on-rejection); closed state (text rejected, system rejected, nothing changes, reopen restores); encapsulation (not the internal list, mutation rejected, view is live); structural guards (no `TenantId` setter, no setters on `Id`/`Channel`/`StartedAt`, no public setters at all) |
| `MessageTests` | 4 | No setters anywhere; no public constructor; values survive later aggregate activity; instant recorded in UTC |

Tests assert observable behaviour and stated invariants. Several assert what does
*not* change after a rejected operation, which is where aggregate bugs hide. No
test asserts an enum's integer value, since serialisation stability has not been
declared a contract.

Four tests are structural (reflection over setters and constructors) rather than
behavioural. That is deliberate: "tenant ownership cannot change" and "message
history cannot be rewritten" are *absence* properties, and absence cannot be
demonstrated by calling something — only by showing there is nothing to call.
They follow the precedent of the accepted Phase 1 assembly-metadata guards.

## 23. Backend test count before

**90** — `OmniAI.Domain.Tests` 87, `OmniAI.Application.Tests` 1,
`OmniAI.Infrastructure.IntegrationTests` 1, `OmniAI.Api.IntegrationTests` 1.

## 24. Backend test count after

**175** — `OmniAI.Domain.Tests` **172** (87 pre-existing + 85 new), the other
three unchanged at 1 each. All pass; none skipped.

## 25. Commands executed

Baseline: `git status --short`, `git log -2 --oneline`, `git rev-list --all
--count`, `git remote`.

Inspection: reads of the Domain and Domain.Tests trees, `TenantId.cs`,
`TenantStatus.cs`, `Tenant.cs`.

Validation: `dotnet build src/OmniAI.Domain/OmniAI.Domain.csproj -c Release`
(early analyzer check), `dotnet restore OmniAI.slnx`,
`dotnet build OmniAI.slnx -c Release`, `dotnet test OmniAI.slnx -c Release`,
`dotnet test … --filter` (per-class counts),
`dotnet list OmniAI.slnx package --vulnerable --include-transitive`.

Invariant verification: greps across the Domain for provider names, HTTP types,
ORM types and system-clock calls; a listing of every `using` directive in Domain
source.

Diff review: `git status --short`, `git diff --check`, `git diff --stat`,
`git diff`, a path scan and a content secret scan over every Phase 3 file.

No VPS command, no SSH, no deployment, no `git commit`, no `git push`, no remote
change, and no npm command.

## 26. Restore result

`dotnet restore OmniAI.slnx` — **success**, all 8 projects, no warnings, no
package changes ("All projects are up-to-date for restore").

## 27. Release build result

`dotnet build OmniAI.slnx -c Release` — **success**:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

All 8 projects, with `TreatWarningsAsErrors` and analyzers at
`latest-recommended` in force.

## 28. Test result

`dotnet test OmniAI.slnx -c Release` — **all pass**:

| Project | Passed | Failed | Skipped |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | 172 | 0 | 0 |
| `OmniAI.Application.Tests` | 1 | 0 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 1 | 0 | 0 |
| `OmniAI.Api.IntegrationTests` | 1 | 0 | 0 |
| **Total** | **175** | **0** | **0** |

The Phase 1 architecture guard — Domain references nothing but `System.*` —
still passes with all nine new types in place.

## 29. Vulnerability scan result

`dotnet list OmniAI.slnx package --vulnerable --include-transitive` — **no
vulnerable packages** in any of the 8 projects. Unchanged, as expected: no
dependency was added or altered.

## 30. Compiler / analyzer warnings

**Zero.** No warning was produced, so none was suppressed. No
`#pragma warning disable`, no `[SuppressMessage]`, and no change to
`Directory.Build.props` or any `.editorconfig`.

One build **error** was hit and fixed on its merits during the phase:

- **xUnit2029** — `Assert.Empty(collection.Where(predicate))` should be
  `Assert.DoesNotContain(collection, predicate)`. The analyzer was right: the
  rewritten form reports which item matched instead of just "not empty". Fixed by
  changing the assertion, not by silencing the rule.

Analyzer constraints that shaped the code rather than being suppressed: `CA1305`
(explicit `CultureInfo.InvariantCulture` on `Guid.ToString` and on the numeric
value in the undefined-enum message).

**HTTP/ASP.NET coupling check (remediation).** Grepped the whole Domain for
`http`, `asp.net`, `ProblemDetails`, status-code vocabulary and the literals
`400`/`409`: **no match in any source file.** The Domain references only
`System.Collections.ObjectModel`, `System.Globalization` and
`OmniAI.Domain.Tenants`. `DomainStateException` derives from `Exception` and adds
no member. The only hits had been two words inside one XML doc comment, removed
during remediation; no behaviour changed.

## 31. Packages added

**None.** `Directory.Packages.props` is byte-for-byte unchanged.

`Guid.CreateVersion7()` and `System.Collections.ObjectModel` are both in the BCL,
so time-ordered identifiers and the read-only collection wrapper needed no
package.

## 32. Confirmation frontend was not modified

**The frontend was not modified.** `git status --short -- frontend/` returns
nothing: zero files added, changed or deleted under `frontend/`.

Phase 3 touched only the Domain layer and documentation. There is still no API
surface beyond `/health` and no client contract, so no frontend validation was
run and **no npm command was executed at all**.

## 33. Architecture-document summary

Created `docs/architecture/conversation-domain.md`, linked from
`docs/architecture/README.md`, split explicitly into **DECIDED NOW** and
**DEFERRED** so nothing unimplemented reads as implemented.

**Decided (C1–C12):** aggregate boundary with `Conversation` owning `Message`;
immutable tenant ownership as a security property; channel independence with the
forbidden-provider-detail list stated; two-state lifecycle with idempotent
transitions; a closed conversation accepting nothing, system messages included,
with the exemption considered and rejected; message immutability; history
encapsulation via a real read-only wrapper; sender roles as authorship not
identity; two content types with no hierarchy; supplied-time determinism;
ingestion ordering with an explicit disclaimer that it is not chronological
order; and two exception types split by category, with HTTP mapping named as a
future Application/API concern.

**Updated during remediation.** C10 now states that no monotonicity is required
and that `LastActivityAt` is a high-water mark updated as
`max(current, supplied)`. C11 was rewritten: history is *ingestion* order,
insertion order is explicitly not a guarantee of external chronological order,
out-of-order ingestion is allowed, and **channel adapters are not responsible for
reconstructing perfect global chronology before invoking the Domain**. C12 no
longer uses transport vocabulary. A new note records the two time concepts future
adapters may need to distinguish — provider/external occurred time versus
platform recorded/received time — as deferred, with neither implemented and no
choice made between them.

**Deferred**, each with its reason: rich media; external channel identifiers;
human handoff; AI concerns; customer identity; domain events; persistence
(schema, indexes, tenant-scoped constraints, sequence allocation); API
pagination; tags/lead scoring/CRM references; and message delivery state.

It closes with eight invariants a reviewer can check independently, all covered
by tests.

`docs/architecture/README.md` gained decision 11 (aggregates supply their own
time) and an extension to decision 9 explaining the second exception type.

## 34. Deferred concerns

Nothing on the brief's exclusion list was implemented. Specifically absent: EF
Core, `DbContext`, repositories, migrations, PostgreSQL, tenant query filters,
RLS, Redis, RabbitMQ, MassTransit, Hangfire, OpenAI, Anthropic, AI abstractions,
RAG, vector storage, AI/Human agent aggregates, Customer aggregate, CRM, ERP,
authentication, authorisation, JWT, SignalR, WebSocket infrastructure, channel
SDKs, Meta API integration, any webhook, web chat UI, billing, reports,
analytics, deployment changes.

**No Application-layer types were added.** No `ConversationService`,
`MessageService`, repository, command, handler, DTO, controller or endpoint.
`OmniAI.Application` is unchanged.

**No domain-event framework** was added: no event abstraction exists in the
accepted architecture and nothing would consume events.

**No external channel identifier** was modelled, generic or otherwise — no Phase
3 invariant required one, and inventing a wrapper with no consumer is exactly the
speculative generality the guardrails forbid.

## 35. Deviations from prompt

**One deviation was introduced and has been corrected at review.**

- **A forward-time invariant was added that the brief did not ask for, and it was
  wrong.** The first submission rejected any instant earlier than the
  conversation's `LastActivityAt`, on the reasoning that it made insertion order
  equal chronological order.

  That reasoning ignored what the platform is. In a distributed omnichannel
  system, out-of-order arrival is normal, not exceptional: webhook delay and
  retries, provider redelivery, network delay, queue retries, concurrent
  processing, provider-side ordering, and imports and migrations all produce it.
  The invariant would have forced every channel adapter to buffer and reorder
  messages before calling the Domain — putting a hard distributed-systems problem
  in the wrong layer, and rejecting legitimate data when the reordering was
  imperfect.

  **Corrected.** The guard was removed. A valid message whose timestamp precedes
  `LastActivityAt` is now accepted, keeps its supplied timestamp untouched, and is
  appended last in ingestion order. `LastActivityAt` became a high-water mark
  updated as `max(current, supplied)`, so it still never regresses. `Close` and
  `Reopen` use the same semantics, for consistency. Validation against the unset
  `default` instant is retained. No clock abstraction, no new time type, no
  ordering framework and no sequence infrastructure were introduced.

**Wording corrected.** The first submission described `DomainStateException` as
distinguishing "400-shaped from 409-shaped" failures. That was a poor way to
describe a Domain concept, and the phrasing has been removed from the Domain's own
doc comments as well as from the documents. The two types describe a **domain**
distinction: input that could never succeed as written, versus a well-formed
operation not permitted by the aggregate's current state that would succeed in
another state. **How either is surfaced to a client is a future Application/API
concern.** The implementation never had any HTTP coupling: `DomainStateException`
derives from `Exception` and carries only a message. Verified by grep across the
whole Domain — no status code, no `Microsoft.AspNetCore.*` type, no
`ProblemDetails`, no HTTP vocabulary in any name or behaviour. The only HTTP
references had been two words in one doc comment, and those are now gone.

**Choices the brief explicitly delegated**, made and documented rather than
deviations: adding `DomainStateException` as a second exception type (the brief
invited it if state violations warrant differentiation); carrying
`ConversationId` on `Message` (the brief restricted duplicating `TenantId`, not
the aggregate's own id); and giving system messages no closed-conversation
exemption.

No other deviation. No third-party package, no infrastructure, no application
types, no frontend change, no commit.

## 36. Known issues

1. **`default(ConversationId)` and `default(MessageId)` are representable**, as
   with the accepted `TenantId`. Mitigated by `IsEmpty` and by `Start` rejecting
   an empty tenant id; not closable in C#. Accepted design.
2. **Ingestion order is not chronological order.** By design (§19, §35): a message
   ingested late appears last in `Messages` even though it was sent earlier. Any
   consumer that needs chronological order must sort by `Message.SentAt`. This is
   a property to know, not a defect § the alternative would put message
   reordering inside the aggregate, which it must not do.
3. **No `Conversation` reconstitution path exists**, so an aggregate cannot yet
   be rebuilt from stored state. Deliberate — the shape of that method depends on
   persistence decisions still open — but it is the first thing the persistence
   phase will need.
4. **`Message.MaxTextLength` counts UTF-16 code units**, not grapheme clusters,
   consistent with `TenantName`. At 32,000 this is a non-issue in practice, but
   it is the definition in use.
5. **`Messages` is a live view, not a snapshot.** A caller holding it sees later
   additions. Correct for an aggregate used within one unit of work; worth
   remembering when a read model is built.
6. Phase 1/2 items still open and unchanged: no frontend linter, no CI pipeline,
   no toolchain pins, `InvariantGlobalization` revisit, Smart App Control's
   constraint on reflective assembly loading, and the open tenant-isolation
   mechanism questions (M1–M6).

## 37. Blockers

**None.** Every authorised Phase 3 objective was completed and validated, and the
one correction required at review has been applied in full.

The open question raised in the first submission — whether the Domain should
require timestamps to move forward — has been answered by the review: it should
not. It is resolved, not outstanding.

## 38. `git status --short`

```
 M docs/architecture/README.md
?? backend/src/OmniAI.Domain/Conversations/
?? backend/src/OmniAI.Domain/DomainStateException.cs
?? backend/tests/OmniAI.Domain.Tests/Conversations/
?? docs/architecture/conversation-domain.md
```

Plus this report, added afterwards under `docs/phase-reports/`. Untracked
directories are collapsed by Git; expanded, the new files are the 16 listed in
§5. No commit was created.

`git diff --check` reports **no whitespace errors**.

## 39. `git diff --stat`

```
 docs/architecture/README.md | 39 +++++++++++++++++++++++++++++++++++++--
 1 file changed, 37 insertions(+), 2 deletions(-)
```

`git diff` covers tracked files only, so it shows the single modified file; every
other Phase 3 file is new and therefore untracked (§38). The full diff was read:
the change is additive documentation, the only deletion being two intro lines
rewritten to mention Phase 3 and both area documents.

Diff review found no accidental changes, no generated or build files, no secrets,
no package additions, no infrastructure leakage into the Domain, no unrelated
frontend changes, and no scope creep.

## 40. Secret review confirmation

**Confirmed — no secrets.** Two checks over every Phase 3 file:

- **Path scan** — nothing under `node_modules`, `bin/`, `obj/`, `dist/`,
  `.angular/`, `.vs/`, or matching `.env`, `.pem`, `.key` would be tracked.
- **Content scan** for API keys, passwords, credentials, bearer values,
  authorization headers, connection strings, private-key headers and
  provider-specific token formats (`sk-`, `xox*-`, `EAA*`). Every match was prose
  in a doc comment or the architecture document *stating that such values must
  not appear in the Domain* — for example ChannelKind's list of forbidden
  provider details. No credential value exists anywhere.

The Domain has no I/O, no configuration and no connection handling. A specific
rule was also written into `AddSystemMessage`: system message content is part of
the conversation and may be shown to anyone who can read it, so provider
payloads, credentials and diagnostics must never be placed there.

## 41. Confirmation VPS was NOT accessed

**Confirmed.** No SSH connection was attempted or made to `186.240.145.196` or
any other host. Nothing was deployed. No Nginx configuration, systemd service,
server directory, package or production application was inspected or modified.
All work was local to `c:\Users\mouay\Desktop\omnichannel-ai-platform`.

Phase 3 required **no network access at all** — no package was added, restore
resolved from the local cache, and no npm command ran.

## 42. Readiness recommendation for Phase 4

**Ready to proceed.**

The conversation model is small, provider-independent, deterministic, fully
encapsulated and exhaustively tested. Three aggregate-shaped concepts now exist
(`Tenant`, `Conversation`, `Message`) with no infrastructure behind them.

Recommended for Phase 4, in this order:

1. **Decide the tenant isolation model (M1) and evaluate PostgreSQL RLS (M2)**
   before writing any persistence code. There are now two tenant-owned aggregates
   waiting on that decision, and retrofitting isolation onto a schema designed
   without it is materially harder.
2. **Then the first vertical slice** — most usefully "start a conversation and
   post a message", end to end through Application, Infrastructure and Api. That
   is where tenant context resolution (D6), aggregate reconstitution (§36.3), and
   the two-exception error strategy meeting an HTTP edge (400 vs 409) all become
   real and testable.
3. **Write the adversarial tenant-isolation tests with that slice**, not after
   it — conversations and messages are exactly the data a cross-tenant leak would
   expose.

Human handoff, AI orchestration and channel adapters should all follow that
slice, since each depends on persistence and tenant context already working.
