# OmniAI Platform

Multi-tenant omnichannel AI customer engagement platform.

> **Status: Phase 1 — repository foundation only.**
> No product features are implemented. The sections below describe what exists
> today, not what the platform is intended to become.

---

## Purpose

One platform serving many customer businesses, rather than a separate codebase
per customer. The product direction covers website chat, WhatsApp, Instagram and
Messenger channels, AI support and sales, knowledge/RAG, human handoff, customer
and lead management, reporting, scheduling, integrations, usage tracking and
billing.

**None of that is built yet.** See [Current state](#current-state).

---

## Architectural direction

### Backend — Clean Architecture

The backend is a .NET solution with a strictly inward dependency direction:

```
OmniAI.Api  ──────────►  OmniAI.Infrastructure
    │                            │
    │                            ▼
    └──────────────────►  OmniAI.Application
                                 │
                                 ▼
                           OmniAI.Domain
```

| Project | Role | May reference |
| --- | --- | --- |
| `OmniAI.Domain` | Business model and rules | nothing |
| `OmniAI.Application` | Use cases and the abstractions the outer layers implement | Domain |
| `OmniAI.Infrastructure` | Implementations of Application abstractions | Application, Domain |
| `OmniAI.Api` | Composition root, hosting and the HTTP surface | Application, Infrastructure |

This is enforced, not merely documented: each test project carries an
architecture guard that reads the compiled assembly's metadata and fails the
build's test stage if a forbidden dependency appears.

### Standing constraints

- **No n8n dependency.** The backend owns workflows, orchestration, scheduling,
  background processing and integrations. If n8n is ever supported it will be an
  optional adapter, and the product must work fully without it.
- **AI stays provider-independent.** Domain and Application must never depend on
  OpenAI, Anthropic or any other vendor SDK. Providers live behind
  Application-owned abstractions in Infrastructure.
- **Channels stay provider-independent.** Meta and other external channel APIs
  must not leak into the Domain.
- **Multi-tenancy is a first-class concern** and will be designed in before
  tenant-scoped data exists. It is deliberately not implemented yet.
- **TLS terminates at the reverse proxy.** The API does not perform its own HTTPS
  redirection.

### Frontend

`frontend/` is the administration application: Angular, standalone components,
zoneless change detection, strict TypeScript and strict Angular templates.

---

## Current state

Implemented in Phase 1:

- Backend solution, four source projects and four test projects, wired with the
  dependency direction above
- One API endpoint: `GET /health` (liveness only — it reports that the process is
  up, not that dependencies are reachable)
- OpenAPI document generation, served in Development at `/openapi/v1.json`
- RFC 9457 `application/problem+json` error responses
- Architecture guard tests
- Angular application shell with accessibility landmarks and a skip link

Not implemented: everything else, including entities, tenancy, persistence,
authentication, AI, channels, background work, billing and reporting.

---

## Prerequisites

| Tool | Version used |
| --- | --- |
| .NET SDK | 10.0.203 |
| Node.js | 24.19.0 |
| npm | 11.17.0 |
| Git | 2.55.0 |

Angular CLI is consumed from the project's local dev dependencies; a global
install is not required.

---

## Backend

Run from `backend/`.

```bash
dotnet restore OmniAI.slnx
dotnet build   OmniAI.slnx -c Release
dotnet test    OmniAI.slnx -c Release
```

Run the API locally:

```bash
dotnet run --project src/OmniAI.Api
```

The `http` launch profile listens on <http://localhost:5027>.
In Development the OpenAPI document is at `/openapi/v1.json`.

Build policy, applied solution-wide from `Directory.Build.props`:

- nullable reference types and implicit usings enabled
- .NET analyzers at `latest-recommended`, code style enforced during build
- **warnings are errors** — do not silence a warning to obtain a green build

NuGet versions are centrally managed in `Directory.Packages.props`. Add the
version there, and reference the package without a version in the `.csproj`.

## Frontend

Run from `frontend/`.

```bash
npm install         # install dependencies
npm run build       # production build
npm run test:ci     # unit tests, single run
npm start           # dev server on http://localhost:4200
npm run format:check # Prettier verification
npm run format      # Prettier, writing changes
```

---

## Repository layout

```
backend/
  src/     OmniAI.Domain, OmniAI.Application, OmniAI.Infrastructure, OmniAI.Api
  tests/   one test project per source project, plus a shared architecture helper
frontend/  Angular administration application
docs/
  architecture/    architectural decisions and constraints
  phase-reports/   per-phase delivery reports
```

---

## Secrets

No credentials belong in this repository. API keys, channel tokens, database
passwords, JWT signing keys and SSH credentials are supplied through the runtime
environment or a secret store, never through source control. `.gitignore` blocks
the common secret and environment file patterns, but it is a safety net, not a
substitute for care.
