# Phase 11 — Tenant & AI agent admin experience

## 1. Phase objective

Turn the Phase 10 admin shell into the first real product workflow, backed by the
real API: choose a development workspace, list AI agents, create one, edit its
name and instructions, control its lifecycle, and preview it — with correct
`ETag` / `If-Match` concurrency throughout. Add only the minimum backend
capability the admin needs (a tenant-scoped, paginated agent list).

## 2. Final status

**PASS.**

Backend Release build clean, **958 of 958 tests pass**, 8/8 projects report no
vulnerable packages. Frontend build clean, **170 of 170 tests pass**, Prettier
clean, 0 npm vulnerabilities. The full workflow was exercised in Chrome against a
real local backend, including a genuine two-tab 412 conflict. No packages added,
no migration, no commit.

## 3. Baseline commit

`186d7204c234ef54eb1438f853dd7dd1dae1b16c` — *feat: establish professional admin
design system* (Phase 10 checkpoint). Still `HEAD`; nothing was committed.

## 4. Initial Git state

At the start of Phase 11 the working tree was clean at `186d720`, with no
remotes.

## 5. Recovery after the accidental VS Code closure

**Recovery state: STATE C** — implementation and automated tests were complete;
browser verification had not started, and documentation had not been written.

**Interruption point.** The previous session was inside backend validation. Its
`dotnet test OmniAI.slnx -c Release` run died with `MSBUILD error MSB4166: Child
node exited prematurely` across six nodes — the signature of the process tree
being killed, not a code defect. Two of four suites had reported before it died.

**What recovery found already complete** (verified by inspection, not assumed):

- Backend: the `ListAiAgents` Application slice (5 files), the repository method,
  the API endpoint and page response, the 400 mapping, DI registration, and all
  three new backend test files.
- Frontend: the workspace context, guard and setup screen; the error model; the
  Tenant and AI agent API services; list, create and editor pages; the preview
  panel; toast and confirm-dialog primitives; the dev proxy; and 8 new spec files.
- Nothing staged. No partial or corrupted file.

**What recovery found missing:** `docs/architecture/admin-ai-agent-experience.md`,
`docs/phase-reports/PHASE_11_TENANT_AI_AGENT_ADMIN_REPORT.md`, and all browser
verification.

**Work completed after recovery:**

1. Restarted the local test PostgreSQL, which had gone down with the session.
2. Re-ran backend restore, Release build, the full Release test suite and the
   vulnerability scan. **This surfaced one genuine failure** (§6).
3. Re-ran the frontend build, tests, format check and audit.
4. Performed the entire Chrome workflow against a real local backend, including
   the two-tab 412 scenario and the responsive/theme sweep. **This surfaced one
   genuine defect** (§42).
5. Wrote the architecture document and this report, and updated the architecture
   index.

No completed work was recreated, reset or discarded.

## 6. A real test failure found by the resumed run

`AiAgentEndpointsTests.Delete_and_list_are_not_exposed` failed. It was a Phase 7
routing guard asserting that **both** `DELETE` and the agent **list** answer 405.
Phase 11 added the list by explicit authorisation, so the guard was correctly
failing.

Fixed by narrowing rather than deleting it: `Delete_is_not_exposed` still asserts
405 and that nothing was removed, and a new `List_is_exposed` records the
deliberate contract change. Silently deleting a guard would have removed the
signal that an endpoint appearing should always be a reviewed decision.

## 7. Backend changes

Five new Application files (`AiAgentSummary`, `AiAgentPage`,
`AiAgentPageValidationException`, `ListAiAgentsQuery`, `ListAiAgentsHandler`), one
new API file (`AiAgentPageResponse`, holding both the summary and page contracts),
one new repository method, one endpoint, one Problem Details arm, one DI
registration. Three new test files.

**No accepted semantics were changed.** Tenant scoping, `xmin`, opaque `ETag`,
`If-Match`, 428/412/409, `IAiTextGenerator`, the OpenAI provider architecture,
`store = false`, disabled SDK retries and preview semantics are all untouched.

## 8. Agent list use case

