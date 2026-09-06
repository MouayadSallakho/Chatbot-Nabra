# Phase 14 — Team members, secure invitations and role assignment

## 1. Phase objective

Give a workspace a way to admit people and administer what they may do once they
are in it: member listing and detail, secure invitations shared manually, role
assignment bounded by the Phase 13 authority ceiling, and the frontend for all of
it — without email delivery, ownership transfer, member removal, or deployment.

---

## 2. Final status

**IMPLEMENTED, DOCUMENTED AND VERIFIED.**

Everything specified is written, builds clean, is covered by tests, and was
exercised in a real browser against a containerised API and an isolated
PostgreSQL 17 database.

| Gate | Result |
| --- | --- |
| Backend tests | **1545 passed, 0 failed, 0 skipped** |
| Frontend tests | **305 passed, 0 failed** (25 files) |
| Release build, 8 projects | 0 warnings, 0 errors |
| Frontend production build | succeeded — 384.22 kB initial, 93.69 kB transfer |
| `prettier --check .` | clean |
| `npm audit` | 0 vulnerabilities (with and without dev dependencies) |
| `dotnet list package --vulnerable --include-transitive` | none, all 8 projects |
| Browser workflow | performed in Chrome, all specified flows |

Three real defects were found — two of them only by driving the browser — and all
three were fixed and covered by regression tests. They are in §17.

Unlike Phase 13, no verification was blocked: the containerised environment
established at the end of that phase was reused, so Smart App Control never
entered the picture.

---

## 3. Baseline commit

`374ac173cfd3341ceac234b84acd3ec073121bb6` — *feat: implement tenant roles and
permissions authorization*.

Nothing was reset, cleaned, restored, stashed or discarded at any point.

---

## 4. Scope honoured

**Built:** member listing and detail, invitation creation with a manually
shareable link, revocation, acceptance by the intended account, membership role
assignment, the Team frontend, the acceptance frontend, and complete
authority-ceiling enforcement.

**Deliberately not built,** as instructed: email or SMTP delivery, ownership
transfer, Owner assignment, membership deletion, self-service leaving, user
profile management, MFA, password recovery, production deployment.

**No new packages** were added to either side. No VPS was accessed, no deployment
performed, no Nginx touched, no OpenAI key configured and no paid API call made.
No production testing backdoor exists; the verification used ordinary
registration and sign-in through the real UI.

---

## 5. Permissions added

Three, and only three:

| Code | Grants |
| --- | --- |
| `members.view` | See who belongs to the workspace and which roles they hold |
| `members.invite` | Create and revoke invitations |
| `members.roles.manage` | Decide which roles a member holds |

`members.owner`, `members.transfer`, `members.delete` and `members.impersonate`
were **not** added, because no operation behind them exists. The reasoning is
T1 in [team-members-invitations.md](../architecture/team-members-invitations.md).

The Owner receives all three intrinsically. **The migration writes no permission
rows at all** — verified by reading it: it contains no `InsertData`, and its one
raw `Sql` call is the `updated_at` backfill in §16. `An_owner_stores_no_permission_rows`
covers the domain rule behind that.

---

## 6. Domain types introduced

| Type | Kind | Role |
| --- | --- | --- |
| `EmailAddress` | readonly record struct | An address, with canonical equality |
| `TenantInvitationId` | readonly record struct | UUIDv7 identity |
| `TenantInvitationStatus` | enum | `Pending` / `Accepted` / `Revoked` / `Expired` |
| `InvitationTokenHash` | readonly record struct | 32-byte digest, constant-time compare |
| `InvitationRoleGrant` | entity | One role on one invitation |
| `TenantInvitation` | aggregate root | `Issue`, `IsUsableAt`, `Accept`, `Revoke`, `Expire` |

`TenantMembership` gained `UpdatedAt` and `MarkRolesChanged`, a high-water mark
that never moves backwards.

---

## 7. The invitation token

