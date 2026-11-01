# Human handoff, assignment and the realtime inbox

Phase 19 turned the read-only conversation screens into a place people work.
A colleague can take a conversation over from the AI, answer it themselves, hand
it back, and route it to somebody — and every screen involved, including the
customer's, updates without anybody pressing Refresh.

There is still **one conversation runtime**. Nothing here is a second messaging
system: the same aggregate, the same ingestion sequence, the same advisory lock
and the same transports.

---

## 1. Handling is its own concept, not a status

`ConversationHandling` is `Ai` or `Human`, beside `ConversationStatus`
(`Open`/`Closed`) rather than folded into it.

Open/Closed says whether a conversation accepts messages at all. Handling says
who produces the replies while it is open. They are independent, and a single
enum spanning both would force every reader to know which members were lifecycle
and which were routing — the first time somebody wrote `Status == Human` next to
`Status == Closed` the mistake would already be shipped.

Two members, because two is what the platform can do. No `Pending`, no `Queued`:
there is no customer "request a human" flow and no queue, so a third member would
be a state nothing can enter and nothing can leave.

**In `Human` mode a customer turn is stored and nothing else happens.** No
retrieval, no generation, no spending, and no invented reply. The turn is settled
as *completed with no reply* — not failed, because nothing went wrong: the
message arrived exactly as the customer sent it and a colleague will answer it.
Recording it as a failure would show the visitor an error for a message that
worked, and would put a non-incident into every count of how often generation
breaks.

**The generation budget is not consumed in `Human` mode.** It counts generation
attempts, and there is not going to be one; charging for it would make taking a
conversation over cost money. The per-client request limit still applies, so
human mode is not a way around Phase 16 — only around the part of it that meters
spending.

---

## 2. Returning to the AI answers nothing retroactively

Messages that arrived while a person was handling a conversation are never
answered by the AI. Not on return, not ever. Only a genuinely new customer turn
starts generation again.

This is a property of the runtime rather than a rule somebody enforces:
generation is only ever started by an incoming customer message, so there is no
code path that could sweep up a backlog. Answering those messages later would
answer questions a colleague may already have dealt with by phone — several
minutes late, out of order, in a transcript the customer is still reading, and
paid for.

---

## 3. Takeover takes the lock generation takes

`ConversationHandoffHandler` acquires the **same PostgreSQL per-conversation
advisory lock** the AI runtime holds across a provider call.

Without it, "take over" can land in the middle of a turn: the provider call has
already been paid for and is in flight, the handler is about to append its reply,
and this write says nobody should be answering any more. The transcript then gets
an AI reply *after* a human took the conversation over — a contradiction the
customer would notice and nobody could explain.

**An in-flight provider call is never cancelled.** It has been bought already;
abandoning it saves nothing and loses an answer that may be the right one. So the
takeover waits, and **whichever operation acquires the lock first owns the
transition**: a turn that started before the takeover finishes and appends its
reply, and the takeover applies immediately afterwards. The order is whatever the
database decided rather than whatever the scheduler happened to do.

Nothing is read before the lock is held — reading first would mean deciding
against a version a turn in flight is about to replace.

Assignment deliberately does **not** take the lock: it touches nothing a
generation touches, so serialising it behind a provider call would make an
instant UI action wait on somebody else's turn for no benefit.

---

## 4. Human replies: attribution and idempotency

A reply is one row in `conversation_human_replies` written in the same
transaction as its message — the same shape `conversation_turns` already uses for
the AI side, because the platform already had one answer to "this write must
happen at most once" and a second, different answer would be one idea too many.

**The sender is resolved on the server** from the authenticated request. The
command has no field for one, so a browser sending somebody else's id is sending
a field nothing reads.

**The row carries the tenant, which its message does not**, and that is the whole
reason it is a table rather than two nullable columns on `messages`. It lets the
database hold a composite foreign key on `(sender_user_id, tenant_id)` into
`tenant_memberships`, so a reply attributed to somebody who does not work at that
workspace is not a bug to be caught by a test — it is a row PostgreSQL refuses.

**Idempotency is the unique index** `ux_human_replies_client_message` on
`(conversation_id, client_message_id)`. Two concurrent requests carrying the same
key both pass any in-process check; the index settles it, and the loser reads
back the winner's message and returns it. A browser that retried a request whose
response was lost sends one reply and sees one reply.

**Sending does not take the conversation over.** Replying to a conversation the
AI is still answering fails with `409` and says so. Inferring a takeover from a
keystroke would mean somebody who typed in the wrong tab silently stopped an
automation for a customer nobody is watching.

---

## 5. What the customer is told, and what they are not

| | Workspace screens | Customer |
| --- | --- | --- |
| Sender of a human reply | the colleague's email | `HumanAgent`, rendered as **"Support"** |
| Handling | "A person" / "The AI" | "A support team member will reply here." |
| Assignment | shown | never sent |