`ListAiAgentsHandler`: validate paging, convert the tenant id, ask storage.
Nothing else. No filtering, no searching, no sort parameter, no generic
pagination type, no specification pattern, no query bus, no MediatR, and no
`IQueryable` exposed. Application still references no EF Core or Npgsql type.

A tenant that does not exist and a tenant with no agents both produce an empty
page — deliberately indistinguishable, so the endpoint cannot be used to discover
which tenant ids are real.

## 9. Agent list API contract

`GET /api/tenants/{tenantId:guid}/ai-agents?page=1&pageSize=20` → `200`

```json
{
  "items": [
    { "id": "…", "tenantId": "…", "name": "…",
      "status": "Draft", "createdAt": "…", "updatedAt": "…" }
  ],
  "page": 1, "pageSize": 20, "totalCount": 4
}
```

Six fields per item. **No instructions**, no `ETag` (header or body), no `xmin`,
no provider or model metadata. `status` is a name, never an integer. An API test
asserts the exact field list.

## 10. Pagination strategy

Defaults `page=1` / `pageSize=20`; maximum `100`. Out-of-range values are
**refused with 400, not clamped** — silently returning fewer rows than asked for
is how a client concludes it has reached the end of a list when it has not. The
error message names the bound, so a corrected request is one edit away. Parameters
bind as nullable integers, so `?page=0` is reported as the mistake it is rather
than reinterpreted as page 1. A non-numeric value is rejected by model binding
before the handler runs.

## 11. Ordering

`updated_at DESC, id ASC`. The identity tie-break is load-bearing: agents created
in one transaction share a timestamp exactly, and without a total order
consecutive pages can overlap or skip rows. Tested against real PostgreSQL with
rows deliberately sharing a timestamp, asserting the order is both correct and
stable across calls.

## 12. Tenant-scoped list safety

The tenant predicate is in the SQL, first, for the page **and the count** — so a
caller cannot infer another tenant's agent count from an inflated total. Proved
at three levels: Application (the fake filters too, so the test cannot pass
against an unscoped handler), Infrastructure against real PostgreSQL, and API over
HTTP including an assertion that another tenant's agent name never appears in the
response bytes.

## 13. Migration changed

**NO.** `git status` over
`backend/src/OmniAI.Infrastructure/Persistence/Migrations` is empty. The existing
`tenant_id` index supports the scoped read and agent counts are expected to be
modest. A composite `(tenant_id, updated_at DESC, id)` index is recorded as
deferred, to be added on measurement rather than speculation. Applying migrations
to the local dev database applied only the two pre-existing ones.

## 14. Frontend feature architecture

```
core/
  api/        api-error.ts (the whole error model), tenant-api.ts
  workspace/  workspace-context-service.ts, workspace-guard.ts
  feedback/   toast-service.ts, toast-host.ts, confirm-dialog.ts
features/
  workspace-setup/
  ai-agents/  ai-agent-api.ts, ai-agent-limits.ts, ai-agent.routes.ts
              agent-list/ agent-create/ agent-editor/ (+ agent-preview)
```

Signals and component-local state throughout. **No NgRx or other global state
library.** `AiAgentApi` is provided at the feature route, so it ships inside the
lazy chunk.

## 15. Development workspace context

`WorkspaceContextService`. It remembers which tenant id the admin UI points at and
nothing else. See §17.

## 16. Workspace setup UX

A full-page screen outside the shell (there is no workspace yet, so
workspace-scoped navigation would lead nowhere). It creates a **real tenant**
through `POST /api/tenants`; the id, status and canonical values all come from the
backend. The operator supplies only a name and slug, and the slug is suggested
from the name until they edit it.

It states on its face that it is not a sign-in, and asks for no credential — a
test asserts the only inputs are the two text fields and that no password input or
password autocomplete exists.

The unreachable state is a different screen: connection error, retry, and a
deliberate "forget", with **no create form**, so nobody makes a duplicate
workspace to repair a backend that is merely not running.

## 17. Why the workspace context is not authentication

