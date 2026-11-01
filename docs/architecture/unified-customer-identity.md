# Unified customer identity

Decisions recorded here are binding until explicitly revisited.

Phase 30 introduces a canonical, workspace-owned **Customer**: the person a
workspace talks to, independent of how they reach it. It is an identity
foundation. It is not a CRM, and every decision below is shaped by keeping it
from becoming one by accident.

---

## C1. A customer is separate from the channel identity that found them

**Decision.** `Customer` holds a workspace, an optional label, a merge pointer
and two timestamps. It holds no phone number, no session credential and no
provider identifier.

**Why.** Each channel identifier belongs to the transport that issued it and has
its own lifetime, masking rule and retention question. A WhatsApp number can be
reassigned to a different person; a web session credential expires. Copying
either onto the customer would create a second place to get personal data wrong,
and would make the canonical record's meaning depend on one channel.

**Forbids.** Adding a channel identifier column to `customers`. Adding notes,
tags, stages, scores, owners or a metadata JSON bag — each is a product in its
own right, and a column added now would decide its design by accident.

---

## C2. Nothing infers identity

**Decision.** The platform makes exactly one automatic identity claim: *the same
transport identity is the same person within one workspace*. Everything else
requires an administrator to say so.

**Why.** Identity is the one mistake this platform cannot walk back. Two records
wrongly held apart can be combined later; one record wrongly holding two people's
conversation histories cannot be taken apart, because nothing records which
message belonged to whom.

**Forbids.** Matching on display name, similar name, email-looking text,
phone-looking text, similar phone number, browser fingerprint, device, IP or any
model. No probabilistic resolution, no fuzzy matching, no "possible duplicates"
suggestion, no ranked merge candidates.

---

## C3. WhatsApp identity is keyed on the workspace and the sender

**Decision.** `whatsapp_customer_identities` is keyed `(tenant_id,
whatsapp_customer_id)` and points at one customer. Resolution happens at the
transport boundary, before and outside the conversation's transaction.

**Why.** It is a separate table from `whatsapp_contacts` because the two answer
different questions. A contact row records *one conversation* — created when a
conversation starts, superseded when the next one does, N rows per person.
Identity is one row per person per workspace, for as long as the workspace
exists. Hanging the canonical customer off the contact row would put the answer
to "who is this?" in N places that could disagree, and no constraint could stop
them.

The key is the workspace and the sender, not the connection: a workspace may run
several business numbers, and somebody messaging two of them is one person. That
is still a transport fact — the provider reports the same sender — and not an
inference from two strings that look alike.

**Consequences.** The same number in two workspaces is two people; it has to be,
because they are two businesses' customer records. A conversation that closes and
a new one that starts months later resolve to the same customer.

**Concurrency.** Two first messages racing each other both attempt the insert;
the primary key admits one, and the loser reads the winner's row. There is no
application lock, because an application lock only works in one process and this
platform expects several.

---

## C4. Web chat has no durable identity, and none is invented

**Decision.** A hosted or embedded web conversation gets a new customer.
`web_chat_sessions` is keyed on `conversation_id`, so the session *is* the
identity, and the canonical link is carried by `conversations.customer_id`.

**Why.** A public visitor arrives with no identity this platform has ever seen.
Manufacturing one from a browser marker, a network address or a device signature
would be exactly the fingerprint-based resolution C2 forbids. Saying so plainly
is better than a continuity that is quietly wrong.

**Deliberately not done.** `web_chat_sessions` carries no `customer_id` column of
its own. Because a session is one-to-one with its conversation, a second column
would be the same fact in two places with an obligation to keep them in step, and
the merge would have to move both. The conversation's foreign key is the single
source.

**Consequences.** Resuming the same authorised conversation keeps the same
customer. A genuinely new anonymous session is a new customer, and the two are
combined only by an administrator.

---

## C5. Every conversation belongs to exactly one customer, enforced by the database

**Decision.** `Conversation.CustomerId` is required in the Domain and enforced by
a **composite** foreign key `(tenant_id, customer_id)` → `customers(tenant_id,
id)`. The same composite key guards `whatsapp_customer_identities`.

**Why.** "A conversation cannot reference another workspace's customer" is an
isolation rule, and isolation rules that live only in application code are one
forgotten call site away from being false. The composite key makes the database
refuse it.

**Forbids.** A nullable `customer_id`. Relying on an application check for tenant
safety. Moving a customer between workspaces — there is no operation that does
it, and there must not be.

---

## C6. Merging is manual, one hop deep, and tombstones rather than deletes

**Decision.** An administrator names a source and a target in the same workspace
and confirms. The source's channel identities and conversations move to the
target by set-based update; the source keeps its row and points at the target.

**The cycle rule.** Only an **unmerged** record may be merged, and only **into**
an unmerged record. That single rule makes the pointer graph a forest of depth
one: the moment `A` points at `B`, `A` is no longer a legal target, so `B → A`
has nothing to attach to and no chain can form for a longer cycle to close. There
is no graph to walk and no cycle detection to get wrong.

**Locking.** Both rows are locked with transaction-scoped advisory locks taken in
an order derived from the ids, not from which record the caller called the
source. Two merges naming the same pair from opposite directions queue behind
each other instead of each holding what the other waits for. Canonical state is
re-read while the locks are held.

**Idempotency.** Merging a source into the target it is already merged into
answers "done" and moves nothing. A source already merged *elsewhere* is refused
with the canonical id rather than silently remapped — moving a history a second
time on the say-so of somebody who did not know it had already moved is worse
than a refusal.

**No unmerge.** Deliberately absent in this phase. Reversible identity tooling is
its own design, and pretending to offer it would be worse than saying it cannot
be undone.

**Forbids.** Deleting a customer or any history. Automatic merging of any kind.
Cross-workspace merging.

