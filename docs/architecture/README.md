# Architecture

Decisions recorded here are binding until explicitly revisited. Each entry states
the decision, why it was taken, and what it forbids.

Phase 1 established the foundation only. No product architecture (tenancy,
persistence, AI, channels) has been designed yet.

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

---

## 4. Multi-tenancy is first-class but not yet implemented

**Decision.** One deployment serves many customer businesses. Tenant isolation
will be a mandatory, enforced concern.

**Why.** Retrofitting tenancy onto data and query paths built without it is the
single most expensive mistake this kind of platform can make.

**Consequence.** Tenancy must be designed before the first tenant-scoped entity
or persistence code is written, so that isolation is structural rather than a
filter every query has to remember to apply.

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
