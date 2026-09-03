# Phase 1 — Greenfield Solution Foundation

**Date:** 2026-09-03
**Scope:** Repository foundation only. No product features.

---

## 1. Phase objective

Establish a clean, production-quality repository foundation for a multi-tenant
omnichannel AI customer engagement SaaS platform, and verify that it builds and
tests correctly. Explicitly excluded: business entities, tenancy, persistence,
authentication, AI, channels, background work, billing, reporting, deployment and
any VPS access.

## 2. Final status

**PASS WITH WARNINGS**

All authorised Phase 1 work is complete and every validation command passes.
The warnings are environmental and advisory, not defects in the deliverable:

1. Browser verification could not be performed — all browser MCP servers failed
   to connect (§13).
2. The development machine runs Smart App Control, which blocks locally built
   unsigned DLLs from being loaded. This was worked around correctly, but it will
   affect any future test that loads an assembly reflectively (§19).
3. A high-severity vulnerability in a transitive dependency was found and
   remediated during this phase (§15).

## 3. Environment detected

| Component | Version |
| --- | --- |
| OS | Windows 11 Home, 10.0.26200, x64 |
| .NET SDK | 10.0.203 (MSBuild 18.3.3) |
| .NET runtime | 10.0.7 (ASP.NET Core 10.0.7) |
| Node.js | v24.19.0 |
| npm | 11.17.0 |
| Git | 2.55.0.windows.5 |
| Angular CLI | 22.1.7 |
| Angular | 22.1.x |
| TypeScript | 6.0.3 |
| Vitest | 4.1.11 |

.NET 10 was available, so it was used as instructed. Angular 22 is the current
stable release and supports Node 24.

## 4. Initial workspace condition

`c:\Users\mouay\Desktop\omnichannel-ai-platform` was **empty** — no files, no
subdirectories, and not a Git repository. Confirmed greenfield; nothing was
overwritten. `git status` failed with *"not a repository"* before any work began.

## 5. Repository structure created

```
/
├── .editorconfig
├── .gitignore
├── README.md
├── backend/
│   ├── Directory.Build.props
│   ├── Directory.Packages.props
│   ├── OmniAI.slnx
│   ├── src/
│   │   ├── OmniAI.Domain/
│   │   ├── OmniAI.Application/
│   │   ├── OmniAI.Infrastructure/
│   │   └── OmniAI.Api/
│   └── tests/
│       ├── .editorconfig
│       ├── Shared/AssemblyReferenceReader.cs
│       ├── OmniAI.Domain.Tests/
│       ├── OmniAI.Application.Tests/
│       ├── OmniAI.Infrastructure.IntegrationTests/
│       └── OmniAI.Api.IntegrationTests/
├── frontend/            (Angular administration application)
└── docs/
    ├── architecture/README.md
    └── phase-reports/PHASE_01_FOUNDATION_REPORT.md
```

## 6. Backend projects created

| Project | Template | Contents |
| --- | --- | --- |
| `OmniAI.Domain` | classlib | `AssemblyMarker` only |
| `OmniAI.Application` | classlib | `AssemblyMarker` only |
| `OmniAI.Infrastructure` | classlib | `AssemblyMarker` only |
| `OmniAI.Api` | webapi | `Program.cs`, settings, `.http` file |
| `OmniAI.Domain.Tests` | xunit | 2 architecture guard tests |
| `OmniAI.Application.Tests` | xunit | 1 architecture guard test |
| `OmniAI.Infrastructure.IntegrationTests` | xunit | 1 architecture guard test |
| `OmniAI.Api.IntegrationTests` | xunit | 1 HTTP integration test |

The solution uses the `.slnx` format, which is what `dotnet new sln` produces on
the .NET 10 SDK.

`AssemblyMarker` is a sealed, private-constructor anchor type per layer. It has a
consumer in this same phase (it keeps each layer assembly non-empty and gives the
guards a stable type to name) and is the standard anchor for later assembly
scanning. It carries no behaviour.

## 7. Backend project reference graph

```
OmniAI.Api ─────────► OmniAI.Infrastructure ─────────► OmniAI.Application ─────────► OmniAI.Domain
     │                          │                              │
     │                          └──────────────────────────────┼─────────────────────►  (Domain)
     └─────────────────────────────────────────────────────────┘
```

Declared references:

| Project | References |
| --- | --- |
| `OmniAI.Domain` | *(none)* |
| `OmniAI.Application` | Domain |
| `OmniAI.Infrastructure` | Application, Domain |
| `OmniAI.Api` | Application, Infrastructure |
| `OmniAI.Domain.Tests` | Domain |
| `OmniAI.Application.Tests` | Application |
| `OmniAI.Infrastructure.IntegrationTests` | Infrastructure |
| `OmniAI.Api.IntegrationTests` | Api |

No circular references. Each test project references only the project it tests.

## 8. Frontend foundation created

Angular 22 application `omniai-admin` in `frontend/`:

- standalone components, zoneless change detection, `bootstrapApplication`
- routing enabled, `routes` deliberately empty
- SCSS styling; no UI framework installed
- **strict mode enabled beyond the template default** — the Angular 22 template
  does not emit `strict: true` or `strictTemplates`, both of which the brief
  requires, so they were added along with `noUnusedLocals`,
  `noUnusedParameters`, `forceConsistentCasingInFileNames` and
  `typeCheckHostBindings`
- `@app/*` path alias to `src/app/*`
- the 20 KB generated Angular splash page was replaced with a minimal accessible
  shell: skip link, `<header>`/`<main>` landmarks, focus target, router outlet
- Vitest is the test runner (the Angular 22 default)

## 9. NuGet packages added

Versions are centrally managed in `backend/Directory.Packages.props`.

| Package | Version | Project | Reason |
| --- | --- | --- | --- |
| `Microsoft.AspNetCore.OpenApi` | 10.0.11 | `OmniAI.Api` | OpenAPI document for the HTTP surface the Angular app will consume. Template default was 10.0.7; upgraded to remediate a vulnerability (§15). |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.11 | `OmniAI.Api.IntegrationTests` | In-memory host so the API is exercised over real HTTP. Kept in step with the version above. |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | all 4 test projects | Test host. Template default. |
| `xunit` | 2.9.3 | all 4 test projects | Test framework. Template default. |
| `xunit.runner.visualstudio` | 3.1.4 | all 4 test projects | Test adapter. Template default. |
| `coverlet.collector` | 6.0.4 | all 4 test projects | Coverage collection. Template default. |

No EF Core, PostgreSQL, Redis, RabbitMQ, MassTransit, Hangfire, OpenAI or
Anthropic package was added. `System.Reflection.Metadata`, used by the
architecture guards, ships in the shared framework and needed no reference.

## 10. npm dependencies materially added

None beyond the Angular 22 `ng new` output. 372 packages installed; no package
was added by hand, and no UI framework, state library, HTTP library or icon set
was installed.

Direct dependencies: `@angular/{common,compiler,core,forms,platform-browser,router}`,
`rxjs`, `tslib`. Dev: `@angular/{build,cli,compiler-cli}`, `typescript`,
`vitest`, `jsdom`, `prettier`.

Three npm scripts were added for validation: `test:ci`, `format` and
`format:check`.

## 11. Commands executed

Discovery: `dotnet --info`, `node --version`, `npm --version`, `git --version`,
`git status`, `git rev-parse --is-inside-work-tree`, `dotnet new list`,
`npm view @angular/cli version`.

Scaffolding: `git init`, `dotnet new sln`, `dotnet new classlib` ×3,
`dotnet new webapi`, `dotnet new xunit` ×4, `dotnet sln add` ×8,
`npx @angular/cli@22.1.7 new omniai-admin --directory=frontend --style=scss --ssr=false --routing=true --skip-git --skip-install --ai-config=none --defaults`.

Validation: `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`,
`dotnet list package --vulnerable --include-transitive`, `npm install`,
`npm run build`, `npm run test:ci`, `npx prettier --check .`, `npm audit`,
`npm outdated`, `dotnet run` + `curl` against `/health`, `/openapi/v1.json` and a
404 path, `ng serve` + `curl`.

Diagnostics: a registry read of the Code Integrity policy state and a read of the
`Microsoft-Windows-CodeIntegrity/Operational` event log, to identify the cause of
the assembly-load failures. **Read-only — no security policy was modified.**

## 12. Validation results

