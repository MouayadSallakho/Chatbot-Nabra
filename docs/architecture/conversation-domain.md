# Conversation domain

**Status:** Phase 3 implemented the core conversation and message model. Nothing
below marked DEFERRED exists in code.

A conversation is an exchange between one tenant's business and one end customer,
on one channel. It is a **platform** concept: not an OpenAI thread, not a WhatsApp
chat, not a Messenger thread. Those are transports it may run over.

---

## DECIDED NOW

### C1 — Aggregate boundary

`Conversation` is the aggregate root and owns its `Message` history. `Message` is
an entity inside that boundary and is never created independently: its factory is
`internal`, and it has no public constructor. That is what guarantees every
message belongs to exactly one conversation.

The boundary is drawn here because a message has no meaning outside the
conversation it belongs to, and because the rule that governs adding one — is the
conversation open — is a conversation-level rule.

### C2 — Tenant ownership is immutable

`Conversation.TenantId` has **no setter at all**, not even a private one. A
conversation belongs to exactly one tenant for its whole life and cannot be moved
to another.

This is a security decision, not a modelling preference: tenant ownership is the
platform's isolation boundary, and a reassignable owner is a cross-tenant leak
waiting for a bug to find it. A test asserts the property has no set accessor.

`TenantId` is **not** duplicated onto `Message`. A message's tenant is its
conversation's tenant, and there is no domain invariant that needs it locally.
Whether to denormalise it into the message table for query or constraint reasons
is a persistence decision, deliberately deferred.

### C3 — Channel independence

`ChannelKind` is a business-level enum: `WebChat`, `WhatsApp`, `Instagram`,
`Messenger`. The Domain is allowed to know *that* a conversation is a WhatsApp
conversation, because that changes how the business reasons about it. It knows
nothing about *how* WhatsApp works.

Explicitly absent from the Domain, and required to stay absent: endpoints and API
URLs, phone number ids, page ids, thread ids, access tokens, webhook secrets,
provider DTOs and SDK types. Those belong to channel adapters in Infrastructure.

One enum rather than an aggregate per channel: the channels differ in transport,
not in what a conversation *is*.

### C4 — Lifecycle

Two states, `Open` and `Closed`. A conversation starts `Open`.

```
Start ──────────────►  Open
Open   ── Close ────►  Closed
Closed ── Reopen ───►  Open
```

`Close` and `Reopen` are **idempotent**: repeating one is a no-op rather than an
error, because the caller's intent is already satisfied and demanding
check-then-act would invite a race while preventing nothing. A no-op records no
activity, since nothing happened.

### C5 — A closed conversation accepts no messages

One rule, no exceptions: while `Closed`, `AddTextMessage` and `AddSystemMessage`
both fail with `DomainStateException`. Reopening restores both.

System messages were deliberately **not** given an exemption. An exemption would
be a hidden special case in the one invariant that defines what "closed" means,
and it is not needed: a note about why a conversation is being closed can be
added before closing it.

### C6 — Messages are immutable

Every `Message` property is get-only and set once at construction. There is no
edit operation and no setter, public or private — asserted by test. A message
records something that was said; if it can be rewritten, the transcript stops
being evidence. A correction is a new message.

### C7 — Message history is encapsulated

The internal list is private. `Messages` exposes a real read-only wrapper, not
the internal list typed as a read-only interface — that would let a caller cast
back to the concrete list type and edit history. Mutation attempts through a
mutable interface fail; both are asserted by test.

The view is live: messages added later appear in it.

### C8 — Sender roles describe authorship, not identity

`MessageSender` is `Customer`, `AiAgent`, `HumanAgent`, `System`. This says who
produced a message. It is **not** a foreign key and does not imply that an AI
agent or human agent entity exists — neither does. *Which* agent or human wrote a
message arrives with those aggregates.

### C9 — Content strategy: two types, no hierarchy

`MessageContentType` is `Text` and `System`, with the invariant that System
content and System sender always go together — enforced by having two separate
methods rather than one method with a rule to remember.

No polymorphic content hierarchy was built. Image, video, audio, document,
location, contact, interactive and template content each bring their own
validation rules, and building an abstraction before knowing those rules would be
designing for imagined requirements.

### C10 — Time is supplied, never read

Every operation that changes state takes the effective instant as a parameter.
The Domain never calls `DateTime.UtcNow` or `DateTimeOffset.UtcNow`, so its
behaviour is fully deterministic and every test is reproducible without a clock
abstraction, a fake, or an injected time provider.

Rules:

- `DateTimeOffset` is used, because these instants are externally meaningful.
- Values are normalised to **UTC** on the way in, so storage and comparison are
  unambiguous.
- The unset value (`default`) is rejected — it is not a real point in time.
- **No monotonicity is required.** An instant earlier than `LastActivityAt` is
  accepted; see C11.

`LastActivityAt` is a **high-water mark**: the latest instant anything is known to
have happened here — a message, a close or a reopen. Every successful operation
applies `LastActivityAt = max(LastActivityAt, instant)`, so it never moves
backwards, and a late-arriving older message leaves it untouched.

### C11 — Ordering is ingestion order, and that is not chronological order

