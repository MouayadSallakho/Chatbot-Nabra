# Phase 37.2: Local AI stand-in for development

An in-process stand-in for the AI provider, for developer machines only. Evidence is in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-2\`.

## 1. Summary

- On a developer machine, website chat and the agent preview now get an AI reply locally. No network, no key, no cost. WhatsApp uses the same reply path, but it cannot be tried locally without Meta. Every reply starts with "Local test reply:" or «رد تجريبي محلي:», so nobody can take it for a real answer.
- Knowledge documents index to Ready locally, and a reply names the passages it found, in English and Arabic.
- Writing `[[fail]]` in a customer message produces the honest failure the visitor sees when the provider is down, and the inbox updates live. `[[slow]]` makes the reply 10 seconds late.
- Analytics and Provider usage count these calls by purpose, at $0, under their own provider name, so they can never be mistaken for real spend.
- Nothing changes on a real deployment: the API refuses to start there if the stand-in is requested.

## 2. Starting point

- Branch `master`, HEAD `3ca0e0f` ("fix: clear the release blockers, and say Nabra where people read"), worktree clean.
- That is the 37.1 checkpoint, as "Starting point" requires.

**One conflict between the plan and the prompt was raised before any code was written (rule 19).** Plan item 5 says the stand-in's ledger rows are "unrated"; the prompt says "zero cost". **Your answer: zero cost**, with these conditions:
- the zero rate exists only when the stand-in is registered;
- a test proves there is no `DevelopmentStandIn` rate without the stand-in;
- real provider rates, and the rule that a call without usage is unrated, stay unchanged;
- `provider-usage-cost-accounting.md` and this report say that the stand-in's token counts are word counts, not real tokens.

This is the one difference from plan item 5; §4 records it. Everything else follows the plan: the two seams, `OMNIAI_DEV_AI_STANDIN=1`, the profile `dev:standin:1536`, the enum value `AiProvider.DevelopmentStandIn`, no migration and the registration matrix. Where the prompt adds build details the plan leaves open (the visible marker, the delay, `[[slow]]`, model `local-stand-in`), I followed the prompt.

## 3. Changes by area

```
 M backend/src/OmniAI.Api/Composition/CompositionRoot.cs
 M backend/src/OmniAI.Domain/Ai/AiProviderUsageEnums.cs
 M backend/src/OmniAI.Infrastructure/Ai/Usage/AiProviderPricingCatalog.cs
 M backend/src/OmniAI.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs
 M docs/architecture/ai-runtime.md
 M docs/architecture/knowledge-base-rag.md
 M docs/architecture/openai-provider.md
 M docs/architecture/provider-usage-cost-accounting.md
?? backend/src/OmniAI.Infrastructure/Ai/StandIn/
     LocalAiStandInOptions.cs, LocalAiStandInSwitch.cs, LocalStandInEmbeddingGenerator.cs,
     LocalStandInText.cs, LocalStandInTextGenerator.cs
?? backend/tests/OmniAI.Api.IntegrationTests/LocalStandIn/
     LocalStandInApiFixture.cs, LocalStandInApiTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Ai/StandIn/
     LocalAiStandInRegistrationTests.cs, LocalStandInEmbeddingGeneratorTests.cs,
     LocalStandInTextGeneratorTests.cs