- `RandomNumberGenerator.GetBytes(32)` — 256 bits from the CSPRNG.
- Rendered unpadded Base64Url, 43 characters. **Confirmed 43 in the browser.**
- The database stores **only** the SHA-256 digest, as `bytea`.
- Comparison is `CryptographicOperations.FixedTimeEquals`.
- `InvitationTokenHash.ToString()` returns its own type name, so a digest cannot
  reach a log line through string interpolation.

The raw token is returned by exactly one endpoint, in exactly one contract shape,
with `Cache-Control: no-store`. No read path can return it — there is no other
interface in the API or the frontend client with a field that could hold one.

---

## 8. Link transport

The link is `…/accept-invitation#token=…` — a **fragment**, never a query string.
A fragment is not sent to the server, appears in no request line or access log,
and is not forwarded in `Referer`.

**Verified in Chrome:** the rendered link contained `#token=`, contained no
`?token=`, and the token was absent from `localStorage`, `sessionStorage` and
`document.cookie` on the inviting page.

---

## 9. Token handling in the browser

Held in a root-provided service for the life of the tab. Never written to
`localStorage`, `sessionStorage`, a cookie, or the authentication state — so an
invitee can register or sign in and have acceptance resume by itself.

**Verified:** after opening a link, `localStorageKeys` was `[]` and
`sessionStorageKeys` was `[]` in the invitee's context, the address bar read
`http://localhost:4400/accept-invitation` with `hash: ""`, and acceptance still
completed after registration.

The only JS-readable cookie on any page was `XSRF-TOKEN`; the session cookie is
`HttpOnly` and never appeared.

---

## 10. Endpoints

| Method | Route | Permission |
| --- | --- | --- |
| `GET` | `/api/tenants/{tenantId}/members` | `members.view` |
| `GET` | `/api/tenants/{tenantId}/members/assignable-roles` | `members.roles.manage` |
| `GET` | `/api/tenants/{tenantId}/members/{userId}` | `members.view` |
| `PUT` | `/api/tenants/{tenantId}/members/{userId}/roles` | `members.roles.manage` |
| `GET` | `/api/tenants/{tenantId}/invitations` | `members.view` |
| `POST` | `/api/tenants/{tenantId}/invitations` | `members.invite` |
| `POST` | `/api/tenants/{tenantId}/invitations/{invitationId}/revoke` | `members.invite` |
| `POST` | `/api/invitations/accept` | authenticated only |

Acceptance is deliberately **not** workspace-scoped: the person accepting does
not belong to a workspace yet. The token says which invitation; the session says
who is accepting.

---

## 11. Concurrency contract on member access

`ETag` from the membership row's `xmin`, `If-Match` required, `428` without it,
`412` on mismatch. `xmin` is a **shadow** property — `The_membership_table_has_no_physical_xmin_column`
queries `information_schema` to assert `tenant_memberships` has no physical column
of that name, which would silently disable the check, and
`The_migration_script_never_creates_an_xmin_column` guards the generated script.

**Proven end to end in Chrome**, from the network log:

```
GET  …/members/{userId}         → 200, etag: "AAAdbA"
PUT  …/members/{userId}/roles   → if-match: "AAAdbA", x-xsrf-token: …
                                → 200, etag: "AAAdbg"
```

`accessUpdatedAt` advanced from `2026-09-06T20:21:04` to `2026-09-06T20:26:21`;
`joinedAt` was unchanged.

---

## 12. Authority ceiling on assignment

The Phase 13 contract required assignment to reuse the D22 ceiling. It does, and
on **both** sides:

```
REPLACE MEMBER ROLES requires  CurrentRoles ⊆ ActorCeiling
                          and  DesiredRoles ⊆ ActorCeiling
```

The current-set half is what stops a delegated administrator stripping authority
from somebody who outranks them — the contract's second obligation. The ceiling
is re-read from the database on every decision and never passed in or cached,
which is the third.

---

## 13. Protected operations

| Attempt | Answer |
| --- | --- |
| Assign or remove the Owner role | `409` — *Membership protected* |
| Change your own roles | `409` — *Membership protected* |
| Assign a role beyond your ceiling | `403`, naming the first permission beyond it |
| Unrecognised permission code | `400` |

`409` rather than `403` for the first two is deliberate: `403` would imply
somebody else is allowed, and for ownership transfer in this release, nobody is.

