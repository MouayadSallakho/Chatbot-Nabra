# Phase 37.0: Backend plan

Plan only. No product code, model or migration changed in this step. From 37.1 on, this document decides which item belongs to which step, and which migrations may be generated.

## 1. Summary

- Nothing changes yet for the people who use Nabra. This step measures today's backend and plans Phase 37.
- The 34 backend asks, plus your three decisions, are each placed in a step. Two asks collide with an existing security rule (21 coded origin refusals, 20 the workspace name on public reads), and one half-ask has no data behind it (31's realtime comparison). The plan says how each is handled.
- Phase 37 needs **8 schema migrations and no data migrations**: 3 for accounts and email, 3 for the inbox, 2 for time zones. Each one only adds tables, nullable columns, or columns with a default.
- 37.3 needs four decisions from you (§4.1). The biggest is about invitation emails, which do not exist today.
- 37.5 is too large for one session, so it is split into 37.5a (knowledge, agents, channels, public chat) and 37.5b (team, permissions, billing, error codes, session check).

## 2. Starting point

| Fact | Value |
| --- | --- |
| Branch | `master` |
| HEAD | `edab918` feat: prove the redesign live, and keep the Overview still |
| Worktree | clean (`git status --short` printed nothing) |
| Backend changes since Phase 35 (`d2c907d`) | none (`git log d2c907d..HEAD -- backend` is empty), so the running dev API matches HEAD |
| Migrations | `20260925225105_Initial`, `20260926215858_ProductionReadinessHardening`; both applied to `omniai_p36` |
| Dev stack | `omniai-p36-api` (127.0.0.1:5027), `omniai_pg25`, `omniai_redis18`, dev server on :4200, all running; canary clean |

## 3. Changes by area

`git status --short` after this step:

```
?? docs/phase-reports/PHASE_37_0_BACKEND_PLAN_REPORT.md
```

That file is the only repository change. Evidence and scratch scripts are in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-0\`.

### 3.1 How the plan was built

- Every Phase 36 report's "Backend asks" and "Risks" sections were read, along with the architecture documents the prompt names.
- The code was surveyed area by area, with file:line citations. The survey notes are in `<evidence>\p37-0\scratch\notes.md`.
- Every endpoint the plan extends was checked against the frontend `*-api.ts` file that calls it, so each change is additive to what the client actually reads.

Facts below without a path come from that survey. The ones that shape the plan most:

| Fact | Where | Consequence |
| --- | --- | --- |
| Identity registers **no token providers** and no `options.Tokens` | `AuthenticationSetup.cs:75-80, 255-277` | A dedicated reset provider changes no other token's lifetime, because no other token exists |
| Data-protection keys live in `data_protection_keys` (custom `PostgresXmlRepository`), application name `OmniAI`, encrypted under `OMNIAI_SECRET_MASTER_KEY`; `api-a` and `api-b` share the database and master key through the `x-api` anchor | `DataProtectionSetup.cs:33-57`; `deploy/compose.production.yml:18-75, 194-208` | A reset token issued by one production API validates on the other |
| No audit log, no sign-in rate limit, no server-side session table, no "sign out everywhere" | auth survey; `authentication-tenant-membership.md:805-814` | 37.3 must add a security-event table and a shared limiter |
| `InvariantGlobalization=true` for the whole backend | `backend/Directory.Build.props:12` | No `CultureInfo("ar")`. Arabic text comes from code catalogues; digits stay Western (localisation decision 3), so invariant formatting is correct |
| Runtime image `aspnet:10.0` is Ubuntu 24.04 with `tzdata 2026c`; PostgreSQL resolves `Asia/Riyadh` | checked in this step | IANA time zones work without touching `deploy/` |
| **Invitations are never emailed** (the raw token is returned once to the inviter), and **scheduled reports have no recipients** (only an in-app/email notification to the schedule owner) | `CreateTenantInvitationHandler.cs:183-197`; `ReportSchedule.cs`; `reports-exports-scheduling.md:246` | "Recipients without an account" do not exist today; see decision D-1 |
| Operational emails: 13 notification kinds, outbox `notification_emails` (no address, subject or body stored), rendered at send time, English only, subject `"OmniAI: " + title` | `NotificationCatalog.cs:58-148`; `OperationalEmailRenderer.cs` | Bilingual emails need no new column; the only varying input is the report type, derivable by join |
| `operational-notifications.md:236-243`: "No token, session, key or invitation ever appears in a link" | doc | A reset email must be a separate *account* email, with its own documented rule |
| Analytics reads raw rows at query time; rollups are forbidden; days are bucketed with `DateOnly.FromDateTime(x.UtcDateTime)` | `analytics-usage-foundation.md` §2; `WorkspaceAnalyticsReader.cs:179-215` | A workspace time zone can be served exactly, including daylight saving |
| `ai_provider_usages.purpose` exists and is required, but the retrieval tester is recorded as `ConversationRetrievalEmbedding`, like a live reply | `KnowledgeRetriever.cs:99`; `SearchAgentKnowledgeHandler.cs:98-100` | "By purpose" needs one new purpose value, not a column |
| No CHECK constraint on any string-enum column (purpose, provider, turn status, launcher position) | `Configurations/*` | New enum values need no migration |
| `tenants` has only `id, name, slug, status`; no update endpoint; no `workspace.manage` | `Tenant.cs:14-86`; `TenantEndpoints.cs:52-55` | A time zone needs a column, an endpoint and a permission. Owner derives `AllCodes`, so a new code needs no data step |
| Embed refusals are one indistinguishable 404 by design; public reads deliberately omit the workspace | `embeddable-web-chat-widget.md` §10; `WidgetContracts.cs:9-12`; `HostedChatEndpoints.cs:116-117` | Asks 20 and 21 collide with these rules; see decisions D-3 and the row for 21 |
| Takeover and return persist only `handling`; no event table; `System` messages are part of the public transcript | `ConversationHandoffHandler.cs:82-151`; `Conversation.cs:238-245` | Handoff events need their own table and must never be stored as messages |
| No member-removal operation exists; reply authors reference `tenant_memberships` with `RESTRICT` | `TeamEndpoints.cs:69-87`; `HumanReplyConfiguration.cs:100-102` | A handoff actor can use the same composite key safely today |

### 3.2 The plan: one row per item

**Legend.** Effort: S = under half a day, M = about a day, L = several days. "Derived" means computed in the query from existing columns. Every new field is additive and every new parameter is optional.

#### Owner decisions

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| O-1 | Phase 37 covers the full list in priority order; records search is outside | All | — | — | — | — | — |
| O-2 | Local AI stand-in for development | 37.2 | See item 5 | — | none | See item 5 | M; leaking into production (guarded three ways) |
| O-3 | Each user's language stored on the server | 37.3 | See item 7 | new `users.language` | Migration M1 | See item 7 | S; existing users have none until they sign in or choose |

#### Release blockers (37.1)

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | NuGet audit fails on a clean restore | 37.1 | none | — | Pin in `Directory.Packages.props` (central transitive pinning is on, `:9`): **`System.Security.Cryptography.Pkcs` 10.0.13** (from MimeKit 4.18.1; GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7 cover 10.0.0–10.0.12) and **`MessagePack` 2.5.307** (from `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.11; GHSA-qhrr-8q5h-9q3h is fixed in 2.5.303, so this stays on 2.x, no major bump) | Clean restore **without** `-p:NuGetAudit=false`; full suite; `dotnet list package --vulnerable` clean; the unchanged `deploy/` Dockerfiles build (throwaway tags, removed after); the SignalR Redis backplane tests | S; MessagePack is used only inside the backplane, and a patch release is low risk. The patched versions come from the advisories and are confirmed by the restore in 37.1 |
| 2 | A failed AI turn is never announced | 37.1 | No contract change: the existing `conversationChanged(id)` event now also fires after `FailTurnAsync` | — | none. In `CustomerTurnRunner.AnswerAsync` (catch, `:249-256`), call `Announce` after `FailTurnAsync` and before `throw`, with `CancellationToken.None`, through the never-throwing `AnnounceQuietlyAsync` | Application unit test (recording notifier sees one announcement on failure, none twice); API integration test (an inbox hub client receives `conversationChanged` when generation fails); replay and lock tests stay green | S; none (the call cannot throw) |
| 3 | "OmniAI" visible to users | 37.1 | Values only, no shape change. Email subject `Nabra: …`; header `Nabra`; footer "your Nabra notification preferences"; "Open Nabra"; `FromName` default `Nabra`; PDF `Creator = "Nabra reports"`, `Producer = "Nabra"`. The install snippet the API returns now carries `data-nabra-key`; `v1.js` accepts **both** `data-nabra-key` and `data-omniai-key`, so sites already embedding keep working. The loader's frame title follows the host page's `<html lang>` (`ar*` → Arabic, otherwise English), with words from `GLOSSARY.md` | — | none | Catalogue and renderer tests updated; a repository test that no user-facing literal in `backend/src` (excluding namespaces, keys, headers, cookies, migrations) contains "OmniAI"; loader test for both attributes and both titles | S; an Arabic email body arrives only in 37.3, so 37.1 is English with the right name |
| 4 | Synthetic password committed | 37.1 | none | — | none. Remove it from `PHASE_36_1_BASELINE_REPORT.md` (it is in **§9 at line 192**, not §6) and from the defaults in **11** tools (`tools/performance/*.mjs` ×9, `seed/seed.mjs`, `k6/lib.js`). The tools then require `SEED_PASSWORD` / `PASSWORD` and exit with a message when it is absent | `git grep` for the value finds nothing; each tool exits non-zero without the variable | S; the value stays in Git history (synthetic, local-only; history is never rewritten) |

#### Development tooling (37.2)

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 5 | No AI path can be tested locally | 37.2 | none. A dev-only, in-process stand-in implements `IAiTextGenerator` and `ITextEmbeddingGenerator`. It is registered only when **all** hold: environment is Development, `OMNIAI_DEV_AI_STANDIN=1`, and no live OpenAI opt-in. Startup **refuses** outside Development if the variable is set. Replies are deterministic, built from the customer's last message and the grounding titles, in Arabic when the message is Arabic. A message containing `[[fail]]` throws `AiRuntimeUnavailableException`, so the failed-turn path is testable. Embeddings are deterministic feature-hashed 1536-dimension unit vectors, so retrieval ranks lexically similar chunks first. The embedding profile id is `dev:standin:1536`, so documents indexed by the stand-in count as stale for OpenAI | — | none. A new enum value `AiProvider.DevelopmentStandIn` (string column, no check constraint). The stand-in records ledger rows, unrated, so usage-by-purpose and quotas work locally | Unit: determinism, `[[fail]]`, Arabic reply, vector norm and dimensions. Registration matrix: (Development, flag) → stand-in; (Production, flag) → startup failure; (Development, flag, live opt-in) → startup failure; no flag → today's behaviour. Integration: a web-chat turn, an indexing run, a preview and a retrieval test all succeed against the stand-in | M; leaking into production is guarded by the environment check, the startup refusal and a test. The provider canary keeps passing (the flag is not a provider secret) |

#### Accounts and email (37.3)

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 6 | Password reset does not exist | 37.3 | `POST /api/auth/password/forgot` `{ email }` → **202** always (429 `auth.throttled` with `Retry-After`; 503 `auth.throttle.unavailable` when Redis is down). `POST /api/auth/password/reset` `{ userId, token, newPassword }` → **204**; 400 `auth.reset.invalid` (bad, expired, used token or unknown user, all identical); 400 `auth.password.*` per Identity error, only after the token validated. Both anonymous, antiforgery-checked like the other anonymous POSTs. Two entry pages, `/forgot-password` and `/reset-password`, replace the "not available yet" note on sign-in | new `account_emails`, new `security_events`; reuses `users.security_stamp` | Migrations M2, M3 | See §3.5 | L; enumeration and session-revocation correctness |
| 7 | The server does not know each user's language | 37.3 | Optional `language` (`"en"`/`"ar"`) on `POST /api/auth/register` and `POST /api/invitations/accept`, falling back to `Accept-Language`, then unset. On `POST /api/auth/login` it fills the value only while unset. New `language` (nullable) on `GET /api/auth/me`. New `PUT /api/me/language` `{ language }` → 204 (signed in, antiforgery). An unsupported value is 400 `language.unsupported`. **The frontend wiring ships in 37.3 itself (owner decision, §4.3):** the client sends the language on sign-up and invitation acceptance, and calls the PUT when a signed-in member switches | new `users.language` | **M1** | Unit: header parsing (`ar-SA;q=0.9` → `ar`); API: each entry point sets it, login fills only when unset, PUT changes it, `me` returns it, unsupported → 400 with code | S; existing users stay unset until they sign in or choose |
| 8 | Notification emails, report emails and invitations in English and Arabic | 37.3 | none on the API. Every operational email and the reset email render in the recipient's language at send time (`users.language`, else English). See §3.4 | `users.language`; report type derived (`notifications.occurrence_id → notification_occurrences.source_id → report_executions.report_type`) | none beyond M1 | Every kind has an English and an Arabic text (completeness test); HTML `lang`/`dir`; plain-text part; subject a constant per (kind, language); workspace names isolated; Mailpit check in Chrome | M; Arabic copy needs a native reading. Invitation emails are added by row 8a (D-1 option A) |
| 8a | Invitations are emailed (D-1 option A) | 37.3 | Optional `language` (`"en"`/`"ar"`) on `POST …/invitations`, defaulting to the inviter's `users.language`, else `en`; the invitation keeps it. When the invitation is created, one invitation email is queued in `account_emails` (kind `Invitation`, linked to the invitation) and rendered in the invitation's language. The email's token is minted at send time and only its SHA-256 is stored (`email_token_hash`). Acceptance matches either the copied link's token or the emailed token, in constant time, so the link the inviter copies keeps working. Like the reset link, the token travels in the URL fragment and is never logged or stored in clear. Revocation and expiry end both tokens. The create response is unchanged apart from the optional field | `tenant_invitations.language`, `tenant_invitations.email_token_hash`; `account_emails` linked to the invitation | **M2a** (approved) | API: language default and explicit; email queued once per invitation; either token accepts, a wrong one refuses identically; revoked and expired refuse both; nothing about either token in logs or audit rows; Mailpit message in en and ar (screenshot without the link) | M; a second token per invitation, hence its own security review in 37.3 |
| 6a | Sign-in rate limiting (D-2) | 37.3 | `POST /api/auth/login` → 429 `auth.throttled` with `Retry-After`, and 503 `auth.throttle.unavailable` when Redis is down (fail closed), through the limiter built for row 6: per address (keyed by the same HMAC of the normalised address, counted for unknown addresses too) and per client network. Limits are configuration with proposed defaults of 10 per 15 minutes per address and 50 per 15 minutes per network, confirmed by measurement in 37.3. A throttled attempt writes `auth.signin.throttled` to `security_events` (M3) | the Redis limiter of row 6 | none beyond M3 | API: each limit, unknown and known addresses throttled alike, `Retry-After`, Redis down → 503, a successful sign-in below the limit unchanged | S; locking out a real member by someone else's attempts (bounded by the per-address window) |
| 6b | Stable codes on sign-in and registration refusals (D-2) | 37.3 | A `code` on every refusal of `POST /api/auth/login` and `POST /api/auth/register`, through the same mechanism as row 28. `auth.credentials.invalid` stays one code for an unknown address and a wrong password. Registration's password refusals use `auth.password.*` per Identity error, as row 6. The exact list is taken from today's refusals in 37.3 and added to `problem-codes.md`; status codes and `detail` are unchanged, and no code reveals whether an address has an account | — | none | API: every refusal carries a code; the unknown-address and wrong-password responses stay byte-identical apart from timing bounds | S; enumeration (checked by the byte comparison) |

#### Inbox (37.4)

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 9 | Conversation rows don't show the customer | 37.4 | New `customer` on list items and on the detail: `{ id, displayName \| null, labelCode, reference \| null, shortCode }`. `labelCode` ∈ `Named`, `WhatsAppCustomer`, `AnonymousVisitor`, `UnnamedCustomer` (the `CustomerLabels.For` rule, now a code). `reference` is the masked WhatsApp number (`••••1234`, masked by `WhatsAppCustomerId.Masked`, never projected raw). `shortCode` is 6 Crockford base32 characters from the random part of the canonical customer id: stable, derived, never a key. Guarded by `conversations.view` (D-4, approved) | `customers.display_name`; `whatsapp_contacts` (by `ix_whatsapp_contacts_conversation`); channels by the customer's conversations | none | API: fields for web and WhatsApp customers; masked reference never raw (raw `wa_id` absent from JSON); merged customer shows the canonical one; tenant isolation; 403 without `conversations.view`; no N+1 (statement count) | M; none (D-4 decided) |
| 10 | Transcript read oldest first only | 37.4 | `GET …/messages` gains `tail=true` (the newest `limit` messages) and `before={sequence}` (the `limit` messages before it). Pages stay **ascending inside the page**, so the client's merge-by-sequence keeps working. New `previousCursor` (long \| null). `after` with `before` or `tail` → 400 `transcript.cursor.conflict`. Without the new parameters, nothing changes | `messages.ingestion_sequence`, served descending by `ix_messages_conversation_ingestion` | none | API: tail, before, the boundary, empty, and conflict; ordering by sequence never `sent_at`; the 232-message conversation; tenant isolation | S; none |
| 11 | Failed AI turns not marked | 37.4 | `GET …/messages?include=events` adds `events: [{ kind, at, afterSequence, actor \| null }]` for the page's sequence span. `kind = AiTurnFailed` comes from `conversation_turns.status = 'Failed'`, placed after its customer message. Human-mode turns (`Completed` with no reply) are never marked | `conversation_turns` (`ux_conversation_turns_customer_message`) | none | API: a failed turn appears once; human-mode turns do not; a later successful turn does not hide an earlier failure; the event is absent from every public transcript | S; none |
| 12 | Takeover and return-to-AI not in the transcript | 37.4 | The same `events` array, kinds `TakenOver` and `ReturnedToAi`, with `actor: { userId, email }` | new table | **M4** | Domain: the event is raised only when handling changes (idempotent calls write nothing). API: who and when; ordering after the in-flight AI reply (lock); `If-Match`/412/428 unchanged; never on public transports | M; none. History starts at 37.4 (nothing invented for earlier handoffs) |
| 13 | "Needs a person" filter | 37.4 | `GET …/conversations?filter=needs-person`. Defined as open, last message from the customer, and either handled by a person or the AI turn for that last message failed (D-5, approved). An unknown filter is 400 `conversations.filter.invalid`. `total` counts the filtered set | derived: `handling`; the last message by `ix_messages_conversation_ingestion` (descending, first row); its turn by `ux_conversation_turns_customer_message`. No index is added to `messages` or `conversation_turns`, whose index sets an analytics test freezes | **M6**: index | API: each branch of the definition, paging and total, tenant isolation, 403, statement count | M; none (D-5 decided) |
| 14 | No unread state | 37.4 | New `unread` (bool) on list items and detail, plus `lastReadSequence` on the detail. A conversation is unread for a member when its newest message's sequence is above that member's read marker, or when the member has no marker and the conversation has a message. New `POST …/conversations/{id}/read` `{ sequence }` → 204 (`conversations.view`; monotonic, so no `If-Match`; a sequence above the newest is clamped). Sending a reply marks the conversation read up to the reply | new table | **M5** | API: unread for each member separately; reply clears own; another member's reply leaves mine unread; monotonic; tenant isolation; a non-member gets 404 | M; on day one every conversation reads as unread for every member (no backfill, by rule 8) |

#### Screens: knowledge, agents, channels, public chat (37.5a)

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 15 | Indexing outcome | 37.5a | New `lastIndexAttempt: { status, failureCode \| null, startedAt, completedAt \| null } \| null` on `GET …/knowledge` items and `GET …/knowledge/{id}` (`knowledge.view`). `Started` means "indexing now". Readiness stays derived and unchanged | `knowledge_index_attempts` (latest by `started_at`, via `ix_knowledge_index_attempts_document`) | none | API: none, failed with code, succeeded, started; latest wins; tenant isolation; p95 before and after | S; none |
| 16 | Source file size in the list | 37.5a | New `sourceByteLength` (long \| null) on list items | `knowledge_sources.byte_length` (already on the detail as `source.byteLength`) | none | API: with and without a file | S; none |
| 17 | Coded file refusals | 37.5a | `code` on every refusal: `knowledge.file.too_large`, `.unsupported_type`, `.type_mismatch`, `.empty`, `.not_utf8`, `.damaged`, `.encrypted`, `.no_text`, `.too_many_pages`, `.too_many_characters`, `.too_many_rows`, `.too_many_columns`, `.cell_too_long`, `.archive_too_large`, `.required`, `.multipart_required`, plus a numeric `limit` extension where one applies. Status codes unchanged | `KnowledgeFileRejectedException` gains a code; the throw sites (about 25) pass it | none | One API test per parser refusal (PDF, DOCX, CSV, text); status and `detail` unchanged | M; none |
| 18 | Channels per agent | 37.5a | New `channels: [{ kind: "WebChat" \| "WhatsApp", id, status }]` on agent list items and detail. Each kind is included only when the caller holds `website-chat.view` or `whatsapp.view`, checked once per request | `web_chat_endpoints.ai_agent_id` (`ux_web_chat_endpoints_tenant_agent`), `whatsapp_connections.ai_agent_id` (`ix_whatsapp_connections_agent`) | none | API: both kinds, each permission missing, tenant isolation, statement count | S; a reader without channel permissions sees fewer channels (documented) |
| 19 | Hosted chat page lacks the widget's appearance | 37.5a | New `greeting`, `accentColor`, `theme` on `GET /api/public/webchat/{publicKey}` (already public on the embedded read) | `web_chat_endpoints.widget_*` | none | API: values, defaults, an unknown key still a bare 404, rate limits unchanged | S; none |
| 20 | Workspace display name on public reads | 37.5a (D-3 approved) | New `workspaceName` on the hosted and embedded config reads | `tenants.name` | none | API: present; nothing else about the workspace disclosed | S; disclosure rule reversed (D-3); the owner controls the name through row 20a |
| 20a | Rename the workspace (D-3) | 37.5a | New `PUT /api/tenants/{id}/name` `{ name }` with `If-Match` (428 without it, 412 when stale) → 200 with the workspace and a new `ETag`. `GET /api/tenants/{id}` now sends an `ETag` (moved here from row 34). The name follows the creation rules; an invalid one is 400 `workspace.name.invalid`. Guarded by the **new permission `workspace.manage`** (group `workspace`); the Owner gains it automatically, because the Owner's codes are derived from the catalogue, and a custom role can be granted it. Each rename writes the audit event `workspace.renamed` to `security_events` (M3) with the actor and the time, never the names (M3 holds no free text); see §4.3 for the workspace reference | `tenants.name` | none (the permission is compile-time) | API: 200 and the new `ETag`; 400, 403, 404, 412, 428; tenant isolation; the hosted and embedded reads show the new name at once; the Owner holds `workspace.manage` with no data step; one audit row per rename | S; visitors see a rename immediately (intended) |
| 21 | Stable codes on origin refusals | 37.5a, **admin side only** | Coded: `website-chat.origin.invalid`, `.too_many`, `.duplicate` on the widget PUT. **Not coded: the public embed refusals** — they stay one bodiless 404 so that sites and keys cannot be enumerated (`embeddable-web-chat-widget.md` §10) | — | none | API: each admin refusal; the public 404s stay byte-identical | S; none |
| 22 | Direction-aware widget corner (optional) | 37.5a | `launcherPosition` accepts `Start` and `End` beside `Left` and `Right`. `v1.js` resolves them from the host page's direction (`<html dir>`, else computed); old values behave as before | `web_chat_endpoints.widget_launcher_position` (varchar, enum name) | none | Loader test across `dir` values; admin PUT round-trip; the old frontend still reads `Left`/`Right` unchanged unless someone sets a new value through the API | S; an old loader would treat a new value as right, but the loader is served by the API, so it is upgraded in the same deployment |
| 28a | Error codes: knowledge, agents, channels | 37.5a | A stable `code` on every 400, 409 and 422 these areas return. See item 28 | — | none | See 28 | M |

#### Screens: team, permissions, billing, errors, session (37.5b)

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 23 | Members per role | 37.5b | New `memberCount` on `GET …/roles` items and `GET …/roles/{id}` (`roles.view`) | `tenant_role_assignments` (`ix_tenant_role_assignments_role`); the Owner has a real assignment row | none | API: owner and custom roles, after a role change, tenant isolation | S; none |
| 24 | Invitation status filter | 37.5b | `GET …/invitations?status=Pending\|Accepted\|Revoked\|Expired` (`members.view`). `Expired` includes stored-pending rows past `expires_at`, the same rule as the read. An unknown value is 400 `invitations.status.invalid`. The list's **N+1 is fixed** (role names and inviter emails each read in one batch) | `tenant_invitations.status`, `expires_at` (`ix_tenant_invitations_tenant`) | none | API: every status including pending-but-expired, paging and total, statement count, p95 before and after | S; none |
| 26 | Permission catalogue codes | 37.5b | New `groupKey` (`workspace`, `ai-agents`, `roles`, `members`, `conversations`, `customers`, `channels`, `knowledge`, `analytics`, `billing`, `reports`). The permission `code` is documented as the stable key for its label and description, so a client translates by `code` and `groupKey` | compile-time catalogue (`PlatformPermissions.cs`) | none | Every permission has a known group key; keys never change (snapshot test) | S; none |
| 27 | Billing read: fallback plan and limits | 37.5b | New `currentPlan: { code, name, limits }` and `fallbackPlan: { code, name, limits } \| null` on `GET …/billing` (`billing.view`). `limits` holds the six plan limits, `null` meaning unlimited. These are plan definitions, not usage; usage stays behind `analytics.view` | `plans`, `workspace_plan_assignments`, `Billing:FallbackPlanCode` through `FallbackPlanResolver` | none | API: free, starter and unlimited plans; fallback unset → null; 404 without permission unchanged | S; none |
| 28 | Stable codes on 400, 409 and 422 | 37.5a (knowledge, agents, channels) and 37.5b (roles, invitations, team, billing) | Every 400, 409 and 422 these six areas return gains a `code`: one per mapped exception type, and one per throw site for `DomainValidationException` and `DomainStateException` in these areas (about 150 sites). Any refusal left without a specific code gets `validation.failed` or `state.conflict`, so a client always has a code. `detail` stays English. Endpoint-level `Results.Problem` refusals are coded too. A catalogue `docs/architecture/problem-codes.md` lists every code | — | none | A test fails if any 400, 409 or 422 from these areas lacks `code`; API tests for the codes the screens show | L; the size, hence the split |
| 29 | Session check without a 401 | 37.5b | New `GET /api/auth/session` (anonymous) → **200** `{ authenticated: false }` or `{ authenticated: true, user: { id, email, language } }`, `Cache-Control: no-store`, rate-limited per client network. `GET /api/auth/me` is unchanged | session cookie | none | API: signed out, signed in, deleted account, revoked stamp; no `Set-Cookie`; rate limit | S; none |

#### Insights (37.6)

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 30 | Usage by purpose | 37.6 | New `aiUsage.byPurpose: [{ purpose, calls, succeeded, failed, unresolved, inputTokens, outputTokens, unratedCalls, ratedCost: [{ currency, estimatedCost, calls }] }]` on the dashboard (`analytics.view`; `aiUsage` stays null without calls). New purpose value `AgentRetrievalTest`: `KnowledgeRetriever` takes the purpose from its caller | `ai_provider_usages.purpose` (grouped with the existing `(tenant_id, started_at)` index) | none | API: each purpose; currencies never added together; the retrieval tester recorded under its own purpose; forbidden-word tests stay green | S; history: see §3.6 |
| 31 | Comparison periods | 37.6, **summary only** | `GET …/analytics/dashboard?compare=previous` adds `previous: { period, summary, aiOperations }` for the equal-length window just before, half-open, in the workspace time zone. **The realtime part is outside Phase 37:** there is no realtime summary endpoint, and `current` is a point-in-time count with no stored history to compare against | raw rows, as today | none | API: windows adjacent and non-overlapping; an event on the boundary counted once; statement bound for the compare path | M; the statement-count guard is raised deliberately for `compare` only |
| 32 | Report catalogue codes | 37.6 | The wire `type` (`ConversationActivity`, …) is declared the stable translation key. New `parameters` on each catalogue entry: `[{ code: "range", values: ["7d","30d","90d","custom"] }, { code: "format", values: [...] }]`. Failure codes already exist | `ReportCatalog` | none | Catalogue snapshot: codes never change | S; none |
| 25 | Report type on report notifications | 37.6 (with 32; see §3.8) | New `parameters` (object \| null) on `NotificationResponse`; for the four report kinds `{ reportType }`, using the same codes as 32 | derived through `notification_occurrences.source_id → report_executions` | none | API: each report kind; other kinds null; recipient isolation | S; none |
| 33 | First-activity date | 37.6 | New `firstActivityOn` (`YYYY-MM-DD` in the workspace time zone, or null) on the dashboard | `MIN(conversations.started_at)`, an index-only first row on `ix_conversations_tenant_started` | none | API: none, one, several; the time-zone boundary | S; the `count(` statement guard allows this one `min(` |
| 34 | Workspace time zone | 37.6 | New `timeZone` on `GET /api/tenants/{id}`, whose `ETag` exists from 37.5a (row 20a). New `PUT /api/tenants/{id}/time-zone` `{ timeZone }` with `If-Match` (428/412), guarded by **`workspace.manage`**, introduced in 37.5a (row 20a) and reused here. 400 `workspace.time_zone.invalid` for an unknown IANA id. Analytics and reports then use local days: the dashboard `period.timeZone`, the catalogue `timeZone`, report periods, schedule due times and renderer labels. Each report execution records its zone. **The monthly plan quota stays on UTC months**, because it is a contractual period | new `tenants.time_zone`, `report_executions.time_zone` | **M7, M8** | UTC default unchanged byte for byte; `Asia/Riyadh` (22:30 UTC counts on the next local day); a daylight-saving zone (`Europe/Berlin` on 25 Oct 2026 has a 25-hour day); schedules due at local midnight; changing the zone recomputes `next_run_at` in the same transaction; 403/404/412/428 | L; every analytics query gains a zone parameter |

#### Report files in the requester's language (37.7, D-6)

| # | Item | Step | Contract | Data | Model change | Tests | Effort, main risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 36 | PDF, Excel and CSV in the requester's language | 37.7 | No new request parameter. An execution takes the requester's `users.language` (else `en`); a schedule takes its creator's language when it is created, and each scheduled run uses the schedule's language. New read-only `language` on execution and schedule responses. The renderers word column headers, titles, period labels and empty states in that language from code catalogues (report names by the codes of row 32). Arabic PDFs lay tables out right to left with the existing Arabic shaping; Excel sheets are marked right to left; CSV keeps its format with translated headers. Existing executions and schedules read `en` and render as today | `report_executions.language`, `report_schedules.language`; `users.language` (M1) | **M9, M10** (approved) | Completeness: every header and label has both languages; per format and report type, an English file identical to today's and an Arabic one; PDF right-to-left layout and shaping; Excel right-to-left flag; a schedule keeps its language after its creator changes theirs | L; Arabic layout in three renderers, and the Arabic wording needs a native reading |

#### Outside Phase 37

| # | Item | Reason |
| --- | --- | --- |
| 35 | Records search (Ctrl+K) | Your decision |
| 31 (realtime part) | Comparison on the realtime "current" figures | No history of open counts exists; it would need stored snapshots, a new feature |
| PDF/XLSX/CSV in the requester's language | **Moved into Phase 37 as step 37.7 (D-6, row 36)** | — |

#### Raised by Phase 36 reports but not on the backend list

Listed so nothing is lost. None is planned unless you add it.

| Ask (source) | Recommendation |
| --- | --- |
| Codes for sign-in and registration refusals (36.5 ask 1) | **Added to 37.3 (D-2, row 6b)** |
| Sign-in rate limiting (deferred in `authentication-tenant-membership.md:814`) | **Added to 37.3 (D-2, row 6a)** |
| Member display names (36.1, 36.4) | Later: needs a profile name column and UI |
| Invitation preview by token (36.5) | Later: a new anonymous read with enumeration questions of its own |
| Email verification (36.5) | Later. Reset does not depend on it: a reset proves control of the inbox |
| Server-held theme and sidebar pin (D22) | Later: only language moves to the server (O-3) |
| "Assigned to me", by handling, by channel filters (36.6) | Later; the filter parameter of item 13 takes more values without a contract change |
| Lighter list realtime (36.6 ask 7) | Later; needs a per-conversation admin group |
| Overview count of documents needing indexing (D22) | Later |

### 3.3 Migrations for your approval

All are generated from the model with `dotnet ef migrations add <Name>` (from `backend/`, `-p src/OmniAI.Infrastructure -s src/OmniAI.Infrastructure`), never hand-edited, and only add objects. **No data-step migration is needed in Phase 37.** Each is tested on a fresh database and on a copy of `omniai_p36` (after a `pg_dump` into the evidence folder), with `DatabaseModelAuthorityTests` green.

| # | Step | Migration name | Exact model change |
| --- | --- | --- | --- |
| M1 | 37.3 | `AddUserLanguage` | `OmniAiUser.Language` → `users.language varchar(8) NULL`; check `ck_users_language`: `language IS NULL OR language IN ('en','ar')`. No index (read by primary key only) |
| M2 | 37.3 | `AddAccountEmailOutbox` | New table `account_emails`: `id uuid` PK `pk_account_emails`; `user_id uuid NOT NULL` FK `fk_account_emails_user` → `users(id)` ON DELETE CASCADE (through `UserReferenceRow`); `kind varchar(32) NOT NULL` (`PasswordReset`); `requested_at timestamptz NOT NULL`; `status varchar(16) NOT NULL` (Pending, Sending, Sent, Failed, Unknown, Skipped); `available_at timestamptz NOT NULL`; `attempts int NOT NULL` with check `ck_account_emails_attempts` (`attempts >= 0`); `claim_token uuid NULL`; `claimed_at timestamptz NULL`; `sent_at timestamptz NULL`; `settled_at timestamptz NULL`; `failure_code varchar(40) NULL`. Indexes: `ix_account_emails_claimable (available_at) WHERE status IN ('Pending','Sending')`; `ix_account_emails_user_requested (user_id, requested_at)`, which also serves the FK. **No address, token, link, subject or body column** |
| M3 | 37.3 | `AddSecurityEvents` | New table `security_events`: `id uuid` PK; `occurred_at timestamptz NOT NULL`; `kind varchar(64) NOT NULL`; `user_id uuid NULL` FK `fk_security_events_user` → `users(id)` ON DELETE SET NULL; `tenant_id uuid NULL` FK `fk_security_events_tenant` → `tenants(id)` ON DELETE SET NULL, like the user key, so an event outlives the workspace it names. It keeps EF's foreign-key convention index `ix_security_events_tenant_id` (no `WithoutConventionIndex`). Workspace-scoped events fill it, such as the D-3 rename (row 20a); account events leave it empty (amended 2026-10-21, §4.3); `subject_key varchar(32) NULL` (HMAC of the normalised address, base64url, for requests that name an address); `client_network_key varchar(32) NULL` (the existing keyed hash of the client network). Indexes: `ix_security_events_user_occurred (user_id, occurred_at) WHERE user_id IS NOT NULL`; `ix_security_events_occurred (occurred_at)`. No address, token, password or free text |
| M4 | 37.4 | `AddConversationHandlingEvents` | New table `conversation_handling_events`: `id uuid` PK; `tenant_id uuid NOT NULL` FK → `tenants(id)` RESTRICT, without a convention index (`.WithoutConventionIndex()`, as the reply table's tenant key: rows are always reached through their conversation); `conversation_id uuid NOT NULL` FK `fk_conversation_handling_events_conversation` → `conversations(id)` ON DELETE CASCADE; `kind varchar(16) NOT NULL` (`TakenOver`, `ReturnedToAi`); `actor_user_id uuid NOT NULL` with composite FK `fk_conversation_handling_events_actor_member` `(actor_user_id, tenant_id)` → `tenant_memberships(user_id, tenant_id)` RESTRICT, no convention index (as `fk_human_replies_sender_member`); `occurred_at timestamptz NOT NULL`; `after_sequence bigint NOT NULL` (the newest message's sequence when the change happened, read inside the conversation lock) with check `after_sequence >= 0`. Index `ix_conversation_handling_events_conversation (conversation_id, after_sequence)` |
| M5 | 37.4 | `AddConversationReads` | New table `conversation_reads`: `conversation_id uuid NOT NULL` FK → `conversations(id)` ON DELETE CASCADE; `user_id uuid NOT NULL`; `tenant_id uuid NOT NULL`; composite FK `(user_id, tenant_id)` → `tenant_memberships(user_id, tenant_id)` ON DELETE CASCADE; `last_read_sequence bigint NOT NULL` with check `>= 0`; `read_at timestamptz NOT NULL`. PK `pk_conversation_reads (conversation_id, user_id)`; index `ix_conversation_reads_member (user_id, tenant_id)` for the FK |
| M6 | 37.4 | `AddConversationHandlingIndex` | Index `ix_conversations_tenant_handling_activity` on `conversations (tenant_id, handling, last_activity_at)`, serving the needs-a-person filter's `handling` branch in list order |
| M7 | 37.6 | `AddWorkspaceTimeZone` | `Tenant.TimeZone` → `tenants.time_zone varchar(64) NOT NULL DEFAULT 'UTC'` (model default, so existing rows read `UTC` without a data step) |
| M8 | 37.6 | `AddReportExecutionTimeZone` | `ReportExecution.TimeZone` → `report_executions.time_zone varchar(64) NOT NULL DEFAULT 'UTC'`, so an execution keeps the zone its period was computed in |
| M9 | 37.7 | `AddReportExecutionLanguage` | `ReportExecution.Language` → `report_executions.language varchar(8) NOT NULL DEFAULT 'en'` (model default, so existing rows read `en` without a data step) |
| M10 | 37.7 | `AddReportScheduleLanguage` | `ReportSchedule.Language` → `report_schedules.language varchar(8) NOT NULL DEFAULT 'en'`; a new schedule takes its creator's language when it is created (an application rule, not a column default) |

Why each is the smallest change:
- **M1**: one nullable column beside Identity's; a separate preferences table was rejected because language is the only server-held preference (D22).
- **M2**: holds only the user and the request time, as the prompt requires. Reusing `notification_emails` was rejected: its rows require a notification and a workspace, and a reset has neither.
- **M3**: rule 11 requires an audit record and none exists.
- **M4**: handoff history cannot be derived. `System` messages were rejected because public transcripts would show them.
- **M5**: per-member state cannot be derived.
- **M6**: the only new filter predicate on a non-indexed column.
- **M7, M8**: a default keeps every existing row's meaning.
- **M9, M10**: the same; an existing execution or schedule keeps rendering in English.

**Approved with D-1 option A (2026-10-21)** (invitation emails, row 8a):

| # | Step | Migration name | Exact model change |
| --- | --- | --- | --- |
| M2a | 37.3 | `AddInvitationEmail` | `tenant_invitations.language varchar(8) NULL` with check `ck_tenant_invitations_language` (`language IS NULL OR language IN ('en','ar')`). `tenant_invitations.email_token_hash bytea NULL` with unique index `ux_tenant_invitations_email_token_hash WHERE email_token_hash IS NOT NULL` (SHA-256 of a token minted at send time; acceptance matches either hash, in constant time). On `account_emails`: `user_id` becomes NULL-able; new `invitation_id uuid NULL` and `tenant_id uuid NULL` with composite FK `fk_account_emails_invitation` `(invitation_id, tenant_id)` → `tenant_invitations(id, tenant_id)` (the existing alternate key) ON DELETE CASCADE, with its index; check `ck_account_emails_recipient`: exactly one of `user_id` and `invitation_id` is set |

**Approved by the owner on 2026-10-21: M1 to M8, M2a, M9 and M10, exactly as described above (11 migrations, all additive, no data step).** Nothing outside this list is migrated later without asking you.

### 3.4 Language and email design (37.3)

**Where the language lives.**
- In `users.language` (M1): `en`, `ar` or null, one per person, not per workspace.
- Null means "not chosen"; every server text then falls back to English.
- The member's choice in the browser (`localStorage['omniai.lang']`) stays the source for the UI. From Phase 38 the client also sends it to `PUT /api/me/language` when the member switches.

**How it is first set.**
1. The optional `language` field on sign-up and on invitation acceptance.
2. Otherwise the request's `Accept-Language`: the first supported primary tag by q-value, so `ar-SA;q=0.9` → `ar`.
3. Otherwise it stays unset.

Acceptance and sign-in fill the value only while it is unset; only `PUT /api/me/language` changes a chosen one. Existing users get a value at their next sign-in.

**Recipients without an account.**
- None exist today. Invitations are not emailed: the inviter copies a link. Scheduled reports have no recipients: the owner, who has an account, is notified.
- So no language column is needed on invitations or schedules.
- If you want invitation emails (D-1 option A), the invitation stores `language` from the inviter's request, defaulting to the inviter's own; M2a covers it.

**The emails that exist, and how each renders.**

| Email | Today | From 37.3 |
| --- | --- | --- |
| 13 operational notifications (report ready/failed ×4, usage 70/90/exhausted, billing ×4, knowledge indexing failed, WhatsApp delivery unknown) | English, `OperationalEmailRenderer`, text + HTML, subject `OmniAI: …` (Nabra from 37.1) | Rendered at send time in the recipient's `users.language`, with the report type derived by join for the four report kinds |
| Password reset (new) | — | Account email, same renderer conventions |
| Invitation (new, D-1 option A, row 8a) | does not exist | Account email in the invitation's language, same renderer conventions |

Rendering rules, both languages:
- Subjects are a fixed string per kind and language, never interpolated, so the header-injection rule stands (`Nabra: …`, `نبرة: …`).
- The HTML part is `<html lang="ar" dir="rtl">` with logical alignment, or `lang="en" dir="ltr"`.
- The plain-text part is the same words. Arabic is fine without marks.
- Workspace names and report names are isolated: `<bdi>` in HTML, FSI…PDI in text.
- Digits are Western, the calendar Gregorian, and instants use the same format in both languages (localisation decision 3; `InvariantGlobalization` stays on, so no ICU and no `deploy/` change).
- No images, fonts or tracking, as today. Words follow `GLOSSARY.md`.

The catalogue becomes `NotificationCatalog.Describe(kind, language, reportType…)` with both texts in code. A test fails if any kind lacks either language.

**PDF and other report files in the requester's language.**
- **Recommendation: outside Phase 37.** The PDF pipeline already shapes Arabic runs (HarfBuzz, `Unicode.Bidi`, embedded DejaVu), but an Arabic report also needs right-to-left table layout, translated column headers in three renderers, and a language on executions and schedules. That is an L of its own, not on the backend list.
- From 37.6 the report *names* are translatable everywhere by code, and report emails arrive in the recipient's language from 37.3.
- If you want it in Phase 37, it fits as **37.7**, adding `report_executions.language` and `report_schedules.language` (`varchar(8) NOT NULL DEFAULT 'en'`, schedules defaulting to the creator's language). These are two more migrations for your approval.

### 3.5 Password reset design (37.3)

**Token providers today:** none. `AddIdentityCore<OmniAiUser>()…AddSignInManager()` with no `AddDefaultTokenProviders`, no `AddTokenProvider` and no `options.Tokens` (`AuthenticationSetup.cs:75-80`). `UserManager.GeneratePasswordResetTokenAsync` would throw today.

**The token.**
- A dedicated `PasswordResetTokenProvider : DataProtectorTokenProvider<OmniAiUser>` with its own options type (`Name = "NabraPasswordReset"`, `TokenLifespan` from `Auth:PasswordReset:TokenLifetime`, default **30 minutes**, bounded 10–60).
- It is registered with `AddTokenProvider<PasswordResetTokenProvider>("NabraPasswordReset")`, and only `options.Tokens.PasswordResetTokenProvider` points at it.
- No other token's lifetime changes, because no other provider exists.
- Its `ValidateAsync` override compares the user id and the security stamp with `CryptographicOperations.FixedTimeEquals`.
- **Data protection is shared by both production APIs:** one key ring in PostgreSQL, the same application name and the same master key for `api-a` and `api-b` (§3.1).

**Lifetime and single use.**
- The token embeds the security stamp. A successful reset rotates the stamp, so that token and every earlier one die at once.
- After 30 minutes it expires.
- Nothing about the token is stored. It is a protected, authenticated payload, so there is nothing at rest to hash.

**What is queued.**
- `forgot` writes one `account_emails` row: the user and the request time.
- A pending row already queued for the user is reused; there is never a second.
- The worker claims rows exactly like `notification_emails` (`FOR UPDATE SKIP LOCKED`, claim token, lease, `Unknown` after a stale `Sending`).
- At send time it re-reads the user. A request older than 15 minutes is `Skipped` (`request_expired`).
- Only then does it generate the token and build the link: the app's reset-password page, with the user id and the token in the URL **fragment**. The fragment never reaches a server log or a `Referer`.
- The token, link and message exist only in memory and are never logged. Mailpit (local) is the only receiver in development, per rule 9.

**No user enumeration.**
- `forgot` answers 202 with the same body for known and unknown addresses.
- It performs the same limiter calls for both, and every outcome is padded to the same minimum response time (default 400 ms, the slow path measured), so database and queue work cannot be timed.
- Rate limits use the existing Redis Lua limiter pattern (`RedisPublicChatProtection`), shared by both APIs through the same Redis, and failing closed:
  - per address: 3 per hour, keyed by an HMAC of the normalised address (Identity's normaliser), **counted for unknown addresses too**;
  - per client network: 10 per hour (the existing `HmacClientNetworkKeys`);
  - `reset`: 20 per 15 minutes per network.
- The HMAC key is derived from the existing network-key secret with a distinct label, so no new secret and no `deploy/` change.

**Sessions.** How every session ends at once on both production APIs:
- The cookie already carries Identity's security-stamp claim.
- The cookie's `OnValidatePrincipal` is replaced by a check on **every request** that compares that claim with the current stamp (constant time). The stamp comes from Redis (`omniai:auth:stamp:v1:{userId}`) or, on a miss or Redis failure, from `users`.
- The reset writes the new stamp to Redis before it answers, so the next request on either API is refused with 401.
- No cookie is reissued per request.
- **Column needed: none.** `users.security_stamp` already is the per-user "valid after" marker. A separate `sessions_valid_after` column was rejected as a duplicate of it.
- Open hub connections: the inbox hub records its connections per user on each instance. The reset publishes `omniai:auth:revoked` on Redis; every instance aborts that user's local connections; and a hub filter re-checks the stamp on every invocation.

**Audit events** (M3), none carrying an address, token or password:
- `auth.password_reset.requested` (user id when known; subject key always)
- `auth.password_reset.throttled`
- `auth.password_reset.email_sent` and `auth.password_reset.email_failed`
- `auth.password_reset.completed`
- `auth.password_reset.rejected`
- `auth.sessions.revoked`

**The two entry pages** (frontend, in 37.3, English and Arabic):
- `/forgot-password`: one field; the same confirmation whatever the outcome.
- `/reset-password`: reads the fragment, removes it from the address bar at once with `history.replaceState`, asks for the new password with the policy shown, then sends the member to sign-in.
- Sign-in replaces "Password reset is not available yet" with a link.
- The anonymous surface in `authentication-tenant-membership.md` grows from five endpoints to seven, and to eight with item 29 in 37.5b; the doc and its route tests change with it.

### 3.6 Insights design (37.6)

**Raw rows or stored daily totals.**
- Analytics days come from **raw rows at query time**, today and after 37.6. `analytics-usage-foundation.md` §2 forbids rollup tables.
- Raw rows serve **every IANA zone correctly**, including half-hour and 45-minute offsets and daylight-saving days of 23 or 25 hours, because PostgreSQL converts each row (`AT TIME ZONE`).
- Stored daily totals would be correct only for the zone they were cut in, normally UTC. Serving another zone from them needs hourly totals (still wrong for :30 and :45 offsets) or a recount.
- The window becomes local midnights converted to instants, still half-open.

**Usage recorded before "purpose" existed.**
- Every usage row already has a purpose: the column has been required since the Initial schema.
- The only historical mismatch is the retrieval tester, recorded as `ConversationRetrievalEmbedding`. Those rows cannot be told apart from live retrieval, so they stay counted there, and the API documentation says so.
- From 37.6 the tester records `AgentRetrievalTest`. No backfill, by rule 8.

### 3.7 Compatibility strategy

| Change | Why the current frontend is unaffected |
| --- | --- |
| New response fields (customer, unread, channels, memberCount, lastIndexAttempt, sourceByteLength, appearance, groupKey, plans, byPurpose, firstActivityOn, parameters, language, timeZone) | Additive; the client reads named fields and ignores others |
| Newest-first transcript | Only with the new `tail`/`before`; pages stay ascending inside |
| Transcript events | Only with `include=events`, as a separate array; `items` unchanged |
| Needs-a-person, invitation status | Only with the new `filter`/`status`; without them the lists are byte-identical |
| Comparison | Only with `compare=previous` |
| Session check | A new endpoint; `/api/auth/me` keeps its 401 |
| Password reset, language PUT, read marker, time-zone PUT | New endpoints; nothing existing calls them |
| `code` on problem responses | An added member; status codes, `title` and `detail` unchanged. The client already reads a top-level `code` |
| Loader `data-nabra-key`, `Start`/`End` | The old attribute and values keep working |
| Brand words, failed-turn announcement | Values and an extra existing event; no shape change |
| Workspace time zone | Defaults to `UTC`, which reproduces today's output; it changes only when someone calls the new PUT |
| Email language | No API change |
| Per-request session check | Same 401 the client already handles, now also on revocation |

Every change is described in `openapi.json` and in its architecture document in the same step, and listed in that step's contract table.

### 3.8 Report codes

- The report-notification parameter codes (25) move from 37.5 to **37.6** and ship with the catalogue codes (32), so both use one code set: the wire `ReportType` names.
- 37.3 only *uses* the derived report type internally to word the email. It adds no field.

### 3.9 Order, dependencies and evidence

| Step | Order inside the step | Depends on | Evidence the step produces |
| --- | --- | --- | --- |
| 37.1 | packages → failed-turn announcement → brand (catalogue, renderer, PDF, snippet, loader) → password cleanup | — | clean restore log without the audit flag; vulnerable-package list; images built from unchanged `deploy/`; hub capture of the failure announcement; repository scan |
| 37.2 | stand-in generators → registration guards → stack script flag | 37.1 (failure announced) | registration matrix; a real reply, an indexing run, a preview, a retrieval test and a `[[fail]]` turn in Chrome, en and ar |
| 37.3 | M1 language → M3 audit and the limiter → sign-in limiting and sign-in/registration codes (6a, 6b) → reset provider and per-request stamp check → M2 outbox and worker → M2a and the invitation email (8a) → bilingual catalogue and renderer → entry pages → frontend language wiring (row 7) → Mailpit | 37.2 (so emails about indexing can be produced locally) | migration SQL, fresh and copy; Mailpit messages, reset and invitation (en and ar, screenshots without links); enumeration timing table; sign-in throttling; revocation across two API instances; audit rows; the frontend sending and saving the language in Chrome |
| 37.4 | M6 index → transcript `tail`/`before` → customer on rows → M4 events and `include=events` → M5 reads → filter | 37.2 (failed turns on demand) | p95 before and after; the 232-message conversation newest-first; events in Chrome |
| 37.5a | problem-code mechanism → knowledge (15, 16, 17) → agents (18) → `workspace.manage`, the tenant `ETag` and the rename (20a) → public chat (19, 20, 21, 22) → codes for these areas | 37.3 (`security_events` for the rename audit) | refusal matrix with codes; public 404 byte comparison; a rename shown on the hosted and embedded reads; the audit row |
| 37.5b | session check (29) → roles (23) → invitations (24, N+1 fix) → catalogue (26) → billing (27) → codes for these areas | 37.5a mechanism | p95 before and after for invitations and roles |
| 37.6 | M7 + PUT (reusing `workspace.manage` and the `ETag` from 37.5a) → zone-aware windows → M8 and reports → purpose (30) → comparison (31) → first activity (33) → codes (32, 25) | 37.4 (no shared files, but the reader changes); 37.5a (`workspace.manage`) | boundary tests in UTC, Riyadh and Berlin; p95 of the dashboard with and without `compare` |
| 37.7 | M9 and M10 → language on executions and schedules → catalogue of headers and labels → CSV → Excel → PDF (right-to-left tables) | 37.3 (`users.language`), 37.6 (report codes and time-zone period labels) | migration SQL, fresh and copy; for each report type and format, an English file compared with today's and an Arabic file; PDF pages in Arabic as screenshots |

## 4. Decisions

### 4.1 Decisions you need to make before the step that needs them

| # | Step | Question | Recommendation |
| --- | --- | --- | --- |
| D-1 | 37.3 | Invitations are not emailed today. Should Phase 37 add an invitation email? **A:** yes. The email carries the token, minted at send time, with its hash stored beside the original (M2a), so the copied link keeps working. **B:** no; invitations stay share-by-link, and "invitations in two languages" means the accept page, which is already bilingual | **B** for Phase 37. A is a new capability with its own security review (a second token per invitation); it fits better beside email verification later |
| D-2 | 37.3 | Add sign-in rate limiting and codes for sign-in/registration refusals (not on the list, S each)? | Yes. The limiter and the code mechanism are built in the same step, and the auth doc lists the missing limit as a production gap |
| D-3 | 37.5a | Show the workspace name on the public hosted and widget reads? It reverses a rule written into the code ("No workspace…") | Yes, because a visitor deserves to know whose chat it is. Phase 38 should tell admins in the widget settings that visitors see the workspace name |
| D-4 | 37.4 | The customer's name and masked number on conversation rows under `conversations.view` alone (not `customers.view`) | Yes. A conversation's participant is part of the conversation, and the WhatsApp detail already shows the masked number under this permission. The customer profile link still needs `customers.view` |
| D-5 | 37.4 | "Needs a person" = open, customer spoke last, and (a person is handling **or** the AI's latest turn failed) | Yes. A customer whose AI reply failed is waiting for a person too. The narrower 36.6 definition (person handling only) is the alternative |
| D-6 | — | PDF/XLSX/CSV in the requester's language | Outside Phase 37 (§3.4), or a 37.7 with two more migrations |

### 4.2 Choices made in this plan

- **The security stamp is the session marker.** It is checked on every request through Redis, so no new column is needed. Rejected: a `sessions_valid_after` column, and `SecurityStampValidator` with a zero interval (two queries and a reissued cookie on every request).
- **Handoff events have their own table,** never `System` messages, because those would reach public transcripts.
- **Transcript pages stay ascending** even when read from the newest end, so the current merge code is untouched and Phase 38 changes only how it asks.
- **The short code is derived** from the immutable customer id. Rejected: a stored code column and its backfill.
- **No backfills anywhere.** Unread state, handoff history and the retrieval-test purpose start at their step, and the documents say so.
- **Public embed refusals stay uncoded,** against ask 21 as written, because anti-enumeration wins.
- **The plan quota keeps UTC months** when a workspace chooses a time zone, because quotas are a billing contract.
- **37.5 is split** into 37.5a and 37.5b, because item 28 alone is about 150 throw sites.

### 4.3 Owner decisions (2026-10-21)

The owner accepted this plan with the decisions below. Where §1, §3.4 or §4.1 describe these points as open options or recommendations, this section applies.

| # | Decision | Effect on the plan |
| --- | --- | --- |
| D-1 | **A.** Invitations are also emailed, in the invitation's language | 37.3 builds it: row 8a, migration M2a (approved) |
| D-2 | **Yes.** Sign-in rate limiting, and stable codes on sign-in and registration refusals | Both join 37.3: rows 6a and 6b |
| D-3 | **Yes.** Visitors see the workspace name on the hosted and embedded reads | 37.5a: row 20. In the same step, a way to rename the workspace with `If-Match`, a new `workspace.manage` permission the Owner gains automatically, and an audit event, so the owner controls what visitors see: row 20a. 37.6 reuses `workspace.manage` for the time zone: row 34 |
| D-4 | **Yes.** The customer's name and masked number on conversation rows under `conversations.view` | 37.4: row 9 |
| D-5 | **Yes.** "Needs a person" as defined in §4.1 | 37.4: row 13 |
| D-6 | **Yes, as a new step 37.7.** Report files (PDF, Excel and CSV) in the requester's language | 37.7: row 36. Migrations M9 (`report_executions.language`) and M10 (`report_schedules.language`) approved, defaulting to `en`, with schedules defaulting to their creator's language |
| 37.3 frontend | 37.3 itself wires the language: it sends it on sign-up and invitation acceptance, and saves it when a signed-in member switches | Row 7 and §7: Phase 38 does not need to |
| Migrations | **M1 to M8, M2a, M9 and M10 approved**, exactly as §3.3 describes them | 11 migrations, all additive, no data step |

Phase 37 now runs 37.1 → 37.2 → 37.3 → 37.4 → 37.5a → 37.5b → 37.6 → 37.7 (§3.9).

**Decided (2026-10-21): M3 gets the workspace of a security event.** D-3 asks for an audit event on each rename, and `security_events` (M3) had no workspace column. The owner approved the recommendation: M3 (`AddSecurityEvents`, generated in 37.3) also gets a nullable `tenant_id uuid` with a foreign key to `tenants(id)`, following the model's usual rules for tenant keys and their indexes (§3.3). Workspace-scoped security events fill it, such as the D-3 workspace rename in 37.5a (row 20a); account events leave it empty. The approved migration list is unchanged: M1 to M8, M2a, M9 and M10, with M3 as amended.

## 5. Verification

The backend gates ran in Docker as in the Phase 33.5 report §11: `mcr.microsoft.com/dotnet/sdk:10.0` with the repository root mounted, and a throwaway `pgvector/pgvector:pg17` and `redis:7-alpine` on a private network through `OMNIAI_TEST_POSTGRES` and `OMNIAI_TEST_REDIS`, all removed afterwards. **For this baseline only, `-p:NuGetAudit=false` was passed on the command line.** Logs are in `<evidence>\p37-0\baseline\`.

| Check | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx -p:NuGetAudit=false` | PASS |
| `dotnet build OmniAI.slnx -c Release` | PASS, **0 warnings, 0 errors** |
| `dotnet test`: Domain | PASS, 797 passed, 0 failed, 0 skipped |
| `dotnet test`: Application | PASS, 714 passed, 0 failed, 0 skipped |
| `dotnet test`: Api.IntegrationTests | PASS, 893 passed, 0 failed, 2 skipped |
| `dotnet test`: Infrastructure.IntegrationTests | PASS, 1,031 passed, 0 failed, 1 skipped |
| `dotnet test` exit code | **PASS on run 3: exit 0.** The test projects ran one at a time (`-m:1`), with the build reused from run 1 (`baseline\backend-gates-run3.log`). The counts above are run 3's. Two earlier runs, with the projects in parallel, did not end green:<br>• run 1: exit 1 with 0 failed tests, from test-class cleanup failures (`Npgsql` read timeouts);<br>• run 2: 1 failed test, the `DatabaseModelAuthorityTests` foreign-key check `fk_human_replies_sender_member`, on an `Npgsql` read timeout, plus the same cleanup timeouts (`baseline\backend-gates-rerun.log`).<br>Both are database timeouts under parallel load, not assertions. See §8. |
| `dotnet list package --vulnerable --include-transitive` | FAIL, as expected (release blocker 1): `System.Security.Cryptography.Pkcs` 10.0.0 (high GHSA-846f-f764-76mx, moderate GHSA-2fmv-rv2c-59x7) in Api, Infrastructure and both integration-test projects; `MessagePack` 2.5.302 (moderate GHSA-qhrr-8q5h-9q3h) in Api and Api.IntegrationTests. In run 1 the command restored again with the audit on and stopped on NU1902/NU1903, so it was repeated with `--no-restore` after the audit-free restore |
| `DatabaseModelAuthorityTests` | PASS in runs 1 and 3 (part of Infrastructure.IntegrationTests). In run 2, one case timed out on its database read under parallel load (above). |
| Migrations on a fresh database and on a copy of the dev database | NOT RUN: this step adds no migration |
| Frontend `npm run format:check` | PASS |
| Frontend `npm run test:ci` | PASS, 93 files, 1,105 tests |
| Frontend `npm run build` | PASS (initial total 310.86 kB raw, 72.91 kB transfer) |
| Frontend `npm run i18n:check` | PASS, 2,035 messages, 0 errors, 0 warnings |
| API checks against the dev stack (new fields, refusals) | NOT RUN: nothing new exists in this step. All 16 baseline reads answered 200 for the owner |
| Chrome, en and ar | NOT RUN: no page changed in this step |
| p95 latency (before) | Recorded below; "after" is measured by each step |
| Provider canary | PASS: every named variable absent, no `OMNIAI_LIVE_*`, no `Email__Smtp__*` (`baseline\canary.txt`).<br>**Re-run after the connection drop, PASS.** Docker Desktop had stopped, taking the dev API, its database and Redis with it. The dev stack was restarted with the `p36-01\stack.md` commands, and the canary ran before any traffic: inside `omniai-p36-api`, and inside both 36.12 live APIs (which restart on their own). |

**p95 baseline** on the seeded dev data. Owner session, Northwind, 60 sequential samples after 5 warm-up, measured from Windows against the API container (`baseline\p95-before.md`). Every read sits on a floor of about 15 ms, which is the loopback path from Windows into Docker.

| Endpoint | Step that changes it | p50 ms | p95 ms |
| --- | --- | --- | --- |
| GET /conversations (page 1, 20) | 37.4 | 15.3 | 22.9 |
| GET /conversations/{232-message}/messages (limit 100) | 37.4 | 15.3 | 19.7 |
| GET /knowledge (page 1, 20) | 37.5a | 15.1 | 21.1 |
| GET /ai-agents (page 1, 20) | 37.5a | 15.3 | 16.5 |
| GET /website-chat | 37.5a | 15.1 | 19.0 |
| GET /roles | 37.5b | 14.9 | 23.3 |
| GET /invitations (page 1, 100) | 37.5b | 32.0 | **65.1** (the N+1) |
| GET /members (page 1, 20) | (none; reference) | 30.8 | 43.4 |
| GET /notifications (page 1, 20) | 37.6 | 15.2 | 19.2 |
| GET /permissions | 37.5b | 15.3 | 17.0 |
| GET /billing | 37.5b | 15.8 | 27.8 |
| GET /reports/catalog | 37.6 | 15.2 | 24.3 |
| GET /reports/executions (page 1, 20) | 37.6 | 17.7 | 32.4 |
| GET /reports/schedules | 37.6 | 15.3 | 23.3 |
| GET /analytics/dashboard (30d) | 37.6 | 32.8 | 42.4 |
| GET /api/auth/me | 37.3 | 15.5 | 16.7 |

## 6. See it live

Nothing new to try in this step. The current product runs unchanged on the dev server (port 4200), with the accounts in `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`.

## 7. Frontend follow-ups (what Phase 38 adopts, step by step)

| Step | Adopt | Contract |
| --- | --- | --- |
| 37.1 | Nothing required. The widget settings page may say the snippet now uses `data-nabra-key` (the old one keeps working) | — |
| 37.2 | Nothing (development only) | — |
| 37.3 | Nothing for language: **37.3 itself sends the language on sign-up and invitation acceptance and saves it when a signed-in member switches (owner decision, §4.3)**, and ships the two entry pages. Phase 38 may show sign-in and registration refusals by `code` (6b), the throttled sign-in message (6a), and the invitation language on the invite form (8a) | `register`/`accept`/`login` `{ …, language? }`; `PUT /api/me/language { language }` → 204; `POST …/invitations { …, language? }` |
| 37.4 | Lead rows with the customer (`customer.displayName` or the label by `labelCode`, `shortCode`, `reference`); open long transcripts with `tail=true` and page back with `before`; draw failed-turn and handoff dividers from `events`; a "Needs a person" view; unread dots and `POST …/read` when a transcript is seen; drop the client copy of `CustomerLabels.For` | `GET …/conversations?filter=needs-person`; `customer{…}`, `unread`; `GET …/messages?tail=true\|before=&include=events`, `previousCursor`, `events[]`; `POST …/conversations/{id}/read { sequence }` |
| 37.5a | A "Workspace name" setting for `workspace.manage` holders, sending `If-Match` (20a), and a note in the widget settings that visitors see the workspace name (20); show "Failed" from `lastIndexAttempt` after reload; file size from `sourceByteLength`; translate file refusals by `code`; agent channels from `channels` (drop the two channel-list reads); hosted page appearance and workspace name; `Start`/`End` corners in the widget settings; translate knowledge, agent and channel refusals by `code` | fields as in §3.2 rows 15–22, 20a, 28a |
| 37.5b | "Saving changes access for N members" from `memberCount`; `?status=Pending` paging; areas by `groupKey`; plan limits and fallback plan on Billing; translate team, role, invitation and billing refusals by `code`; use `GET /api/auth/session` on signed-out pages (no more console 401) | rows 23–29 |
| 37.6 | Purpose breakdown; previous-period deltas with `compare=previous`; "data from …" with `firstActivityOn`; a time-zone setting (with `workspace.manage`) and every date in `period.timeZone` instead of a hard-coded UTC; report names and notification report names by code (drop the regex on the notification body) | rows 25, 30–34 |
| 37.7 | Nothing required. Optionally show each execution's and schedule's `language` | row 36 |

## 8. Risks and open issues

- **Integration tests time out under parallel load.**
  - **What happened:** with the four test projects running in parallel against one throwaway PostgreSQL on this machine, `Npgsql` read timeouts appeared. Run 1 had test-class cleanup failures (exit 1 with 0 failed tests); run 2 had them too, plus one failed `DatabaseModelAuthorityTests` case.
  - **Result:** run 3, one project at a time (`-m:1`), was fully green and exited 0. No assertion failed in any run.
  - **What to do:** every Phase 37 step runs its backend gates with `-m:1`, or reports the same timeouts as environmental with the log. A step that sees an assertion failure must stop.
- **Leftover test databases: none remain.**
  - Runs 1 and 2 left 13 and 8 `omniai_test_*` databases inside their throwaway PostgreSQL; the cleanup timeouts kept them from being dropped. Those containers were removed, and the databases with them.
  - Run 3 left 0, and its containers and network were removed too.
  - After the connection drop, `docker ps -a` showed no `omniai_p370_*` container left.
- **Package versions** come from the advisories. The 37.1 restore confirms them; if 10.0.13 or 2.5.307 is not resolvable, 37.1 stops and asks.
- **Enumeration by timing** can only be bounded, not proven absent. 37.3 measures the known and unknown paths and reports the distributions.
- **Per-request stamp check:** one Redis read per signed-in request. 37.3 records the p95 of `/me` before and after; the budget is +2 ms.
- **Day-one unread:** after 37.4, every conversation is unread for every member until opened (no backfill, by rule 8). Nothing is deployed, so only the dev data shows it.
- **Arabic copy** for emails, refusal codes and entry pages needs a native reading, as in Phase 36.
- **Statement-count guards** in the analytics tests are raised deliberately in 37.6, once for `compare=previous` and once for `firstActivityOn`; each change is named in that step's report.
- **A future member-removal feature** must handle the new `RESTRICT` key from handoff events, as it already must for reply authors and assignees.
- **The drill CA of the 36.12 live stack expires on 2026-10-27.** Phase 37 does not touch that stack.

## 9. Proposed checkpoint commit message

```
docs: plan the backend that the redesign asked for

Phase 37.0. Places all 34 backend asks from Phase 36, and the owner's
three decisions, into steps 37.1 to 37.6 (37.5 split in two), with the
contract, data source, model change, tests and risk of each.

Lists the eight additive schema migrations Phase 37 may generate (no data
migrations), the compatibility rule for every changed behaviour, and the
designs for server-held language, bilingual email, password reset,
workspace time zones and usage by purpose.

Baseline: backend 0 warnings and 3,435 tests passed in Docker (NuGet
audit disabled on the command line for this baseline only); frontend
format, 1,105 tests, build and i18n green; p95 of every read Phase 37
will change, recorded on the seeded dev data.
```
