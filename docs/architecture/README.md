# Architecture

Decisions recorded here are binding until explicitly revisited. Each entry states
the decision, why it was taken, and what it forbids.

Phase 1 established the foundation. Phase 2 added the tenant core domain model.
Phase 3 added the conversation and message core domain. Phase 4 added PostgreSQL
persistence for those aggregates. Phase 5 added the first vertical slice --
tenant onboarding -- through API, Application, Domain and Infrastructure. Phase 6
added the AI agent core domain, provider-independent and Domain-only. Phase 7
made that domain persistable and usable through tenant-scoped Application use
cases and HTTP endpoints. Phase 8 added the first AI-oriented flow: a
provider-independent text-generation contract and a stateless agent preview.
Phase 9 implemented the first real provider behind that contract: OpenAI, through
the Responses API, in Infrastructure only. A deployment with no key configured
still refuses generation explicitly. Phase 10 turned to the frontend and built
the admin design system and application shell: semantic tokens, light/dark/system
theming, a responsive sidebar and top bar, and the reusable primitives that
product screens will be built from. Phase 11 turned that shell into the first
real product workflow: a development workspace, and AI agent listing, creation,
editing, lifecycle and preview against the real API, with a tenant-scoped
paginated list endpoint added to support it. Phase 12 closed the gap those phases
left open: real accounts, a cookie session, antiforgery protection, and a tenant
membership that every tenant-scoped request is checked against, so a tenant id in
a route addresses a resource rather than granting access to it. Phase 13 replaced
that membership's all-or-nothing meaning with tenant-scoped roles: a protected
Owner whose authority is the platform's whole capability catalogue, custom roles a
workspace composes from it, and a permission check inside every tenant-scoped use
case. Phase 14 built team administration on top of those roles: members, secure
invitations and role assignment. Phase 15 delivered the first real conversational
surface -- a hosted website chat an anonymous visitor can hold a real AI
conversation on -- deliberately without abuse protection. Phase 16 protected it:
Redis-backed distributed rate limits, a pseudonymous client-network identity
derived from a genuinely trusted address, a workspace generation-attempt safety
budget, lazy conversation creation, per-conversation credential cookies and an
absolute public session lifetime. Phase 17 gave agents something to draw on: a
tenant knowledge base of written documents, deterministic chunking, an embedding
abstraction with an OpenAI adapter, pgvector storage and retrieval in the same
PostgreSQL, explicit agent-by-agent assignment, and a grounding contract that
keeps retrieved text structurally separate from an agent's own instructions.
Phase 18 put that runtime onto other people's websites: a versioned loader script,
a cross-origin iframe on the OmniAI origin, a per-endpoint allowlist of exact
origins, a header-borne conversation credential that needs no third-party cookie,
and a postMessage channel that carries nothing but how big the rectangle should
be. Phase 21 gave that knowledge base a second way in: a workspace administrator
can upload a PDF, Word document, text file or CSV, and the text read out of it
becomes an ordinary knowledge document that still has to be indexed deliberately.
The uploaded file itself is never stored. Phase 22 turned the Analytics
placeholder into a real dashboard: conversation, message, channel, agent and AI
outcome counts queried directly from the operational records, bounded to three
periods, scoped to one workspace and free to read. It reports what happened, not
what it cost -- the platform holds no provider usage accounting yet, and
inventing one would be worse than its absence. No channel-provider architecture
has been designed, and the platform is still
not authorised for production deployment -- email verification and password
recovery remain ahead of it, and there is no provider usage accounting, billing or
alerting behind the safety budget or behind embedding spend.

Eighteen areas have their own documents:

- [multi-tenancy-isolation.md](multi-tenancy-isolation.md) - tenant isolation
- [conversation-domain.md](conversation-domain.md) - conversation and message modelling
- [persistence-postgresql.md](persistence-postgresql.md) - PostgreSQL and EF Core persistence
- [tenant-onboarding-vertical-slice.md](tenant-onboarding-vertical-slice.md) - the first API-to-database slice
- [ai-agent-domain.md](ai-agent-domain.md) - the AI agent core domain
- [ai-agent-vertical-slice.md](ai-agent-vertical-slice.md) - AI agent persistence, use cases and endpoints
- [ai-runtime.md](ai-runtime.md) - the provider-independent AI text-generation contract and agent preview
- [openai-provider.md](openai-provider.md) - the OpenAI Responses adapter behind that contract
- [admin-design-system.md](admin-design-system.md) - the admin design system and application shell
- [admin-ai-agent-experience.md](admin-ai-agent-experience.md) - the development workspace and the AI agent admin workflow
- [authentication-tenant-membership.md](authentication-tenant-membership.md) - accounts, the cookie session, antiforgery, and the membership authorization boundary
- [tenant-rbac.md](tenant-rbac.md) - tenant-scoped roles, the permission catalogue, and what a member may do inside a workspace
- [team-members-invitations.md](team-members-invitations.md) - member administration, secure invitations, and role assignment
- [conversation-runtime-website-chat.md](conversation-runtime-website-chat.md) - the conversation runtime, hosted website chat, and the anonymous visitor boundary
- [public-chat-protection.md](public-chat-protection.md) - distributed rate limiting, client-network identity, cost safety and the public session lifecycle
- [knowledge-base-rag.md](knowledge-base-rag.md) - knowledge documents, deterministic chunking, pgvector retrieval and the grounding privilege boundary
- [human-handoff-realtime-inbox.md](human-handoff-realtime-inbox.md) - AI/human handling, conversation assignment, human reply attribution and idempotency, and the SignalR realtime inbox with its short-lived public tickets
- [analytics-usage-foundation.md](analytics-usage-foundation.md) - workspace analytics over the operational records: the analytics/billing boundary, metric definitions, UTC bucketing, tenant isolation and the query shapes behind them
- [knowledge-file-ingestion.md](knowledge-file-ingestion.md) - uploading business files into Knowledge: what a file is allowed to be, how its text is read within bounds, and why the file itself is never kept
- [embeddable-web-chat-widget.md](embeddable-web-chat-widget.md) - the loader, the cross-origin iframe, origin allowlisting, the wall between the hosted and embedded transports, and the Development opt-in that keeps an inherited `OPENAI_API_KEY` from spending money
- [provider-usage-cost-accounting.md](provider-usage-cost-accounting.md) - what a call to an AI provider cost: provider-reported usage only, explicit tenant attribution, durable records written before the money is spent, decimal costs from configured rates, and why none of it is billing
- [workspace-plans-entitlements-quotas.md](workspace-plans-entitlements-quotas.md) - what a workspace may hold and consume: why a permission is not an entitlement, plans as platform configuration, unlimited as an absence rather than a sentinel, race-safe reservation in PostgreSQL, the monthly provider-operation quota, and why none of it is billing
- [saas-subscription-billing.md](saas-subscription-billing.md) - what a workspace pays and what that grants: why Stripe is not the entitlement engine, plan versus offer versus subscription, money as minor units, hosted checkout with no card ever reaching this platform, raw-body webhook verification into a payload-free durable inbox, snapshot-driven state so ordering cannot matter, a finite fallback plan, and non-destructive downgrade
- [unified-customer-identity.md](unified-customer-identity.md) - who a workspace is talking to, across every channel: why a customer is separate from the channel identity that found them, the single transport-based identity claim the platform makes and the inference it refuses, WhatsApp identity keyed on workspace and sender, why web chat has no durable identity and none is invented, composite foreign keys for tenant safety, manual depth-one merging with deterministic locking, and why none of it is a CRM
- [reports-exports-scheduling.md](reports-exports-scheduling.md) - deterministic reports over the Analytics reads: the fixed catalog and its permission matrix, queued generation with claim tokens, the artifact-store abstraction and atomic filesystem writes, CSV formula-injection defence, XLSX and PDF generation without external processes, bounded periods and exports, weekly/monthly schedules with occurrence idempotency and owner re-authorisation, and retention

Operational procedures live in [../operations/](../operations/):

- [meta-whatsapp-real-validation-runbook.md](../operations/meta-whatsapp-real-validation-runbook.md) - preparing for, running and cleaning up after a real Meta / WhatsApp validation: the actual configuration names, the single webhook callback route, the Development live-Meta safeguard, the temporary-tunnel rules, the credential-handling rules, the local preflight, and the Phase 26B sequence that has not been run

---

## 1. Clean Architecture with an enforced inward dependency direction

**Decision.** Four backend projects — Domain, Application, Infrastructure, Api —
with dependencies pointing inward only.

**Why.** The platform will accumulate many volatile outer concerns: channel APIs,
AI vendors, CRMs, ERPs, billing providers. Keeping business rules independent of
all of them is what allows a vendor to be swapped or added without reworking the
core.

**Forbids.** Domain referencing anything. Application referencing Infrastructure
or Api. Infrastructure referencing Api. Business rules in controllers.

**Enforcement.** Each test project carries a guard that reads the compiled
assembly's metadata and asserts no forbidden reference is present. Guards assert
only *forbidden* dependencies: an assembly reference is emitted solely when a
type is actually used, so a missing reference proves nothing, while a present one
proves a violation.