| Check | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx` | **Pass** — all 8 projects, no warnings |
| `dotnet build OmniAI.slnx -c Release` | **Pass** — 0 warnings, 0 errors |
| `dotnet test OmniAI.slnx -c Release` | **Pass** — 5 passed, 0 failed, 0 skipped |
| `dotnet list package --vulnerable --include-transitive` | **Pass** — 0 vulnerable across all 8 projects |
| `npm install` | **Pass** — 372 packages, 0 vulnerabilities |
| `npm run build` (production) | **Pass** — initial bundle 190.80 kB raw / 52.18 kB transfer |
| `npm run test:ci` | **Pass** — 3 of 3 tests, 1 file |
| `npm run format:check` | **Pass** — all files match the Prettier config |
| `npm audit` | **Pass** — 0 vulnerabilities |

Backend test breakdown:

| Project | Passed |
| --- | --- |
| `OmniAI.Domain.Tests` | 2 |
| `OmniAI.Application.Tests` | 1 |
| `OmniAI.Infrastructure.IntegrationTests` | 1 |
| `OmniAI.Api.IntegrationTests` | 1 |

## 13. Browser verification

**Not performed.**

Every browser integration configured for this session failed to connect with
`CONNECT_TIMEOUT` after 30 s: `chrome-devtools-direct`,
`plugin:chrome-devtools-mcp:chrome-devtools` and `plugin:playwright:playwright`.
A tool search confirmed no browser automation tool was reachable. As the brief
states, this is not a Phase 1 blocker.

**Substitute verification performed via CLI**, with both applications actually
running locally:

*Backend* (`dotnet run`, `http` profile, `http://localhost:5027`):

| Request | Result |
| --- | --- |
| `GET /health` | `200 OK`, `application/json`, body `{"status":"healthy"}` |
| `GET /openapi/v1.json` | `200 OK`, valid OpenAPI 3.1.1 document listing the single `/health` operation |
| `GET /does-not-exist` | `404`, `application/problem+json`, RFC 9457 body with `traceId` |

*Frontend* (`ng serve`, `http://localhost:4299`):

| Request | Result |
| --- | --- |
| `GET /` | `200 OK`, `text/html`, correct `<title>OmniAI Admin</title>`, `<app-root>` present |

Startup logs for both processes were clean — no errors or warnings. Both
processes were then stopped, and a port check confirmed neither is still
listening.

**What this does not cover:** client-side rendering, runtime console errors and
network behaviour in a real browser. The Angular page is client-rendered, so the
`curl` response is the pre-hydration document shell. Component rendering is
covered instead by unit tests asserting the `<h1>` text, the skip link `href` and
the `<main>` landmark id. Genuine browser verification should be repeated in
Phase 2 once an MCP browser connection is available.

## 14. Compiler / build / test warnings

**Zero.** `dotnet build -c Release` reports `0 Warning(s), 0 Error(s)` across all
eight projects; the Angular production build and the Vitest run emit no warnings.

No warning was suppressed to achieve this. Two build failures were encountered
and fixed on their merits during the phase:

1. **NU1504 — duplicate `PackageReference`.** Caused by my own error: I wrote
   `PackageReference` instead of `PackageVersion` in `Directory.Packages.props`.
   Fixed by using the correct item type.
2. **CA1707 ×7 — underscores in member names**, on test method names. Resolved by
   a scoped override in `backend/tests/.editorconfig` disabling CA1707 **for test
   projects only**, with a comment giving the reason. The rule remains fully
   active across `backend/src`. This is a naming-convention decision, not a
   suppression to obtain a green build.

Non-build advisories observed and reported rather than hidden:

- `npm warn allow-scripts` — npm 11 did **not** run install scripts for
  `esbuild`, `lmdb`, `msgpackr-extract` and `@parcel/watcher`. This is npm's
  supply-chain default. Build and tests both work without them, so nothing was
  approved. Do not run `npm approve-scripts` without a specific reason.
- `npm outdated` reports `typescript` 7.0.2, `vitest` 5.0.0 and `jsdom` 30.0.1 as
  newer than the installed 6.0.3 / 4.1.11 / 28.1.0. These are pinned by Angular
  22's supported ranges and were deliberately **not** upgraded.

## 15. Dependency / security / vulnerability scan results

**One real high-severity vulnerability was found and fixed.**

`Microsoft.AspNetCore.OpenApi` 10.0.7 — the .NET 10 template's own default —
pulls `Microsoft.OpenApi` 2.0.0 transitively, which carries
[GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc), a
known **high severity** advisory. Because `TreatWarningsAsErrors` is enabled,
NuGet's NU1903 advisory failed the restore instead of passing silently.

