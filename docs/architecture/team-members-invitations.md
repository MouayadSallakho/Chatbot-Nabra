# Team members and secure invitations

How somebody joins a workspace, and how their access is administered once they
are in it.

Phase 13 built roles and permissions but left no way to hand them to anybody: the
only member of a workspace was the person who created it. This document records
the model that closed that gap — member administration, invitations, and role
assignment — together with the decisions behind it and what was deliberately left
out.

```
Invitation created            "this address may join, with these roles"
        |                     token issued once, only its SHA-256 hash stored
        v
Link shared manually          fragment, never a query string
        |
        v
Acceptance                    authenticated, address must match
        |                     inviter's authority re-checked at this moment
        v
TenantMembership              the person is now in the workspace
        |
        v
TenantRoleAssignment(s)       administered from the Team screens
        |
        v
Effective permissions         unchanged from Phase 13: the union, in catalogue order
```

Phase 13 closed with a **CONTRACT FOR PHASE 14** in
[tenant-rbac.md](tenant-rbac.md). Its three obligations are discharged in T6, T7
and T8 below; the ceiling rule itself is D22 there and is not restated.

---

## T1. Three permissions, and deliberately not six

**Decision.** Phase 14 adds exactly three capabilities to the catalogue:

| Code | Grants |
| --- | --- |
| `members.view` | See who belongs to the workspace and which roles they hold |
| `members.invite` | Create and revoke invitations |
| `members.roles.manage` | Decide which roles a member holds |

**Why.** Each names an operation that exists. The obvious further codes —
`members.owner`, `members.transfer`, `members.delete`, `members.impersonate` —
name operations this release does not perform, and a permission that grants
nothing is worse than no permission at all: it appears in the catalogue, it can
be ticked into a role, and it silently implies a capability the platform will
later have to either honour or take away.

**Forbids.** Publishing a code before the operation behind it exists.

**Consequence.** Removing a member, transferring ownership and leaving a
workspace are not possible in this release for anybody, including the Owner. That
is a real product gap, recorded in DEFERRED rather than papered over with a
permission.

---

## T2. The Owner receives the three intrinsically, with no grant rows

**Decision.** The Owner role's permissions remain intrinsic — computed as the
whole catalogue rather than stored — so the three new codes reach the Owner with
no migration writing grant rows, exactly as Phase 13's D5 established.

**Why.** It is the property that makes the catalogue extensible at all. Had Owner
permissions been rows, every phase that adds a capability would need a data
migration, and any workspace missed by one would have an Owner who could not
administer their own workspace.

**Consequence.** The Phase 14 migration adds no permission rows whatsoever.

---

## T3. A member's *access* is the versioned resource, not the membership row

**Decision.** `TenantMembership` gained an `UpdatedAt` instant, stamped by
`MarkRolesChanged`, and the member read/write endpoints carry an `ETag` derived
from the membership row's `xmin`. The strong-validator contract is Phase 12's,
unchanged: `If-Match` required, `428` without it, `412` on a mismatch.

**Why.** Two administrators editing the same person's roles is the ordinary case,
not the exotic one, and the failure it produces is a silent lost update: the
second save quietly restores a role the first deliberately removed. Removal is
exactly the operation that must not be lost.

**Why the membership row and not the assignment rows.** Assignments are a set
that the write replaces wholesale. Versioning the individual rows would make the
precondition meaningless — the rows a stale caller knows about are the rows their
own write is about to delete.

**Forbids.** A physical `xmin` column. As in Phase 4, `xmin` is PostgreSQL's own
system column mapped as a shadow property; a physical column of that name would
shadow it and silently disable the concurrency check. An executable test asserts
the table has no such column.

**Consequence.** `UpdatedAt` is a high-water mark, consistent with README §11: it
never moves backwards, and `joinedAt` is untouched by a role change. The two are
shown separately in the UI, because "joined in March" and "access changed
yesterday" answer different questions.

---

## T4. `GET .../members/assignable-roles` is a convenience, never a control

**Decision.** A dedicated endpoint returns the roles the caller could actually
hand out — the ceiling applied to the role list. The frontend renders only these.
Every write re-checks independently.

**Why.** Showing an administrator a role they will be refused is a bad screen,
and a "you cannot assign this" error after the fact is worse. But a narrowed list
is a rendering decision made at read time, and a role can gain permissions
between that read and the save that follows. The list is therefore advice.

**Forbids.** Treating the response as authorisation. Deriving the write's
validation from what the list happened to contain.

**Consequence.** The list and the save can legitimately disagree, and the save
wins. This is the same relationship Phase 13 established between frontend
permission awareness and server authorisation.

