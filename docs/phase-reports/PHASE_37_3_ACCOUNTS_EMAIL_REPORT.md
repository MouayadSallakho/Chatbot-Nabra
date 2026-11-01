# Phase 37.3: Accounts and email

Password reset, each person's language, every email in English and Arabic, invitation emails, sign-in limits and codes, and a local mail sink. Evidence is in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-3\`.

## 1. Summary

- Somebody who forgot their password can ask for a link from the sign-in page and set a new one. Every session they had ends at once, a "your password was changed" email follows, and nobody can learn from the page, its timing or its limits whether an address has an account.
- Every email Nabra sends is in its reader's language: Arabic right to left, or English. This covers all 13 operational emails and the reset, password-changed and invitation emails.
- Each person's language is stored on their account, from sign-up or invitation acceptance, and saved again when they switch language while signed in.
- Invitations are now also emailed, in the language the inviter chose (else the inviter's own). The copied link keeps working.
- Sign-in is limited per address and per network. Sign-in and registration refusals carry stable codes. Locally, every email lands in Mailpit.

## 2. Starting point

- Branch `master`, HEAD `e1265dc` ("feat: answer locally with a marked stand-in, never on a real deployment"), worktree clean. That is the 37.2 checkpoint, as "Starting point" requires.

**Three conflicts between the plan and the prompt were raised before any code was written (rule 19). Your answers:**
- **"Your password was changed" email** (in the prompt, not in plan §3.5): **added**. It is a third `account_emails` kind, `PasswordChanged`, with no link to act on. It is audited as `auth.password_changed.email_sent` / `…email_failed`. No migration beyond M2 was needed, because `kind` has no check constraint.
- **First stored language** (prompt: "…then English"; plan: "otherwise unset"): **the plan's NULL**. When neither the field nor `Accept-Language` gives `en` or `ar`, the column stays NULL; every email still reads English, and a later sign-in can fill it.
- **Expired-link check** (prompt: 1-minute lifetime; plan: 10–60 minutes): **1–60 minutes in Development only**, 10–60 everywhere else. The live check used 1 minute and the API was restored to 30 afterwards.

**Raised during the work (rule 3, a frontend change outside the step's list):** the registration page still said "there is no password reset". **Your answer: reword it now.** It now says only "Email addresses are not verified yet." / «لا يُتحقَّق من عناوين البريد الإلكتروني بعد.»

**Raised during the Chrome checks:** the Chrome DevTools MCP could not start, because its shared profile was held by a Chrome that another of your Claude sessions had opened. **Your answer: use Playwright instead.** After a session restart the Playwright MCP server could not connect (connection timeout). The checks were therefore run with the Playwright library (1.64, already on this machine in the npx cache; nothing installed) driving the **installed Google Chrome** (`channel: "chrome"`), headless, with a fresh temporary profile per run (§5).

The plan's other 37.3 items were built as the plan describes them: rows 6, 6a, 6b, 7, 8 and 8a, and migrations M1, M3, M2 and M2a. The prompt does not mention rows 6a, 6b and 8a, but rule 19 makes the plan decide which items belong to a step.

## 3. Changes by area

```
 M backend/src/OmniAI.Api/Auth/{AuthContracts,AuthEndpoints,MeEndpoints}.cs
 M backend/src/OmniAI.Api/Composition/CompositionRoot.cs
 M backend/src/OmniAI.Api/Security/AuthenticationSetup.cs
 M backend/src/OmniAI.Api/Team/{TeamContracts,TeamEndpoints}.cs
 M backend/src/OmniAI.Application/Invitations/{AcceptTenantInvitation/AcceptTenantInvitationHandler,
     CreateTenantInvitation/CreateTenantInvitationHandler,Persistence/ITenantInvitationRepository}.cs
 M backend/src/OmniAI.Application/Notifications/{NotificationCatalog,NotificationPorts,NotificationWorkers,
     OperationalEmail,OperationalEmailRenderer}.cs
 M backend/src/OmniAI.Application/Security/IAccountDirectory.cs
 M backend/src/OmniAI.Domain/Invitations/TenantInvitation.cs
 M backend/src/OmniAI.Infrastructure/Identity/{IdentityAccountDirectory,OmniAiUser}.cs
 M backend/src/OmniAI.Infrastructure/Notifications/NotificationDirectory.cs
 M backend/src/OmniAI.Infrastructure/Persistence/{PersistenceConverters,PersistenceSchema}.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Configurations/{IdentityConfiguration,TenantInvitationConfiguration}.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Migrations/OmniAiDbContextModelSnapshot.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Repositories/TenantInvitationRepository.cs
 M backend/src/OmniAI.Infrastructure/WebChat/Protection/{PublicChatProtectionSetup,RedisPublicChatProtection}.cs
 M backend/tests/... (fixtures, two schema tests, the /me shape test, the email tests; see §5)
RM backend/tests/OmniAI.Infrastructure.IntegrationTests/Notifications/SmtpSink.cs -> backend/tests/Shared/SmtpSink.cs
 M docs/architecture/{admin-design-system,authentication-tenant-membership,human-handoff-realtime-inbox,
     operational-notifications,public-chat-protection,team-members-invitations}.md
 M frontend/src/app/... (22 files) and frontend/src/locale/messages{,.ar}.json
?? backend/src/OmniAI.Api/Auth/{AccountProblems,PasswordResetEndpoints}.cs
?? backend/src/OmniAI.Api/Realtime/SessionConnections.cs
?? backend/src/OmniAI.Api/Security/{PasswordResetTokens,SessionStamps}.cs
?? backend/src/OmniAI.Application/Accounts/{AccountPorts,AccountEmailRenderer}.cs
?? backend/src/OmniAI.Application/Notifications/EmailLayout.cs
?? backend/src/OmniAI.Domain/{Accounts/AccountEmail,Security/SecurityEvent,Users/UserLanguage}.cs
?? backend/src/OmniAI.Infrastructure/Accounts/{AccountEmailDispatcher,AccountEmailOutbox,AccountProtection,
     AccountServices,PasswordReset}.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Configurations/{AccountEmailConfiguration,SecurityEventConfiguration}.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261021113739_AddUserLanguage{,.Designer}.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261021113933_AddSecurityEvents{,.Designer}.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261021114100_AddAccountEmailOutbox{,.Designer}.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261021114515_AddInvitationEmail{,.Designer}.cs
?? backend/src/OmniAI.Infrastructure/WebChat/Protection/NetworkKeySecret.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Accounts/ (fixture + 6 suites)
?? backend/tests/OmniAI.Application.Tests/Notifications/BilingualEmailTests.cs
?? backend/tests/OmniAI.Domain.Tests/Accounts/AccountDomainTests.cs
?? docs/architecture/problem-codes.md
?? frontend/src/app/core/i18n/language-service.spec.ts
?? frontend/src/app/features/auth/{forgot-password/,reset-password/,password-policy.ts,password-reset-failure.ts}
?? docs/phase-reports/PHASE_37_3_ACCOUNTS_EMAIL_REPORT.md
```

**Outside the repository (allowed by this step):**
- `p36-01\p36-stack.sh` and `stack.md`:
  - a `mailpit` command;
  - `api` now runs the API inside the mail sink's network namespace, with the loopback SMTP settings and `Email__AppBaseUrl`;
  - `up` and `stop` include the two new containers;
  - the canary allows only a loopback `Email__Smtp__Host` (port, security and sender), and fails on an SMTP username, an SMTP password or any `OMNIAI_LIVE_*`;
  - `P37_RESET_LIFETIME` is a knob for the expired-link check only.

  Copies of both files as they were before this step are in `p37-3\scratch\`.
- New containers: `omniai-p37-netns` (publishes the API on `127.0.0.1:5027` and Mailpit's UI on `127.0.0.1:8026`) and `omniai-p37-mailpit` (`axllent/mailpit:v1.27`, no relay). Port 8025 belongs to another project's Mailpit, which was not touched.

### 3.1 Each person's language (row 7, M1)

- `users.language` holds `en`, `ar` or NULL. It is read as `UserLanguage` through `IAccountDirectory.FindLanguageAsync` and written by `IAccountLanguages`, one conditional `UPDATE` each.
- **How it is first set:** the optional `language` on sign-up, sign-in and invitation acceptance; else `Accept-Language` (first supported primary tag by q-value); else NULL. Sign-in and acceptance only fill an empty value. `PUT /api/me/language` changes a chosen one.
- **Frontend:**
  - The client sends the page's language on sign-up and on acceptance.
  - `LanguageService.choose` saves the choice with the PUT for a signed-in person. It waits at most 2 s before the reload, and a failure never blocks the switch. Being in the service, it covers the account menu, the command palette and the entry frame.
  - The interface still takes its language from `localStorage` (D22 recorded in `admin-design-system.md`).
- **Recipients without an account:** as the plan defines. Invitees get the invitation's language (stored on the invitation by M2a, defaulting to the inviter's). Scheduled reports still have no external recipients, only the schedule's owner, who is emailed in their own language. A schedule's own language is M10, in step 37.7.

### 3.2 Every email in the reader's language (row 8)

- `EmailLayout` is one layout for every email:
  - a constant subject per kind and language (`Nabra: …` / `نبرة: …`);
  - `<html lang="ar" dir="rtl">` with `dir` repeated on the body and the card, or `lang="en" dir="ltr"`;
  - a plain-text part with the same words;
  - names somebody wrote (a workspace, a report) isolated with FSI…PDI in text and `<bdi>` in HTML;
  - nothing to load, no tracking.
- `NotificationCatalog.Describe(kind, language, reportType…)`: the Arabic sentences are word for word the client's own `notification.*` and `reports.type.*`, which follow `GLOSSARY.md`. The report type is derived at send time (notification → occurrence → execution). The in-app notification keeps its stored English text.
- **The emails, each in both languages (16):**
  - report ready, report failed, scheduled report ready, scheduled report failed;
  - AI usage 70 %, AI usage 90 %, monthly limit reached;
  - payment past due, subscription suspended, subscription active again, subscription ended;
  - knowledge indexing failed;
  - WhatsApp delivery outcome unknown;
  - set a new password, your password was changed, invitation.

### 3.3 Password reset (row 6, M2, M3)

- **Request.** `POST /api/auth/password/forgot` always answers 202 with no body. Both cases do the same work:
  - the limiter counts unknown addresses too;
  - the database runs the same two statements in one transaction (`SELECT … FOR UPDATE` of the account row, then one statement that queues the email if the account exists and has none pending, and writes the `auth.password_reset.requested` audit row in every case);
  - every answer is padded to 400 ms by a stopwatch loop.
- **The outbox** stores only who and when. The worker mints the token and builds the link at send time, and skips a request older than 15 minutes.
- **Token.** No token provider was registered before; I confirmed this in `AuthenticationSetup`. `PasswordResetTokenProvider : DataProtectorTokenProvider<OmniAiUser>` (`NabraPasswordReset`) is registered with `AddTokenProvider` and set as `Tokens.PasswordResetTokenProvider`.
  - It writes the same payload as the base class: time, id, purpose and stamp. It uses the platform clock, compares the id and the stamp with `FixedTimeEquals`, and encodes the token as base64url.
  - It lasts 30 minutes, and a reset rotates the stamp, so a token works once.
  - The shared key ring makes it valid on both production APIs.
  - It lives in the API project, because the ASP.NET Core shared framework is not referenced by Infrastructure, and adding it would have been a dependency change.
- **Link.** `Email:AppBaseUrl` + `/reset-password#uid=…&token=…`. The page strips the fragment with `history.replaceState` as soon as it reads it.
- **Reset.** `POST /api/auth/password/reset`:
  - 204 on success;
  - one 400 `auth.reset.invalid` for every bad link;
  - `auth.password.*` only once the token has been accepted.

  On success, in order: the lockout is lifted, the new stamp is cached, the account's hub connections close on every instance, the password-changed email is queued, and `completed` and `sessions.revoked` are audited.
- **Sessions.** `OnValidatePrincipal` now compares the cookie's stamp with the account's current stamp on every request:
  - from Redis (`omniai:auth:stamp:v1:{userId}`, 10-minute expiry, filled with `SET NX`), else from the database;
  - a mismatch is re-read from the database before anybody is signed out;
  - the cookie is re-issued at most once per 30-minute validation interval, as Identity's validator did. The first full run showed that dropping this renewal changed sliding expiry, and `AiAgentTimeApiTests` caught it.
- **Hubs.** `SessionConnections` (per instance), the Redis channel `omniai:auth:revoked`, and `SessionStampHubFilter`, which also refuses any invocation whose stamp is stale.

### 3.4 Invitation email (row 8a, M2a)

- An optional `language` on `POST …/invitations`; else the inviter's language; else none.
- One `Invitation` email is queued in the same transaction (only when email is configured, as for operational email).
- The worker mints a second 256-bit token and stores its SHA-256 in `email_token_hash`. The hash write and the dispatch boundary happen in one transaction, and only while the worker owns the email and the invitation is open.
- Acceptance looks the invitation up by either hash, then `TenantInvitation.IsRedeemableWith` compares both in constant time. Both comparisons always run.
- Revoking, expiring or accepting the invitation ends both tokens.

### 3.5 Sign-in limits and refusal codes (rows 6a, 6b)

| Counter | Default |
| --- | --- |
| ~~Sign-in per address / per network~~ | ~~10 / 50 per 15 minutes~~ (superseded by Revision 1) |
| Failed sign-ins: one address on one network (Revision 1) | 5 per 15 minutes |
| Failed sign-ins: one network (Revision 1) | 50 per 15 minutes |
| Failed sign-ins: one address on every network (Revision 1) | 100 per hour |
| Reset requests per address / per network | 3 / 10 per hour |
| Reset attempts per network | 20 per 15 minutes |

Revision 1: sign-in counts failures only (a read before the password, an increment after a failure, a success clears the address on its network), and Identity's lockout is no longer applied at sign-in. See R1.2.

- **Store.** Redis, the public chat's atomic script, and the same key secret. Addresses are hashed under a key derived from that secret with its own label. `NetworkKeySecret` now feeds both key sources.
- **Refusals.** `429 auth.throttled` with `Retry-After`. `503 auth.throttle.unavailable` when the store is unreachable or, following P11, when the client's network cannot be established.
- **Codes** are listed in the new `problem-codes.md`. An unknown address and a wrong password are one code and one body.

### 3.6 Contract table

| Endpoint / surface | Change | New fields or parameters | Permission | Backward compatible |
| --- | --- | --- | --- | --- |
| `POST /api/auth/register` | Stores the language; refusals carry codes | request `language?` (`en`/`ar`); response `language`; problem `code`, `codes` | anonymous + antiforgery (unchanged) | Yes. Optional field; statuses, titles and `detail` unchanged; new response field is ignored by today's client |
| `POST /api/auth/login` | Rate-limited; fills an empty language; refusals carry codes | request `language?`; response `language`; problem `code`; new 429 `auth.throttled` / 503 `auth.throttle.unavailable` | anonymous + antiforgery | Yes. A normal sign-in is unchanged; 429/503 occur only past the limits or without the limiter, and the current client shows its generic failure for them |
| `GET /api/auth/me` | Stored language | `language` (`"en"`, `"ar"` or `null`) | signed in | Yes. Additive field |
| `POST /api/auth/password/forgot` | **New** | `{ email }` → 202, no body; 429, 503 | anonymous + antiforgery | New endpoint |
| `POST /api/auth/password/reset` | **New** | `{ userId, token, newPassword }` → 204; 400 `auth.reset.invalid` / `auth.password.*`; 429; 503 | anonymous + antiforgery | New endpoint |
| `PUT /api/me/language` | **New** | `{ language }` → 204; 400 `language.unsupported`; 401 | signed in + antiforgery | New endpoint |
| `POST /api/tenants/{id}/invitations` | Invitation language; queues an invitation email | request `language?`; 400 `language.unsupported` | `members.invite` (unchanged) | Yes. Response shape unchanged (`invitation`, `token`); the copied link works as before |
| `POST /api/invitations/accept` | Accepts the emailed token too; fills an empty language | request `language?` | signed in (unchanged) | Yes. The old token and all status codes behave as before |
| Session cookie | Stamp checked on every request; a reset ends every session | none | — | Yes. Renewal cadence kept (30 min) |
| `/hubs/workspace-inbox` | Connections close on a reset; a stale stamp refuses invocations | none | unchanged | Yes |
| Emails (SMTP) | In the recipient's language; three account emails | none | — | Yes. English emails read as before, with names isolated |

**OpenAPI.** The document is generated from the endpoints, and none carried metadata before. The new and changed endpoints now carry summaries and response types (`WithSummary`, `Produces`, `ProducesProblem`), and the new request and response fields appear through their records.

### 3.7 Migrations (all generated with `dotnet ef migrations add`, none edited)

| Migration | Model change | SQL (evidence `sql\`) |
| --- | --- | --- |
| `20261021113739_AddUserLanguage` (M1) | `users.language varchar(8) NULL`, `ck_users_language` | `AddUserLanguage.sql` |
| `20261021113933_AddSecurityEvents` (M3, as amended) | table `security_events`; FKs `fk_security_events_user` / `fk_security_events_tenant` SET NULL; `ix_security_events_user_occurred` (partial), `ix_security_events_occurred`, convention index on `tenant_id` | `AddSecurityEvents.sql` |
| `20261021114100_AddAccountEmailOutbox` (M2) | table `account_emails`, `pk_account_emails`, `ck_account_emails_attempts`, `fk_account_emails_user` CASCADE, `ix_account_emails_claimable` (partial), `ix_account_emails_user_requested` | `AddAccountEmailOutbox.sql` |
| `20261021114515_AddInvitationEmail` (M2a) | `tenant_invitations.language` + `ck_tenant_invitations_language`, `tenant_invitations.email_token_hash bytea` + `ux_tenant_invitations_email_token_hash` (partial); `account_emails.user_id` nullable, `invitation_id`, `tenant_id`, `fk_account_emails_invitation` CASCADE with its index, `ck_account_emails_recipient` | `AddInvitationEmail.sql` |

How they were tested:
- On a fresh database: the whole chain applies (`gates\migration-checks.log`).
- On a copy of the dev database restored from the `pg_dump`: it moves forward from 2 to 6 migrations, with counts unchanged (14 users, 14 invitations, 405 messages). The two new checks refuse bad rows.
- `has-pending-model-changes`: none.
- `DatabaseModelAuthorityTests` pass.
- `omniai_p36` was migrated forward with `migrate`, after `pg_dump` into `sql\omniai_p36-before-p37-3.dump`.

One naming note: EF named the convention indexes `IX_security_events_tenant_id` and `IX_account_emails_invitation_id_tenant_id`, in upper case like every convention index in the Initial. The plan wrote `ix_…`. They were kept, not hand-renamed.

## 4. Decisions

- **Your three plan–prompt answers** (§2), and the reworded registration note.
- **Equal work, by construction rather than by sleeping alone.** Both cases run the same two SQL statements in one transaction, the audit row included, and every answer is then padded to a floor. I rejected queueing a lookup-and-send job for unknown addresses, because the outbox would then have had to hold an address.
- **The token provider writes its own payload,** so its clock can be moved in tests and the id and stamp are compared in constant time. It lives in the API (no new framework reference in Infrastructure).
- **Stamp check: Redis first, a database re-read on a mismatch.** A stale cache can never lock a fresh session out, and the plan's +2 ms budget holds (§5).
- **Cookie renewal kept at Identity's 30-minute cadence**, so sliding expiry behaves exactly as before.
- **Unknown is not a bucket** (public-chat-protection.md P11): the account endpoints refuse a request with no establishable network, rather than give it a shared lane. Only `TenantApiFixture` needed a simulated peer for this.
- **The SMTP test sink moved to `tests/Shared`.** It now reads bytes faithfully (Latin-1), so Arabic 8-bit parts can be decoded, and it un-stuffs SMTP dot-stuffing.
- **Deferred:** a language on report schedules (M10, 37.7); limiting registration; auditing successful and failed sign-ins (only throttled sign-ins are audited); email bounce handling.

## 5. Verification

| Check | Result |
| --- | --- |
| Clean restore, NuGet audit on (`dotnet restore OmniAI.slnx --force --no-http-cache`, SDK container) | **PASS**, exit 0, no NU19xx |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: no vulnerable packages in any of the 8 projects (Pkcs 10.0.13, MessagePack 2.5.303) |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: 0 warnings, 0 errors |
| Tests, `-m:1`, throwaway PostgreSQL 17 + pgvector and Redis 7 (run 2, `gates\backend-gates-run2.log`) | **PASS**, details in the next rows. 0 leftover test databases; containers removed |
| — `OmniAI.Api.IntegrationTests` | **PASS**: 943 passed, 2 skipped (37.2: 906; +37) |
| — `OmniAI.Application.Tests` | **PASS**: 803 passed (37.2: 732; +71: every email kind × language, layout, report names, account emails, invitation language) |
| — `OmniAI.Domain.Tests` | **PASS**: 823 passed (37.2: 797; +26) |
| — `OmniAI.Infrastructure.IntegrationTests` | **PASS**: 1,106 passed, 1 skipped (37.2: 1,104; +2 send-time language) |
| — Total | **3,675 passed, 3 skipped, 0 failed** (37.2: 3,539) |
| Run 1 (for the record) | **FAIL, fixed.** Two schema tests pinned the old tables, and `AiAgentTimeApiTests` caught the lost cookie renewal; all three were fixed. Its cleanup timeouts and 6 leftover databases came from running alongside the frontend suite and a dev build; run 2, alone, had none |
| `DatabaseModelAuthorityTests` | **PASS** |
| Migrations on a fresh database and on a copy of the dev database | **PASS** (§3.7) |
| Enumeration timing (API test, 120 requests per case, interleaved) | **PASS**: known median 402.1 / p90 405.1 ms; unknown 402.2 / 405.6 ms; both ≥ 400 ms floor |
| Expiry and single use, rate limits across two hosts, every session ending, hub connections closing on both instances, audit rows, nothing stored or logged with the link (exact columns; every log line captured), email content in both languages | **PASS** (the `Accounts` suites) |
| Frontend `format:check` | **PASS** |
| Frontend `test:ci` | **PASS**: 96 files, **1,166** tests (37.2: 93 / 1,119; +47). A first run alongside the backend suite had one 5 s timeout in `app-shell.spec.ts`; alone it passes 39/39, and the full re-run passes |
| Frontend `build` | **PASS**: initial total 313.56 kB raw / 73.66 kB transfer |
| Frontend `i18n:check` | **PASS**: 2,075 messages, 0 errors, 0 warnings |
| Dev API rebuilt (`build`, `api`) and recreated in the Mailpit namespace; `omniai_p36` migrated after `pg_dump` | **PASS** |
| Provider canary before any traffic, after each recreation | **PASS**: every provider variable and `OMNIAI_LIVE_*` absent; no SMTP username or password; SMTP host loopback (`gates\canary*.txt`) |
| API checks against the dev stack (`live\api-checks.json`) | **PASS** 21 of 22: the new fields, codes and statuses; the invitation in Arabic; a member without `members.invite` → 403; another workspace's owner → 404 to invite and to list; the 11th sign-in → 429 with `Retry-After`. **One observation:** `forgot` answered 429 for both the known and the unknown address, because the live checks had already spent this network's hourly budget of 10. Identical answers are what the design requires; the 202 path is proven live in the next rows and in the tests |
| Live checks, en and ar: **Playwright driving Google Chrome** (`channel: chrome`, headless, temporary profile), **not the Chrome DevTools MCP** (§2) | **PASS** 32/32 + 4/4 + 8/8 (`live\*.json`, 46 screenshots) |
| — Accounts created through the sign-up page, sending their language | **PASS** (`me.language` en / ar) |
| — Known and unknown address get the same screen; only the known one is emailed | **PASS** |
| — The reset email arrives in Mailpit in the account's language; the link is clicked in Mailpit and the page removes `uid` and `token` from the address bar | **PASS**. Screenshots show the button, never the link |
| — New password set; the old browser's session ends (`/me` 401, sent to sign-in); the new password signs in | **PASS** |
| — Used link: the honest message, with a link to ask again | **PASS** |
| — Expired link (dev API started with a 1-minute lifetime for this check only, then restored to 30 and canary re-run) | **PASS**; the email says «دقيقة واحدة» / "1 minute" |
| — Switching language while signed in stores it, and the next email follows it | **PASS** (en → ar and ar → en) |
| — "Password changed" email in the account's language | **PASS** |
| — Invitation email in Mailpit in en and ar; its link opens the acceptance page with a clean address bar | **PASS** |
| — Forgot, reset and sign-in pages: en/ar × light/dark × 390/1920 px, direction right, no horizontal scroll; "Forgot your password?" on sign-in; keyboard and labels | **PASS** |
| — Console | Only expected statuses: 401 from the existing session probe on signed-out pages (and the deliberate `/me` after the session ended), and 400 from the used links. No script error |
| p95 of changed list endpoints | **NOT RUN (n/a)**: no list endpoint changed. The plan's budget for the per-request stamp check is on `GET /api/auth/me`: **before** p95 17.4 and 16.8 ms; **after** 17.1, 16.9 and 16.6 ms warm (300 reads each; the first run after a restart was 20.8 ms while warming). Within +2 ms |
| Secrets | No password, token or link in any repository file, kept log, screenshot or this report. Live passwords were random and lived only in each script's memory; no `tmp\` folder was needed |

## 6. See it live

Dev app http://localhost:4200, API http://127.0.0.1:5027, **Mailpit http://127.0.0.1:8026**.

1. **Forgot password.** Open http://localhost:4200/sign-in → "Forgot your password?" → enter the address of an account you create at http://localhost:4200/create-account. Do it again with an address that has no account: the screen is the same.
2. **The email.** In Mailpit, open "Nabra: Set a new password" (or «نبرة: تعيين كلمة مرور جديدة» for an Arabic account) and click the button. The reset page opens with a clean address bar. Set a new password: any other browser signed in as that account is signed out on its next request, and "Your password was changed" arrives.
3. **A used link.** Click the same button again and try: the page says the link can no longer be used, with a way to ask for a new one.
4. **Language.** Sign up with the page in العربية: emails come in Arabic, right to left. Switch the language while signed in, ask for another reset, and the next email follows the switch.
5. **Invitations.** Invite somebody from Team → the invitation email is in Mailpit, in the inviter's language unless another was chosen (the API takes `language`). Its button opens the acceptance page; the copied link still works too.
6. **Expired link (optional).** `P37_RESET_LIFETIME=00:01:00 bash "$S" api`, ask for a link, wait a minute, open it; then `bash "$S" api` to restore 30 minutes.

## 7. Frontend follow-ups (Phase 38)

- **Language:** nothing; 37.3 already sends and saves it.
- **Sign-in throttling and outages:** word `auth.throttled` (show `Retry-After`) and `auth.throttle.unavailable` on the sign-in page; today they fall into the generic failure. Contract: `POST /api/auth/login` → 429 / 503 problem with `code`.
- **Refusal codes:** word sign-in and registration refusals by `code` (`problem-codes.md`); `codes` lists every password rule.
- **Invitation language:** a language choice on the invite form. Contract: `POST /api/tenants/{id}/invitations { email, roleIds, language? }`.
- **Optional:** show `GET /api/auth/me`'s `language` in the account menu as "emails in …".

## 8. Risks and open issues

- **Enumeration by timing is bounded, not proven absent.** The medians differ by 0.1 ms over 120 requests, under a 400 ms floor. A floor longer than the slowest real path is assumed; a very slow database could exceed it for both cases alike.
- **A failed stamp write to Redis** after a reset leaves the old stamp cached for up to 10 minutes. Old sessions could then pass for that long, and the log says so (event 4321). New sessions are never locked out (the database re-read).
- **`ck_account_emails_recipient` checks `user_id` against `invitation_id` only,** as approved. PostgreSQL does not enforce the composite invitation key while `tenant_id` is NULL, so the code always sets both together (the `ForInvitation` factory, covered by a test).
- **Guessing spread over many networks can reach the per-address ceiling** (Revision 1): 100 failures in an hour, from at least twenty networks, refuse the owner's sign-in from every network until the counter expires. It never locks the account, and a password reset still works.
- **Registration is still not rate-limited,** and a taken address is still reported (the documented exception until email verification).
- **Successful and failed sign-ins are logged, not audited;** only throttled ones are.
- **Arabic email wording** reuses the client's reviewed sentences; the three account emails are new Arabic and need a native reading before release.
- **The Chrome DevTools MCP was not used** for this step's live checks (§2). Playwright drove the real Chrome instead.
- **Mailpit's "HTML Check" scores the emails at 73–76 %.** It is a client-compatibility heuristic for the inline layout, not an error; the layout is the 37.1 one.

## 9. Proposed checkpoint commit message

```
feat: reset passwords, write to everyone in their language, email invitations

- Password reset: POST /api/auth/password/forgot (202 always, same work for
  every address, padded) and /reset (one refusal for every bad link). A
  dedicated Identity token provider, 30 minutes, single use, both APIs. The
  link is built at send time and carries its secret in the fragment only.
- A reset ends every session at once: the session stamp is checked on every
  request (Redis, then the database) and the account's hub connections close
  on every instance. "Your password was changed" follows.
- users.language (M1), set from sign-up, sign-in and acceptance, changed with
  PUT /api/me/language; every email renders in its reader's language, Arabic
  right to left, names isolated.
- Invitations are emailed in the invitation's language (M2a); the emailed and
  the copied token both work, compared in constant time.
- Sign-in and reset limits per address and per network in Redis, failing
  closed; stable codes on sign-in and registration refusals; security_events
  (M3) and account_emails (M2).
- Frontend: forgot-password and reset-password pages, the sign-in link, the
  language sent on sign-up and acceptance and saved when it changes.
- Dev stack: Mailpit on loopback in the API's network namespace.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-10-21)

### R1.1 The notes

Your review found that sign-in protection broke decision D-2 ("No hard lockout. Nobody can lock another person out"):
1. **A stranger could lock an owner out, two ways.** Identity's lockout was still on (`lockoutOnFailure: true`, 5 failures, 15 minutes), and the per-address counter counted every attempt from every network.
2. **Successful sign-ins used up the network budget.** A support team of more than 50 agents behind one office IPv4 address would have been refused at the start of a shift.

You asked for: count failures, not attempts; three configurable counters; Identity's lockout off for sign-in; the listed tests; and the §3.5 table, the architecture doc and §8 brought up to date.

### R1.2 What changed

| Area | Change | Files |
| --- | --- | --- |
| Sign-in limiter | Three **failure** counters replace the two attempt counters: `SignInFailuresPerAddressOnNetwork` 5 / 15 min, `SignInFailuresPerNetwork` 50 / 15 min, `SignInFailuresPerAddress` 100 / 1 h, each `Limit` + `WindowSeconds`. `IAccountProtection.AuthoriseSignInAsync` becomes `CheckSignInAsync` (before the password), `RecordSignInFailureAsync` (after a failed check; an unknown address counts too) and `RecordSignInSuccessAsync` (counts nothing; clears the address's failures on that network) | `AccountPorts.cs`, `AccountProtection.cs`, `AuthEndpoints.cs`, `AccountProblems.cs` |
| One addition beyond the literal notes (security review) | "Only read the counters" before the password would let a burst of parallel guesses all pass the check before the first was counted. So the check atomically **reserves an in-flight slot** in each counter, and failures plus sign-ins in flight never exceed a limit. A failure turns the slot into a counted failure; a success releases it and counts nothing. A slot whose outcome is never recorded expires after 10 s, and a refusal caused only by sign-ins in flight says `Retry-After: 1`. Observable behaviour is as you specified; only concurrency is bounded | `AccountProtection.cs` |
| Identity lockout | Sign-in passes `lockoutOnFailure: false`. The lockout settings stay configured and are no longer reached from sign-in. A lockout left from before is honoured until it expires, and a password reset still lifts it | `AuthEndpoints.cs`, `AuthenticationSetup.cs` (remark only) |
| Failing closed | A failure that cannot be counted answers 503 `auth.throttle.unavailable`, like a check that cannot be made. A success whose slot cannot be released is logged (event 4341) and still succeeds | `AuthEndpoints.cs` |
| Tests | `AccountProtectionApiTests` rewritten for sign-in: a stranger on network A does not stop the owner on B; 60 successful sign-ins from one network all succeed (failure limit 6); failures reach 429 with `Retry-After`, identical for a known and an unknown address; the per-address ceiling across networks; the per-network failure limit; a success clears its address on its network; two instances share the counters; **a burst of 20 parallel guesses cannot overshoot the limit**. New `NoLockoutApiTests`: 20 failures, then the right password signs in, with no lockout end and no failure count. In `AuthEndpointsTests`, "Repeated failures lock the account" became "…are throttled on their network and never lock the account" | `Accounts/AccountProtectionApiTests.cs`, `Accounts/AccountApiFixture.cs`, `Auth/AuthEndpointsTests.cs` |
| Docs | `authentication-tenant-membership.md`: D11 (lockout superseded), D25, the D30 table, the failure semantics and the remaining risk; `problem-codes.md` (`auth.credentials.invalid`, `auth.throttled`); `public-chat-protection.md` P29; this report's §3.5 table and §8 | docs |

**Unchanged:** the reset-request and reset-attempt limits (they still count every attempt), every endpoint's shape, statuses and codes, and the frontend.

**Superseded:**
- §3.5's "Sign-in per address / per network 10 / 50 per 15 minutes" (struck through in place);
- in §3.6, "rate-limited" in the `POST /api/auth/login` row now means failure-limited;
- in §5, the lockout behaviour that `AuthEndpointsTests` used to prove.

### R1.3 Contract

| Endpoint / surface | Change | Permission | Backward compatible |
| --- | --- | --- | --- |
| `POST /api/auth/login` | 429 `auth.throttled` now follows **failures** only (5 per address on a network, 50 per network, 100 per address everywhere); no account is locked any more | anonymous + antiforgery | Yes. Same statuses, codes and bodies, and fewer refusals than before. The configuration keys are renamed (`AccountProtection:SignInsPer*` → `SignInFailuresPer*`); no deployment had set them |

### R1.4 Verification

| Check | Result |
| --- | --- |
| Backend gates (`gates\backend-gates-rev1.log`): clean restore, NuGet audit on | **PASS**, exit 0 |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: none in all 8 projects |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: 0 warnings, 0 errors |
| Tests, `-m:1`, throwaway PostgreSQL and Redis | **PASS**: Api.IntegrationTests 950 passed, 2 skipped (+7); Application 803; Domain 823; Infrastructure.IntegrationTests 1,106 passed, 1 skipped. **3,682 passed, 3 skipped, 0 failed**; 0 leftover test databases |
| A first full run, on the code before the in-flight reservation | **PASS** (3,681), superseded; kept as `gates\backend-gates-rev1-superseded.log` |
| Frontend `format:check` | **PASS** |
| Frontend `test:ci` | **PASS**: 96 files, 1,166 tests, run alone. A first run overlapped the build, because I started them in parallel; it timed out once in `app-shell.spec.ts` and is kept as `fe-rev1-test-overlapped.log` |
| Frontend `build` | **PASS** (run alone): initial total 313.56 kB raw / 73.66 kB transfer |
| Frontend `i18n:check` | **PASS**: 2,075 messages, 0 errors, 0 warnings |
| Dev API rebuilt (`build`) and recreated (`api`) | **PASS** (`gates\dev-build-rev1.log`) |
| Provider canary | **PASS** before any traffic (`gates\canary-rev1.txt`) |
| Live on the dev API (`live\rev1-live.json`), default limits | **PASS** 5/5. Five wrong passwords → 401 `auth.credentials.invalid`, the sixth → 429 `auth.throttled`. The right password on the same network → 429 with `Retry-After: 900`. The account has no lockout end and a failed count of 0. Ten successful sign-ins in a row → all 200. After 4 failures a success clears the count: 5 more failures are all 401 |
| The other required behaviours (network B unaffected, 60 successes, identical known/unknown answers, cross-network ceiling, no lockout, two hosts, parallel burst) | **PASS** in tests (R1.2) |

### R1.5 Risks

- **Guessing spread over many networks can reach the per-address ceiling.** 100 failures in an hour, from at least twenty networks, refuse the owner's sign-in from every network until that counter expires. It never locks the account, and a password reset still works (§8 and D30).
- **Concurrent sign-ins are bounded by the failure limits.** If more than 50 sign-ins from one network are in flight **at the same instant**, the extra ones are refused with `Retry-After: 1`. Sequential sign-ins, however many, are never refused.
- The other items in §8 stand.

### R1.6 Proposed checkpoint commit message (replaces §9)

```
feat: reset passwords, write to everyone in their language, email invitations

- Password reset: POST /api/auth/password/forgot (202 always, same work for
  every address, padded) and /reset (one refusal for every bad link). A
  dedicated Identity token provider, 30 minutes, single use, both APIs. The
  link is built at send time and carries its secret in the fragment only.
- A reset ends every session at once: the session stamp is checked on every
  request (Redis, then the database) and the account's hub connections close
  on every instance. "Your password was changed" follows.
- users.language (M1), set from sign-up, sign-in and acceptance, changed with
  PUT /api/me/language; every email renders in its reader's language, Arabic
  right to left, names isolated.
- Invitations are emailed in the invitation's language (M2a); the emailed and
  the copied token both work, compared in constant time.
- No hard lockout: sign-in counts failures only, per address on a network,
  per network and per address everywhere, with an atomic in-flight
  reservation; Identity's lockout is off at sign-in, and a success counts
  nothing. Reset limits per address and per network. All in Redis, failing
  closed; stable codes on sign-in and registration refusals; security_events
  (M3) and account_emails (M2).
- Frontend: forgot-password and reset-password pages, the sign-in link, the
  language sent on sign-up and acceptance and saved when it changes.
- Dev stack: Mailpit on loopback in the API's network namespace.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
