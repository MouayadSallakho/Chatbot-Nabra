# Phase 26A — Meta / WhatsApp real-validation readiness

**No Meta account exists, none was created, and nothing in this phase contacted
Meta.** Every WhatsApp exchange described here happened between this machine and
a deterministic stub on `127.0.0.1`.

**No live provider traffic of any kind.** `OMNIAI_LIVE_META` was never set.
`OMNIAI_LIVE_OPENAI` was never set and no OpenAI key was configured. No Stripe
key was configured. Zero requests to `graph.facebook.com`, `api.openai.com` or
`api.stripe.com` — asserted from the browser's own network log, from the API's
854 log lines, and from a delivery row that records the guard's refusal rather
than a connection failure.

**Baseline.** `f46dfbd`, clean tree.

---

## What this phase was, and was not

Phase 20 built the WhatsApp channel and remains authoritative for all of it: the
connection aggregate, the encrypted per-workspace token, webhook verification,
the durable inbox and outbox, the shared runtime, human handling, delivery
statuses and the admin screens. **None of that was rebuilt, redesigned or
duplicated.**

Phase 26A hardened the one boundary a future real validation runs across, and
wrote down what somebody will need to know when they run it. It produced **no
migration, no new package and no new HTTP endpoint**.

---

## 1. The Development live-Meta safeguard

**The problem.** A developer machine that has been pointed at Graph once keeps
that setting in a file or a shell, and anything started from there inherits it.
The OpenAI guard exists because exactly that happened in Phase 18 and spent
money. The same accident here puts words into a real person's WhatsApp, which
cannot be unsent.

**The rule.** In **Development**, when `WhatsApp:GraphBaseAddress` is not on
loopback, the container composes `LiveMetaDisabledMessageSender` unless
`OMNIAI_LIVE_META` is exactly `1`. Outside Development the variable is not
consulted at all.

| Environment | Address | `OMNIAI_LIVE_META` | Composed |
| --- | --- | --- | --- |
| Development | `https://graph.facebook.com/` | absent | **disabled sender** |
| Development | `https://graph.facebook.com/` | `0`, `true`, `yes`, `on`, `1 `, `01`, blank | **disabled sender** |
| Development | `https://graph.facebook.com/` | `1` | Meta transport |
| Development | `https://graph.example.test/` | absent | **disabled sender** |
| Development | `http://127.0.0.1:4244/` | absent | Meta transport (to the stub) |
| Production | `https://graph.facebook.com/` | anything, including absent | Meta transport |

Four decisions worth stating:

1. **Loopback is the test, not the hostname.** Asking "is this Meta?" would leave
   every typo, regional alias and proxy on the permitted side. Asking "is this on
   my machine?" is the safe way round. The cost is one deliberate inconvenience:
   a non-loopback stub needs the opt-in too.
2. **The refusal is structural, not conditional.** `LiveMetaDisabledMessageSender`
   has no `HttpClient`, no address and no use for the credential it is handed.
   There is no code path from it to Meta to get wrong. A flag checked inside the
   real adapter would have left the real transport one mistaken branch away from
   Graph.
3. **Refused, not unknown.** A send that never left the process definitively did
   not arrive. Calling it ambiguous would leave a durable row nobody can ever
   settle — and the one thing a safety guard must not do is make stored state
   less truthful than a real refusal would.
4. **It is not a security control.** It protects developer machines from
   accidents. Webhook signature verification, which *is* a security control, is
   required on every environment and was not touched.

The reason recorded on the delivery names the variable, and carries no
credential, no recipient and no host — it is stored in the database and shown in
the inbox, so it has to survive being screenshotted.

---

## 2. Secrets and configuration

**No fallbacks exist, and now a test says so.** There is no default app secret,
no default verify token, no hard-coded access token and no committed master key.
A deployment missing the webhook pair composes an authenticator that refuses
every request; a deployment with one of the pair is treated as unconfigured
rather than as partly working.

Documented under their **real names**, unchanged — nothing was renamed to make a
table look tidier:

| Purpose | Name | Kind |
| --- | --- | --- |
| Meta app secret (HMAC key) | `OMNIAI_WHATSAPP_APP_SECRET` | secret, environment |
| Webhook verify token | `OMNIAI_WHATSAPP_VERIFY_TOKEN` | secret, environment |
| Credential encryption master key | `OMNIAI_SECRET_MASTER_KEY` | secret, environment |
| Graph base address | `WhatsApp:GraphBaseAddress` | setting |
| Graph API version | `WhatsApp:GraphApiVersion` | setting, default `v26.0` |
| Send timeout | `WhatsApp:Timeout` | setting, default 30s |
| Development live opt-in | `OMNIAI_LIVE_META` | Development only |

Structural tests now assert that no `appsettings*.json` or `launchSettings.json`
in the repository names either webhook secret, that no committed configuration
switches `OMNIAI_LIVE_META` on, that no property of the transport options object
could hold a credential, and that **no TypeScript file under `frontend/src`
mentions any Meta secret name or the Graph host at all**. The Angular bundle
carries no Meta configuration, because there is none to carry: the connection is
configured through the API and the credential is write-only.

---

## 3. Provider URL and HTTP client safety

Reviewed rather than rewritten. Everything below was already true and is now
stated and, where it was not, asserted:

| Requirement | How it holds |
| --- | --- |
| HTTPS required for real mode | validated at startup; only loopback may be plain HTTP |
| Expected Graph host policy explicit | `WhatsAppTransportOptions.MetaGraphHost`, with the loopback rule documented beside it |
| Path constructed server-side | `$"{version}/{phoneNumberId}/messages"`, built in the adapter |
| `PhoneNumberId` cannot inject path or query | the value object accepts **ASCII digits only**, 1–64 of them; anything else is refused in the Domain |
| API version validated | `^v\d+\.\d+$`, at startup |
| Token never in a query string | set as a `Bearer` header on the request, never on the shared client |
| Explicit timeout | 30s, finite, configurable, bounded to two minutes |
| Cancellation honoured | the caller's token propagates and wins over the client timeout |
| No indiscriminate retries | no resilience handler is registered, deliberately; retry is the caller's decision, made against the durable row |
| No Authorization or message-body logging | the adapter writes no log line at all |
| Ambiguous-send semantics preserved | timeouts, dropped connections and 5xx/408 remain *unknown*, never *failed* |
| Provider response validated | only named fields are read; the raw body is never passed through |

---

## 4. The local preflight

`WhatsAppRealValidationPreflight` answers, without contacting anybody, whether a
real validation could begin. It holds no HTTP client, so it could not contact
Meta if it wanted to.

Ten checks, each `Ready`, `Missing` or `Invalid`: webhook authentication,
provider address, provider version, live sending, secret protection, the
connection's existence, its identifiers, its credential, its status, and its
agent.

Two things about it are deliberate:

- **It has no HTTP surface.** It reports on the deployment's secrets by presence
  and on a workspace's credential by recoverability, which is exactly the shape
  of thing that becomes an information leak the moment it is reachable. It is
  composed into the container and exercised by tests; exposing it would need its
  own authorization decision, and there is no product need for one.
- **No detail sentence ever carries a value.** Asserted: the whole report is
  scanned for the access token, the app secret, the verify token, the Graph host
  and any `https://`, and contains none of them.

The check worth having is `connection.credential`, which does not ask whether a
credential is stored but whether it can still be **decrypted**. After a master-key
rotation the connection still says it has one, and without this the first send of
a scheduled validation session is what discovers otherwise.

`Ready` means this deployment has done its part. It never means Meta accepted
anything, and nothing in the product says it does.

---

## 5. Webhook readiness

Re-run against the running API through real signed HTTP, not only in the suite:

| Case | Result |
| --- | --- |
| `GET` with the correct verify token | **200**, challenge echoed |
| `GET` with a wrong verify token | **403**, nothing echoed |
| `POST` correctly signed | **200**, message stored |
| `POST` with one trailing byte added to the signed body | **401** |
| `POST` with a malformed signature | **401** |
| `POST` signed with a different secret | **401** |
| Unknown phone number id | acknowledged, creates nothing *(suite)* |
| Connection not serving | acknowledged, creates nothing *(suite)* |

Nothing was weakened to make any of this easier. The route stays anonymous to the
session system because it is authenticated by signature instead.

---

## 6. Outbound, delivery status and human handoff, end to end

Driven through the real admin in Chrome, against the local stub. The request the
stub recorded is the exact one the production adapter builds:

```
POST /v26.0/100200300400500/messages
Authorization: Bearer <the token this workspace stored>
{"messaging_product":"whatsapp","recipient_type":"individual",
 "to":"447700900123","type":"text",
 "text":{"body":"Yes, we deliver every day until 6pm.","preview_url":false}}
```

Configured version, the connection's own phone number id, `/messages`, the bearer
credential and the documented text envelope — with `preview_url` off, so a reply
never makes Meta fetch whatever a customer linked to.

| Step | Result |
| --- | --- |
| Inbound signed webhook | 200, stored in the durable inbox |
| Worker takes it through the runtime | a WhatsApp conversation appears in the shared inbox |
| Customer reference | masked — `••••0123`, never the full number |
| Take over | conversation switches to human handling |
| Human reply | **exactly one** outbound send, with the decrypted credential |
| `sent` → `delivered` → `read` | status advances in order |
| duplicate `read`, then a late `delivered` | **neither regresses it**; it stays `Read` |
| Any receipt | **no transcript message created** by one |
| Second customer message while in human mode | joins the same conversation; **zero AI calls, zero further sends** |

---

## 7. Chrome regression

Windows Chrome, driven through the `chrome-devtools` CLI against the real admin.
Local stack throughout: PostgreSQL 17 + pgvector and Redis in Docker, the API in
Docker on `127.0.0.1:5027`, the Angular dev server on `:4200`, the Meta stub on
loopback.

| Checked | Result |
| --- | --- |
| WhatsApp Admin screen loads | yes, from an empty state through to a serving connection |
| Create a connection | works; created **switched off**, as designed |
| Edit settings | works; the screen refuses edits while in service |
| Write-only credential | "Stored encrypted and never shown again"; saving confirms it cannot be displayed again |
| **No credential returned** | `GET` connection and list responses carry `hasCredential: true` and **no token, and no field named for one** |
| Inbox WhatsApp channel | conversation, transcript, masked reference, delivery status all render |
| Human handoff | take-over, reply, one send, status to `Read` |
| Console messages | **zero** |
| Network hosts | **`localhost:4200` only**, across the whole session |
| "Connect with Meta" / "Connect with Facebook" buttons | **none**, and none added |
| False "Connected" or "Verified" claim | **none** in the WhatsApp or Channels screens |

### The guard, in the browser (section 27)

A second Development API instance, configured at the **real Graph hostname**
(`https://graph.facebook.com/`, the default), with `OMNIAI_LIVE_META` absent and a
fake credential. For safety the container additionally mapped
`graph.facebook.com` to `127.0.0.1`, so that even a broken guard could not have
reached Meta — and, usefully, so that the two outcomes are distinguishable:

- guard working → a **definitive refusal** carrying its own sentence;
- guard bypassed → a connection failure, recorded as **unknown**.

What the delivery row actually holds:

```
Failed | attempts 1 | provider_message_id (none)
  | "Live WhatsApp sending is switched off on this development deployment,
     so nothing was sent and nothing reached WhatsApp. Set OMNIAI_LIVE_META=1
     to allow it, or point this deployment at a local stub."
```

One attempt, no provider message id, no retry, and the inbox says plainly "This
reply did not reach the customer." The persisted state is truthful.

**Leak scan of the API's 854 log lines**: zero occurrences of the access token,
the app secret, the verify token, the master key, `Bearer`, the customer's number
or the customer's words. Zero occurrences of `graph.facebook.com`,
`api.openai.com` or `api.stripe.com`.