There is no sign-in, session, token, principal or permission model anywhere in
this platform. The tenant id lives in `localStorage`, where the operator can edit
it, and the backend believes whatever tenant id the URL carries. **Anyone who can
open this application can reach any tenant whose id they know.**

It is named for what it is; `AuthService`, `CurrentUserService` and
`IdentityService` were all rejected as names that would outlive the phase and be
trusted by somebody who had not read the note. A test asserts the service's
surface contains no `user`, `token`, `auth`, `login`, `role` or `permission`.

The route guard answers "is a tenant selected and does it exist", never "may this
person see it" — correctness, not security.

## 18. Workspace persistence and stale handling

`localStorage`, key `omniai.workspace-tenant-id`.

- **404 → the stored id is cleared** and the operator returns to setup. The
  backend has said the tenant is genuinely gone.
- **Transport failure or 5xx → the id is kept.** A connection failure says
  nothing about whether the tenant exists; clearing on it would discard a good
  workspace on every backend restart.

Both paths are covered by tests, as is recovery once the backend answers again.
Reset is labelled **"Change workspace"**, never "Sign out".

## 19. Angular API services

`TenantApi` (create, get) and `AiAgentApi` (list, get, create, rename, update
instructions, activate, disable, preview). Typed interfaces. **No component calls
`HttpClient` directly**, and no client is generated.

## 20. Local `/api` proxy strategy

All paths are relative (`/api/...`). `proxy.conf.json` maps `/api` to
`http://localhost:5027` for the dev server only, wired through `angular.json`.
Production remains same-origin behind a reverse proxy. **No CORS change was
made.** No absolute URL exists anywhere in executable frontend source — the only
`localhost:5027` in the codebase is inside a comment explaining why hosts are not
hard-coded.

## 21. AI agent list page

Header, description, and a *Create agent* action. Name, status and last-updated
per row; the whole row is one link, so a keyboard reader gets one stop per
destination. **No instructions and no identifiers as visible content** — the id
appears only in the link target.

## 22. List responsive behaviour

One markup, two layouts: an aligned grid with column headers from Bootstrap's
`md`, and stacked cards below it. Not a horizontally scrolling table on a phone.
These rows are navigational rather than tabular data somebody compares column by
column, which is why they are a list of links rather than a `<table>`.

## 23. Empty, loading, error and pagination states

- **Loading** — skeleton rows shaped like the real ones, with `aria-busy` and a
  hidden "Loading agents…" status so the wait is perceivable to a screen reader.
- **Empty** — explains what an AI agent *is* before offering *Create your first
  agent*. Never "No data".
- **Error** — a message with a *Try again* that genuinely refetches, and no
  server internals (a test plants `Npgsql.PostgresException` and asserts it never
  reaches the DOM).
- **Pagination** — "Showing 1–20 of 34", Previous/Next disabled at the ends, and
  no pager at all when everything fits on one page.

## 24. Create agent experience

Two fields, and only two: name and instructions. A test asserts the form offers
no other control and that the request body has exactly those two keys — the id,
tenant, status, timestamps, provider and model are all the backend's.

A live character count against the platform maximum (20,000), described as a
platform limit and **never as a token or context limit** (asserted by test).
Double submission is prevented. A 400 is reported in the backend's own words with
the reader's work left intact for correction.

## 25. Create success

The created representation **and its entity tag** are handed to the editor through
navigation state, so the editor opens ready to save without re-reading what it was
just given. If the handoff is absent for any reason the editor simply reads the
agent itself, so this is an optimisation rather than something correctness
depends on. A toast confirms creation.

## 26. Agent detail / editor architecture

Four separated concerns, not one giant card: **General** (name), **Instructions**,
**Status** (lifecycle), and **Preview**. Single column below 1200px; two columns
above it with the editor given the larger share. The header shows the name, the
status badge and a route back — no internal ids, no provider configuration.

## 27. Name editing

Focused section with its own save. **No request is sent when the value is
unchanged** — the button is disabled, because a save that rewrites the same value
would still move `updatedAt` and advance the version, making somebody else's open
editor stale for nothing. Sends `If-Match`; adopts the new tag from the response.