Message history is kept in **insertion order**: the order in which this platform
ingested the messages. That is the Domain's whole ordering contract.

**Insertion order is explicitly not a guarantee of external chronological
order.** This is a distributed omnichannel system, and a message can legitimately
be ingested after one that was sent later:

- webhook delivery delay, webhook retries, provider redelivery
- network delay, queue retries, concurrent processing
- provider-side ordering
- imports and migrations
- channel behaviour that does not exist yet

**Out-of-order ingestion is allowed.** The aggregate accepts a valid message
whose `SentAt` precedes `LastActivityAt`, appends it at the end in ingestion
order, and leaves its timestamp exactly as supplied. It never rewrites a
timestamp, never reorders, and never sorts.

**Channel adapters are not responsible for reconstructing perfect global
chronology before invoking the Domain.** Requiring that would put a hard
distributed-systems problem — buffering, windowing and reordering every
message — in the wrong layer, and would make correct adapters far harder to
write than they need to be.

A caller that needs chronological order sorts by `Message.SentAt` itself. Read
models and API ordering are transport concerns, deferred.

### C12 — Two exception types, by category

`DomainValidationException` for input that could never succeed as written;
`DomainStateException` for a well-formed operation that is not permitted by the
aggregate's current state and would succeed in another state. Collapsing them
would lose a real distinction; splitting further would be exception
proliferation. See architecture decision 9.

The Domain states the distinction and stops there. It contains no status codes,
no ASP.NET types and no transport vocabulary. **How each category is surfaced to
a client is a future Application/API concern.**

---

## DEFERRED

Not implemented, and not designed. Listed so nobody assumes otherwise.

| Concern | Why deferred |
| --- | --- |
| **Rich media content** — image, video, audio, document, location, contact, interactive, templates, stickers | Each carries its own validation and channel constraints. Adding a content hierarchy before knowing them is speculative (C9). |
| **External channel identifiers** — WhatsApp/Instagram/Messenger thread and user references, web visitor and session ids | No Phase 3 invariant needs one. Modelling provider identifiers now would either leak provider shape into the Domain or invent a generic wrapper with no consumer. These belong with the channel adapters, which will map external references to conversations. |
| **Human handoff** — waiting-for-agent, assigned, escalated, bot-handling, human-handling | Its own phase. Those states encode routing rules that do not exist yet (C4). |
| **AI concerns** — model, prompt, thread ids, token counts, cost, latency, confidence, sentiment | Not conversation attributes. AI orchestration is a separate concern behind Application-owned abstractions, and none exist yet. |
| **Customer identity** — a Customer aggregate, contact details, phone numbers, emails | Its own aggregate. A conversation currently records *that* there is a customer side, not *who*. |
| **Provider / external timestamps** | See the note below. Deferred deliberately; nothing in Phase 3 blocks them. |
| **Domain events** | No event abstraction exists in the accepted architecture, and nothing consumes events. Background processing and integration events are designed with the infrastructure that dispatches them. |
| **Persistence** — schema, indexes, tenant-scoped constraints, message ordering columns, sequence allocation | Belongs with the tenancy isolation decisions still open in [multi-tenancy-isolation.md](multi-tenancy-isolation.md). |
| **API pagination** of message history | A transport concern. The Domain exposes ordered history; paging strategy is decided where the read model is. |
| **Tags, lead scoring, CRM/ERP references, assigned employee** | Later features, each with its own rules. |
| **Message delivery state** — sent, delivered, read, failed | Channel-specific semantics that differ per provider; arrives with the adapters. |

### A note on time, for whoever builds the channel adapters

`Message.SentAt` is a single instant supplied by the caller. Real channel
integrations may need to distinguish two different things:

- **provider / external occurred time** — when the provider says the message was
  sent, on their clock
- **platform recorded / received time** — when this platform ingested it

They diverge in exactly the delayed-delivery cases listed in C11, and reporting,
SLA measurement and transcript display do not all want the same one.

**Neither is implemented, and no choice between them has been made.** This is
recorded only so the choice stays open: nothing in Phase 3 assumes the two are
the same, precisely because nothing requires ingestion order to match timestamp
order. When the distinction is needed it will be added deliberately — not as a
generic metadata bag on `Message`, and not with provider-specific fields.

---

## Invariants a reviewer should be able to check

1. `Conversation.TenantId` has no set accessor.
2. `Conversation` references nothing outside `System.*` and `OmniAI.Domain`.
3. `Messages` cannot be mutated from outside the aggregate.
4. `Message` has no setters and no public constructor.
5. A closed conversation rejects every message, including system messages.
6. No provider type, credential or endpoint appears anywhere in the Domain.
7. No domain code reads the system clock, and no operation requires timestamps
   to increase; a message sent earlier than `LastActivityAt` is accepted, keeps
   its timestamp, is appended last, and leaves `LastActivityAt` alone.
8. `default(ConversationId)` and `default(MessageId)` report `IsEmpty`, matching
   the accepted `TenantId` decision.

All eight are covered by tests in `OmniAI.Domain.Tests`, and #2 is additionally
enforced by the Phase 1 assembly-metadata guard.