---

## 14. Invitation lifecycle

Seven-day lifetime. Expiry is **lazy**: usability is always computed from
`ExpiresAt`, and the stored status is stamped `Expired` when something touches
the invitation — including, in the same transaction, when a replacement is issued
for the same address.

The partial unique index reads the stored status only
(`HasFilter("status = 'Pending'")`) because PostgreSQL rejects a volatile
`now()` in an index predicate. **Verified in the browser:** a fresh invitation
showed an expiry of Sep 13 for a Sep 6 creation.

Acceptance consumes the invitation with a conditional update whose predicate
includes `status = 'Pending' AND expires_at > now()`, so the database decides
which of two concurrent attempts wins. Membership, role assignments and the
status change commit together.

---

## 15. Acceptance-time revalidation

An invitation is a stored **intent**, not a stored authorisation. Acceptance
checks, in this order:

1. the token matches a stored hash,
2. the invitation is still `Pending`,
3. it has not passed its expiry,
4. the accepting account's address matches the invited address,
5. the account is not already a member,
6. the inviter is still a member,
7. the inviter still holds `members.invite` and `members.roles.manage`,
8. every role still exists, is not Owner, and still sits inside the inviter's
   **current** ceiling.

Usability (2–3) is checked **before** identity (4) so a withdrawn or expired link
answers `410` regardless of who holds it — otherwise a forwarded link would
become an oracle telling the holder that a live invitation belongs to somebody
else. No response ever names the invited address.

---

## 16. Migration

`20260906171126_AddTenantInvitationsAndMemberAccessVersioning` — one migration,
as required.

Two hand-corrections were made to the scaffolder's output, both verified by
reading the file and by schema tests:

- **The scaffolder emitted a physical `xmin` column** on `tenant_memberships`.
  Left in place it would have shadowed PostgreSQL's system column and silently
  broken every concurrency check. Removed, matching all three prior migrations.
- **`updated_at` would have defaulted to `0001-01-01`** and the generated
  `AlterColumn` emitted no `NOT NULL`. Replaced with nullable → backfill from
  `created_at` → explicit `SET NOT NULL`. Verified against seeded Phase 13-era
  data: a membership created 2026-03-01 kept that instant.

Applied to a clean database and to a seeded one; the schema was read back both
times.

---

## 17. Real defects found, and how

Three. Two were invisible to the test suite and surfaced only by driving Chrome —
which is the argument for the browser requirement.

### 17.1 A second invitation opened in the same tab was silently ignored

Opening a link in a tab that had already handled one changes **only the
fragment**. That is a same-document navigation: the component is never destroyed,
`ngOnInit` never runs again, and a token read once at construction was never
read. The previous outcome stayed on screen — so somebody pasting a second
invitation was told *"You are in Acme Trading"* while their new invitation was
discarded and no request was made. Confirmed from the network log: no
`POST /api/invitations/accept` was issued.

**Fixed** by listening to the browser's `hashchange`. My first attempt used the
router's parsed fragment and was **wrong**: `history.replaceState` is invisible to
the router, so its remembered fragment still held the spent token and reopening
the *same* link compared equal and did nothing. Verified in the browser, then
pinned by two tests.

A second, related fault appeared once the first was fixed: the router writes the
URL it parsed when a navigation completes, putting the token **back** into the
address bar after the strip. Rather than racing two writers, removal is now a
standing correction — whenever a navigation settles, a token must not be in the
address bar. Verified: the address bar read `/accept-invitation` with `hash: ""`.

All three regression tests were confirmed to **fail without the fix** and pass
with it.

### 17.2 The wrong-account refusal gave the wrong advice

The server writes an actionable sentence — *"This invitation was sent to a
different account. Sign in with the invited address to accept it."* — but the
screen showed the platform's shared `403` copy: *"Ask somebody who administers
this workspace."* That is wrong here; the remedy is the reader's own.

**Fixed at the screen, not by widening the global `403` policy** — 403 is
excluded from the detail-passthrough set deliberately, and that reasoning still
holds for permission denials. The acceptance screen now supplies its own
sentence, still without naming the address. Verified in Chrome.