## 28. Instructions editing

Multiline, monospace-height textarea preserving line breaks, a live counter
against the 20,000 limit, dirty-state indication, explicit save, disabled/loading
state, and a discard. **No silent truncation.** Sends `If-Match`; adopts the new
tag. Nothing is converted to any provider-specific format.

## 29. Dirty-state handling

Derived, not tracked: a field is dirty when it differs from what the server holds.
Unsaved edits are shown and announced. A background reconcile after a 409 updates
only fields the reader is **not** editing. Reloading after a conflict warns before
replacing work. No form-state framework was added.

## 30. Lifecycle UX

Draft → *Activate*. Active → *Disable* (confirmed). Disabled → *Activate*.
**Impossible transitions are never offered** — a draft gets no Disable button,
because an action guaranteed to be refused invites a click and teaches the reader
that the product's buttons do not work.

Disable confirmation uses a native `<dialog>`: real focus trap, Escape, inert
backdrop and focus return, all from the platform, with no UI framework added.
Activation is a direct action — confirming everything is how confirmation stops
meaning anything.

## 31. `ETag` handling

Captured from the `ETag` response header on load, create and every mutation; held
in a signal; **replaced with the new tag after every success**. Never shown,
parsed, compared, unquoted or constructed. `AiAgentApi` throws if a response
carries no `ETag`, rather than returning an empty string that would later be
rejected as a confusing 412.

## 32. `If-Match` behaviour

Every mutation sends it — rename, instructions, activate, disable. The API
method signatures will not compile without a tag. Preview sends none.

## 33. 412 conflict UX

The centrepiece. On a rejected save:

1. The save is refused.
2. **The reader's unsaved text stays exactly where it is.**
3. A panel says the agent was updated somewhere else, confirms nothing was
   overwritten, and offers *Reload latest version*. The status code is never
   shown.
4. **Nothing is retried, merged or overwritten automatically.**
5. The local tag is dropped, so no further mutation can be attempted with a
   version known to be stale.
6. Reload warns before discarding work, performs a fresh `GET`, and adopts the
   new tag.

Covered by eight editor tests **and** verified with two real browser tabs (§39).

## 34. 428 defensive behaviour

The frontend never sends an unconditional mutation — a missing tag disables the
save controls. If a 428 arrived anyway it is handled exactly as 412 rather than as
a routine error inviting a retry. Tested.

## 35. 409 domain conflict UX

Handled separately from stale concurrency: reported in the backend's own words
beside the status card, never as an entity-tag conflict, and followed by a quiet
re-read to reconcile the displayed status without disturbing in-progress edits.

## 36. Preview UX

Single-turn, because the endpoint is stateless. The transcript is this tab's own
record — never sent back as context, never persisted, and the panel says so.
Works for Draft, Active and Disabled. Sends no `If-Match` and consumes no entity
tag, so it cannot disturb the editor's version. If the instructions on screen are
unsaved, the panel says preview uses the saved ones.

## 37. 502 / 503 / 504 behaviour

| Status | What the reader sees |
| --- | --- |
| 502 | "The AI provider returned a response this platform could not use." |
| 503 | "AI preview is currently unavailable because no provider is configured." |
| 504 | "The AI provider took too long to respond. Try again." |

**Nothing is retried automatically** — a timeout may mean the provider is still
working and still being billed. The message is preserved so the reader can retry
without retyping. No provider name, status code or upstream body reaches the
screen; tests plant `OpenAI returned an empty completion` and
`OpenAI key rejected for org-1234` and assert neither appears.

## 38. OpenAI key in the frontend / direct browser calls

**NO** and **NO.**

No OpenAI SDK, no direct provider request, no key in any Angular source file,
environment file, bundle, storage or request. The browser calls this platform's
backend and nothing else. Phase 11 was built and verified **with no provider
configured at all**, and no live OpenAI call was made in this phase. The only
occurrences of the string "OpenAI" in the frontend are two tests asserting the
name is *not* shown and one comment stating that the character limit is not an
OpenAI token budget.

