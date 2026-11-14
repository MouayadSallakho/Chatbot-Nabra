# Analytics and usage foundation

Phase 22. Binding until explicitly revisited.

A workspace can see what it actually handled: how many conversations arrived,
how many messages each kind of sender sent, which channels and agents carried
them, how much happened each day, and how AI reply attempts ended. Every number
is counted from the operational records the platform already keeps.

---

## 1. Analytics is not billing

**Decision.** This phase reports **what happened**. It reports nothing about
**what it cost**.

Not implemented, and nothing on the screen implies otherwise: dollar cost,
invoices, subscription plans, token pricing, Meta or OpenAI pricing, spend
limits, credits, usage-based billing.

**Why.** The platform does not persist provider token counts, request durations
or prices. A dashboard showing "$0" or "0 tokens" would not be an approximation —
it would be a fabrication with a currency symbol on it, and somebody would make a
purchasing decision from it.

**Forbids.** Displaying a cost, a token count or an estimate derived from
anything other than persisted provider accounting. When that accounting exists,
it can be added; until then the absence is the honest answer.

The dashboard's own copy makes the boundary visible: *"Counted from what the
runtime recorded, not estimated."*

---

## 2. There is no analytics store

**Decision.** Analytics **queries the authoritative operational tables**. No
events are duplicated into a second system.

| Metric | Source of truth |
| --- | --- |
| Conversations started, open, handling | `conversations` |
| Customer / AI / human / system messages | `messages`, joined through `conversations` |
| AI reply attempts and outcomes | `conversation_turns` |
| Agent attribution (web chat) | `web_chat_sessions.ai_agent_id` |
| Agent attribution (WhatsApp) | `whatsapp_contacts` → `whatsapp_connections.ai_agent_id` |
| Agent names | `ai_agents` |
| WhatsApp delivery outcomes | `whatsapp_deliveries` |

**Why.** These tables already are the record of what happened. Copying them into
an analytics store would create two versions of the truth and a reconciliation
job to argue between them — and the first time they disagreed, nobody would know
which one was wrong.

**Forbids.** Kafka, RabbitMQ, a generic event bus, an event warehouse,
ClickHouse, Elasticsearch, an analytics microservice, and denormalised rollup
tables. PostgreSQL is sufficient at this scale and for these query shapes.

**New persisted data added: none.** Phase 22 created no table and no column.

---

## 3. The permission

One code: **`analytics.view`**.

There is deliberately no `analytics.manage`. Analytics has nothing to configure —
it is a read over records that other capabilities already govern — so a second
code would grant authority over nothing and leave an administrator wondering why
ticking it had no effect.

Owner receives it through the existing intrinsic behaviour: Owner authority *is*
`PlatformPermissions.AllCodes`, read at the moment the question is asked, so
adding the code extended every Owner in every workspace with no migration and no
backfill. Existing custom roles receive nothing automatically.

| Caller | Answer |
| --- | --- |
| Not signed in | `401` |
| Signed in, not a member | `404` |
| Member without `analytics.view` | `403` |
| Member with it, or Owner | `200` |

`404` rather than `403` for a non-member is the platform's standing rule:
somebody outside a workspace learns nothing about whether it exists.

---

## 4. Time

### Ranges

Three presets: **7, 30 and 90 days**, defaulting to 30. Anything else is `400`.

**Why a closed set.** Three presets are three query shapes whose cost is known in
advance. An arbitrary `?days=100000` would let anybody who can read analytics ask
the deployment to aggregate every row it has ever stored.

Refused rather than clamped: a caller who asked for 45 days and silently received
30 would draw conclusions about a period they did not choose.

### The window