---

## T5. Assignment is bounded on both the current and the desired role set

**Decision.** Replacing a member's roles requires **both** to sit inside the
actor's ceiling:

```
REPLACE MEMBER ROLES requires  CurrentRoles ⊆ ActorCeiling
                          and  DesiredRoles ⊆ ActorCeiling
```

**Why.** The desired set alone is the obvious half and stops manufacturing
authority. The current set is the half that is easy to miss, and it is the
Phase 13 contract's second obligation: without it a delegated administrator could
*strip* a senior colleague's roles — the ceiling would happily accept an empty
desired set. Revocation is as much an exercise of authority as granting.

**Consequence.** A member holding anything beyond the reader's ceiling is
read-only to them, and the screen says so by name: "Beyond your access: Senior
support". Naming the role is deliberate — "you lack permission" leaves somebody
with no idea what to ask for.

---

## T6. Owner is not assignable, and not by a permission check

**Decision.** The Owner role cannot be assigned or removed through member
administration by anybody, Owner included. The attempt is `409`, not `403`.

**Why.** This discharges the first obligation of the Phase 13 contract. `403`
would say *you are not allowed to do this*, which invites the reading that
somebody else is. Nobody is: handing over a workspace is an ownership operation
that this release does not implement, so the honest answer is that the request
contradicts the resource's state.

**Forbids.** Reaching ownership transfer by ticking a checkbox.

---

## T7. Nobody changes their own access here

**Decision.** An actor replacing their own roles is refused with `409`.

**Why.** It is the one change that can lock a workspace permanently. An
administrator who removes their own last role, in a workspace where they are the
only administrator, has no recovery path — there is no platform-admin escape
hatch, and this release has no way to add one. Refusing the whole operation is
cheap; the alternative is a rule about which self-removals are safe, which is a
rule that will eventually be wrong.

**Consequence.** Changing an administrator's own roles requires another
administrator, or the Owner. The screen says which of the three read-only reasons
applies — owner, self, or outranked — because the next step differs: one is
permanent, one means ask a colleague, one means ask for more permission.

---

## T8. The ceiling is re-read at every decision, including acceptance

**Decision.** No ceiling is ever passed in, cached, or carried across a request.
`TenantAuthorizer` resolves it from the database each time, as Phase 13's D26
requires.

**Why.** This is the contract's third obligation. It matters most for
invitations, which are the longest-lived interaction in the platform: an
invitation issued today may be accepted a week from now, by which time the
inviter may have been demoted, had their roles removed, or lost membership
entirely.

**Consequence — acceptance re-validates the inviter, not just the invitee.**
Accepting an invitation checks, in order:

1. the token matches a stored hash,
2. the invitation is still `Pending`,
3. it has not passed its expiry,
4. the accepting account's address matches the invited address,
5. the accepting account is not already a member,
6. the inviter is still a member of the workspace,
7. the inviter still holds `members.invite` and `members.roles.manage`,
8. every role on the invitation still exists, is not Owner, and still sits inside
   the inviter's *current* ceiling.

An invitation is therefore not a stored authorisation. It is a stored *intent*,
authorised again at the moment it is used.

---

## T9. The token is 256 bits, and only its hash is stored

**Decision.** `RandomNumberGenerator.GetBytes(32)`, rendered unpadded Base64Url
(43 characters). The database stores the SHA-256 hash as `bytea` and never the
token. Comparison is `CryptographicOperations.FixedTimeEquals`.

**Why.** An invitation token is a bearer credential that admits somebody to a
workspace, so it is treated as one. 256 bits of CSPRNG output is not guessable.
Storing only the hash means a database disclosure — a leaked backup, an
over-broad read — does not yield usable invitations, which is the same reasoning
that applies to passwords.

**Forbids.** `Guid.NewGuid()` or any non-cryptographic source. Logging the token.
A `ToString()` that could put a hash into a log line — `InvitationTokenHash`
returns its own type name.

**Consequence.** A lost link cannot be recovered, only revoked and reissued. The
UI says so at the point the link is shown.

---

## T10. The raw token is returned exactly once, from exactly one shape

**Decision.** Creating an invitation is the only response in the platform that
contains a token, and it is the only contract with a field that could hold one.
That response carries `Cache-Control: no-store`. No read path returns it.

**Why.** A token that can be re-read is a token with a much larger exposure
window: every later list, every proxy cache, every browser history entry becomes
a place it lives. Confining it to one response makes "could this leak the token"
a question about one endpoint rather than about the whole read surface.