?? docs/phase-reports/PHASE_37_2_LOCAL_AI_REPORT.md
```

**Outside the repository (allowed by this step):**
- `p36-01\p36-stack.sh`: `api` starts the API with `OMNIAI_DEV_AI_STANDIN=1` by default, and `P36_AI_STANDIN=0` starts it without. The canary now checks every `OMNIAI_LIVE_*` name by prefix, not just three names, and lists the stand-in switch as `info`, never as a failure.
- `p36-01\stack.md`: a "Local AI stand-in" section covering the markers `[[fail]]` and `[[slow]]`, the delay setting, the profile, how usage shows, and how to start the API without the stand-in.

### 3.1 Plan written before the code

- **Seams:** `IAiTextGenerator` (Application) and `ITextEmbeddingGenerator` (Application). Both are registered in exactly one place, `AddOmniAiRuntime` in Infrastructure, which today picks between the OpenAI adapters and the "unconfigured" refusals.
- **Smallest change:** a third branch in that same method, ahead of the key check. It registers two new Infrastructure classes behind the unchanged interfaces. No handler, endpoint, retriever, store or ledger changed, so every AI path the product has (web chat, WhatsApp, preview, indexing, retrieval) uses the stand-in without being touched.
- **Guard:** one public decision method, `LocalAiStandInSwitch.IsEnabled(configuration, development)`. The composition root calls it first, before any other startup check; `AddOmniAiRuntime` calls it again, so no path composes the stand-in without the rule. The OpenAI adapters and the `OMNIAI_LIVE_OPENAI` guard are untouched.
- **Vectors:** retrieval already filtered on the active profile, on both the document and the chunk (`KnowledgeSearchStore`, `d.indexed_profile = @profile AND c.embedding_profile = @profile`). Registering the profile `dev:standin:1536` was therefore enough; no query changed.
- **Rejected:** a fake HTTP endpoint for the OpenAI SDK (it would need a base-URL override in the real adapter and a socket); a compile-time symbol (the dev API runs a Release build); a test double registered in the API (test doubles stay in the test projects).

### 3.2 The stand-in (`backend/src/OmniAI.Infrastructure/Ai/StandIn/`)

| File | What it does |
| --- | --- |
| `LocalAiStandInSwitch.cs` (public) | The run-time switch `OMNIAI_DEV_AI_STANDIN`. Exactly `1` in Development turns it on. Startup is refused when it is set outside Development, set to any other value, or set together with `OMNIAI_LIVE_OPENAI=1`. Each refusal message names the variable. |
| `LocalAiStandInOptions.cs` | Model `local-stand-in`, profile `dev:standin:1536`, the markers `[[fail]]` and `[[slow]]` (10 s), and `LocalAiStandIn:ReplyDelayMilliseconds` (default 1000, from 0 to 60000, validated at startup). |
| `LocalStandInText.cs` | Words and language. A word is a run of letters and digits. English is lower-cased. Arabic has diacritics and tatweel removed, alef forms unified, ى read as ي and ة as ه, and a leading «ال» dropped. A message is Arabic when Arabic letters outnumber the others. |
| `LocalStandInTextGenerator.cs` | One ledger row, then the delay, then the reply or the failure. The reply is deterministic, written in the latest message's language, starts with the marker, says no AI model wrote it, quotes the message and names up to three retrieved passages in retrieval order ("And N more." beyond that). `[[fail]]` in the latest customer message settles the row as `Failed` and throws `AiRuntimeUnavailableException`, exactly what an unavailable provider does. A caller who cancels during the delay settles the row as `Failed`. |
| `LocalStandInEmbeddingGenerator.cs` | The same batch validation as the OpenAI adapter, then one ledger row per batch. Each word is hashed with 64-bit FNV-1a into one of 1,536 buckets, weighted 1 + ln(count), and the vector is scaled to unit length. FNV-1a rather than `string.GetHashCode()`, which is randomised per process: stored vectors must match queries embedded by a later run. |

**The two replies, word for word:**
- "Local test reply: this answer was written by a local test stand-in, not by an AI model." / Your message: "…" / "Knowledge passages that match your message (N):" with numbered titles and short quotes, or "No knowledge passage matched your message."
- «رد تجريبي محلي: كتب هذا الرد بديلٌ محلي للاختبار، وليس نموذج ذكاء اصطناعي.» / «رسالتك: «…»» / «مقاطع من المعرفة تطابق رسالتك (N):», or «لا يوجد في المعرفة مقطع يطابق رسالتك.» Beyond three passages: «ومقاطع أخرى عددها N.»
- Digits are Western, as everywhere else in the product. "Knowledge" is «المعرفة», per `GLOSSARY.md`.

### 3.3 Registration and the zero rate

- `AiProvider.DevelopmentStandIn = 2` (`backend/src/OmniAI.Domain/Ai/AiProviderUsageEnums.cs`). It is stored by name in the existing `provider varchar(32)` column, which has no check constraint, so no migration is needed.
- `AddOmniAiRuntime(configuration, requireLiveProviderOptIn, allowLocalStandIn = false)`. The new parameter defaults to the old behaviour, and the composition root passes `environment.IsDevelopment()`. With the stand-in on:
  - the registered `EmbeddingProfile` is `dev:standin:1536`, so the knowledge screens, the indexer and the retriever agree;
  - the stand-in's two classes are registered, and the method returns before the key is looked at.
- `AiProviderPricingCatalog.Create(entries, includeLocalStandIn = false)` adds the built-in zero rate (text generation and embedding, model `local-stand-in`, USD, in force from the Unix epoch) only when `includeLocalStandIn` is true. `AddOmniAiRuntime` passes `standIn`. A configured `AiPricing:Rates` entry for `DevelopmentStandIn` (by name, any case, or by number) stops startup, so no configured rate can outlive the stand-in.
- **Token counts are word counts, not real tokens.** Generation input is the words of the instructions, every turn and every grounding passage; output is the words of the reply. Embedding input is the words of every input. Total is their sum. The token columns are filled so that the rate applies and the cost is exactly 0.

### 3.4 Contract table

| Endpoint / surface | Change | New fields or parameters | Permission | Backward compatible |
| --- | --- | --- | --- | --- |
| `GET /api/tenants/{tenantId}/analytics/dashboard`, `models[].provider` (string) | May also read `DevelopmentStandIn`, only on a Development host running the stand-in | none | unchanged (`analytics.view`) | Yes. The field and its type are unchanged and the value is additive; the current frontend shows the model name, not the provider. A real deployment cannot produce it: the API refuses to start with the stand-in outside Development |
| Report files (PDF, CSV, XLSX), model usage rows, provider column | The same value, in the same local case only | none | unchanged | Yes, a value only |
| Every AI path: `POST …/ai-agents/{id}/preview`, hosted and embedded chat `POST …/messages`, WhatsApp turns, `POST …/knowledge/{id}/index`, retrieval | none. A Development host with `OMNIAI_DEV_AI_STANDIN=1` answers through the stand-in, with the same response shapes and status codes (200, or 503 for `[[fail]]` as for an unavailable provider) | none | unchanged | Yes. Opt-in, Development only; without the switch, behaviour is byte for byte today's |
| Knowledge `readiness` | none. Documents indexed by the stand-in carry profile `dev:standin:1536` and read as `NeedsIndex` under a real provider, as any profile change already does | none | unchanged | Yes, the existing profile rule |
| Startup | New refusal: `OMNIAI_DEV_AI_STANDIN` outside Development, with a value other than `1`, or with `OMNIAI_LIVE_OPENAI=1` | none | n/a | Yes. No deployment sets the variable today |

- **OpenAPI:** no request or response shape changed; `provider` is typed as a string, not an enum. The generated document is unchanged.
- **Architecture docs:**
  - `ai-runtime.md`: new **C19**, plus a note in C13 on the one exception;
  - `openai-provider.md`: D15, the adapter is unchanged and the stand-in cannot run together with the live provider;
  - `knowledge-base-rag.md`: §6, the stand-in's profile and why vectors never mix;
  - `provider-usage-cost-accounting.md`: new **§16**, plus an exception note in §1 (word counts, not tokens).

### 3.5 Migrations

None. The plan lists none for 37.2. The new enum value is stored by name in an existing unconstrained column, and EF's model snapshot does not record enum members, so `DatabaseModelAuthorityTests` and the pending-model check are unaffected. `omniai_p36` was not migrated, so no `pg_dump` was needed.

## 4. Decisions

- **Zero cost instead of "unrated" (difference from plan item 5).** Your ruling under rule 19. Plan item 5 said the stand-in's rows are "unrated"; they are now rated at a built-in zero rate that exists only while the stand-in is registered. To make a rate apply, the rows carry **word counts in the token columns, not real tokens**. `LocalAiStandInRegistrationTests` proves:
  - without the stand-in there is no `DevelopmentStandIn` rate;
  - configured rates are unchanged by it;
  - a call reporting no usage stays unrated even at the zero rate;
  - a configured rate for the stand-in is refused.
- **`OMNIAI_DEV_AI_STANDIN=1`, the plan's name, rather than the prompt's example `Ai__Provider=LocalStandIn`.** The prompt said "such as", and the plan is the authority on what a step contains. Like `OMNIAI_LIVE_OPENAI`, the switch accepts exactly `1`. Unlike that guard, any other value **stops startup** rather than being ignored, because a developer who wrote `true` would otherwise believe they were testing the stand-in.
- **The refusal runs first in the composition root,** ahead of the Phase 35 production settings check, so an operator who sets the variable on a deployment sees the reason, not a list of unrelated missing settings.
- **`[[fail]]` writes a `Failed` ledger row,** as the OpenAI adapter does when the provider refuses. Provider usage then shows the failed call locally, which is what 37.4's failed-turn markers and 37.6's usage by purpose need to see.
- **Markers are read from the latest customer message only.** An earlier `[[fail]]` in the conversation does not fail every later turn.
- **A cancelled stand-in call is settled `Failed`, not `Unknown`.** A real provider may have billed a cancelled call; the stand-in knows it did no work.
- **Language from letters, not characters,** so digits, punctuation and the markers do not decide the language of an Arabic message. Mixed messages follow the majority script.
- **Light Arabic normalisation, no stemming beyond the article.** It is enough for «الاسترداد» to match «استرداد». Deeper stemming would add rules nobody needs for a test stand-in.
- **The reply delay is a setting, `[[slow]]` is a constant.** Tests run with 0 ms, and the dev stack uses the 1 s default.
- **Rejected:**
  - a fake OpenAI HTTP server (it needs a socket and an endpoint override in the real adapter);
  - borrowing `AiProvider.OpenAi` for the rows (local rows would look like real spend);
  - a configurable stand-in rate (it could outlive the stand-in);
  - a delay or failure marker on embeddings (indexing should be quick and dependable locally; the failure path the plan names is the failed turn).
- **Deferred:** nothing from item 5.

## 5. Verification

| Check | Result |
| --- | --- |
| Clean restore, NuGet audit **on** (`dotnet restore OmniAI.slnx --force --no-http-cache`, SDK 10.0.400) | **PASS**, exit 0 |
| Resolved versions | **PASS**: Pkcs 10.0.13, MessagePack 2.5.303 (unchanged) |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: no vulnerable packages in any of the 8 projects |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: **0 warnings, 0 errors** |
| Tests, `-m:1`, throwaway PostgreSQL 17 + pgvector and Redis 7 (removed afterwards; 0 leftover test databases) | **PASS**, details in the next rows |
| — `OmniAI.Api.IntegrationTests` | **PASS**: 906 passed, 2 skipped (37.1: 897). +9: the composition root composes the stand-in; English and Arabic web-chat replies; `[[fail]]` answers 503 and keeps the message with a `Failed` row; two documents index to Ready and the reply names the right one; an Arabic question retrieves the Arabic passage; preview answers; another workspace's preview is still refused and spends nothing; the API refuses to start in `Production` with the switch |
| — `OmniAI.Application.Tests` | **PASS**: 732 passed (unchanged) |
| — `OmniAI.Domain.Tests` | **PASS**: 797 passed (unchanged) |
| — `OmniAI.Infrastructure.IntegrationTests` | **PASS**: 1,104 passed, 1 skipped (37.1: 1,053). +51: the registration matrix and the rate tie (`LocalAiStandInRegistrationTests`, 28); replies, markers in both languages, `[[fail]]`, `[[slow]]`, cancellation, zero cost and word counts (`LocalStandInTextGeneratorTests`, 14); dimension, unit length, determinism, retrieval order in English and Arabic, normalisation, ledger and batch limits (`LocalStandInEmbeddingGeneratorTests`, 9) |
| — Total | **3,539 passed, 3 skipped, 0 failed** (37.1: 3,479) |
| `DatabaseModelAuthorityTests` | **PASS** (inside the Infrastructure run) |
| Migrations on a fresh database and on a copy of the dev database | **NOT RUN**: this step has no migration |
| Frontend `format:check` | **PASS** |
| Frontend `test:ci` | **PASS**: 93 files, 1,119 tests (unchanged; no frontend file changed) |
| Frontend `build` | **PASS**: initial total 310.86 kB raw / 72.91 kB transfer (unchanged) |
| Frontend `i18n:check` | **PASS**: 2,035 messages, 0 errors, 0 warnings |
| Dev API published with `p36-stack.sh build` and recreated with `api` (stand-in on) | **PASS** (`gates\dev-build.log`, `gates\dev-api.log`) |
| Provider canary before any traffic | **PASS** (`gates\canary.txt`): `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, every `OMNIAI_LIVE_*` name (now checked by prefix) and every `Email__Smtp__*` (so `Username` and `Password` too) absent. `OMNIAI_DEV_AI_STANDIN` present, listed as `info` |
| API checks against the dev stack (`live\api-checks.json`) | **PASS**: English and Arabic hosted-chat replies 200 with the marker (1.7 s and 1.1 s, the delay); `[[fail]]` 503 with the message read back 200; analytics 200 with `DevelopmentStandIn` / `local-stand-in` at cost 0; preview 200 |
| Refusals on the dev stack | **PASS**: preview by a member without the permission (analyst) 403; preview and analytics from an account outside the workspace (newcomer) 404 and 404. No endpoint or field is new; these are the existing rules |
| Chrome, en: reply, live inbox, Lagoon voice | **PASS**: typing state, then the marked reply; the row appeared at the top of the open inbox without a reload; the reply sits in `bubble-ai` |
| Chrome, en: `[[fail]]` | **PASS**: the visitor sees the honest failure; the inbox row and the open transcript update live |
| Chrome, en: Take over and Return to AI on a conversation with AI replies | **PASS**: both states; after Return to AI the next message got a marked reply |
| Chrome, en: preview | **PASS**: the agent editor's customer view answered |
| Chrome, en: knowledge | **PASS**: a seeded document indexed to "Indexed" (Ready), was assigned, and a refunds question named it |
| Chrome, en: Analytics and Provider usage | **PASS**: AI replies 4; 10 provider calls (8 generation, 2 embedding; 2 failed), `local-stand-in`, $0.00 |
| Chrome, ar (RTL, شركة النور): the same six checks | **PASS**: «رد تجريبي محلي:» replies, live inbox in `bubble-ai`, استلام المحادثة and إعادتها إلى الذكاء الاصطناعي, `[[fail]]` live (`live\inbox-ar-failed-turn-live.png`), preview, «سياسة الشحن داخل المملكة» indexed and named, 7 calls at «0.00 US$» |
| Console | **PASS**: no error or warning on staff pages; the only entry on each visitor page is the browser's log of the deliberate `[[fail]]` 503 |
| p95 of changed list endpoints | **NOT RUN (n/a)**: no list endpoint changed in this step |
| Secrets | **PASS**: the password is in none of the 52 evidence and changed files (scanned by value, not printed). The sign-in helper was stopped; no `tmp\` folder was needed |

Logs are in `p37-2\gates\`:
- `backend-gates-run1.log`, with the raw restore, build and test logs in `run1\`;
- `fe-*.log`;
- `dev-build.log` and `dev-api.log`;
- `canary.txt`.

The live notes and 14 screenshots are in `p37-2\live\` (`live-checks.md`).

## 6. See it live

The dev API is on http://127.0.0.1:5027 with the stand-in on, and the dev server is on http://localhost:4200. Accounts are in `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`.

1. **A marked reply, live in the inbox.**
   1. Sign in as the owner, choose Northwind, and open http://localhost:4200/conversations.
   2. In a private window, open http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw and send any message. The typing state shows for about a second, then "Local test reply: …".
   3. The conversation appears at the top of the inbox without a reload. Open it: the reply is in the AI voice.
   4. In Arabic: switch the app to العربية, choose شركة النور, and use http://localhost:4200/chat/o-M5OubU2TJkyTCA-CRvYg. The reply starts «رد تجريبي محلي:».
2. **Failures on demand.** Put `[[fail]]` in a message: the visitor sees the honest failure, and the message appears in the inbox live. Put `[[slow]]` in a message: the reply comes about 11 seconds later.
3. **Handoff.** On that conversation, Take over, then Return to AI, then send another message from the visitor: the AI answers again.
4. **Preview.** AI Agents → Northwind Concierge → "Customer view": send a message.
5. **Knowledge.**
   - Northwind: "Return and refund policy (updated October 2026)" is now Indexed and assigned to Northwind Concierge. Ask "How long does a refund take?" in a new chat, and the reply names it.
   - شركة النور: «سياسة الشحن داخل المملكة» is indexed and assigned to مساعد النور. Ask «كم يستغرق الشحن داخل المملكة؟».
   - Any other document: open it and choose Index document.
6. **Usage.** Analytics → "AI provider usage": calls by kind, failed calls, and model `local-stand-in` at $0.00. Its "tokens" are word counts.
7. **Without the stand-in:** `P36_AI_STANDIN=0 bash p36-stack.sh api`, then the canary. AI answers 503 as before 37.2. `stack.md` has the details.

## 7. Frontend follow-ups

- **Required for Phase 38:** nothing. Every contract is unchanged.
- **Optional:** Provider usage could label the `DevelopmentStandIn` provider as "Local test stand-in" / «بديل محلي للاختبار», if Phase 38 ever shows the provider column. Today it shows only the model name, `local-stand-in`, which already says what it is.
- **For the Phase 38 end-to-end tests:** the stand-in makes AI replies, failed turns (`[[fail]]`), slow replies (`[[slow]]`), knowledge indexing and preview testable against the real local API, with deterministic text to assert on.

## 8. Risks and open issues

- **Word counts in token columns.** A local Provider usage page shows "tokens" that are word counts. They appear only for provider `DevelopmentStandIn` and model `local-stand-in`, cost $0, and are documented in `provider-usage-cost-accounting.md` §16. A real deployment cannot write such a row.
- **Switching a database between the stand-in and a real provider means indexing again.** That is the existing profile rule (`knowledge-base-rag.md` §6), and it is deliberate.
- **The stand-in replies even with no knowledge.** That is intended; the reply says no passage matched.
- **The marker words are English and Arabic only.** A message in a third language gets the English reply. The product supports these two languages.
- **`[[slow]]` holds the conversation's turn lock for 11 seconds.** That is the point of it, and it stays inside every timeout (the OpenAI adapter's 30 s). A second message in the same conversation waits, exactly as it would behind a slow provider.
- **The seeded dev data changed through the API.** Two seeded documents are now indexed under `dev:standin:1536` and assigned to their agents, and the test conversations above exist. This is ordinary use of the product.
- **Hosted chat page language** (from 37.1): its interface text stays English for an Arabic workspace; the stand-in's reply itself is Arabic.

## 9. Proposed checkpoint commit message

```
feat: answer locally with a marked stand-in, never on a real deployment

- A Development-only, in-process stand-in behind IAiTextGenerator and
  ITextEmbeddingGenerator, switched on by OMNIAI_DEV_AI_STANDIN=1: no
  network, no key, no cost. The API refuses to start with it outside
  Development, with any other value, or together with OMNIAI_LIVE_OPENAI.
- Replies are deterministic and start with "Local test reply:" or
  «رد تجريبي محلي:», in the customer's language, and name the knowledge
  passages they were given. [[fail]] fails like an unavailable provider;
  [[slow]] adds ten seconds.
- Embeddings are hashed word counts, normalised, at 1,536 dimensions,
  under their own profile dev:standin:1536, so they never mix with real
  vectors.
- Calls are accounted as provider DevelopmentStandIn, model
  local-stand-in, at a zero rate that exists only with the stand-in;
  their counts are word counts, not tokens.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