Whole **local days of the workspace's zone**, **half-open**: `[start of the local
day n-1 days ago, now)`. The zone is `UTC` unless somebody chose another (Phase
37.6), and in UTC this is exactly the window it always was.

Half-open is the only boundary rule under which an event is counted exactly once
when two adjacent periods are compared. Both edges are tested against real rows —
an event on the first instant is in; one tick earlier is out.

### The workspace time zone (Phase 37.6)

**Decision.** Every day boundary is a local day of the workspace's zone
(`tenants.time_zone`, an IANA name, `UTC` by default), and the response says
which: `period.timeZone`.

**How a day is cut.**
- The window's edges are local midnights turned into instants by the runtime's
  zone rules (`WorkspaceTimeZone.StartOf`).
- Each row's local date is computed by PostgreSQL: `CAST(started_at AT TIME ZONE
  @zone AS date)` in the two daily statements. Nothing is materialised to be
  bucketed in C#, and there is still no rollup table (§2): stored daily totals
  would be right only for the zone they were cut in.
- So every IANA zone is served correctly, including half-hour and 45-minute
  offsets.

**Daylight saving.**
- A local day is usually 24 hours, and 23 or 25 on the days clocks change
  (`Europe/Berlin` on 25 October 2026 runs from 22:00 UTC to 23:00 UTC the next
  day). The window counts days by date, never by multiples of 24 hours, so no hour
  is lost or counted twice.
- Where clocks go forward *at* midnight, that midnight never happens, and the day
  starts at the first local minute that does. Where they go back across midnight,
  the day starts at the earlier of the two midnights. Tests check, for every day of
  2026 in ten zones, that a day's start falls on that day and the instant before it
  on the day before.

**Changing the setting.**
- It takes effect on the next read. Analytics stores nothing per zone, so there is
  nothing to recompute: the same rows are counted again in the new zone.
- A dashboard read just before and one just after a change describe different
  local days; each response says its own zone.
- Reports keep the zone they were computed in, and schedules move to the new zone's
  calendar (`reports-exports-scheduling.md` §9 and §11).
- The plan quota's months stay in UTC: they are a contractual period
  (`workspace-plans-entitlements-quotas.md`).

**The two zone databases.** Window edges use the runtime's tzdata and row dates
use PostgreSQL's. They agree as long as both carry the same rules for the zone;
a rule change shipped to one before the other could misplace events within an hour
of a changed boundary. The time-zone PUT accepts a name only when both know it.

### The previous period (Phase 37.6, row 31)

`compare=previous` adds `previous`: the same number of **whole** local days, ending
where the current window starts, in the same zone. The two are adjacent and
half-open, so an event on the boundary is counted once, in the current period.
The current window ends at the request instant, part-way through today, while the
previous one is complete; a client that shows a change should say "so far" for
today.

`previous` carries `period`, `summary` and `aiOperations` only: the period's
totals. The "open right now" figures (`current`) have no history to compare
against, which the plan leaves outside Phase 37. It costs three more counting
statements, read only when asked for.

### Analytics time is not transcript order

**Decision.** Analytics buckets a message by its persisted `SentAt`. A transcript
orders messages by **ingestion sequence**, forever.

These answer different questions. "How many messages on Tuesday" is about when
something was said. "What order did this conversation happen in" is about what
this platform received and when. A redelivered webhook carrying an old timestamp
must land in the earlier bucket and must **not** move in the transcript.

**Forbids.** Ordering a transcript by `SentAt`. Using SignalR event arrival time
as analytics truth.

A regression test writes two messages out of timestamp order and asserts both:
the buckets follow the timestamps, and the transcript still follows ingestion.

---

## 5. Metric definitions

Each is exact. A metric that could not be answered exactly was omitted rather
than estimated.

| Metric | Definition |
| --- | --- |
| Conversations started | Conversations whose `StartedAt` is in the window. |
| Customer messages | Messages with sender `Customer` and `SentAt` in the window. |
| AI replies | Sender `AiAgent`, same window rule. |
| Human replies | Sender `HumanAgent`, same window rule. |
| System messages | Sender `System`. Counted separately, never folded into the others. |
| Open conversations | Status `Open` **right now**, ignoring the window. |
| Handled by AI / by a person | Open conversations by `Handling`. |
| Daily activity | The four counts above, per UTC day, every day present. |
| Channel breakdown | The same, grouped by the conversation's `Channel`. |
| Agent breakdown | The same, per attributed agent, top 10 by conversations. |
| AI attempts / completed / failed / in progress | `conversation_turns` rows by `Status`, created in the window. |
| WhatsApp delivery | `whatsapp_deliveries` rows by `Status`, created in the window. |

**System messages are kept apart** because a handover note is neither a question
asked nor an answer given; adding it to either would overstate the work done.

**The snapshot ignores the window** on purpose. "Open right now" is a fact about
the present; restricting it to the last thirty days would hide the conversation
that has been waiting two months — exactly the one somebody needs to see. The
card is headed "Right now" and says so.

### AI operations are exact, and that is why they are included

The runtime creates a `ConversationTurn` before the provider is called and
settles it afterwards as `Completed` or `Failed`. Counting rows by status answers
the question with no inference. `InProgress` is the unsettled remainder — a turn
still running, or one whose process died mid-flight — reported rather than
silently dropped so the parts add up to the whole.

**Omitted, with the reason:** provider latency, token counts, retry counts and
cost. The runtime does not persist them. Publishing a guess would be worse than
publishing nothing.

### Channels

Only channels the workspace has actually used appear. A row of zeroes for
Instagram would advertise a channel this platform does not implement.

**Hosted and embedded web chat are not split.** `web_chat_sessions` records a
client mode, but a conversation's channel is `WebChat` either way and the
conversation is the unit being counted; inventing a split at the conversation
level from a session-level field would be a number that does not mean what it
says.

### Agents

A conversation's agent is **not stored on the conversation**. It is reached
through whichever channel opened it — a web chat session names the agent
directly; a WhatsApp conversation names it through the contact mapping to the
connection. Attribution is therefore two queries per metric, one per channel,
merged over a handful of agents.

Human replies are counted **in the agent's conversations**, not against the
agent: a person replying where an agent was handling is work on that agent's
watch, and the column heading says so.

An agent removed out from under its conversations is named "Removed agent"
rather than dropped, so the breakdown still adds up.

Bounded to ten. An unbounded list would grow the response with the workspace, and
the eleventh agent by volume is not what somebody opens a dashboard to find.

---

## 6. Tenant isolation

**Every query filters on the workspace.** `messages` and `conversation_turns`
carry no tenant column of their own, so they are reached **only** by joining
through `conversations`, which does. There is no path in the reader that touches
a message without having established which workspace owns its conversation.

Proved twice: an Infrastructure test builds two identically-shaped workspaces and
asserts the numbers describe one; an API test does the same over HTTP and also
asserts that asking for the other workspace by id returns `404`.

---

## 7. Query architecture

```
GetWorkspaceAnalyticsHandler  ->  IWorkspaceAnalyticsReader  ->  PostgreSQL
    (permission, window)              (one method)              (COUNT / GROUP BY)