**Consequence.** Reissuing means revoking and inviting again. This is a
deliberate trade against convenience, and the same one T9 makes.

---

## T11. The link carries the token in the fragment, not the query string

**Decision.** `https://…/accept-invitation#token=…`, never `?token=…`.

**Why.** A fragment is not sent to the server. It never appears in a request
line, an access log, or a `Referer` header — all three of which a query string
does reach, and the `Referer` case means a query-string token would leak to every
third-party resource the acceptance page loaded.

**Consequence.** The server cannot read the token from the URL, so the page must
post it explicitly. That is the correct shape anyway: acceptance is a write.

---

## T12. The token lives in memory, and leaves the address bar immediately

**Decision.** On arrival the page reads the fragment, holds the token in a
root-provided service, and removes it from the URL with `history.replaceState`.
It is never written to `localStorage`, `sessionStorage`, a cookie, or the
authentication state.

**Why.** Most people opening an invitation have no account yet and must register
or sign in first, which means navigating away and back — so something has to hold
the token across those navigations. A service scoped to the tab has exactly the
right lifetime. Storage would leave a bearer credential on disk, readable by
every later script on the origin and surviving the browser being closed, to buy
nothing but surviving a refresh.

**Consequence.** Refreshing the acceptance page loses the token and the reader
reopens their link. `replaceState` rather than a navigation means Back cannot
return to a URL that still contains the secret.

**Two mechanisms are needed, not one, and browser verification is what found
that.** Removing the fragment has to survive both of the things that write this
URL:

- A second invitation opened in a tab that already handled one changes only the
  fragment. That is a same-document navigation: the component is never destroyed
  and `ngOnInit` never runs again, so a token read once at construction is
  ignored while the previous outcome stays on screen — telling somebody they had
  joined a workspace they had not. The page therefore listens to the browser's
  `hashchange`. It deliberately does not use the router's parsed fragment:
  `replaceState` is invisible to the router, so its remembered fragment still
  holds the spent token, and reopening the *same* link would compare equal and do
  nothing.
- The router writes the URL it parsed when a navigation completes, which puts the
  token back after the strip. Rather than racing it, removal is a standing
  correction: whenever a navigation settles, a token must not be in the address
  bar.

---

## T13. Expiry is lazy, and the uniqueness index does not read the clock

**Decision.** An invitation is created with a seven-day `ExpiresAt`. Usability is
always computed from that instant. The stored `Status` is stamped `Expired` only
when something touches the invitation — and, when a replacement is issued for the
same address, inside the same transaction as that replacement.

**Why.** PostgreSQL will not accept a volatile function in a partial index
predicate, so `WHERE status = 'Pending' AND expires_at > now()` is not
expressible. Predicating the index on the stored status alone is expressible, and
it is honest about what the index enforces: at most one *open* invitation per
address per workspace.

**Forbids.** Trusting `Status` as the answer to "may this be used". Every
usability decision reads `ExpiresAt`.

**Consequence.** A row can sit in `Pending` after its expiry has passed. It is
unusable throughout, and the index will not block a fresh invitation to the same
address, because issuing one stamps the stale row `Expired` first. No background
job exists, and none is needed for correctness.

---

## T14. Invitation roles are relational, and cross-tenant ones are unrepresentable

**Decision.** `InvitationRoleGrant` is a table, not a serialised column. It
carries `TenantId` alongside the invitation and role identifiers, and two
composite foreign keys — to the invitation and to the role — both read the same
`tenant_id` column.

**Why.** It is the same construction Phase 13 used for membership-role
assignments, for the same reason: an invitation to workspace A that carries a
role from workspace B is not a bug to be validated against, it is a state the
schema refuses to represent. Sharing one `tenant_id` between both keys is what
forces the two ends to agree.

**Forbids.** JSON or delimited role lists, which no foreign key can constrain.

---

## T15. Acceptance is atomic, and safe under concurrency

**Decision.** Acceptance consumes the invitation with a conditional update whose
predicate includes `status = 'Pending' AND expires_at > now()`. Membership,
role assignments and the status change commit together.

**Why.** Two tabs, a double-click, or a retried request must not produce two
memberships or consume an invitation twice. Putting the usability condition into
the `WHERE` clause means the database, not the application, decides which
attempt wins — the loser updates zero rows and is refused.

**Consequence.** A concurrent second attempt sees the same refusal a used
invitation gets, which is the correct answer.

---

## T16. Refusals are ordered so a withdrawn link tells nobody anything

**Decision.** Acceptance checks the invitation's usability *before* it compares
addresses. A used, withdrawn or expired invitation answers `410 Gone`
irrespective of who is holding it; only a usable invitation presented by the
wrong account answers `403`.