Remediation: upgraded `Microsoft.AspNetCore.OpenApi` to 10.0.11, which resolves
`Microsoft.OpenApi` to **2.7.5**. `Microsoft.AspNetCore.Mvc.Testing` was moved to
10.0.11 to stay aligned. The advisory was fixed, not suppressed.

Post-fix scan results:

- `dotnet list package --vulnerable --include-transitive` — no vulnerable
  packages in any of the 8 projects
- `npm audit` — 0 vulnerabilities across 373 audited packages

## 16. Files created or materially modified

53 files would be tracked by Git (including this report). Hand-authored or materially modified:

**Root:** `.gitignore`, `.editorconfig`, `README.md`

**Backend:** `Directory.Build.props`, `Directory.Packages.props`, `OmniAI.slnx`,
all 8 `.csproj` files (rewritten for Central Package Management and the reference
graph), `src/OmniAI.Api/Program.cs`, `src/OmniAI.Api/OmniAI.Api.http`,
`src/*/AssemblyMarker.cs` ×3, `tests/.editorconfig`,
`tests/Shared/AssemblyReferenceReader.cs`, and the four test classes.

**Frontend:** `tsconfig.json` (strict mode, path alias), `src/app/app.ts`,
`src/app/app.html`, `src/app/app.scss`, `src/app/app.spec.ts`, `src/index.html`,
`package.json` (scripts). All generated files were reformatted once with the
project's own Prettier config, which the CLI output did not match.

**Docs:** `docs/architecture/README.md`, this report.

**Deleted template boilerplate:** `Class1.cs` ×3, `UnitTest1.cs` ×4, and the
`WeatherForecast` sample endpoint and record from `Program.cs`.

## 17. Architectural decisions made

Recorded in full in `docs/architecture/README.md`. Summary:

1. **Clean Architecture with enforced inward dependencies** — guarded by tests,
   not just documentation.
2. **Architecture guards read metadata, they do not load assemblies** —
   `System.Reflection.Metadata` over the compiled file. Correct for the job, and
   independent of host code-integrity policy.
3. **Guards assert only forbidden dependencies.** An assembly reference is
   emitted only when a type is actually used, so a *missing* reference proves
   nothing — a first draft asserting "Application references Domain" failed
   correctly, because nothing in Application uses a Domain type yet. Positive
   direction is enforced by the `.csproj` graph and by MSBuild, which rejects
   cycles.
4. **Warnings are build errors** solution-wide, with analyzers at
   `latest-recommended` and code style enforced in-build. This is what surfaced
   the OpenAPI advisory.
5. **Central Package Management** with transitive pinning — one version per
   package, and the ability to remediate a transitive dependency directly.
6. **No application-level HTTPS redirection** — TLS terminates at the reverse
   proxy in the intended topology, so an in-app redirect would point at a scheme
   the process does not serve. Forwarded-headers handling is a Phase-2 item.
7. **Problem Details as the uniform error contract**, verified live.
8. **`/health` is liveness only** — deliberately dependency-free. Readiness
   checks arrive with the components they would check.
9. **Angular strict mode enabled explicitly**, since the Angular 22 template no
   longer emits `strict: true`.
10. **Minimal accessible shell over the generated splash page.**

## 18. Deviations from the prompt

1. **`git init` was run.** The workspace was not a repository, so `git status
   --short` and `git diff --stat` — both required by the brief — could not
   otherwise run, and `.gitignore` would have had no effect. This creates no
   commit, no remote and no credential change, all of which remain forbidden and
   were not done.
2. **The solution file is `OmniAI.slnx`, not `OmniAI.sln`.** This is what
   `dotnet new sln` produces on the .NET 10 SDK. The brief said "create the
   backend solution" without specifying a format.
3. **`backend/tests/Shared/` was added**, holding one linked source file. The
   brief listed exactly four test projects, and this adds no fifth project — it
   avoids duplicating the guard helper three times while keeping the authorised
   project structure intact.
4. **The generated Angular splash page was replaced** with a smaller accessible
   shell. The brief forbids layouts *beyond* the generated foundation; this is
   less than what was generated, not more.
5. **Prettier was run once across `frontend/`.** The Angular CLI's own generated
   files did not satisfy the `.prettierrc` the CLI itself generated, so
   `format:check` would have failed on a clean checkout.
6. **`Microsoft.AspNetCore.OpenApi` was upgraded past the template default** to
   fix the security advisory in §15.

## 19. Known issues

