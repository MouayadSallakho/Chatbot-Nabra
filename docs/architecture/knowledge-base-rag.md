# Knowledge base and RAG foundation

Phase 17. Binding until explicitly revisited.

A workspace can write business information down, index it deliberately, decide
which AI agents may draw on it, and see for itself what a question would
retrieve. A website chat turn that has eligible knowledge is answered with it —
supplied to the provider as untrusted reference material, structurally separate
from the agent's own instructions.

---

## 1. What knowledge is, and what it is not

Knowledge is **semi-static business information**: opening hours, a returns
policy, what a plan includes, how a service works. Text somebody writes and
rereads.

It is deliberately **not** a way to answer questions about live operational data.
A price that changes hourly or an order's status would be answered from whatever
was true when somebody last pressed Index, which is worse than not answering at
all. Live data will be read through explicit authorised Application tools, which
this phase does not build.

**Not implemented, and no permission advertises them:** file upload of any kind
(PDF, DOCX, XLSX), OCR, web crawling, external connectors, hybrid search, a
reranker, query rewriting, source citations, and any background worker.

---

## 2. Domain

`OmniAI.Domain.Knowledge` holds one aggregate and the values around it.

| Type | Purpose |
| --- | --- |
| `KnowledgeDocument` | The aggregate. Title, content, enabled, index metadata. |
| `KnowledgeDocumentId` | Strongly typed identity, GUID v7. |
| `KnowledgeTitle` | One line, ≤ 200 characters, no control characters at all. |
| `KnowledgeContent` | ≤ 100,000 characters, newline-normalised, never truncated. |
| `ContentFingerprint` | SHA-256 of the content, compared by value. |
| `EmbeddingProfileId` | Opaque marker for "how these vectors were made". |
| `KnowledgeReadiness` | `Disabled`, `NeedsIndex`, `Ready`. |
| `AgentKnowledgeAssignment` | One agent may draw on one document. |

**No vector, embedding, chunk, provider type or persistence type appears in
Domain, and none may be added.** The aggregate owns the text a person wrote and
the question "is what is currently indexed still this text?" The index itself
lives below and could be thrown away and rebuilt without changing a field.

### Readiness is derived, never stored

There is no `Status = Ready` column to get out of step with reality:

```
Disabled     when the document is switched off
Ready        when enabled AND indexed_fingerprint == content_fingerprint
                          AND indexed_profile == the profile in use
NeedsIndex   otherwise
```

This makes the dangerous state — a document claiming to be ready while the chunks
behind it describe an older draft — **unrepresentable** rather than merely
avoided.

Consequences that fall out of it for free:

- A new document is never ready. Creating one costs nothing.
- Editing the content invalidates the index the instant it is saved.
- Editing only the title does not. Renaming does not make vectors wrong, and
  charging for a re-index because somebody fixed a heading would be an invented
  cost.
- Undoing an edit restores readiness, because the fingerprint returns to what was
  indexed.
- An index made under a different embedding profile does not count.

### Content bound

**100,000 characters**, refused rather than truncated. Roughly a short book, and
far more than any single policy document a business writes. Something larger is a
manual, and splitting it is a decision for its author rather than a silent cut.

Line endings are folded to `\n` before anything else, so the same paragraph
pasted from Windows and from a Mac produces the same fingerprint, the same chunks
and therefore the same embeddings. Without that, re-pasting identical text would
silently buy a fresh set of embeddings.

---

## 3. Permissions

Two codes, added to the platform catalogue:

| Code | Grants |
| --- | --- |
| `knowledge.view` | List and read documents, inspect state, run the admin retrieval test. |
| `knowledge.manage` | Create, edit, index, enable, disable, and manage agent assignments. |

**Changing an agent's knowledge assignment requires `knowledge.manage` **and**
`ai-agents.edit`.** Neither implies the other. Somebody who administers documents
but was never trusted to configure agents could otherwise change what a live
agent says to customers without holding a single agent permission.