---

## 2. The backend owns orchestration — no n8n dependency

**Decision.** Workflows, conversation orchestration, scheduling, background
processing, integrations and business events are implemented in .NET.

**Why.** Orchestration is the product. Delegating it to an external workflow
engine would put core behaviour, its versioning and its failure modes outside the
codebase and outside the test suite.

**Forbids.** Any component whose absence would stop the product from functioning
being hosted outside this solution. If n8n is supported later it is an optional
adapter behind an Application abstraction.

---

## 3. AI and channels remain provider-independent

**Decision.** Domain and Application never reference a vendor SDK. Providers are
implemented in Infrastructure behind Application-owned abstractions.

**Why.** OpenAI is the first AI provider, not the only one; Anthropic, other
hosted models and private models must be addable later. The same reasoning
applies to WhatsApp, Instagram and Messenger.

**Forbids.** Vendor types in Domain or Application signatures. Meta-specific
payload shapes leaking inward.

**Deliberately deferred.** The abstractions themselves are *not* written yet.
Designing them before a real use case exists would produce a speculative
framework rather than a fitted one.

**Exercised in Phase 6.** The AI agent core domain is the first place this rule
had teeth: `AiAgent` models a tenant's assistant configuration with no model
name, provider id, sampling parameter, prompt object or vector reference, and two
reflection tests fail if any of those appear. **An `AiAgent` is not an OpenAI
Assistant** -- see [ai-agent-domain.md](ai-agent-domain.md) A1. No provider
abstraction was written, because no provider integration exists to fit one to.

---

## 4. Multi-tenancy is first-class but not yet implemented

**Decision.** One deployment serves many customer businesses. Tenant isolation
will be a mandatory, enforced concern.

**Why.** Retrofitting tenancy onto data and query paths built without it is the
single most expensive mistake this kind of platform can make.

**Consequence.** Tenancy must be designed before the first tenant-scoped entity
or persistence code is written, so that isolation is structural rather than a
filter every query has to remember to apply.

**Status.** Phase 2 implemented the tenant *domain model* — `Tenant`, `TenantId`,
`TenantName`, `TenantSlug`, `TenantStatus`. Phase 4 added the first relational
safeguards: a non-null `tenant_id` foreign key on conversations, and a unique
index on the slug. Phase 5 added an endpoint that *creates* tenants but **changed
nothing about isolation**: there is no caller identity and no request tenant
context, so query scoping and row level security are **still not implemented**
and an unscoped tenant-owned query remains incorrect rather than impossible. The
full strategy, its fixed invariants and its open questions are in
[multi-tenancy-isolation.md](multi-tenancy-isolation.md).

---

## 5. Warnings are build errors

**Decision.** `TreatWarningsAsErrors` is on solution-wide, with .NET analyzers at
`latest-recommended` and code style enforced during build.

**Why.** Warnings that are merely visible get ignored once there are enough of
them. This also converts NuGet vulnerability advisories (NU1901–NU1904) into
build failures, which is how the Phase 1 `Microsoft.OpenApi` advisory was caught.

**Forbids.** Suppressing a diagnostic to obtain a green build. Suppression
requires a scoped, commented justification — see the one existing example,
`backend/tests/.editorconfig`, which disables CA1707 for test projects only so
that `Subject_scenario_expectation` test names are usable, leaving the rule fully
active across `backend/src`.

---

## 6. Architecture guards read metadata, they do not load assemblies

**Decision.** `backend/tests/Shared/AssemblyReferenceReader.cs` inspects an
assembly with `System.Reflection.Metadata` instead of `Assembly.Load`.

**Why.** It is the correct tool for the job — the guards need reference metadata,
not executable code, and this avoids running any assembly under test. It also
made the guards independent of host code-integrity policy: the development
machine's Smart App Control policy blocks locally built unsigned DLLs from being
*loaded*, which broke the original reflection-based implementation.

**Consequence.** No package dependency is needed;
`System.Reflection.Metadata` ships in the shared framework.

---

## 7. TLS terminates at the reverse proxy

**Decision.** The API does not call `UseHttpsRedirection`.

**Why.** The intended deployment places the application behind a reverse proxy
that terminates TLS and forwards to a loopback-bound Kestrel. An application-level
HTTPS redirect in that topology redirects to a scheme the process does not serve.

**Consequence.** When deployment is authorised, forwarded-headers handling must
be configured so the application sees the original scheme and client address.

---

## 8. Central Package Management

**Decision.** All NuGet versions live in `backend/Directory.Packages.props`;
project files reference packages without versions, and transitive pinning is on.

