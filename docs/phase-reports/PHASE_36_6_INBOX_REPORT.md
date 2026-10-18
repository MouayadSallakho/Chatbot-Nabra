# Phase 36.6: The Inbox (conversations and customers)

Evidence folder: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-06\` (below: `<evidence>`).
Nothing was committed, pushed, tagged or deployed. No backend, migration, permission, endpoint,
`deploy/` or npm change.

## 1. Summary

- On a screen 1280 px or wider, the conversation list stays beside the open conversation, with a collapsible details panel: work a queue without losing your place.
- Who is answering is the heart of every row and header: a voice chip on each conversation, and Take over / Return to AI as a 320 ms Lagoon ↔ Ember sweep, announced and instant under reduced motion.
- The transcript reads like a conversation: day separators, bubbles per voice, handoff dividers from real author changes, an honest note when the AI has not replied, and a "N new messages" button instead of a jump.
- The composer is keyboard-first (Ctrl/Cmd+Enter sends, Enter still adds a line), keeps a draft per conversation, and retries with the same key, so a reply is never stored twice.
- Conversations and Customers now speak Arabic: every string is translated, Arabic names and masked numbers render correctly, and unnamed customers get Arabic labels.

## 2. Starting point

- Branch `master`, HEAD `dfa55e6` ("feat: give the first screens Nabra's voice, in English and
  Arabic"): the 36.5 checkpoint, with the brand step `6331671` before it. Worktree clean
  (`git status --short` empty). Matches "Phase 36.5 accepted".
- Baseline build: PASS with one existing warning (`embed-chat.scss` 4.07 kB). Startup JavaScript
  132,581 B gzip (`<evidence>\startup-js-baseline.json`). Provider canary PASS
  (`<evidence>\canary-start.log`).

### What the API returns (written before designing; full inventory `<evidence>\api-inventory.md`)

| Read | Returns | Not there |
|---|---|---|
| `GET …/conversations?page&pageSize` (`conversations.view`) | `{items, page, pageSize, total}`; max 50; most recent activity first. Row: id, channel, status, handling (`Ai`/`Human`), startedAt, lastActivityAt, agentName, messageCount, lastMessagePreview, lastMessageSender, assignedTo `{userId,email}` | customer id/name, unread, attention or SLA flags, search/filter/sort |
| `GET …/conversations/{id}` | the same fields minus the preview, plus a strong `ETag` | customer id/name |
| `GET …/{id}/messages?limit&after` | `{items, nextCursor}`, **oldest first**, `after` pages **forward**, max 200. Message: id, sender, text, sentAt, sequence, sentBy | failed AI turns (a failed turn writes nothing), handoff/assignment events, read state, a newest-first cursor |
| Writes | take-over / return-to-ai / assignment with `If-Match` (428/412); reply idempotent on `clientMessageId`, 409 while the AI is handling | — |
| Realtime `/hubs/workspace-inbox` | one event, `conversationChanged(id)`; `null` after a reconnect | message payloads, customer events |
| `GET …/customers` (`customers.view`) | `{items, page, pageSize, totalCount}`; max 100; row: id, displayName, label (server English), channels, conversationCount, lastActivityAt, createdAt | search/filter/sort |
| `GET …/customers/{id}` | identities (masked), conversations metadata (only with `conversations.view`), canReadConversations, merge pointer, `ETag` | — |

## 3. Changes by area

`git status --short` at the end (27 modified, 14 new paths):

```
 M docs/design/MASTER.md
 M frontend/src/app/core/media/media.ts
 M frontend/src/app/core/navigation/route-transition.spec.ts
 M frontend/src/app/core/navigation/route-transition.ts
 M frontend/src/app/core/realtime/conversation-realtime.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/conversations/conversations.routes.ts
 M frontend/src/app/features/customers/customer-detail/customer-detail.html|.scss|.ts
 M frontend/src/app/features/customers/customer-list/customer-list.html|.scss|.ts
 M frontend/src/app/features/customers/customers.routes.ts
 M frontend/src/app/shared/page-header/page-header.scss|.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_base.scss
 M frontend/src/styles/_primitives.scss