---

## C7. Customer data and transcripts are different authorization concepts

**Decision.** `customers.view` reads the directory and a customer's profile.
`customers.manage` sets a label and combines records. A customer's conversation
history appears only for a caller who *also* holds `conversations.view`.

**Why.** A list of everybody a business serves is worth protecting even without
the transcripts, and being trusted to read what customers said is a different
decision from being trusted with the customer base. Neither implies the other,
in keeping with the rest of the platform.

**Forbids.** `customers.view` becoming a way around `conversations.view`. A
`customers.delete` permission — nothing deletes a customer, and a permission
implying otherwise would be a promise the product does not keep.

**Note.** The conversation permission is *asked* rather than *required* on the
detail page: somebody who administers customers without reading mail is a
legitimate reader, and refusing their whole request over the section they cannot
see would make the permission useless on its own. The counts are still reported;
knowing somebody has fifteen conversations is not being able to read one.

---

## C8. Privacy

- Channel identifiers leave **masked** — a WhatsApp number as its last four
  digits — and are masked by the Domain value object before they reach the API
  layer. There is no unmasked variant and no parameter that produces one.
- No directory or detail response can carry message text: the shapes have no
  field for it.
- Safe logs use workspace, customer, conversation, channel kind and mutation
  type. A raw sender identity, a customer's label, a provider profile name and
  conversation content are never logged.
- Nothing about customer identity is exposed to Analytics. Phase 22 was
  deliberately not expanded: unique-customer metrics, retention and cohorts are
  future Analytics work with their own privacy decisions.

---

## C9. A display name is a label, never a key

**Decision.** Optional, bounded to 120 characters, single-line, free of control
characters, trimmed but never otherwise repaired, and rendered escaped
everywhere.

**Why.** Nothing resolves, matches, deduplicates or merges on it, and nothing
ever may: two people called "Sam" are two people. When it is absent the interface
shows a label built from how somebody reaches the workspace — "Anonymous
visitor", "WhatsApp customer" — never a name taken from something they said or
from a provider profile, which would be the platform asserting a fact about a
real person that nobody checked.

**Concurrency.** Setting it requires `If-Match`; a missing version is `428` and a
stale one is `412`, with no automatic retry.

---

## C9a. Who a conversation is with, on the inbox rows (Phase 37.4)

**Decision.** Every conversation list item and the conversation detail carry
`customer: { id, displayName, labelCode, reference, shortCode }`, under
`conversations.view` alone (plan decision D-4): the participant is part of the
conversation, and the WhatsApp detail already showed the masked number under
that permission. Opening the customer's profile still needs `customers.view`.

- `labelCode` is the C9 label rule as a code: `Named`, `WhatsAppCustomer`,
  `AnonymousVisitor`, `UnnamedCustomer` (`CustomerLabels.CodeFor`, the same
  rule as the English label the directory and reports use). The client words it
  in the reader's language; the API sends no sentence.
- `reference` is set on WhatsApp conversations only: the number that
  conversation runs on (`whatsapp_contacts`), masked by `WhatsAppCustomerId.Masked`
  straight after the read (`••••1234`). The raw `wa_id` leaves no method and no
  response.
- `shortCode` is six Crockford base32 characters (no I, L, O, U) derived from the
  **last 30 bits** of the customer id, the random end of a UUIDv7. It is never
  taken from the leading characters, which are a timestamp and would repeat for
  everybody who arrived in the same minute. It is stable (derived from an
  immutable id), needs no column and no backfill, and is **a label, never a
  key**: two customers of one workspace can share a code, so nothing resolves a
  customer by it. The client writes "Visitor 7KQ2MX" / «زائر 7KQ2MX».
- A conversation always names its canonical customer: a merge moves
  conversations in the same transaction (C6), so a merged source never appears.

**Cost.** A fixed number of statements per page: the customers' names, the
channels each has used (the label rule needs them), and, only when the page has a
WhatsApp conversation, the numbers. Never a query per row.

---

## C10. Future channels follow the same contract

Instagram and Messenger, when they arrive, add their own provider-specific
identity table keyed on `(tenant_id, <provider sender>)` with a composite foreign
key to `customers(tenant_id, id)`, resolved at their own transport boundary. They
do **not** add columns to `customers`, and they do not introduce a generic
identity table: a weak `(type, value)` bag would lose the per-provider uniqueness
that makes identity resolution a constraint rather than a hope.

---

## C11. Migration and backfill

One migration, `AddUnifiedCustomerIdentity`. The column arrives nullable, the
backfill runs, and only then is it made required and the foreign key added.

- **WhatsApp.** Every distinct `(workspace, sender)` in `whatsapp_contacts`
  becomes one customer and one identity row, dated from that person's earliest
  conversation. A person's older closed conversations and their current one land
  on the same record.
- **Everything else.** Each remaining conversation becomes its own customer,
  dated from its own start. That covers web chat — whose sessions are keyed on
  the conversation, so there is no evidence two were the same visitor — and any
  conversation with no channel mapping at all. An unmatched row is isolated
  rather than pooled with strangers, which is the safe direction.

Backfilled ids come from `gen_random_uuid()`. New customers use UUID version 7
for index locality; a backfilled row has no meaningful creation ordering to
encode, and inventing one would misstate when the platform learned it.

---

## C12. Out of scope, and deliberately so

CRM pipelines, leads, opportunities, deals, sales stages, notes, tasks, tags,
custom fields, email marketing, scoring, automated enrichment, automatic phone or
email matching, customer deletion, GDPR erasure workflows, external CRM sync,
Instagram, Messenger.

Customers consume no Phase 24 entitlement. There is no `MaxCustomers`, and no
plan, price or quota changed: identity is not something a workspace should be
charged for holding.