**Why.** With eight projects and growing, version drift between projects is a
real and silent source of runtime mismatch. Transitive pinning is also what makes
it possible to remediate a vulnerable transitive dependency directly.

---

## 9. Domain rule violations raise one exception type

**Decision.** `DomainValidationException` is raised for every domain invariant
violation. BCL argument exceptions (`ArgumentNullException`) are kept for
programming defects such as passing null where a value object is required.

**Why.** An outer layer needs to tell "the caller supplied a value the business
rejects" (a client error) from "the code has a bug" (a server error). One
dedicated type makes that translation a single `catch` at the edge. A hierarchy
of exception classes carrying no extra behaviour would add ceremony without
adding information, and a hand-rolled `Result<T>` framework would be a
substantial piece of infrastructure built for a domain that currently has one
aggregate.

**Forbids.** A third-party result library. A generic enterprise result framework.
A new exception type per rule. Catching `DomainValidationException` to control
normal flow.

**Revisit when** a use case needs to report *several* validation failures at once
— exceptions report the first failure only. That is a real limitation, and the
point at which an explicit result type earns its cost.

**Held in Phase 5.** The first real use cases did not need a result type. The
tenant slice added exactly one Application exception,
`TenantSlugAlreadyExistsException`, and expressed "not found" as a nullable
return rather than a third type -- so the count of error mechanisms went from two
to three, not to a framework. The revisit trigger is unchanged: a use case that
must report several validation failures at once. See
[tenant-onboarding-vertical-slice.md](tenant-onboarding-vertical-slice.md) V5.

**Extended in Phase 3.** A second type, `DomainStateException`, covers an
operation that is well-formed but not permitted by the current state of its
aggregate - adding a message to a closed conversation. The two describe genuinely
different failures: invalid input could never succeed as written, while an
invalid transition is a well-formed request that would succeed once the aggregate
is in another state. Collapsing them would lose that distinction and force every
caller to re-derive it. The rule stays **one type per meaningful category of
failure, never one per rule**; two categories are known, so there are two types.

The Domain draws the distinction and stops there. It contains no status codes,
no ASP.NET types and no transport vocabulary. **How each category is surfaced to
a client - status codes, error contracts, retry advice - is a future
Application/API concern**, and the two types exist so that layer has something
unambiguous to map.

---

## 10. Identity and constrained strings are value objects, not primitives

**Decision.** `TenantId` wraps `Guid`; `TenantName` and `TenantSlug` wrap
`string`. Validation happens once, at construction.

**Why.** For `TenantId` this is a security property, not tidiness: tenant
identity is the isolation boundary, and a bare `Guid` can be transposed with any
other `Guid` without the compiler noticing. For the strings it removes the
possibility of an unvalidated value existing at all — there is no path that
produces a blank name or a malformed slug.

**Consequence.** `TenantId` is a `readonly record struct`, so C# permits
`default(TenantId)`, which holds `Guid.Empty`. No factory can produce it, but the
hole cannot be closed in the language; `IsEmpty` exists so any boundary accepting
an identity from outside the Domain can reject it explicitly.

**Forbids.** Passing raw `Guid` tenant identity through domain or application
signatures. Validating a name or slug at a call site instead of at construction.

---

## 11. Aggregates supply their own time; the Domain never reads the clock

**Decision.** Every state-changing domain operation takes the effective instant
as a `DateTimeOffset` parameter, normalised to UTC on the way in. No domain code
calls `DateTime.UtcNow` or `DateTimeOffset.UtcNow`.

**Why.** It makes domain behaviour deterministic and every test reproducible
without a clock abstraction, a fake clock or an injected time provider - a
framework avoided by pushing one parameter down instead. It also keeps the
decision of *what time it is* at the edge, which is where it actually belongs:
an ingested channel message carries its own timestamp, and the Domain should
record that instant rather than the moment the process happened to handle it.

**Forbids.** Reading the system clock anywhere in Domain. Storing local time.
Introducing a time abstraction while a parameter suffices. **Requiring supplied
instants to increase**, which would push a distributed-systems problem into the
wrong layer.

**Consequence.** Callers must supply the instant, and out-of-order arrival is
accepted: a value earlier than an aggregate's last activity is recorded as given,
never rewritten. Where an aggregate tracks a "last activity" instant, that is a
high-water mark updated as `max(current, supplied)`, so it never regresses.
Collection order is ingestion order and is not a claim about chronological order;
callers needing chronological order sort by the timestamp themselves. See
[conversation-domain.md](conversation-domain.md) C10-C11.
