# Meta / WhatsApp real-validation runbook

**Who this is for.** The person who will connect OmniAI to a real WhatsApp
Business Account for the first time, and the authorized business representative
who will create the Meta account it needs. It assumes no Meta account exists
today, because none does.

**Status.** Phase 26A made the platform ready. **No Meta account has been
created, nothing has been sent to Meta, and no real WhatsApp message has been
exchanged.** Every step in [Phase 26B](#phase-26b-the-validation-itself) is
written down and none of it has been run.

---

## 1. Phase 26A and Phase 26B are different things

| | Phase 26A (done) | Phase 26B (not started) |
| --- | --- | --- |
| Meta account | none, and none needed | required |
| Real WhatsApp traffic | none | the point of it |
| What it proves | this deployment is configured, guarded and consistent | that Meta accepts it |
| Where it runs | one developer machine, loopback only | a developer machine plus a temporary public HTTPS endpoint |
| Cost | none | a Meta account, and somebody's afternoon |

Phase 26A deliberately produced **no migration, no new package and no new
endpoint**. If Phase 26B needs code changes, something in this runbook was
wrong — say so rather than working around it.

---

## 2. What OmniAI actually reads

These are the real names in the code, not illustrative ones. Nothing here is
renamed for documentation's sake.

### Platform secrets — environment only

| Purpose | Name | Absent means |
| --- | --- | --- |
| Meta app secret, used as the HMAC key for webhook signatures | `OMNIAI_WHATSAPP_APP_SECRET` | every webhook request is refused |
| Webhook verify token, echoed back to Meta when it subscribes | `OMNIAI_WHATSAPP_VERIFY_TOKEN` | every webhook request is refused |
| Master key for encrypting stored access tokens (base64, 32 bytes) | `OMNIAI_SECRET_MASTER_KEY` | no access token can be stored at all; Development alone falls back to an ephemeral key discarded on restart |

**There are no defaults for any of these.** A deployment missing the first two
composes an authenticator that refuses everything, verified by test. A deployment
that half-configures them — one set, the other blank — is treated as
unconfigured, not as partly working.

### Non-secret settings — configuration section `WhatsApp`

| Setting | Default | Notes |
| --- | --- | --- |
| `WhatsApp:GraphBaseAddress` | `https://graph.facebook.com/` | must be HTTPS unless it is loopback; validated at startup |
| `WhatsApp:GraphApiVersion` | `v26.0` | pinned, never floating; must match `v<major>.<minor>` |
| `WhatsApp:Timeout` | 30 seconds | finite by design; exceeding it is *ambiguous*, never *failed* |
| `WhatsApp:Workers:*` | see `WhatsAppWorkerOptions` | batch size, claim timeout, retry ceiling |

### The Development safeguard

| Name | Value that permits live sending |
| --- | --- |
| `OMNIAI_LIVE_META` | exactly `1` |

**What it does.** In **Development**, when `WhatsApp:GraphBaseAddress` points at
anything that is not loopback, OmniAI composes a sender that refuses every
message *in the process*. No HTTP request is made, no credential reaches a
socket, and the delivery settles as failed with a sentence naming this variable.
Set `OMNIAI_LIVE_META=1` and the real transport is composed instead.

**What it does not do.** Outside Development the variable is not consulted at
all. A production deployment that has deliberately configured the channel cannot
be silenced by a forgotten flag.

**Why it exists.** The equivalent OpenAI guard exists because a key was inherited
from a shell once and spent money. The same accident here puts words into a real
person's WhatsApp, which cannot be unsent.

**It is not a security control.** It protects developer machines from accidents.
Webhook signature verification, which is a security control, is required
everywhere and is unaffected by it.

### Local rehearsal needs no flag

A base address on loopback — `http://127.0.0.1:<port>/` — is never guarded. The
whole of local verification (signed webhooks, the durable inbox, human handoff,
delivery receipts) works on a laptop where nobody has set anything.

---

## 3. The callback URL

There is **one** WhatsApp webhook route, and Phase 26A added no second one:

```
/api/whatsapp/webhook
```

So the callback URL configured in Meta will be:

```
https://<PUBLIC_HOST>/api/whatsapp/webhook
```

| Meta does | OmniAI answers |
| --- | --- |
| `GET` with `hub.mode`, `hub.verify_token`, `hub.challenge` | echoes the challenge **only** when the token matches exactly; refuses otherwise and echoes nothing |
| `POST` with `X-Hub-Signature-256: sha256=<hex>` | verifies HMAC-SHA256 over the **exact raw body** with the app secret, in constant time; writes to the durable inbox and returns |

Requirements Meta imposes:

- the callback must be reachable over **public HTTPS** with a valid certificate;
- verification is a `GET` and must succeed before events flow;
- the app must be **subscribed to the WABA** before any message is delivered.

Requirements OmniAI imposes and will not relax:

- a payload whose signature does not verify **byte for byte** is refused —
  changing one character of the body invalidates it;
- an unknown `phone_number_id` creates nothing;
- a connection that is not serving creates nothing;
- the route is deliberately anonymous to the session system because it is
  authenticated by signature instead. Do not "fix" that.

---

## 4. Temporary HTTPS tunnel

Phase 26A **did not install, configure or run a tunnel**, and none should be
opened without explicit authorization.

```
Meta  ──HTTPS──▶  temporary public endpoint  ──▶  local OmniAI API  ──▶  /api/whatsapp/webhook
```

Rules for when one is opened:

- open it **only** for the duration of a controlled validation session, and close
  it immediately afterwards;
- expose **only** the API port, and nothing else on the machine;
- never put a secret in a URL, a query string or a tunnel name — the verify token
  goes in Meta's dashboard field, the app secret in this machine's environment;
- treat the URL as public the moment it exists: anybody who learns it can send
  unsigned requests, which is exactly why signature verification is not relaxed
  for it;
- a tunnel is **not a deployment**. Nothing validated through one is thereby
  released, and no VPS is involved in Phase 26B.

---

## 5. What the business representative must prepare

This is the Meta-side checklist. **None of it exists yet, and nothing here should
be invented or guessed.** Every value is read from Meta's own screens when the
time comes.

- [ ] Meta Developer access for the authorized representative
- [ ] a Business Portfolio, where Meta requires one for the product
- [ ] a Meta App
- [ ] the **WhatsApp** product added to that app
- [ ] a WhatsApp Business Account (WABA)
- [ ] a sender — Meta's provided test number is enough for the first validation
- [ ] the **WABA ID** (Meta calls it the WhatsApp Business Account ID)
- [ ] the **Phone Number ID** — *not* the phone number itself; OmniAI routes on
      this and on nothing else
- [ ] a **temporary/test access token** to begin with, not a permanent one
- [ ] the **App Secret** from the app's settings
- [ ] the webhook callback URL and verify token entered in the dashboard
- [ ] the app **subscribed** to the WABA

Notes:

- **A production customer number is not needed for the first validation.** Meta's
  test sender is the right instrument: it costs nothing, it is revocable, and a
  mistake with it reaches nobody's customers.
- **Do not claim "Connected to Meta" anywhere in the product** until a real
  provider exchange has actually happened. A connection configured locally is a
  configured connection; it is not a verified one, and Phase 26A added no
  "Verified" state to suggest otherwise.

---

## 6. Handling the real credentials

When they exist, the access token, app secret and verify token are supplied
**through environment or secret configuration on the machine that runs the API**,
and nowhere else. The access token additionally goes into the WhatsApp Admin
screen, which stores it encrypted and never returns it.

They must **never** be:

- pasted into Claude, ChatGPT or any other assistant;
- committed to Git, in any file, in any branch;
- put into `appsettings*.json`, `launchSettings.json` or any Angular environment
  file — the frontend has no Meta configuration at all, and a test asserts it;
- placed in a URL, a query string or a tunnel name;
- captured in a screenshot, a Markdown report or a phase document;
- logged. The outbound adapter deliberately writes no log line, because every
  value in its scope is a credential, a phone number or a customer's words.

**Do not create a `.env` file containing secrets in this repository.** Set them in
the shell or the deployment's secret store for the session that needs them.

---

## 7. Local preflight

`WhatsAppRealValidationPreflight` answers, **without contacting Meta**, whether a
validation could begin. It contacts nobody and holds no HTTP client. It has **no
HTTP endpoint on purpose** — it reports on the deployment's secrets and on a
workspace's credential, which is exactly the shape of thing that becomes an
information leak the moment it is reachable.

It returns one line per check, each `Ready`, `Missing` or `Invalid`, and never a
value:

| Check | Ready means |
| --- | --- |
| `webhook.authentication` | app secret and verify token are configured |
| `provider.address` | the address is HTTPS, or loopback where there is no wire |
| `provider.version` | the pinned API version is present and well formed |
| `provider.live-sending` | sending is permitted — or the deployment is aimed at a local stub, which is stated plainly |
| `secrets.protection` | a value can be protected and recovered on this machine |
| `connection.exists` | the connection is in this workspace |
| `connection.identifiers` | WABA id and Phone Number ID are both present |
| `connection.credential` | an access token is stored **and can still be decrypted** |
| `connection.active` | the connection is serving |
| `connection.agent` | the agent it answers with exists, in the same workspace |

The credential check is the one worth having: after a master-key rotation the
connection still says it has a token, and without this the first send of the
validation session is what discovers otherwise.

`Ready` means **this deployment has done its part**. It never means Meta has
accepted anything.

---

## 8. Phase 26B: the validation itself

Not yet authorized. Written here so it can be executed without improvisation.

1. Authorized Meta account ready (section 5).
2. Meta App with the WhatsApp product configured.
3. Test sender / number available.
4. **WABA ID** obtained from Meta.
5. **Phone Number ID** obtained from Meta.
6. Temporary access token loaded securely (section 6).
7. App Secret loaded into `OMNIAI_WHATSAPP_APP_SECRET`.
8. Verify token loaded into `OMNIAI_WHATSAPP_VERIFY_TOKEN`.
9. Local stack running: PostgreSQL 17 + pgvector, Redis, the API, the Angular
   admin. `WhatsApp:GraphBaseAddress` left at its default, and
   `OMNIAI_LIVE_META=1` set **for this session only**.
10. Temporary HTTPS tunnel opened to the API (section 4).
11. Callback configured in Meta: `https://<PUBLIC_HOST>/api/whatsapp/webhook`
    plus the verify token.
12. **Webhook verification succeeds** — Meta's `GET` is answered with the
    challenge.
13. App subscribed to the WABA.
14. Run the preflight (section 7). Every item `Ready` before going further.
15. Send a **real inbound WhatsApp message** from a phone to the test sender.
16. Confirm it appears in the OmniAI Inbox as a WhatsApp conversation, with the
    customer reference masked.
17. **Take over** the conversation — human handling, so no AI call is made.
    Live AI over WhatsApp needs its own separate authorization.
18. Send a **real outbound reply** from the Inbox. Expect exactly one delivery.
19. Watch `Sent` → `Delivered` → `Read` arrive as receipts. Confirm no transcript
    message was created from any of them.
20. Inspect the API logs and the browser network panel for leaked secrets. Expect
    none: no Authorization header, no token, no customer message body.
21. **Revoke or rotate** the temporary access token; remove it from the machine's
    environment and from the workspace if the connection is not being kept.
22. **Close the tunnel.** Unset `OMNIAI_LIVE_META`.

### Cleanup checklist after any real test

- [ ] tunnel closed and its URL considered burned
- [ ] `OMNIAI_LIVE_META` unset
- [ ] temporary access token revoked in Meta
- [ ] the workspace's stored credential replaced or the connection disabled
- [ ] app secret and verify token left only where they belong, and rotated if
      they were ever exposed
- [ ] webhook callback removed from the Meta app if it pointed at a tunnel
- [ ] no screenshot, log or note containing a credential kept anywhere

---

## 9. What is deliberately not here

- **Meta Embedded Signup.** Connections are configured manually today. Embedded
  Signup is Phase 27's problem, and a "Connect with Meta" button that does
  nothing would be worse than none.
- **Any real credential, identifier or phone number.** Every value in this
  document is a placeholder or a configuration name.
- **A readiness API.** See section 7.