The public view types have **no field** an employee identity could be assigned
to, so this is a property of the shape rather than a rule somebody has to
remember. The transcript record does carry the author for the admin API, and the
public handlers drop it when they build their own view.

There is **no typing indicator in human mode**. "Somebody is typing" when nobody
may be at their desk is a promise the platform cannot keep, and a fake one aimed
at a waiting customer is worse than silence.

---

## 6. Assignment is routing, and confers nothing

A conversation may be assigned to one member or to nobody. Being assigned does
not let somebody reply, and not being assigned does not stop anybody replying —
it exists so two people do not answer the same customer at once.

Because it grants no permission, the Phase 13 delegation authority ceiling
deliberately does not apply: there is nothing to escalate.

Eligibility *is* checked, for a product reason rather than a security one:
routing a customer to a colleague who cannot reply is a silent way to make that
customer wait for ever. The target must be a member of this workspace and must
currently hold `conversations.reply`, computed from their roles at the moment it
is asked. The workspace boundary is enforced three times over — in the use case,
by the tenant-scoped lookup, and by the composite foreign key
`fk_conversations_assigned_member` into `tenant_memberships`.

---

## 7. Permissions

Three new codes, and Owner receives them through the existing intrinsic
behaviour — an Owner role stores no grant rows and answers with the catalogue, so
adding a code extends every Owner in every workspace in the same edit. Existing
custom roles gain nothing.

| Code | Grants |
| --- | --- |
| `conversations.reply` | Send human replies. Also what makes somebody eligible to be assigned. |
| `conversations.handoff` | Take over, and return to the AI. |
| `conversations.assign` | Decide which member a conversation is routed to. |

`conversations.view` remains required to see a conversation, and is a
prerequisite for all three: somebody who may hand a conversation over but may not
read it is being asked to decide blind. No permission implies another —
the platform's standing rule.

The three outcomes are unchanged: unauthenticated `401`, non-member `404`,
member-without-permission `403`.

---

## 8. Conversation ETag

Handoff and assignment are conditional writes against the conversation's `xmin`.
A read emits a strong `ETag`; a mutation without `If-Match` is `428`, with a
stale one `412`, and a success returns the new tag. Nothing retries: a `412`
means the intention was formed against a state that no longer exists.

**Replying is deliberately not conditional.** A composer is a box somebody has
been typing in while the conversation moved underneath them — the customer wrote
again, a colleague claimed it — and none of that makes the reply they typed
wrong. Requiring a version would reject correct replies for irrelevant reasons.
What must not happen twice is the reply itself, and the idempotency key prevents
exactly that.

---

## 9. Realtime: structural events only

SignalR, with two hubs.

| | `/hubs/workspace-inbox` | `/hubs/public-chat` |
| --- | --- | --- |
| Who | signed-in staff | one visitor |
| Authorised by | the existing session cookie | a short-lived realtime ticket |
| Group | `tenant:{id}` | `conversation:{id}` |
| Joining | client asks; server checks membership + `conversations.view` | decided by the ticket; the hub has no callable methods |

**The payload is a conversation id and nothing else.** The temptation is to push
the new message down the wire and let the client append it, which would be faster
and is the wrong shape: a realtime fan-out is not an authorisation boundary, and
the moment transcript text travels on it, every question the HTTP reads answer
carefully — is this credential still valid, is this website still approved, has
this session expired, does this employee still have permission — has to be
answered again on a connection authorised minutes ago. Sending only "something
changed" means the worst a stale listener can learn is that something changed.

Clients then re-read through the ordinary authorised API. So a website removed
from the allowlist a second ago can still be told something changed and still
cannot read what.

**Arrival order is not transcript order.** Two instances behind a backplane
deliver when they deliver; the ingestion sequence decides what the transcript is,
exactly as it does for somebody who pressed Refresh.

**Delivery is best effort, and that is enforced at the call site.** Every
announcement in the Application layer goes through `AnnounceQuietlyAsync`, which
never throws. A contract kept only by an implementation's good behaviour is kept
only until somebody writes a second implementation. A reply that was stored and
not announced is a reply the next read finds; a reply refused because the
announcement failed is a reply nobody ever gets.

**A failed AI turn is announced too (Phase 37.1).** The customer's message is
committed before the provider is called, so when generation fails the
conversation has still changed. `CustomerTurnRunner` announces once, after
`FailTurnAsync` has committed and before the original failure is rethrown, on
both channels (website chat and WhatsApp share the runner). It uses
`AnnounceQuietlyAsync` with no cancellation token, so a broken backplane or an
abandoned request can neither replace the failure the caller sees nor change the
replay outcome. A replay of the same client message id is refused as already
failed before it reaches that point, so it is never announced twice. The event is
the existing `conversationChanged(conversationId)`: no new event, field or
payload. Before 37.1 the message reached an open inbox only after a reload.

---

## 10. The public realtime ticket