**Why.** The reverse order would turn a forwarded link into an oracle: present it
as yourself, and a `403` rather than a `410` tells you the invitation is live and
belongs to somebody else.

**Consequence.** `410` is distinct from `404` on purpose. Not-found has somebody
checking the link for typos; gone tells them to ask for a new one. Neither
response ever names the invited address — a leaked link must not become a way to
learn a colleague's email.

**The `403` says what to do about it.** The platform's shared copy for a refused
permission sends the reader to an administrator, which is the wrong advice here:
the remedy is theirs, and it is to return as the invited account. The acceptance
screen supplies its own sentence, still without naming the address. This is the
error model working as designed — `toApiError` maps the failure, and the screen
decides what it means for the reader in front of it.

---

## T17. Email is a value object whose equality is canonical

**Decision.** `EmailAddress` stores the address as entered and a canonical
upper-invariant form. `Equals`, `GetHashCode` and `==` all compare the canonical
form; a shadow `canonical_email` column carries it into the database.

**Why.** The alternative — a plain string plus a `Matches` helper used at the one
place that compares — is a defect waiting for its second caller. Somebody will
eventually write `a == b`, it will compile, and it will refuse a valid acceptance
because of a capital letter. Making `==` correct removes the trap rather than
documenting it.

**Consequence.** Display keeps the address as the person typed it. Matching and
uniqueness use the canonical form.

---

## T18. No email is sent, and the UI is built around that

**Decision.** The platform creates a link and shows it once. There is no SMTP
configuration, no email provider dependency, and no delivery.

**Why.** Delivery is its own problem — provider selection, bounce handling,
deliverability, per-tenant sending identity — and none of it changes the security
model of the token. Shipping the model first, with manual sharing, keeps the two
decisions independent.

**Consequence.** The invite screen is explicit that the link must be sent by
whatever channel the administrator already uses, that it works once, and that it
is shown only now.

**Superseded in Phase 37.3 (D-1 option A).** Invitations are now also emailed; see
T20. The link the inviter copies keeps working beside the emailed one, so the invite
screen's promise still holds.

---

## T20. The invitation email, and its second token (Phase 37.3)

**Decision.** When an invitation is created and the deployment can send email, one
invitation email is queued in the same transaction (`account_emails`, kind
`Invitation`, linked to the invitation through its `(id, tenant_id)` key). It is
written in the invitation's language: the optional `language` on `POST
…/invitations` (`"en"` or `"ar"`), else the inviter's own stored language, else
English. The invitation keeps it (`tenant_invitations.language`, migration
`AddInvitationEmail`). An unsupported value is `400 language.unsupported`, and
nothing is created.

**The email carries its own token.** Minted by the account email worker at the
moment it sends, 256 bits like the first (T9), and only its SHA-256 is stored, in
`tenant_invitations.email_token_hash` (unique where set). The hash is written
together with the dispatch boundary, in one transaction, and only while the worker
still owns the email and the invitation is still open, so a worker that lost its
claim can never replace the hash of a message somebody already has. The link is
`/accept-invitation#token=…`, in the fragment, exactly as T11.

**Acceptance matches either token.** The repository looks the invitation up by
either hash; `TenantInvitation.IsRedeemableWith` then compares the presented hash
with both, in constant time, and both comparisons always run. Revocation, expiry and
acceptance end both tokens at once, because they change the invitation, not a token.
A wrong token is still `404`, identical to one never issued.

**What the invitee is told.** The workspace name (encoded and isolated), that they
can sign in with this address or create an account with it, and when the invitation
expires (UTC). Not the roles, and not the inviter's address. Sent and failed emails
are audited (`auth.invitation.email_sent` and `…email_failed`, with the workspace).

**Acceptance fills the language.** `POST /api/invitations/accept` takes an optional
`language` too; the accepting account's language is filled from it (else
`Accept-Language`) only while the account has none.

---

## T19. Known staleness: an administrator's own permissions can move underneath them

Phase 13's D26 recorded a deferred requirement: when member administration
arrives, a change to the roles a signed-in person holds must refresh their
effective permissions. Phase 14 makes that reachable in a new way — another
administrator can now change your roles while you are on screen — and does not
solve it. No polling was added.

It is a staleness problem, not a security one. The frontend's permission copy is
UX only, exactly as Phase 13 established; every request is authorised server-side
against freshly read state, so a stale screen can offer an action that is then
refused, but it cannot perform one. A reload or a workspace switch re-resolves
it.

---

## T21. The invitation list filters by status, and reads each kind of fact once (Phase 37.5b)