### 17.3 Unpadded cards and a full-width form

Content sat flush against card borders on the Team screens: the design system's
`.card` is a surface with no padding, and padding comes from `.card-body` or from
a component that brings its own. **This is pre-existing** — committed Phase 13
screens have it too (`.card.role-form` computes `padding: 0px`) — so I fixed it
on the screens Phase 14 delivers rather than changing the design system or
touching committed work. Plain wrappers now use `.card-body`; styled containers
carry their own padding, as `.role-options` already did.

The invite form also ran the full width of a 1600px layout. It now uses the
`page-narrow` (46rem) convention that `agent-create` established, with the same
documented reasoning.

---

## 18. Verification environment

Windows Chrome → Angular dev server on `:4400` → `/api` proxy → containerised
.NET API on `127.0.0.1:5027` → **isolated** PostgreSQL 17 (`omniai-pg`, database
`omniai_p14_browser`, network `omniai-verify`).

No production database, no production credential, no existing business database,
and no EF InMemory substitution — the integration tests refuse to fall back to a
fake provider, which was confirmed when a wrong environment variable produced 629
hard failures rather than silent passes.

---

## 19. Actual test totals

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `OmniAI.Domain.Tests` | 429 | 0 | 0 |
| `OmniAI.Application.Tests` | 372 | 0 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 303 | 0 | 0 |
| `OmniAI.Api.IntegrationTests` | 441 | 0 | 0 |
| **Backend total** | **1545** | **0** | **0** |
| Frontend (Vitest, 25 files) | 305 | 0 | — |

Phase 14 contributed 133 backend tests and 34 frontend specs:

| File | Tests |
| --- | ---: |
| `EmailAddressTests` | 11 |
| `TenantInvitationTests` | 20 |
| `MemberRoleAssignmentTests` | 17 |
| `InvitationSecurityTests` | 25 |
| `TenantInvitationSchemaTests` | 12 |
| `TeamApiTests` | 19 |
| `InvitationApiTests` | 29 |
| `team-page.spec` | 7 |
| `member-detail.spec` | 10 |
| `invite-member.spec` | 7 |
| `accept-invitation.spec` | 10 |

---

## 20. Browser workflow performed

Chrome, four isolated browser contexts (owner, Bob, Dave, and a second owner tab).

**Owner:** registered, created *Acme Trading*, saw the Team entry appear in the
navigation and the Owner badge on the Team page, created a *Support* role — which
confirmed the three new permissions render in the catalogue — and invited Bob.

**Invitee:** opened the link in a clean context, saw *"You have been invited"*,
registered as `bob@example.test`, and acceptance **resumed automatically** →
*"You are in / Acme Trading"*. His access read
`{permissions: ["ai-agents.view"], rolesStatus: 403, membersStatus: 403, agentsStatus: 200}`.

**Role removal:** the Owner unticked Support, saw *"You have unsaved changes."*,
saved, and the full `ETag` → `If-Match` → new `ETag` exchange in §11 was captured
from the network log. Bob's session then read `permissions: []` and AI agents
`403` — revocation propagates without re-authentication.

**Wrong account:** an invitation addressed to `carol@example.test`, opened while
signed in as Bob → refused. Membership count stayed at 2, Bob still held no
roles, and the invitation remained `Pending`.

**Revoked:** the Owner revoked it (with a confirmation dialog naming the
address), it left the pending list, and opening the link answered *"This
invitation was withdrawn. Ask whoever invited you for a new one."* — the
withdrawn answer, not the wrong-account one, confirming the §15 ordering.

**Delegated Team Manager:** Dave, holding only `members.view`, `members.invite`,
`members.roles.manage` and `roles.view`, was invited and accepted.

- The invite form and the assignable-roles endpoint offered **only** *Team
  Manager* — not *Support*, not *Senior support* — while `GET /roles` still
  returned `200`. Visibility and delegability are separate.
- Bob, holding *Senior support*, was **read-only** to Dave, with zero checkboxes,
  no save button, and the explanation *"Beyond your access: Senior support"*.