## 39. Browser workflow performed

Chrome, against a **local** backend (`localhost:5027`) and a **local** Angular dev
server (`localhost:4310`). No VPS. The backend ran with **no `OPENAI_API_KEY`**,
against a dedicated local `omniai_dev` database whose connection string was
supplied only through the shell environment and appears in no file.

Performed end to end:

1. Opened the app with no workspace → redirected to setup.
2. Created a real workspace ("Acme Trading"); a real UUIDv7 tenant id was stored
   and the shell showed the real name.
3. AI Agents → genuine empty state.
4. Created "Sales Assistant" → navigated to the editor, status **Draft**, toast.
5. Renamed to "Sales & Support Assistant" → toast, heading updated.
6. Edited instructions → dirty state shown, saved, dirty cleared.
7. **Activate** → Active; the Activate control was replaced by Disable.
8. **Disable** → confirmation dialog opened modally with focus inside it and
   nothing sent until accepted → Disabled.
9. **Activate again** → Active.
10. **Preview** → genuine **503** UX: "AI preview is currently unavailable because
    no provider is configured", the message preserved, no transcript fabricated,
    and the string "openai" nowhere on the page. **No fake provider was injected.**
11. Returned to the list → canonical server state: the updated name, Active, the
    real timestamp, and no instructions in the row.
12. **Change workspace** → the local id was cleared and the setup screen returned.

## 40. Browser network verification

Exactly **8 XHR requests** for the whole workflow — one per action. No duplicates,
no polling, no request loops, no external origin.

Confirmed on the wire:

- `GET` agent → response carries `etag`.
- Rename → request `if-match: "AABIqQ"`, response `etag: "AABIqg"` — a
  **different** tag, and the client continued with the new one.
- Final activate → request `if-match: "AABIrQ"`, response `etag: "AABIrg"`.
- Instructions, activate and disable each carried `If-Match`.
- **Preview carried no `If-Match` and returned no `ETag`.**
- No `Authorization` header, no API key, and no request to any origin other than
  the local app.
- Tags are opaque and short; no raw `xmin` value is exposed.

## 41. Browser concurrency (412) test result

**Performed with two real tabs against the real backend.**

Tab A and Tab B both opened the same agent. Tab B renamed it, advancing the server
version. Tab A then edited its instructions to a distinctive string and saved.

Observed:

- **412** on the wire (one request, `reqid 131`).
- **Nothing was sent after it** — no automatic retry.
- Tab A's unsaved text remained on screen, byte for byte.
- The conflict panel appeared: "This agent was updated somewhere else… Your
  unsaved changes have not been overwritten and are still shown below."
- The raw status code was not displayed.
- Further saving was blocked.
- *Reload latest version* warned first, then performed a fresh `GET`, adopted the
  new tag, and a subsequent save succeeded.

## 42. Responsive verification

| Width | Layout | Horizontal overflow | Overflowing elements |
| --- | --- | --- | --- |
| 390 | stacked cards; editor single column | none | 0 |
| 768 | stacked/grid; editor single column | none | 0 |
| 1024 | grid list; editor single column | none | 0 |
| 1440 | grid list; editor two columns | none | 0 |
| 1920 | two columns, page capped at 1600px | none | 0 |

Light mode inspected at 390 and 1440; dark mode at 390, 768 and 1440, plus 1920.
Empty state, populated list, editor, confirmation dialog, conflict panel and
preview panel were all inspected visually.

**A real defect was found here and fixed:** in the stacked mobile row the label
and timestamp rendered as "UpdatedSep 5, 2026" with no space, because Angular
strips whitespace between elements. Fixed with a real `&nbsp;` in the markup
rather than a CSS margin — a purely visual gap would still have been announced as
"UpdatedSep" by a screen reader.

## 43. Browser console and network result

**Zero console errors and zero warnings** across the entire session, including
preserved messages across navigations.

Network: 8 requests, one per action. The only non-2xx responses were the two
deliberately exercised states — the 503 preview and the 412 conflict. No
unexpected 4xx or 5xx, no duplicate calls, no loops, no polling, and no external
AI call.