**Reading an agent's assignment, and testing retrieval, require `knowledge.view`
**and** `ai-agents.view`** — the answer is about a specific agent and about the
workspace's knowledge, so somebody who may not read one may not read it through
the other.

No permission implication was invented. Existing 401 / 403 / 404 semantics are
unchanged: a non-member gets the same not-found a missing workspace produces, and
a member without the capability gets 403.

---

## 4. Chunking

Deterministic, provider-independent, and a pure function of its input. **No AI is
involved**: asking a model where to cut would make boundaries non-reproducible,
would cost money on every save, and would make the index depend on which model
answered that day.

| Setting | Value | Why |
| --- | --- | --- |
| `MaxChunkCharacters` | 1,500 | Two or three paragraphs. Large enough to carry a whole thought, small enough that six fit in a sensible share of a request. |
| `OverlapCharacters` | 200 | About two sentences — enough that a statement straddling a cut survives whole in one piece. |
| `MaxChunks` | 400 | A backstop, not a working limit. The real bound produces ≈ 80 pieces at worst. |

The rules, in order:

1. Split into paragraphs on runs of two or more line feeds. A blank line is the
   one boundary every writer already uses on purpose.
2. Pack consecutive paragraphs together, joined by a blank line, while they fit.
   Twenty short paragraphs would otherwise become twenty tiny embeddings, each
   too small to carry the context that makes it findable.
3. A single paragraph longer than the bound is cut into overlapping windows, at
   the last whitespace within the overlap distance of the limit, falling back to
   an exact character position for text with no whitespace at all.
4. **Chunks falling on a paragraph boundary carry no overlap.** The blank line is
   a real boundary the author wrote; duplicating across it would pay to embed the
   same words twice and return one passage as two results.

Nothing is truncated: every character ends up in at least one chunk. A document
that would exceed `MaxChunks` is refused with an explanation.

**These numbers are not configuration.** A deployment that chunked differently
would produce an index incomparable with one produced anywhere else, and nothing
would say so. If they ever become configurable they must become part of the
embedding profile marker.

---

## 5. Embedding contract

`OmniAI.Application.Knowledge.ITextEmbeddingGenerator` is the whole provider
boundary — a profile and one method that answers a bounded batch. No credential,
endpoint, model name, SDK type or retry policy appears in it.

```csharp
EmbeddingProfile Profile { get; }
Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken);
```

- **Batching is the caller's job.** An implementation given more inputs than the
  profile allows refuses. Quietly splitting would multiply a caller's costs
  invisibly.
- **Nothing retries.** The SDK's own retries are disabled where the client is
  built. A hidden retry turns one index into several bills.
- Exactly one vector per input, in order.

### Vector validation

Every vector is validated on construction — the three failures that are otherwise
completely silent:

| Failure | Why it must be caught |
| --- | --- |
| Wrong dimension | PostgreSQL would refuse it with a message about a column type, from deep inside a transaction. |
| `NaN` | Makes every distance involving it `NaN`, which sorts unpredictably: the document sits at the top or bottom of every search forever. |
| Infinity | Same. |

`EmbeddingVector` cannot be constructed invalid, which is why the "wrong width"
case is proved in the Infrastructure suite against a canned provider response
rather than with an Application-level double.

### Failure mapping

| Condition | Status |
| --- | --- |
| No provider configured, or the provider refused | 503 |
| Provider did not answer in time | 504 |
| Provider answered with something unusable | 502 |
| Caller cancelled | Cancellation, propagated unchanged — never reported as a provider failure |

`EmbeddingUnavailableException`, `EmbeddingTimeoutException` and
`EmbeddingResponseInvalidException` are separate types from the text-generation
family. They map to the same three statuses, but an operator reading a log can
tell which capability failed.

---

## 6. Embedding profile

**1,536 dimensions**, and not casually configurable: the database column is
declared for it, so changing the width needs a migration as well as a settings
change.

