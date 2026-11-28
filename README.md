# Nabra · نبرة

Nabra gives each workspace a shared customer inbox. A website visitor can ask an AI agent a question grounded in the workspace's knowledge documents, then continue the conversation with a team member. The Angular admin and visitor experiences support English and Arabic, including right-to-left layouts.

## Channel readiness

| Channel | Available in this version | Next validation |
| --- | --- | --- |
| Website chat | Hosted page, embeddable widget, local AI replies, human takeover and realtime inbox | Run the browser journeys against the local development stack |
| WhatsApp | Local text adapter, signed webhooks, encrypted credentials, inbound deduplication, durable outbound delivery and receipts | Exchange messages with a Meta test number when the owner is ready |
| Messenger | Local Page events, AI and staff replies, delivery status in the inbox and recovery of simulated sends | Validate the Page connection and transport with a Meta test Page when the owner is ready |
| Instagram Direct | Local text DMs, replay protection, AI replies and staff takeover in a separate Instagram conversation | Verify the adapter, ownership events and delivery on a real professional account during deferred onboarding |

Production approval for the Meta channels is pending. The WhatsApp adapter currently defaults to Graph API v26.0. Keep that tested default for the local baseline and validate the version supported by the new Meta app before enabling live traffic.

In development, run the API with `OMNIAI_DEV_AI_STANDIN=1`, apply the database migrations and open **Channels → Local testing → Messenger** in the Angular development server. The tester needs workspace management, agent viewing and inbox viewing permissions. Each active agent has a simulated Page and each tester has a customer thread per Page. The simulator collects the latest 40 messages, including replies after a team member takes over. Use a test workspace because these conversations appear in its inbox and activity figures. The simulator posts Messenger-shaped Page events to its authenticated local `page-events` endpoint. It accepts customer text and attachments for the selected Page and tester, preserves opaque message IDs for replay, and skips echoes and read notifications. Public Meta callbacks and sending through Facebook are pending. [Meta Messenger Page event format](https://developers.facebook.com/docs/messenger-platform/getting-started/quick-start/)

Under **Delivery test for new replies**, choose confirmation, refusal or a lost confirmation. The simulator exercises the Page text request against an in-process HTTP receiver. On refusal, **Retry delivery** sends that reply once after the simulated refusal clears. On lost confirmation, **Check receipt** looks for the receiver’s recorded acceptance. An uncertain attempt stays held across restarts. The staff transcript shows the latest 40 outbound delivery states. The local receipt lookup is a simulator capability. [Meta Page messages reference](https://developers.facebook.com/docs/graph-api/reference/page/messages/)

**Channels → Local testing → Instagram** opens a separate professional-account simulator under the same development switch. Send a text DM and open the conversation in the staff inbox to take over. Each tester and account has its own Instagram conversation, independent of Messenger. The local mailbox shows the latest 40 public messages. Its authenticated `direct-events` endpoint accepts local text and attachment fixtures and ignores echoes, deleted messages, and read notifications. Choose a delivery scenario to refuse a reply or lose its confirmation after acceptance. Retry a refusal or check the simulated receipt without sending a second copy. Transfer a conversation to another app to keep collecting customer messages while both AI and staff replies pause. Returning control preserves the previous AI or staff handling mode and does not replay paused turns. These actions simulate local ownership and delivery. Live callbacks, provider ownership events and verification with a professional account remain pending. [Instagram webhook reference](https://developers.facebook.com/docs/graph-api/webhooks/reference/instagram/) · [Meta Instagram Send API](https://developers.facebook.com/documentation/instagram-platform/instagram-api-with-instagram-login/messaging-api)

Use **Give control to another app**, then **Repeat takeover notification** to replay the same simulated Instagram notification. Return control to Nabra and replay the takeover again to test late delivery. The authenticated local `handover-events` route accepts one Instagram `messaging_handover` change with a recipient, sender, event timestamp and a pass or take identifying the new app. PostgreSQL retains the latest event time per conversation. Equal-time conflicting owners pause replies until a newer event settles ownership. [Meta Instagram webhook fields](https://developers.facebook.com/docs/graph-api/webhooks/reference/instagram/)

In the Messenger and Instagram simulators, **Test attachment** accepts a sample parcel image or a local PNG or JPEG up to 512 KiB and 2048 pixels per side. The simulator stores a JPEG thumbnail up to 320 pixels wide or tall with the message. Customer and staff see the same preview or a localized reason it is unavailable. Captions follow the usual reply rules. A media-only turn is recorded without calling the text assistant. Replaying a message ID preserves the original attachments. External media retrieval is deferred to provider integration.

In **Workspace settings → Automatic conversation assignment**, managers with conversation assignment permission can select a workspace role for each channel and language. On the first customer message, Nabra uses Arabic script or common English words as a local language hint. A matching rule assigns an eligible role member with the fewest open conversations across the workspace. Members must be able to view and reply. The assignee can find the conversation in **Mine**. Missing rules, unclear language, media without a caption and teams without eligible members leave the conversation in the general inbox under **Unassigned**. Changing a rule affects new conversations. Assignment and taking over replies are independent actions.

The **Business hours and after-hours assignment** panel optionally sets one shift per weekday in the workspace time zone. A day can also be closed or open all day. A shift includes its opening minute, excludes its closing minute, and continues overnight if its end is earlier than its start. Nabra evaluates the first customer message timestamp, including on daylight-saving transitions. Inside the schedule it applies the channel and language rules. Outside the schedule, the selected on-call role handles new conversations across channels and languages, including media-only messages. Selecting the general inbox, removing the role, or having no eligible member leaves the conversation unassigned. The schedule starts disabled and changes apply to new conversations.

In **Inbox → First reply overdue**, the team sees open, unassigned conversations that have reached the workspace's first reply target, longest wait first. Each row shows elapsed minutes from the first customer message. A greeting before that message does not count as a reply. A later customer message does not restart the clock. A recorded AI or team reply, assignment, or closure removes the conversation from this queue. The elapsed clock also runs outside business hours. Workspace routing settings default to 15 minutes and accept 1–1,440 minutes. The open queue refreshes on inbox changes and checks for newly overdue conversations every 30 seconds.

## Repository

- `backend/` contains the .NET 10 domain, application use cases, infrastructure and HTTP API. PostgreSQL with pgvector stores tenant data and knowledge embeddings. Redis supports realtime and protection services.
- `frontend/` contains the Angular admin, website chat and Playwright journeys.
- `deploy/` contains the production edge, API and database migration configuration.
- [Architecture index](docs/architecture/README.md) and [Phase 39 handoff](docs/phase-reports/PHASE_39_SUMMARY.md) describe the inbox, agents, knowledge, channels, customers, team permissions, billing, analytics and reports.

## Verify a checkout

The backend owns orchestration, scheduling and integrations. Provider SDKs live in Infrastructure behind Application contracts. Domain models use tenant-scoped identities, and TLS terminates at the edge.

Run from the repository root using PowerShell 7 and Docker with Linux containers.

```powershell
pwsh -File tools/quality/test-backend.ps1
```

The runner restores packages with transitive NuGet auditing, builds Release and runs all four test projects. It creates private PostgreSQL and Redis services, then removes their containers, network and volumes. Coverage XML stays under `.quality/` locally and is available as the `backend-coverage` Actions artifact for seven days. The 75% gate measures unique executable lines across the four product assemblies, excluding generated migration designers and model snapshots. Use a separate checkout for concurrent runs because .NET writes build outputs under `backend/`.

Linux is the reference environment for integration tests. Direct Windows runs require separate disposable services and can differ in time-zone resolution and PDF text extraction. The three opt-in load and crash-boundary tests have separate performance procedures.

Use Node 24.19 and npm 11.17 for the frontend.

```sh
cd frontend
npm ci
npm audit
npm run format:check
npm run i18n:check
npm run test:ci
npm run build
```

For interactive development, start the development API on port 5027 and run `npm start` in `frontend/`. The UI is served on port 4200. For browser journeys, use a disposable Development workspace, the local AI stand-in, the loopback Mailpit sink and installed Chrome, then run `npm run e2e`. These journeys create synthetic users and conversations. See the [development stack baseline](docs/phase-reports/PHASE_36_1_BASELINE_REPORT.md) and [operations runbook](docs/operations/operations-runbook.md).

GitHub Actions runs backend, frontend and Git history checks on pull requests and pushes to main. It also rebuilds the API, edge and migrator images and fails on fixable High or Critical vulnerabilities. The `main` branch requires all six checks. See [deployment](docs/operations/production-deployment.md), [backup and restore](docs/operations/backup-restore.md) and the [WhatsApp validation runbook](docs/operations/meta-whatsapp-real-validation-runbook.md) for the corresponding operational procedures.