```

`IWorkspaceAnalyticsReader` has **one method**: a dashboard is one screen and
should be one request. Eight endpoints would move the fan-out into the browser
and let the cards on one screen describe eight slightly different instants.

**Deliberately not** `IAnalyticsDatabase`, `IReportingRepository` or
`IGenericRepository`. A repository answers "give me this aggregate"; a dashboard
asks "count these rows", which is a different thing and is named as one.

**Aggregation happens in PostgreSQL.** Every count is a `COUNT` with a
`GROUP BY`; the rows that come back are one per day, per channel, per sender or
per agent. The merging done in C# is of those small grouped results — never of
the records behind them.

**Forbids.** Loading conversations or messages into memory to count them.
Per-agent or per-day lookups in a loop.

Two guards hold this in place: a test asserts that every statement touching
`conversations`, `messages`, `conversation_turns` or `whatsapp_deliveries`
contains `count(`, and that the statement count is bounded regardless of how much
data the workspace holds.

### Translation is verified against a real database

Earlier phases were bitten by expressions that looked fine and silently evaluated
on the client. Every query here is exercised against real PostgreSQL 17 with
pgvector, against rows a person could count by hand. Two translation defects were
caught that way and are described in the phase report.

Analytics does **not** depend on Redis for correctness.

---

## 8. HTTP surface

```
GET /api/tenants/{tenantId}/analytics/dashboard?range=30d
```

One route. It only reads: there is no `POST`, `PUT`, `PATCH` or `DELETE` under
this path and none is intended — a test asserts each returns `405`.

**No antiforgery filter**, because nothing here changes state. The shared filter
waves safe methods through anyway; leaving it off says plainly that this group
has no unsafe verb to protect.

**No entity tag**, because nothing is written: there is no version to present and
no precondition to forget.

The response carries `period`, `summary`, `current`, `dailyActivity`, `channels`,
`agents`, `aiOperations` and `whatsappDelivery` — the last being `null` for a
workspace that has never sent an outbound WhatsApp message, rather than a row of
zeroes somebody would go looking for an outage behind.

**Phase 37.6 (additive, opt-in where it changes the work done):**

```
GET /api/tenants/{tenantId}/analytics/dashboard?range=30d[&compare=previous]
```

| Member | Meaning |
| --- | --- |
| `period.timeZone` | The IANA zone every date in the response is a local date of; `UTC` unless chosen |
| `firstActivityOn` | `YYYY-MM-DD`, the local date of the workspace's first conversation ever, or `null` with none. One `min(started_at)` from `ix_conversations_tenant_started`. For "since" labels and honest empty states |
| `aiUsage.byPurpose` | Provider calls by purpose (`provider-usage-cost-accounting.md` §17). Present inside `aiUsage`, which stays `null` without calls |
| `previous` | Only with `compare=previous`: `{ period, summary, aiOperations }`. Absent otherwise — not `null`, absent — so the existing request's response gains no member it did not ask for |

`compare` other than `previous` (or empty) is `400 analytics.compare.invalid`,
checked before membership.

---

## 9. Privacy

**Analytics carries counts and safe dimensions. Nothing else.**

Never present, as a field or as a key: message text, knowledge text, customer
phone numbers, WhatsApp customer ids, provider message ids, employee emails,
access tokens, conversation credentials, realtime tickets, embeddings, filenames.

An agent's name is a label the workspace wrote and already reads on every other
screen.

**Not logged:** any customer or knowledge content. Verified against a running API
with real conversations in it.

A test asserts the raw response body contains none of the message text that was
seeded into the workspace, nor the words that would accompany it.

---

## 10. Indexes and the migration

**One migration, `AddAnalyticsIndexes`, containing two indexes and nothing else.**

| Index | Why |
| --- | --- |
| `conversations (tenant_id, started_at)` | Every dashboard query starts here. The existing index pairs the workspace with `last_activity_at`, which answers a different question. |
| `whatsapp_deliveries (tenant_id, created_at)` | The existing index leads with `status`, which is what the dispatcher asks. This table had **no index on its workspace column at all**. |

**Deliberately not added:** anything on `messages`, `conversation_turns`,
`web_chat_sessions` or `whatsapp_contacts`. Each is reached by a join whose key
is already the leading column of an existing index, from a conversation set the
two indexes above have narrowed. An index costs something on every write, and
these would buy nothing today.

No denormalised rollup table. No historical migration was edited.

A schema test asserts both indexes exist on the right columns, and a second
asserts that `messages` and `conversation_turns` still carry exactly the indexes
they had.

---

## 11. No caching

**Decision.** No Redis cache, no memoisation, no automatic polling.

**Why.** Analytics is a bounded read and correctness matters more than a cache
that could serve yesterday's numbers as today's. Refresh is a button; a page that
refreshed itself would move numbers under a reader mid-sentence.

If profiling ever shows a real need, a cache can be added with an invalidation
story. Adding one now would be solving a problem nobody has measured.

---

## 12. The screen

Replaces the Phase 10 placeholder. Period selector, four summary cards, a "Right
now" card, a daily chart, a channel breakdown, an agent table, and — only when
there is authoritative data — AI outcomes and WhatsApp delivery.

**No chart library was added, and no frontend package of any kind.** The chart is
CSS bars, and **it is not the only representation**: every value is repeated in a
table beneath it with real `<caption>`, `scope="col"` and `scope="row"`. The
chart is `aria-hidden`, because the table is the accessible representation rather
than a fallback for one.

**Series are named in words as well as coloured.** Every colour is a semantic
token, so the chart reads correctly in both themes; a hard-coded chart colour
would look deliberate in light mode and be unreadable in dark.

**The empty state is truthful.** A workspace with no activity shows real zeroes
and says "No activity in this period". No sample chart, no demo data.

The period control is a `fieldset` of radios rather than three buttons: it is one
setting with three values, which is what a screen reader should hear and what
arrow keys should move between. A polite off-screen live region announces what
arrived, without moving focus.

Navigation and the page respect `analytics.view`; the backend remains
authoritative and answers `403` regardless of what the client shows.

---

## 13. Overview cleanup

The Overview page claimed "Nothing below reports live data yet", which is no
longer true. That claim is gone, the per-panel copy no longer says the API is
unwired, and — for a reader holding `analytics.view` — a four-number, seven-day
strip now shows real figures with a link through to Analytics.

**Not a second dashboard.** The charts, channel split and agent breakdown live on
Analytics; duplicating them would be two places to keep in step and two places to
be wrong. A failed read of the strip is simply absent: an overview is not the
place to report that an optional summary did not load.

---

## 14. Deferred

- **Provider usage and cost accounting.** Token counts, latency and price are not
  persisted. When they are, cost reporting belongs beside these metrics — and
  must be labelled as accounting, not as analytics.
- **Exports and reports.** No CSV, Excel, PDF, AI-generated or scheduled reports.
  They build on this foundation later.
- **A comparison on the realtime "open right now" figures** (Phase 37 plan, row
  31). There is no stored history of open counts to compare against; it would need
  snapshots.
- **Hosted versus embedded web chat split**, pending a model that can distinguish
  them at the conversation level.
- **Retention and archival.** Ranges are bounded to 90 days, so the question of
  what happens to very old rows has not yet been forced.

## 15. Answered by the AI (Phase 39.1, BA39-1)

`summary.conversationsAnsweredByAi` (and, with `compare=previous`,
`previous.summary.conversationsAnsweredByAi`): of the conversations started in the
period (`conversationsStarted`), those **the AI replied in and no person replied in
or took over**. Counted as of the read: a person stepping in later takes a
conversation out.

- A person replied: a message from `HumanAgent`.
- A person took over: a `TakenOver` handling event (recorded since Phase 37.4), or the
  conversation being with a person now.
- One statement, beside the summary's two; each test is an index range
  (`ix_messages_conversation_ingestion`, `ix_conversation_handling_events_conversation`).
  No migration, no new event, no new index.

**Exact for everything the API recorded.** Handling events exist from migration
`AddConversationHandlingEvents` (Phase 37.4) on, and a deployment starts with every
migration applied, so on a deployment the figure is exact. Only a database that
already held conversations before that migration (the local development databases)
can hold a conversation that was taken over **and handed back** before it with no
reply from the person: nothing recorded that takeover, so it counts as answered by
the AI there.