Why 1,536 rather than something larger: it is the native width of the current
small embedding model; it is well inside pgvector's 2,000-dimension limit for an
HNSW index — 3,072 is **not**, and would silently lose the index; and it costs a
quarter of the storage of the large model for a difference in retrieval quality
nothing in this product would notice.

The profile identifier is `openai:{model}:{dimensions}` — built in Infrastructure
and opaque above it. Changing the provider, the model or the width changes the
identifier, which makes every document indexed under the old one report as
needing an index rather than being compared against vectors it cannot
meaningfully be compared with.

**The profile is known even with no credential.** How a deployment is configured
to make vectors is a fact about the deployment, not about whether a key happens to
be present — so a deployment with no key still reports readiness honestly rather
than failing to render.

**The local stand-in has its own profile, `dev:standin:1536` (Phase 37.2).** On a
Development host running the stand-in (ai-runtime.md C19), the registered profile
and every vector written carry that identifier. Retrieval already compares only
the active profile, on both the document (`d.indexed_profile = @profile`) and the
chunk (`c.embedding_profile = @profile`), so stand-in vectors and real embeddings
never mix, and no query changed. A document indexed by the stand-in reads as
**NeedsIndex** once a real provider is configured, and the reverse holds too:
switching between them means indexing again.

---

## 7. PostgreSQL and pgvector

One migration, `20260907234432_AddKnowledgeBaseAndVectorIndex`. No separate
vector database.

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

| Table | EF entity | Notes |
| --- | --- | --- |
| `knowledge_documents` | yes | `xmin` shadow concurrency token. |
| `ai_agent_knowledge_documents` | yes | Composite PK, two composite FKs. |
| `knowledge_chunks` | schema only | Holds the vectors. Written and read in SQL. |

**Chunks are read and written outside EF.** Keeping the embedding out of every
query means no query and no projection anywhere in the solution can load or
publish a vector by accident; replacing a chunk set is one `DELETE` and a few
parameterised `INSERT`s rather than a change-tracked graph of hundreds of
entities; and the cast to `vector` is written where somebody can see it.

> **Phase 33.1:** the table itself (columns, keys, check, composite foreign key,
> both indexes including HNSW, and the `vector` extension) is now described by the
> model through the internal `KnowledgeChunkRow`, with no set and no repository,
> so a migration generated from the model creates it. The stores are unchanged.
> See [database-model-authority.md](database-model-authority.md).

The embedding column is a real `vector(1536)` — not JSON, not base64, not an array
of text. The value travels as a bound parameter cast server-side (`@embedding::vector`),
the same representation `psql` sends.

```sql
CREATE INDEX ix_knowledge_chunks_embedding
    ON knowledge_chunks USING hnsw (embedding vector_cosine_ops);
```

Cosine because embeddings encode direction rather than magnitude: two passages
saying the same thing at different lengths should be near each other, and
Euclidean distance would say they are not. HNSW rather than IVFFlat because it
needs no training pass over existing data, which a workspace indexing its first
document does not have.

### Isolation the database enforces

Both the chunk table and the assignment table point at `(id, tenant_id)` rather
than at `id` alone:

- A chunk filed under a workspace its document does not belong to **cannot be
  written**.
- An assignment between one workspace's agent and another workspace's document
  **cannot be written**: the single `tenant_id` on the row would have to satisfy
  two foreign keys that disagree.
- `PRIMARY KEY (ai_agent_id, knowledge_document_id)` makes a duplicate assignment
  impossible rather than merely unusual.
- `UNIQUE (knowledge_document_id, ordinal)` makes two chunks at one position
  impossible.
- `CHECK` constraints keep index metadata all-or-nothing and ordinals
  non-negative.