## 44. Accessibility verification

The Phase 10 baseline is preserved (landmarks, skip link, `aria-current`,
`aria-expanded`, `inert`, focus rings, reduced motion). New screens add:

- Labelled form controls with `aria-describedby` hints and
  `aria-errormessage` / `aria-invalid` on failure.
- Errors in `role="alert"`, the toast host as a polite `role="status"` live
  region that exists before anything is announced into it.
- `aria-busy` plus a hidden status for the loading list.
- Native `<dialog>` for confirmation — focus trap, Escape, focus return, and an
  accessible name from `aria-labelledby` pointing at the visible heading.
- Status conveyed by badge text and a dot, never colour alone.
- Row links carrying their whole row, so one destination is one tab stop.
- The mobile timestamp fix above, which was an announced-text problem.

## 45. Backend tests before / after

Phase 9 reported **908**. This phase measures **958**, all passing in Release.

| Suite | Now (measured) |
| --- | --- |
| Domain | 288 |
| Application | 216 |
| Infrastructure | 233 |
| Api | 221 |
| **Total** | **958** |

New this phase: `ListAiAgentsHandlerTests` (Application),
`AiAgentListTests` (Infrastructure, real PostgreSQL, including SQL inspection
proving `instructions` is never selected), `AiAgentListApiTests` (Api), plus the
split routing guard. Domain is untouched, and no existing test was weakened —
the one pre-existing test that changed is the routing guard described in §6.

## 46. Frontend tests before / after

**49 → 170** (+121), all passing, 13 files.

| File | Tests |
| --- | --- |
| `app-shell.spec.ts` (Phase 10, +1 this phase) | 25 |
| `api-error.spec.ts` | 20 |
| `agent-editor.spec.ts` | 19 |
| `ai-agent-api.spec.ts` | 18 |
| `agent-preview.spec.ts` | 16 |
| `agent-list.spec.ts` | 15 |
| `theme-service.spec.ts` (Phase 10) | 14 |
| `workspace-setup.spec.ts` | 13 |
| `workspace-context-service.spec.ts` | 11 |
| `agent-create.spec.ts` | 8 |
| `theme-toggle.spec.ts` (Phase 10) | 6 |
| `theme-startup.spec.ts` (Phase 10) | 3 |
| `app.spec.ts` (Phase 10) | 2 |
| **Total** | **170** |

Counts include parameterised cases generated by loops, which is why they exceed
the number of literal `it(` calls in the files.

## 47. Builds

Backend Release: **clean, zero warnings, zero errors.** Frontend production
build: **clean.**

## 48. Backend vulnerability scan

**8/8 projects: no vulnerable packages.**

## 49. npm audit

**0 vulnerabilities.** The registry endpoint returned a transient error on two
attempts and succeeded on retry; the result above is the successful run. `npm
audit fix` was not run.

## 50. Formatting and lint

Prettier: **clean.**

Lint: **not run — there is still no lint script and no ESLint configuration in
this project**, exactly as reported in Phase 10. None was added; introducing a
linter is a separate decision with its own baseline. Strict TypeScript with
`strictTemplates` passes with zero warnings, and no diagnostic was suppressed.

## 51. Bundle before / after

| Chunk | Phase 10 | Phase 11 |
| --- | --- | --- |
| main | 311.20 kB / 83.90 kB | — |
| **Initial total (raw / transfer)** | **322.80 kB / 75.23 kB** | **376.83 kB / 89.40 kB** |

**Delta: +54.03 kB raw, +14.17 kB transferred.**

Explained: `HttpClient` (with `withFetch`), `@angular/forms` reactive forms, the
workspace context and guard, the error model, and the toast host — all needed
before any feature screen renders, so all in `main`. The styles bundle is
unchanged at 5.45 kB.

The AI agent feature is lazily loaded and contributes nothing to the initial
download: `agent-editor` 22.97 kB / 5.81 kB, `agent-list` 7.29 kB / 2.50 kB,
`agent-create` 5.90 kB / 2.09 kB, `workspace-setup` 7.92 kB / 2.63 kB. No chart
library, animation framework, UI framework or state library was added.