- The Owner was read-only — *"Owner access is protected"*.
- Dave's own row was read-only — *"This is your own access"*.
- Direct requests, bypassing the UI:

| Attempt | Result |
| --- | --- |
| Assign *Senior support* (beyond ceiling) | `403` — *requires the 'ai-agents.view' permission* |
| Assign *Team Manager* (within ceiling) | `200` |
| Change his own roles | `409` — *Membership protected* |
| Change the Owner's roles | `409` — *Membership protected* |

**Member concurrency, two tabs:** both read version *V1*. Tab B added a role and
saved → *V2*. Tab A saved *V1* → **`412`**. The conflict banner appeared, **the
reader's ticked boxes were preserved** (*Senior support* and *Team Manager*), and
the network log showed **exactly one `PUT` and no retry**. *Reload latest*
replaced the screen with the server's truth and cleared the banner.

---

## 21. Responsive, theme, console and network

Viewports 390, 768, 1440 and 1920; Light and Dark exercised at 390, 768 and 1440.

- **390** — navigation collapses to a hamburger, the member table becomes stacked
  cards, `scrollWidth === innerWidth` (no horizontal overflow).
- **768** — columned table, collapsed navigation.
- **1440 / 1920** — full sidebar; the invite form capped at 46rem after §17.3.
- The invitation link wraps inside its box rather than forcing the page wide.

**Console: no messages of any kind** on the owner, invitee and delegated-manager
pages. **Network:** every response carried the expected status; the acceptance
and creation responses carried `Cache-Control: no-store`; no token appeared in
any URL, any storage, or any log.

---

## 22. Files changed

**91 files: 27 modified and 64 new** — 13,301 insertions, 55 deletions — across
Domain, Application, Infrastructure, Api, all four test projects, the frontend,
and documentation.

`git diff --check` reports **no whitespace errors** — only Git's routine
LF→CRLF advisory notices on this Windows checkout.

---

## 23. Secret review

The diff and every new file were scanned for API keys, passwords, bearer tokens,
private keys, connection strings and `sk-` patterns. **Every match is prose** —
documentation comments explaining how the invitation secret is handled. No
credential, no connection string and no key is present. No `.env`, no
`node_modules`, no `dist`, no `bin` and no `obj` appears among the new files.

No production testing backdoor was added. No development-only endpoint exists.

---

## 24. Not committed

Per the brief, **nothing was committed**. The working tree holds the complete
Phase 14 change against `374ac17`. No push, no remote, no VPS access, no
deployment.

---

## 25. Known gaps and honest caveats

- **`npm run lint` does not exist** in this project. The frontend quality gates
  are `prettier --check`, `ng build` and `ng test`; all three are clean. Earlier
  phases recorded the same.
- **An administrator's own permissions can go stale on screen** if another
  administrator changes their roles while they are working. This is Phase 13's
  D26 deferred requirement, now reachable in a new way and still unsolved. It is
  a staleness problem, not a security one: the frontend copy is UX only and every
  request is authorised server-side against freshly read state. Recorded as T19.
- **Removing a member, leaving a workspace and transferring ownership are not
  possible for anybody**, including the Owner. A workspace can accumulate members
  who cannot be removed until a later phase adds it. This is a real product gap,
  stated rather than hidden behind an unimplemented permission.
- **A lost invitation link cannot be recovered**, only revoked and reissued — a
  deliberate consequence of storing only the hash.
- Invitation status carries no audit history beyond its current value.

---

## 26. Recommendation for Phase 15

The three obligations Phase 13 left for this phase are discharged. Phase 14
leaves two of its own for whoever implements member removal:

1. **Removal must be bounded by the ceiling on the target's current roles**, for
   the same reason assignment is — removing somebody who outranks you is an
   exercise of authority you do not have.
2. **The last-administrator problem becomes reachable.** Self-change is refused
   today, which is what keeps a workspace from being locked; removal reintroduces
   the question directly, and it must be answered before the feature is built,
   not during it.

Role deletion remains unimplemented, so orphan-assignment semantics are still
undefined — and invitations now reference roles too, which widens that question.

---

**PHASE 14 COMPLETE — WAITING FOR REVIEW**