### Responsive and themes

WhatsApp list, WhatsApp connection detail, the conversation list and the
conversation itself, at **390 / 768 / 1440 / 1920**, and in light and dark at
**390 / 768 / 1440**.

**No horizontal page overflow in any combination, and no element extending past
the viewport in any of them.** Dark mode resolves to the design system's own
surface and text tokens on every screen checked. No visual pass beyond this
regression check was attempted, and none was needed — no Phase 26A change touches
a stylesheet.

---

## 8. Quality gates

| Gate | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx` | succeeds |
| `dotnet build OmniAI.slnx -c Release` | succeeds, **0 warnings, 0 errors** |
| `dotnet test OmniAI.slnx -c Release` | **2,995 passed, 0 failed** |
| `dotnet list package --vulnerable --include-transitive` | **no vulnerable packages** in any of the eight projects |
| `npm ci` | succeeds |
| `npm run build` | succeeds; one pre-existing budget warning on `embed-chat.scss` (72 bytes over a 4 kB budget), unchanged by this phase |
| `npm run test:ci` | **672 passed, 0 failed** (50 files) |
| `npm run format:check` | **all matched files use Prettier code style** |
| `npm audit` | **0 vulnerabilities**. `npm audit fix` was not run. |
| lint | **there is no lint script in this project.** `package.json` defines `ng`, `start`, `build`, `watch`, `test`, `test:ci`, `format` and `format:check`, and nothing else. Formatting is checked by Prettier; there is no ESLint configuration in the repository. |

Backend suites: 719 Domain, 677 Application, 803 Infrastructure, 796 API.

**Phase 26A added 42 tests** — 27 in Infrastructure (18 guard, 9
configuration-safety) and 15 in Application (the preflight).

---

## 9. What was deliberately not done

- **No migration.** Phase 26A is configuration, security and readiness. Nothing
  here needed a new column, and inventing one to look ready would have been
  worse than nothing.
- **No new package.** The existing `HttpClient`-based adapter remains; no Meta or
  WhatsApp SDK was added, and no frontend dependency.
- **No Embedded Signup, and no button pretending to be one.** Connections are
  configured manually, and that stays acceptable until Phase 27.
- **No readiness endpoint.** See section 4.
- **No tunnel.** Nothing was installed, configured or opened. The runbook
  describes one; running it needs explicit authorization.
- **No Meta contact of any kind.** No account, no app, no WABA, no message.
- **No AI over WhatsApp.** Phase 26B validates Meta using human handling. A live
  AI test needs separate authorization.

---

## 10. Risks and open points

1. **The stub is not Meta.** It answers what this adapter reads, not everything
   Graph returns. The shapes verified here are the ones the platform acts on; an
   error body Meta produces that nothing in this codebase reads would still be
   handled as "refused, reason unavailable", which is safe but unhelpful.
2. **A non-loopback stub now needs the opt-in.** Deliberate, and stated in the
   runbook, but somebody will meet it and be briefly confused.
3. **The preflight is exercised by tests, not by a screen.** That is the design,
   and it means its first real-world run is in Phase 26B. Its checks are
   individually proven; their ordering and phrasing in an operator's hands is
   not.
4. **Pre-existing, unrelated:** after the API restarts, the browser's session is
   gone and the first sign-in attempt can fail until the page is reloaded,
   because the SPA holds a stale antiforgery token. It predates this phase,
   affects every screen equally, and was met once during this run.

---

## 11. Documents

- Runbook: [`docs/operations/meta-whatsapp-real-validation-runbook.md`](../operations/meta-whatsapp-real-validation-runbook.md)
- Architecture: the safeguard is recorded in
  [`docs/architecture/whatsapp-cloud-api.md`](../architecture/whatsapp-cloud-api.md);
  the architecture index now points at the operations runbook.

## Next recommended step

Reviewer's call on whether to checkpoint Phase 26A. Phase 26B cannot begin until
the authorized business representative has prepared a Meta account from the UAE,
which is intentionally deferred.