## 52. Packages added

**NONE.** `frontend/package.json` and `backend/Directory.Packages.props` are both
unmodified. Everything in this phase was built on what Phases 1–10 already
installed.

## 53. Files created / modified

**47 created, 19 modified — 66 total.** Tracked-file diff: 19 files, 459
insertions, 23 deletions (the 47 new files are untracked and not counted there).

Backend created: 5 `ListAiAgents` files, `AiAgentPageResponse.cs`, 3 test files.
Backend modified: `AiAgentEndpoints`, `CompositionRoot`,
`DomainProblemDetailsHandler`, `IAiAgentRepository`, `AiAgentRepository`,
`InMemoryAiAgentRepository`, `AiAgentEndpointsTests`.

Frontend created: `proxy.conf.json`; `core/api` (3), `core/workspace` (3),
`core/feedback` (5); `features/workspace-setup` (4); `features/ai-agents` (17).
Frontend modified: `angular.json`, `app.config.ts`, `app.routes.ts`,
`navigation.ts`, `overview.html`, and the shell and sidebar (6 files).

Docs created: `admin-ai-agent-experience.md` and this report. Docs modified:
`architecture/README.md`.

## 54. Architectural decisions

Recorded as D1–D16 in
[docs/architecture/admin-ai-agent-experience.md](../architecture/admin-ai-agent-experience.md).
In brief: the workspace context is explicitly not authentication; 404 and
unreachable are different and only one clears state; the list publishes no
instructions and no entity tag; paging is refused rather than clamped; ordering
is total; the API boundary is same-origin and interceptor-free; the error model
is one function and a closed union; entity tags are opaque client state; 412
preserves the reader's work and never retries; 409 is a different failure with a
different UI; preview is single-turn and tag-free; no provider secret reaches the
browser.

## 55. Security / development-only warning

**This application and the API behind it are NOT authorised for public
deployment.**

There is no authentication, no authorisation and no verified principal. The tenant
id is chosen by the browser and believed by the backend. Tenant scoping is a
correctness boundary enforced into the SQL — it stops one tenant's data being
reached through another tenant's route — but nothing stops a caller naming
whichever tenant it likes. The unauthenticated preview endpoint can consume paid
provider credit when a key is configured.

No fake authentication, JWT, API key or admin credential was introduced, because
a placeholder control would hide the gap rather than close it.

## 56. Deferred concerns

Authentication; authorization and permissions; a real current-tenant principal;
tenant membership and switching; user profile; server-stored preferences; agent
search, filtering and sorting; bulk operations; instruction version history;
collaborative editing and merge; agent deletion or archive; a composite list
index; conversations, RAG, tools and channels; dashboard aggregation; ESLint;
production deployment.

## 57. Deviations

1. **Lint not run** — no script and no config exist; none was added (§50).
2. **`npm audit` needed retries** — a transient registry error, not a
   vulnerability (§49).
3. **A Phase 7 routing guard was modified** — it asserted the list endpoint did
   not exist, which Phase 11 changed by authorisation. Narrowed rather than
   deleted (§6).
4. **A Phase 10 shell test was updated** — it used `/ai-agents` as a placeholder
   example; that area is now real, so the test moved to `/knowledge` and a new
   test asserts the real feature renders.
5. **Overview panel badge made conditional** — it hard-coded "Not implemented",
   which became false for AI Agents. Still no metrics of any kind.
6. **A local `omniai_dev` database was created** for browser verification. Its
   connection string was supplied only through the shell environment and appears
   in no file; `appsettings.json` still carries an empty connection string.

## 58. Known issues

None outstanding. Three real defects were found and fixed during this phase:

1. **The preview form never submitted.** `<form (ngSubmit)>` had no `formGroup`,
   so `ngSubmit` was bound as a DOM listener for an event that never fires —
   clicking *Send* would have done nothing and triggered a native page reload.
   Found by a test that clicked Send and saw no request. Fixed by giving the
   control a `FormGroup`.