**Decision.** `GET …/invitations?status=Pending|Accepted|Revoked|Expired`, under
`members.view` as before. The status is the one the list shows (T13): a stored
`Pending` row past its `ExpiresAt` is `Expired`, so `Pending` means stored pending and
`expires_at > now`, and `Expired` means stored expired or stored pending with
`expires_at <= now`. One instant decides the filter and every status on the page.
`total` counts the filter. Without `status` the list is exactly what it was. The
value is a name in any letter case; a number, a list or anything else is `400
invitations.status.invalid`, checked before membership, so the answer is the same for
anybody.

**And the N+1 is gone.** The page used to read each invitation's roles one by one and
its inviter's address separately. Now the count, the page, the role names of every
invitation on it (`ITenantRoleRepository.FindNamesAsync`) and the inviters' addresses
(`IAccountDirectory.FindEmailsAsync`) are one statement each, scoped by the workspace,
on `ix_tenant_invitations_tenant`. A role that has gone is still left out, and names
still sort ignoring case.

**Forbids.** Filtering on the stored status alone, which would list expired
invitations as pending.

---

## DECIDED NOW

- Three member permissions, and no codes for operations that do not exist
- Owner receives them intrinsically, with no grant rows and no data migration
- Member listing and member detail, scoped by `members.view`
- A member's access as the versioned resource: `UpdatedAt` plus an `xmin` `ETag`
- `xmin` as a shadow property, asserted by test to have no physical column
- `If-Match` / `428` / `412` on member role replacement
- An assignable-roles endpoint that narrows the UI and authorises nothing
- The Phase 13 authority ceiling applied to member assignment
- Bounded on the current role set as well as the desired one, so revocation is
  bounded too
- Owner strictly not assignable, answered `409`
- Self role changes refused, answered `409`
- Read-only member screens that say which of the three reasons applies, and name
  the roles beyond the reader's access
- `TenantInvitation` aggregate with an explicit status
- 256-bit CSPRNG token, SHA-256 hash stored, constant-time comparison
- The raw token returned once, from one response, with `Cache-Control: no-store`
- Fragment-carried links, stripped from the address bar on arrival
- In-memory-only token handling in the browser, across sign-in and registration
- Seven-day lifetime, with lazy expiry and a clock-free partial unique index
- Relational invitation roles with composite foreign keys sharing one `tenant_id`
- Acceptance-time revalidation of the invitation, the invitee and the inviter
- Atomic acceptance, safe under concurrent attempts
- Usability checked before identity, so a withdrawn link discloses nothing
- `410` distinguished from `404`, and neither naming the invited address
- `EmailAddress` as a value object with canonical equality
- Manual link sharing, and from Phase 37.3 an invitation email beside it (T20)

## DEFERRED

- ~~Email delivery of invitations~~ (Phase 37.3, T20). Bounce handling and a
  per-tenant sending identity remain deferred
- Removing a member from a workspace
- Leaving a workspace
- Ownership transfer, and Owner assignment
- Resending an invitation without revoking it first
- Invitation expiry as a background job, and any scheduled cleanup
- Refreshing a signed-in person's permissions when somebody else changes their
  roles (T19)
- Bulk invitation and bulk role assignment
- Domain-based auto-join, and open workspace signup links
- Invitation audit history beyond the current status
- Per-workspace invitation lifetime configuration
- Role deletion, and the orphan-assignment semantics it still requires

## T22. The display name beside the email (Phase 39.1)

Additive, under the same permissions as before (`members.view`):

| Read | New member |
| --- | --- |
| `GET …/members` (each `MemberSummaryResponse`) | `displayName`, `null` when the person has none |
| `GET …/members/{userId}` and `PUT …/members/{userId}/roles` (`MemberResponse`) | `displayName` |
| `GET …/invitations` and `POST …/invitations` (`InvitationResponse`) | `invitedByDisplayName` beside `invitedByEmail`; `null` when the inviter has none or the account is gone |

The name is read in the statement that already reads the email: the member list's
one users query for the page, the member detail's account read, and, for the
invitation list, one `IAccountDirectory.FindPeopleAsync` call for the page (address
and name together), which replaced the page's `FindEmailsAsync` call. No extra round
trip. The email is unchanged everywhere. The name is the person's own
(`PUT /api/me/display-name`, `authentication-tenant-membership.md`); an
administrator cannot set it. Another workspace's member reads `404` on every one of
these routes, as before, so a name never crosses workspaces.

The document now also declares the member list's and the member detail's bodies
(`MemberPageResponse`, `MemberResponse`), metadata only.