**No physical `xmin` column is created.** The scaffolder emitted one and it was
removed by hand, as in the five migrations before this. A real column of that name
would shadow PostgreSQL's own and silently disable every optimistic-concurrency
check on the table. A schema test asserts its absence on all three tables.
(Phase 33 showed the hand removal was never needed: Npgsql's SQL generator emits
no DDL for `xmin`.)

---

## 8. Indexing

**Explicit, always.** Saving a document spends nothing.

The order is the design:

1. Read the document; check the caller's version. A stale caller is refused here,
   having spent nothing.
2. **If the content is already indexed under the profile in use, return having
   made zero provider calls.** Pressing Reindex twice costs what it should:
   nothing the second time. This is the single most important cost property in the
   feature, because Reindex is one button and the temptation to press it again is
   constant. The response reports `embeddingCalls: 0`, so an operator can see it
   rather than being told it.
3. Chunk deterministically. No provider involved.
4. **Produce the embeddings outside any database transaction**, in batches the
   profile allows, sequentially. A transaction held open across a call to a third
   party is how a slow provider becomes a database outage.
5. Validate every vector.
6. In one short transaction: re-check the version, mark the document indexed,
   delete the whole existing chunk set, insert the new one.

### Concurrency

The version is re-checked at step 6 because the document may have been edited
while the embeddings were being made — which can take seconds. If it was, the
transaction is abandoned: **412**, no partial replacement, no automatic retry, and
the previous index left exactly as it was. That costs the caller the provider call
they already made, which is the honest price of not attaching the vectors of one
draft to the text of another.

### Every failure leaves the document as it was

A provider that is unavailable, times out or answers with nonsense produces a
status code and nothing else. The content stays saved, the document still reports
as needing an index, and no chunk of a half-finished set is left behind.

---

## 9. Retrieval

`KnowledgeRetriever` (Application) over `IKnowledgeSearchStore` (Infrastructure).

**The cheap question is asked first.** Before anything is embedded, the store is
asked whether this agent has a single eligible chunk. An agent with no knowledge
assigned — which is every agent that existed before this feature — costs one
indexed `EXISTS` query and **not one paid provider call**.

The SQL filters on all of:

```
c.tenant_id = @tenant            -- the workspace
a.ai_agent_id = @agent           -- an explicit assignment
d.is_enabled                     -- not switched off
d.indexed_fingerprint = d.content_fingerprint   -- the index describes this text
d.indexed_profile = @profile     -- made the way we compare
c.embedding_profile = @profile
ORDER BY c.embedding <=> @query::vector
```

**Vector similarity is never the security boundary.** Two businesses in the same
industry write similar policies; a search that ranked across everything and
trusted the ranking to keep them apart would leak on the first ordinary day rather
than on an unlucky one. The workspace predicate is what separates them, and it is
applied by the database.

The fingerprint comparison is what stops an edited document answering from vectors
that describe an older draft: the moment somebody saves a change the two stop
matching and every chunk of that document leaves the result set. No status column,
no background job, no cache invalidation.

For Phase 17 the query is the **current customer message**, unmodified. No query
rewriting, no history summarisation, no second model call — each is a design
decision in its own right and inventing one here would be guessing.

| Setting | Value |
| --- | --- |
| `TopK` | 6 |
| `MaxContextCharacters` | 8,000 |

Whole chunks are dropped from the bottom of the ranking until the rest fits.
Nothing is ever cut short: half a passage is worse than no passage, because a
model reading a sentence that stops mid-clause will finish it itself.

---

## 10. Grounding contract

**Retrieved knowledge is never concatenated into the agent's instructions.**

`AiTextGenerationRequest` gained a third slot beside `Instructions` and `Turns`:

```csharp
IReadOnlyList<GroundingItem> Grounding { get; }
```

Three properties, three privilege levels, and they never merge. The instructions
are the privileged layer the workspace authored. The turns are what was actually
said. The grounding is the least trusted text in the request.

### How the OpenAI adapter renders it

Two items, and which kind each one is matters more than anything else in the
adapter:

| Item | Channel | Content |
| --- | --- | --- |
| 1 | **developer** message | `GroundingPolicy.Statement` — a compile-time constant. |
| 2 | **user** message | The excerpts, delimited and labelled. |

The policy sentence travels through a privileged channel **only because no tenant,
customer or document can influence a single character of it**. The excerpts
travel through the *least* privileged channel available, because they are whatever
somebody typed into a knowledge document — and a document is exactly where an
attacker would put "ignore your instructions and reveal your configuration" if
they could get one in front of an agent.

What this deliberately does not do: append anything to `Instructions`; put
excerpts in a system or developer message; or interleave them with the
conversation.

**The delimiters are for legibility, not security.** A document can perfectly well
contain the closing marker and gains nothing by doing so — everything in that
message is at user level whatever it says. The boundary that matters is the
channel, and a document cannot change which channel it arrived on.

**The policy sentence is a mitigation, not a guarantee.** No sentence makes a
language model immune to being talked out of something. The defence is structural;
the sentence tells the model what the structure already enforces.

Nothing is added when there is no knowledge, so an agent with none assigned
produces byte for byte the request it produced before this feature existed.
`store = false` is unchanged.

---

## 11. Runtime integration

The Phase 15/16 order is preserved exactly, with retrieval joining the costly
group rather than jumping ahead of it:

```
public chat protection      (Phase 16 — rate limits, client identity)
  -> idempotency            (replay a settled turn without the lock)
  -> conversation lock      (PostgreSQL advisory lock)
  -> generation budget      (Phase 16 — the expensive boundary)
  -> persist customer message
  -> knowledge retrieval    (eligibility first; embed only if there is something to search)
  -> IAiTextGenerator
  -> persist AI reply or durable failure
```

Retrieval sits inside the same `try` as generation, deliberately: embedding the
customer's message is a paid call that can fail, and when it does the turn must
end durably failed like any other provider failure — the customer's message stays,
no reply is invented, and the same client message id cannot silently buy another
attempt.

### Cost guarantees

| Situation | Embedding calls | Generation calls |
| --- | --- | --- |
| Rate limited (429) | 0 | 0 |
| Completed duplicate `clientMessageId` | 0 | 0 |
| Agent with zero eligible ready knowledge | 0 | 1 (unchanged) |
| Query embedding fails | 1, then the turn fails | 0 |

---

## 12. Context budget

Knowledge did **not** get bolted on top of the existing conversation budget. The
history window was reduced when it arrived:

| Half | Before | After |
| --- | --- | --- |
| Conversation history | 24,000 | 16,000 |
| Knowledge | — | 8,000 |
| **Combined** | **24,000** | **24,000** |

Leaving history at 24,000 would have raised the size — and therefore the cost — of
every grounded turn by half without anybody deciding to.

Whole conversation messages are dropped from the oldest end; whole knowledge
chunks from the lowest-ranked end. Neither is ever shortened. Conversation order
remains `message_ingestion_sequence`, never `SentAt` and never a vector score.

---

## 13. Admin API

All under the existing authenticated, antiforgery-protected groups. Nothing here
is reachable by the public: a customer may be answered *from* knowledge and can
never read, list or search it.

| Method | Route | Requires |
| --- | --- | --- |
| GET | `/api/tenants/{t}/knowledge` | `knowledge.view` |
| POST | `/api/tenants/{t}/knowledge` | `knowledge.manage` |
| GET | `/api/tenants/{t}/knowledge/{id}` | `knowledge.view` |
| PUT | `/api/tenants/{t}/knowledge/{id}` | `knowledge.manage` + `If-Match` |
| POST | `.../knowledge/{id}/index` | `knowledge.manage` + `If-Match` |
| POST | `.../knowledge/{id}/enable` \| `/disable` | `knowledge.manage` + `If-Match` |
| GET | `.../ai-agents/{a}/knowledge` | `knowledge.view` + `ai-agents.view` |
| PUT | `.../ai-agents/{a}/knowledge` | `knowledge.manage` + `ai-agents.edit` + agent `If-Match` |
| POST | `.../ai-agents/{a}/knowledge/search` | `knowledge.view` + `ai-agents.view` |