2. **The editor read a required input during construction.** `aiAgentId` is set
   by Angular *after* construction, so the initial load would have thrown. Moved
   to `ngOnInit`.
3. **The mobile list row announced "UpdatedSep 5".** Fixed with a real space in
   the markup, not a CSS margin (§42).

## 59. Blockers

**None.**

## 60. `git status --short`

```
 M backend/src/OmniAI.Api/AiAgents/AiAgentEndpoints.cs
 M backend/src/OmniAI.Api/Composition/CompositionRoot.cs
 M backend/src/OmniAI.Api/ErrorHandling/DomainProblemDetailsHandler.cs
 M backend/src/OmniAI.Application/AiAgents/Persistence/IAiAgentRepository.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Repositories/AiAgentRepository.cs
 M backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentEndpointsTests.cs
 M backend/tests/OmniAI.Application.Tests/AiAgents/InMemoryAiAgentRepository.cs
 M docs/architecture/README.md
 M frontend/angular.json
 M frontend/src/app/app.config.ts
 M frontend/src/app/app.routes.ts
 M frontend/src/app/features/overview/overview.html
 M frontend/src/app/layout/app-shell/app-shell.html
 M frontend/src/app/layout/app-shell/app-shell.spec.ts
 M frontend/src/app/layout/app-shell/app-shell.ts
 M frontend/src/app/layout/navigation.ts
 M frontend/src/app/layout/sidebar/sidebar.html
 M frontend/src/app/layout/sidebar/sidebar.scss
 M frontend/src/app/layout/sidebar/sidebar.ts
?? backend/src/OmniAI.Api/AiAgents/AiAgentPageResponse.cs
?? backend/src/OmniAI.Application/AiAgents/ListAiAgents/
?? backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentListApiTests.cs
?? backend/tests/OmniAI.Application.Tests/AiAgents/ListAiAgentsHandlerTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/AiAgentListTests.cs
?? docs/architecture/admin-ai-agent-experience.md
?? docs/phase-reports/PHASE_11_TENANT_AI_AGENT_ADMIN_REPORT.md
?? frontend/proxy.conf.json
?? frontend/src/app/core/api/
?? frontend/src/app/core/feedback/
?? frontend/src/app/core/workspace/
?? frontend/src/app/features/ai-agents/
?? frontend/src/app/features/workspace-setup/
```

`git diff --check` reports no whitespace errors. Nothing is staged, `HEAD` is
still `186d720`, there are no remotes, and no commit was created.

## 61. `git diff --stat`

```
 19 files changed, 459 insertions(+), 23 deletions(-)
```

(Tracked files only; the 46 new files are untracked.)

## 62. Secret review

**Clean.** Every created and modified file was scanned for `sk-`-shaped strings,
`OPENAI_API_KEY`, provider credentials, passwords, bearer tokens, private keys,
SSH credentials, the production server address, database passwords and connection
strings. **Zero matches.**

No `dist`, `node_modules`, `bin`, `obj`, `.angular`, screenshots, image files or
`.env` files appear. No screenshot was committed. `appsettings.json` still carries
an empty connection string; the local dev connection string existed only as a
shell environment variable.

## 63. VPS not accessed

**Confirmed.** No SSH connection was attempted or made. No deployment, no Nginx
configuration, no remote command. All verification ran against `localhost` only.

## 64. No deployment occurred

**Confirmed.**

## 65. Readiness recommendation for Phase 12

**Ready — and authentication should be Phase 12.**

Phase 11 delivers a real, working admin product for AI agents, and in doing so it
has made the missing security layer the single most consequential gap in the
platform. Everything else now exists: a shell, a design system, a real API, real
concurrency handling, and a real workflow. What does not exist is any notion of
who is using it.

The shape of that work is already visible: `WorkspaceContextService` is the seam
where a verified principal replaces a browser-chosen tenant id, and the route
guard is where "does this tenant exist" becomes "may this person see it". Neither
should be extended in place — the development context should be deleted, not
promoted.

Until then the recommendation is unchanged and firm: **do not deploy this
publicly.**