?? frontend/src/app/core/realtime/conversation-realtime.spec.ts
?? frontend/src/app/features/conversations/conversation-composer/   (component + spec)
?? frontend/src/app/features/conversations/conversation-context/    (details panel)
?? frontend/src/app/features/conversations/conversation-inbox/      (the workspace)
?? frontend/src/app/features/conversations/conversation-transcript/ (component + spec)
?? frontend/src/app/features/conversations/conversation-words.ts
?? frontend/src/app/features/conversations/inbox-placeholder/
?? frontend/src/app/features/conversations/inbox-state.ts
?? frontend/src/app/features/conversations/transcript-rows.ts (+ spec)
?? frontend/src/app/features/customers/customer-words.ts (+ spec)
?? frontend/src/app/shared/channel/channel-badge.ts
```

Before screenshots: `<evidence>\before\` (`{en,ar}-light-{1440,390}-{conversations,conversation-detail,customers,customer-detail}.png`).
After screenshots: `<evidence>\matrix\` (`{en,ar}-{light,dark}-{1920,1440,1280,1024,768,390}-{inbox,long,human,awaiting,customers,customer}.png`, 144 files).

### Layout (the workspace)

- `/conversations` is now a parent route (`ConversationInbox`) with the list and a child outlet;
  `''` shows a placeholder pane and `:conversationId` the conversation. URLs, titles, `?page=` and
  Back are unchanged. Before: `before\en-light-1440-conversation-detail.png`; after:
  `matrix\en-light-1440-human.png`, `matrix\en-dark-1920-long.png`.
- 1280 px and wider: list (20–25 rem) | transcript | details (17–21 rem, toggled by "Details",
  remembered per browser in `localStorage`). Each pane scrolls itself; the shell's content area
  stops scrolling via a new generic `.page-fills-content` primitive.
- Below 1280 px: one pane at a time. The list stays alive while hidden, so Back finds the same page
  and state. On a screen 600 px or taller the conversation fills it (header, scrolling transcript,
  composer); shorter screens or zoom scroll as a page (`MediaQueries.tall`). Phones get a compact
  header (icon-only Refresh and Details, chip-only handling, via the new `.visually-hidden-narrow`).
  After: `matrix\en-light-390-human.png`, `matrix\ar-dark-390-long.png`, `matrix\en-light-1024-human.png`.
- One `h1`: the list's title when nothing is open, the conversation's otherwise (`PageHeader` gained
  `level` and `headingDir`). Opening a row moves focus to the conversation's heading.
- Switching conversations beside the list skips the route crossfade (`data.panes`,
  `route-transition.ts`); the conversation component reloads on every id change.

### Rows

Voice chip, channel glyph and word, time (today: time; earlier: date), "Customer / AI / Team"
before the last message (`dir="auto"`), "Assigned to <email>" (LTR island), and "Waiting for the
team" only where a person is answering and the customer wrote last. The selected row is neutral
with the ink edge. Arrow keys, Home and End move between rows, in addition to Tab. No customer
name, unread count or filter: the API has none (Backend asks). After: `matrix\en-light-1440-inbox.png`.

### Transcript

Bubbles per voice (customer neutral at the start, AI Lagoon and person Ember at the end), speaker
and time on each, text `dir="auto"`, colleague emails as LTR islands, WhatsApp delivery in words.
Day separators are `h3` headings ("Today", "Yesterday", the date). Handoff dividers come only from
author changes. At the end, when the AI is handling and the customer spoke last: "The AI has not
replied to the last message. When it cannot answer, nothing is sent to the customer and nothing
appears here." It never claims a failure the API cannot report. The newest 100 messages are drawn,
50 more per "Show earlier messages", with the place kept; a 56 rem reading column. The voice track
sits in the header. After: `matrix\en-light-1280-awaiting.png`, `matrix\en-dark-1920-long.png`.

**Fixed on the way:** the transcript pages forward from the oldest message, so the old "Load older
messages" button actually loaded newer ones, and a live refresh re-read only the first page:
messages after it never appeared live. The screen now reads to the end on open, and re-reads from a
20-message overlap before the end.

### Handling controls (the handoff moment)

The header shows the voice chip, a sentence ("The AI is answering" / "A person is answering") and
one action: Take over (primary, ink) or Return to AI (secondary). On a change (your own, or a
colleague's through realtime) the chip wipes from the old voice's tint to the new one from the
reading start (clip-path, mirrored for RTL), in 320 ms on the emphasized curve, announced politely.
Permissions, the absence of confirmations, `If-Match`, no retry on 412, and the focus moves (Take
over → reply box, Return → Take over) are as before. A 412 now shows the shared stale-version
notice with "Show the latest version". Assignment lives in the details panel; focus returns to the
select after it changes.

### Composer

Its own component. Enter adds a line (unchanged); Ctrl/Cmd+Enter sends (new, shown as "Ctrl +
Enter to send"; hidden on touch). Read-only and busy while sending; failures keep the text and offer
"Try again" only where a retry can work (network, timeout, 429, 5xx). The 409 "AI is answering"
case explains itself. **The idempotency key now belongs to the draft text**: a retry after a lost
response reuses it, so the server stores one reply (before, every press minted a new key).
Character count from 1,000 left; 32,000 limit as before. Drafts are kept per conversation in memory
(`InboxState`), with a `beforeunload` prompt while any draft exists.

### Realtime

Rows and the transcript re-read in place (tracked by id, no skeletons). New bubbles settle in over
220 ms; a reader at the end follows, a reader scrolled up gets "N new messages". **Fixed:**
`ConversationRealtime.listen()` claimed the workspace only after an `await`, so the list and the
conversation subscribing in the same tick opened two sockets, one of which leaked after both
unsubscribed (new spec proves it against the old code). Hub negotiations per inbox load: 3 before,
2 after (conversations + the notification centre's own connection).

### Customers

Directory: a letter tile for named customers and a dashed person tile for unnamed ones; names
`dir="auto"`; channels as glyph + word; counts in words with all six Arabic plurals. Unnamed labels
are localised on the client by the server's own rule. Profile: name as `h1` (`dir="auto"`), a facts
strip, then "How they reach you" (masked references as LTR islands) and Conversations (voice chip,
channel, status, time) beside the Name and Combine cards. The merge summary and candidate facts are
single translatable sentences with LTR references (the old markup lost the spaces: "ref.aaaaaa").
The name field now has the unsaved-changes guard the other editors use. Before:
`before\ar-light-1440-customers.png`; after: `matrix\ar-light-1440-customers.png`,
`matrix\ar-light-390-customer.png`.

### Shared and global

`ChannelBadge` (glyph + localised word), `MediaQueries.inbox`/`tall`, `.page-fills-content`,
`.visually-hidden-narrow`, skeleton bar/bubble shapes (moved from the component to stay under its
4 kB budget), `PageHeader` level/dir. 162 new messages with Arabic (651 in total); MASTER.md: nine
decision rows.

## 4. Decisions

- **Panes from 1280 px by viewport, as specified**; details collapsible and remembered locally only (D22).
- **Rows lead with the last message**, not a customer name (not in the API). Rejected: a name guessed from the transcript, or a channel label that could be wrong for a named customer.
- **"Waiting for the team" is the only attention marker**, derived from handling + last sender. Rejected: an "unread" dot (no data) or a client-side "Needs a person" tab (D22).
- **Failed AI turns:** stated as "no reply exists" plus the documented runtime fact. Rejected: inferring failures from gaps.
- **Read the transcript to the end** (200-message pages, up to 50 pages, then "Load newer messages"). Rejected: showing the oldest 100 first (the old behaviour) with the newest hidden.
- **Render window 100 / step 50.** `content-visibility: auto` was tried and **rejected**: it cut the opening layout (216 → 73 ms at 4×) but slowed every scrolled frame (median 17 → 67 ms).
- **Ctrl/Cmd+Enter added, Enter unchanged.** Proposal for you: nothing further. Enter-to-send is not proposed, for the reason the old comment gives (a stray keystroke must not post).
- **No confirmation dialogs added** to Take over or Return (there were none; kept "exactly as today").
- **The track and the sweep both live in the header** (MASTER 3 and 5); the sweep leads, the 4 px track stays quiet.
- **Unsaved-changes guard on the customer name**: added, consistent with the other editors.
- Deferred: a customer link from a conversation, filters/tabs, unread state (Backend asks).

## 5. Verification

| Check | Result |
|---|---|
| `npm run format:check` | **PASS**: "All matched files use Prettier code style!" (`<evidence>\final-format.log`) |
| `npm run test:ci` | **PASS**: 83 files, 961 tests, 0 failures (`<evidence>\final-test.log`; was 78 / 925 at 36.5). New specs: transcript rows, transcript, composer, realtime, customer words, list (4 new tests), route transition (2). Specs changed only where the DOM or behaviour changed on purpose: the list row selector; the detail's 200-message page and read-to-end (replaces "Load older messages"). Mid-phase run: 98 failures before these fixes (`test-1.log`), most of them a TestBed cascade from two root causes. |
| `npm run build` | **PASS**: 1 warning, the existing `embed-chat.scss` 4.07 kB (also in the baseline); no new budget warning; initial 290.12 kB raw / 70.30 kB transfer (< 500 kB) (`<evidence>\final-build.log`) |
| Startup JavaScript | 132,581 → 135,674 B gzip (**+2.3%**, under the 10% gate); raw 406,296 → 411,149; Brotli 117,283 → 120,053 (`startup-js-baseline.json`, `startup-js-final.json`) |
| `npm run i18n:check` | **PASS**: 651 messages, 0 errors, 0 warnings |
| Chrome matrix | **PASS**: 6 routes (inbox, 232-message conversation, person-handled, AI-awaiting, directory, Arabic-named profile) × 1920/1440/1280/1024/768/390 × light/dark × en/ar = 144 captures: page overflow 0, console errors 0, exactly one `h1` (in the DOM and visible) on every capture, unnamed controls 0 (`<evidence>\matrix\matrix.json`) |
| Contrast | **PASS**: 46 new pairs × light, dark, light+more, dark+more = 184 checks, 0 failures; tightest is a UI part at 3.08:1 (`<evidence>\contrast\*.md`, `design-system-tokens` `contrast_check.py` on the built CSS) |
| Keyboard | **PASS**: list → open (Enter) → Take over → reply (Enter adds a line, Ctrl+Enter sends) → Return to AI, real key events, focus visible at every step (`<evidence>\interactive-checks.md`) |
| Reduced motion | **PASS**: tokens 0.01 ms; sweep and settle 1e-05 s; state still changes |
| RTL mirroring | **PASS**: Arabic captures at every width; `rtl_logical_lint.py` 0 findings in 36 changed files; sweep mirrored |
| Realtime | **PASS**: no jump when scrolled up; "1 new message"; follows at the end; 220 ms settle (`interactive-checks.md`) |
| Permissions (analyst) | **PASS**: refusals on all four routes, en/1440 and ar/390, no data rows (`scratch\peek\analyst-*.png`) |
| Performance (232 messages, prod build) | **PASS for smoothness**: 1× scroll p50/p95/max 17 ms, "Show earlier" 33 ms; 4× scroll p95 17 ms; CLS 0.00. LCP at 4× 3.5–4.3 s, dominated by the boot chain; no baseline LCP was taken, so no claim about LCP regression (`<evidence>\performance.md`, `trace\`) |
| Lints | `erp_screen_lint.py` 0 findings; `motion_audit.py` 1 HIGH (heuristic false positive: reduced motion is global in `_tokens.scss`, verified in Chrome) |
| Provider canary | **PASS** at start and end (all names absent) (`canary-start.log`, `canary-end.log`) |
| AI (Lagoon) bubbles and handoff dividers with live data | **NOT RUN** in Chrome: the stack has no AI provider, so no AI reply exists; covered by unit tests only |
| WhatsApp masked number and delivery badges in Chrome | **NOT RUN**: no WhatsApp conversation exists (the connection was never activated) |
| Forced colours, 200% zoom / 320 px reflow, a real screen reader | **NOT RUN**: not exercised this phase (the 390 px and short-screen layouts are the closest proxies) |

Skills used: `frontend-pro-max:enterprise-erp-ux`, `ui-ux-design-lead`, `motion-interaction`, `angular-enterprise`, `api-contracts-integration`, `frontend-testing-quality`, `web-performance` (plus `design-system-tokens` scripts). None missing.

## 6. See it live

- Inbox: **http://localhost:4200/conversations** (dev server running; API on 127.0.0.1:5027)
- 232-message conversation: http://localhost:4200/conversations/01a15013-917d-75a7-870c-98ceb0b9e7f8
- AI awaiting a reply: http://localhost:4200/conversations/01a14eb9-a9d9-7b7a-ac76-b5d7067a3515
- Customers: http://localhost:4200/customers; Arabic-named profile: http://localhost:4200/customers/01a14d54-2e28-7c49-912f-1767774bfc6a
- Accounts and data: `<evidence>\demo-accounts-p36-06.md` (owner, support and analyst; sign-in details are in `<evidence>\..\p36-01\demo-accounts.md`, outside the repository)

## 7. Backend asks

1. **Customer on the conversation**: customer id and label on the list row and detail, so rows can lead with the name and a conversation can link to its profile.
2. **Unread / attention**: per-member read state, and server-side filters or tabs ("Needs a person" = handling Human and customer last, "Assigned to me", by handling, by channel), plus search.
3. **Turn outcomes in the transcript**: a failed AI turn is invisible to the admin today.
4. **Handoff and assignment events in the transcript** (who took over, when), so dividers can be drawn for a takeover with no reply yet.
5. **A newest-first transcript read** (a `before` cursor or a `tail` parameter), so a long conversation isn't read from its first message.
6. **A label code for unnamed customers** (or localised labels), so the client doesn't duplicate `CustomerLabels.For`.
7. **Lighter realtime for the list**: a per-conversation group or an event kind, so the list doesn't re-read a whole page on every message (existing known limitation).
8. Still open from 36.1: the NuGet audit failure on a clean restore.

## 8. Risks and open issues

- **Arabic copy** (162 new messages) follows the glossary but needs the native review the glossary asks for before shipping.
- **Very long conversations** cost one request per 200 messages on open (capped at 50, then "Load newer messages") until ask 5 exists.
- **The client applies the server's naming rule** for unnamed customers; a server change must be mirrored (a spec pins it).
- **At 1280 px with the sidebar pinned**, the transcript column is about 450 px. It works, but it's tight; the matrix ran with the rail.
- **Startup chunking changed**: the bundler now emits a small `main` plus a shared chunk (both initial); +2.3% gzip in total.
- **"Ctrl + Enter" is printed on macOS too** (Cmd+Enter also works).
- **Drafts live in memory** (lost on reload, which asks first); deliberately never on disk.
- **The local database changed** (synthetic only): one 232-message conversation; the Canada conversation answered once and returned to the AI; the long one returned and taken over again.
- `motion_audit.py` HIGH is a known false positive. Lagoon bubbles, handoff dividers and WhatsApp badges were not seen with live data.

## 9. Proposed checkpoint commit message (not committed)

```
feat: make the inbox a workspace where the handoff is the heart

Conversations become a three-pane inbox from 1280px (list, transcript,
details) and two pages below it, with the same URLs, ?page= and Back.
Every row says who is answering; Take over and Return to AI sweep the
voice chip between Lagoon and Ember. The transcript gets day separators,
author-change handoff dividers, an honest note when the AI has not
replied, a "new messages" button instead of jumps, and is read to its
newest message. The composer is keyboard-first (Ctrl/Cmd+Enter), keeps a
draft per conversation and retries with the same idempotency key.
Customers get a clearer directory and profile with Arabic labels.

Fixes: live refresh never reached messages past the first transcript
page; the conversation realtime service opened a second socket (and
leaked it) when two screens subscribed in the same tick.

Everything in English and Arabic (162 new messages), one h1 per page,
WCAG 2.2 AA contrast in all four themes, reduced motion honoured.
Frontend only; no new dependency or endpoint.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