**A conversation credential never travels in a URL.** A websocket cannot carry a
header, so whatever authorises one ends up in the query string — logged by every
proxy, kept in every access log, visible in the network panel of the page that
opened it. The hosted credential lives in an `HttpOnly` cookie precisely so that
script cannot read it; putting it in a URL would undo that in one line.

So the credential is spent once, through the already-authorised transport, to
mint a ticket:

- `POST /api/public/webchat/conversations/{id}/realtime-ticket` — hosted cookie,
  antiforgery-protected like every other hosted write.
- `POST /api/public/embed/conversations/{id}/realtime-ticket` — embedded header
  credential, no antiforgery for the same reason the other embed routes have
  none, and **the allowlist is re-read on this request**.

A ticket is 32 random bytes, base64url. It names **one conversation and one
transport**, lives ~90 seconds, reads no API and writes nothing. Only its
SHA-256 digest is stored, in Redis, with a TTL — so anybody reading the key space
learns which conversations have listeners and nothing they could use to become
one. It is never logged.

Not single-use: a SignalR connection presents its token on negotiate and again on
the transport, so burning it on first sight would break the connection it exists
to authorise. The lifetime is the bound instead.

**Hosted and embedded stay separate.** The transport is part of what the ticket
records and what the hub checks, so Phase 18's separation is not bridged by the
realtime channel. A conversation id alone mints nothing, because on its own it
authorises nothing.

If Redis is unavailable no ticket can be minted or checked and a listener is
turned away — costing live updates, never a disclosure. The chat itself is
unaffected.

---

## 11. Multi-instance

`Microsoft.AspNetCore.SignalR.StackExchangeRedis`, on the Redis the public-chat
protection already configures, namespaced by channel prefix.

Without it a hub reaches only the browsers connected to the process that raised
the event. On one instance that is indistinguishable from working; on two it is
an inbox that updates for roughly half the staff, depending on which instance
their websocket landed on — silent, load-dependent, and absent from every local
test.

The backplane serialises messages between instances with MessagePack, which
arrives through the backplane package. From Phase 37.1 it is pinned centrally to
2.5.303, the first 2.x release that clears GHSA-qhrr-8q5h-9q3h.
`FailedTurnRealtimeApiTests` proves the whole path with two in-process API hosts on
one Redis: a member connected to the second instance receives
`conversationChanged` for a turn that failed on the first.

Redis remains authoritative for Phase 16 protection, which is unchanged. Realtime
is best effort: if the backplane fails, persisted data is still correct, no
message is lost, and clients re-fetch on reconnect. SignalR is never the source
of truth.

**Ending an account's connections (Phase 37.3).** A connection is authenticated
once, when it opens, so a password reset closes it explicitly. Each instance keeps
its admin hub connections by account (`SessionConnections`, filled by
`SessionStampHubFilter`). The reset aborts the account's connections on its own
instance and publishes the account on the Redis channel `omniai:auth:revoked`;
every instance aborts its own. The same filter re-checks the session stamp on every
hub invocation, so a connection that missed the message is refused and closed on
its next call (authentication-tenant-membership.md D32).
`SessionRevocationApiTests` proves both with two in-process hosts on one Redis. The
public chat hub has no account and is unaffected.

---

## 12. Migration

One migration, `AddHumanHandoffAndAssignment`, hand-edited in three places the
scaffolder got wrong or could not express:

1. `handling` is added nullable, backfilled with `'Ai'`, then set `NOT NULL`. The
   scaffolder's `defaultValue: ""` would have put a value the enum has no member
   for into every existing row — failing on the first read of an old
   conversation rather than at deployment.
2. Both composite foreign keys are written by hand, because EF cannot express a
   composite principal key for a single-column dependent without modelling
   membership as a navigation on the conversation aggregate.
3. The scaffolder's index over the human-replies tenant foreign key is dropped:
   nothing reads replies by workspace alone.

> **Phase 33.1:** items 2 and 3 now live in the model. EF can express both
> composite keys through `HasPrincipalKey` on the membership key, and the
> dropped index is an explicit `WithoutConventionIndex()` on the relationship.
> The development migrations were replaced by one generated, unedited Initial.
> See [database-model-authority.md](database-model-authority.md).

No historical migration was edited. No physical `xmin` column exists.

---

## 13. Known limitations

- **A visitor learns the handling mode only when their client reads.** The
  transcript response carries it, so a takeover reaches them on the next
  notification — which is immediate in practice, but it is a read rather than a
  push.
- **The admin list re-reads whole pages on any conversation change.** One
  workspace group carries every conversation's events, so a busy workspace
  re-reads its first page often. A per-conversation admin group would be the
  next step if that becomes a problem.
- **Value-object ids cannot be unwrapped in EF predicates.** Several reads are
  deliberately two queries rather than one join for this reason; each is
  commented where it occurs.
- No queue, no unread counts, no SLA, no canned replies, no attachments, and no
  customer-initiated "request a human" — none of which exists.