1. **Smart App Control blocks locally built DLLs from loading.** The machine has
   `VerifiedAndReputablePolicyState = 1`; the CodeIntegrity event log records
   Smart App Control blocks (Policy ID `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`,
   events 3033/3077/3118) for `OmniAI.Domain.dll` and `OmniAI.Infrastructure.dll`
   under `testhost.exe`. The architecture guards no longer load assemblies, so the
   suite is unaffected — but **any future test that reflectively loads an
   assembly will fail on this machine**, and the block is not consistent across
   assemblies (`OmniAI.Api.dll` and `OmniAI.Application.dll` loaded fine). No
   security policy was changed: disabling Smart App Control is irreversible
   without an OS reinstall and is the user's decision, not mine. If reflective
   loading is needed later, the practical options are to build in a location
   covered by a different policy, sign the assemblies, or run the suite in CI.
2. **The architecture guards are currently trivially satisfied**, since the
   layers contain only marker types. They become meaningful as real code lands —
   which is the reason for adding them now rather than later.
3. **No linter for the frontend.** Angular 22 ships no ESLint by default, and
   `angular-eslint` was not added, since no dependency should be added without a
   reason. Prettier covers formatting only. Recommended for Phase 2.
4. **No CI pipeline**, no `global.json` SDK pin, no `.nvmrc`. Deliberate: CI
   belongs with the deployment phase.
5. **`InvariantGlobalization` is enabled solution-wide.** Correct for the current
   projects, but it must be revisited before locale-aware formatting or
   culture-aware collation is introduced.

## 20. Blockers

**None for Phase 1.** Every authorised objective was completed and validated.

For Phase 2, one item needs a decision from you rather than from me: whether
Smart App Control (§19.1) should stay on for this workspace, and how tests that
need reflective assembly loading should be run if it does.

## 21. Confirmation — no secrets committed

**Confirmed.** No API key, token, password, connection string, signing key or
credential of any kind was written to any file in this repository.

- The only configuration files are the stock `appsettings.json` and
  `appsettings.Development.json`, containing logging levels and `AllowedHosts`
  only.
- A scan of everything Git would track found no match for `node_modules`, `/bin/`,
  `/obj/`, `/dist/`, `.env`, `secret`, `.key`, `.pem` or `password`.
- `.gitignore` covers .NET build output, `node_modules`, Angular output, IDE
  files, OS files, and environment/secret/key patterns including SSH private keys.
- The VPS SSH password was never requested, received, written, printed or logged.

## 22. Confirmation — VPS not accessed

**Confirmed.** No SSH connection was attempted or made to `186.240.145.196` or
any other host. Nothing was deployed. No Nginx configuration, systemd service,
server directory, package or production application was inspected or modified.
No server discovery was run. All work was local to
`c:\Users\mouay\Desktop\omnichannel-ai-platform`.

The only network access was to `api.nuget.org` and `registry.npmjs.org` for
package restore, and to `localhost` for the runtime verification in §13.

## 23. `git status --short`

```
?? .editorconfig
?? .gitignore
?? README.md
?? backend/
?? docs/
?? frontend/
```

The repository has no commits, so all content is untracked and Git collapses the
directories. 53 files would be added; none is build output, a dependency or a
secret-shaped file.

## 24. `git diff --stat`

```
(empty)
```

`git diff` compares the working tree to the index. Nothing has been staged and no
commit exists, so there is no diff to report. This is expected for a repository
in this state, and is not a sign that files are missing — §23 and §16 describe
the actual content.

## 25. Readiness recommendation for Phase 2

**Ready to proceed.**

The foundation builds clean with warnings as errors, has a passing test suite,
carries no known vulnerabilities, and enforces its own layering. Nothing outside
the authorised scope was implemented.

Recommended order for Phase 2, before any feature work:

1. **Design multi-tenancy first.** Tenant identity, resolution and isolation must
   be settled before the first tenant-scoped entity exists. This is the one
   decision that is disproportionately expensive to retrofit.
2. **Then persistence**, shaped by that tenancy model — provider choice, migration
   strategy, and whether isolation is by discriminator, schema or database.
3. **Then the first vertical slice** through all four layers, so the layering is
   proven by real code rather than by marker types.
4. Alongside these: `angular-eslint`, a CI pipeline running the same validation
   commands, `global.json` and `.nvmrc` toolchain pins, forwarded-headers
   configuration, and a decision on Smart App Control (§19.1, §20).

Deferring AI, channels, billing and reporting until the tenancy and persistence
foundations exist remains the right sequence.