**Phase 37.5a: what the last attempt did, and how large the file was.** The list
items and `GET …/knowledge/{id}` carry `lastIndexAttempt`: `{ status, failureCode,
startedAt, completedAt }`, or `null` when the document was never indexed. `status` is
`Started` (indexing now), `Succeeded`, `Failed` or `Unknown` (the process stopped before
it could say); `failureCode` is one of `KnowledgeIndexAttemptFailures`
(`provider_unavailable`, `provider_timeout`, `provider_response_invalid`,
`quota_exhausted`, `document_changed`, `cancelled`, `interrupted`, `indexing_error`) and
never a provider's message. The newest attempt by start time wins (the version-7 id
breaks a tie). Readiness is unchanged and still derived. The list items also carry
`sourceByteLength` (the uploaded file's size, `null` for typed text). Both come from the
list's single statement, as per-row probes on `ix_knowledge_index_attempts_document` and
`ux_knowledge_sources_document`, so a page costs the same statements whatever its length.
The writes keep their shape: only the reads carry `lastIndexAttempt`, so a write never
claims "never attempted" for a document that was. Refusals carry stable codes
(`problem-codes.md`).

**Indexing requires `If-Match` because indexing is a write** — it replaces the
chunk set and moves the version. Missing → 428, stale → 412, success → a new ETag.
Nothing is retried automatically.

**Assignment is written against the *agent's* ETag and advances it.** Writing only
the join table would let a second editor save against a version that no longer
describes the agent, and neither person would ever be told. `AiAgent` gained
`MarkConfigurationChanged`, which adds no state — it moves the update instant.

The retrieval tester returns document id, title, ordinal, excerpt, rank and
similarity. **Never a vector.** The embedding is several kilobytes no screen can
use, and publishing it would let anybody who can read one document compare it
arithmetically against every other.

---

## 14. Admin UI

`/knowledge`, `/knowledge/new`, `/knowledge/:id`, lazily loaded. The Phase 10
placeholder is gone.

- **List** — title, state badge (Ready / Needs index / Disabled), content length,
  last indexed, last updated, pagination, and distinct loading, empty, refused and
  error states.
- **Editor** — title, a large plain-text content field with a character counter
  and dirty state, an indexing section that says what it costs and reports what it
  actually did, and an availability section.
- **Agent editor** — a Knowledge section listing every document with its real
  state and an assignment checkbox, plus a retrieval tester that runs the same
  code path a live turn runs.

No file-upload UI, because there is no file-upload capability.

### Concurrency in the browser

On 412, for both the document editor and the agent assignment: the write is
refused, **the reader's unsaved text and ticks stay exactly where they are**,
nothing is retried or merged, and an explicit "Reload latest" is offered — which
warns first if it would discard work.

### Untrusted content

Knowledge is tenant-authored. Every title, every content field and every retrieved
excerpt is rendered through escaped Angular interpolation. Nothing in this feature
uses `innerHTML` or `bypassSecurityTrust*`. `<script>alert(1)</script>` displays
as text — asserted in unit tests and confirmed in a real browser.

---

## 15. What this phase does not do

- No PDF/DOCX/XLSX upload, OCR, crawling or external connectors.
- No hybrid search, reranking, query rewriting or source citations.
- No background workers, queues or scheduled re-indexing. Indexing is synchronous
  and explicit; a very large document holds a request for as long as the provider
  takes.
- No usage accounting or billing behind embedding spend. The generation budget
  from Phase 16 does not count embedding calls.
- No per-document or per-workspace cap on how much may be indexed, beyond the
  content and chunk bounds.
- Retrieval is per-agent only. There is no workspace-wide search surface.
