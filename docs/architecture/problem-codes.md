# Problem codes

Every refusal the API gives carries a stable `code` in its RFC 9457 problem body,
beside the status, the title and an English `detail`. **The code is the contract;
the detail is not.** The client words a refusal by its code, in the person's
language, and never shows `detail`. A code, once published, keeps its meaning.

Phase 37.3 started this list with the account endpoints (D-2, row 6b). Phase 37.5a
added the guarantee that every correctable refusal carries a code, and the codes of
the knowledge, agent, channel and workspace areas (row 28a). Phase 37.5b named the
codes of roles, invitations, team and billing (row 28b). Phase 37.6 added the
workspace time zone's code and the analytics comparison's (rows 31 and 34).
Phase 37.7 reused `language.unsupported` for the report and schedule `language`.
Phase 38.7b (BA-4) named the last areas that still answered the generic codes:
customers, conversations, notifications, and reports and schedules; and (BA-12) put
`limitUnit` beside every knowledge file `limit`. Phase 39.1 added the display name's
two codes (with `limit` and `limitUnit`), the `field` member on the three greeting
refusals, and `mine` as a known conversation filter.

## Accounts (Phase 37.3)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `auth.credentials.invalid` | 401 | `POST /api/auth/login` | The address or the password is wrong (or a lockout left from before Revision 1 has not expired yet). One code and one body for every case, so nobody learns whether an address has an account |
| `auth.registration.incomplete` | 400 | `POST /api/auth/register` | The address or the password is missing |
| `auth.email.taken` | 400 | `POST /api/auth/register` | An account already uses this address (Identity `DuplicateEmail` / `DuplicateUserName`). The documented registration exception (auth D25) |
| `auth.email.invalid` | 400 | `POST /api/auth/register` | Identity refused the address (`InvalidEmail` / `InvalidUserName`) |
| `auth.password.<rule>` | 400 | `register`, `password/reset` | Identity's password rule, from its error code: `PasswordTooShort` → `auth.password.too_short`, `PasswordRequiresUniqueChars` → `auth.password.requires_unique_chars`, and so on. With today's policy (12 characters, nothing else) only `too_short` occurs in practice |
| `auth.registration.rejected` | 400 | `register` | Any other Identity refusal |
| `auth.reset.invalid` | 400 | `POST /api/auth/password/reset` | The reset link cannot be used: malformed, altered, expired or already used token, or an unknown account. One code and one body for all of them |
| `auth.throttled` | 429 | `login`, `password/forgot`, `password/reset`, `GET /api/auth/session` (37.5b) | Too many failed sign-ins (for `login`, only failures count) or too many reset requests. `Retry-After` says, in whole seconds, when to try again; the body names no limit, window or address |
| `auth.throttle.unavailable` | 503 | `login`, `password/forgot`, `password/reset`, `GET /api/auth/session` (37.5b) | The rate limiter could not decide (store unreachable, or the client's network could not be established), so the request was refused rather than served unlimited |
| `language.unsupported` | 400 | `register`, `login`, `invitations/accept`, `POST …/invitations`, `PUT /api/me/language`; from Phase 37.7 also `POST …/reports`, `POST …/reports/schedules`, `PUT …/reports/schedules/{id}` (checked before membership) | `language` is not `"en"` or `"ar"` |

When Identity reports several errors at once, `code` is the first and `codes` lists
them all, in order, for a form that shows each rule next to its field.

## Display name (Phase 39.1)

`PUT /api/me/display-name` changes the caller's own name; there is no route that
names another account. A blank, empty or `null` name is not refused: it clears the
name. Before the rules apply the name is trimmed and every inner run of whitespace
(spaces, tabs, line breaks) becomes one space.

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `account.display_name.too_long` | 400 | `PUT /api/me/display-name` | Longer than 80 characters (UTF-16 code units) after trimming; never truncated. The body also carries `limit: 80` and `limitUnit: "characters"` |
| `account.display_name.control_characters` | 400 | the same | A control character, or a format character other than the two joiners Arabic and emoji need (U+200C ZWNJ, U+200D ZWJ): a direction mark such as U+200E or U+202E is refused |

## Conversations (Phase 37.4)

All four are `400` and are checked before membership or permission, so the
answer is the same for anybody and says nothing about the workspace.

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `messenger.simulator.invalid_scenario` | 400 | Test local Messenger delivery | Select a supported local delivery scenario. |
| `messenger.simulator.invalid_page_event` | 400 | Import local Messenger Page events | Supply a Page envelope addressed to the selected simulated Page, with up to 50 events. |
| `messenger.simulator.invalid_message` | 400 | Simulate a Messenger text | Provide an event ID and 1–2000 characters of text. |
| `conversations.quick_reply.text_invalid` | 400 | Save a workspace reply | Supply 1–500 characters of plain text. |
| `conversations.quick_reply.language_invalid` | 400 | Workspace quick replies | Choose `en` or `ar`. |
| `conversations.filter.invalid` | 400 | `GET …/conversations` | `filter` is not `needs-person` or, from Phase 39.1, `mine` (or empty). Refused rather than ignored, so a misspelt filter never shows every conversation under a heading that promises a few |
| `transcript.cursor.conflict` | 400 | `GET …/conversations/{id}/messages` | `after` was combined with `before` or `tail=true`: a page cannot be read forwards and backwards at once |
| `transcript.include.invalid` | 400 | `GET …/conversations/{id}/messages` | `include` names something other than `events` |
| `conversations.read.sequence.invalid` | 400 | `POST …/conversations/{id}/read` | `sequence` is missing or negative |

**Replies, handoff and assignment (Phase 38.7b).** These answered `validation.failed`
or `state.conflict` before. The status of each is unchanged.

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `conversations.id.invalid` | 400 | any `…/conversations/{id}` route | An all-zero conversation id |
| `conversations.reply.not_taken_over` | 409 | `POST …/conversations/{id}/messages` | The AI is answering this conversation. Take it over first, then reply |
| `conversations.client_message_id.required` | 400 | `POST …/conversations/{id}/messages`; the public chat's message routes | No `clientMessageId` |
| `conversations.client_message_id.too_long` | 400 | the same | Past the id bound |
| `conversations.client_message_id.invalid` | 400 | the same | Characters other than letters, digits, hyphens and underscores |
| `conversations.message.required` | 400 | `POST …/conversations/{id}/messages` | The reply is blank. (The public chat refuses a visitor's blank message first, as `chat.message.required`) |
| `conversations.message.too_long` | 400 | the same | Past the message bound; never truncated |
| `conversations.message.control_characters` | 400 | the same | Control characters other than tab, carriage return and line feed |
| `conversations.assignee.not_member` | 422 | `PUT …/conversations/{id}/assignment` | The person named is not a member of this workspace |
| `conversations.assignee.cannot_reply` | 422 | the same | The member named holds no permission to reply, so the customer would wait. Give them `conversations.reply` first |
| `conversations.closed` | 409 | take over, return, assign, reply | The conversation is closed. No route closes a conversation today; the rule is the model's own, named for when one does |

## Customers (Phase 38.7b)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `customers.page.invalid` | 400 | `GET …/customers` | `page` or `pageSize` out of range |
| `customers.id.invalid` | 400 | any `…/customers/{id}` route | An all-zero customer id |
| `customers.display_name.too_long` | 400 | `PUT …/customers/{id}/display-name` | Past the name bound (120); never truncated. A blank name is not refused: it clears the name, as before |
| `customers.display_name.control_characters` | 400 | the same | A name is one line, with no control characters |
| `customers.merge.target_required` | 400 | `POST …/customers/{id}/merge` | The request names no customer to keep |
| `customers.merge.self` | 400 | the same | A customer cannot be merged into itself |
| `customers.merge.not_in_workspace` | 400 | the same | The two records are not both customers of this workspace. The route answers 404 for another workspace's id first, so this is seen only if a record disappears during the merge |
| `customers.merged` | 409 | rename, merge | The customer was merged into another. The body also carries `customerId` (the one named) and `canonicalCustomerId` (the one to use), as it always did |

## Notifications (Phase 38.7b)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `notifications.page.invalid` | 400 | `GET …/notifications` | `page` or `pageSize` out of range |
| `notifications.preferences.incomplete` | 400 | `PUT …/notifications/preferences` | Each of `reports`, `usage`, `billing`, `knowledge` and `channelHealth` must say true or false. Checked before membership |

An all-zero notification id on `POST …/notifications/{id}/read` stays a `404`
("not yours"), as before.

## Reports and schedules (Phase 38.7b)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `reports.type.invalid` | 400 | `POST …/reports`, `POST …/reports/schedules`, `PUT …/reports/schedules/{id}` | Not a report type in the catalogue (names only; a number is refused) |
| `reports.format.invalid` | 400 | the same | Not `Csv`, `Xlsx` or `Pdf` |
| `reports.format.unsupported` | 400 | the same | That report is not offered in that format |
| `reports.frequency.invalid` | 400 | schedule create and update | Not `Weekly` or `Monthly` |
| `reports.client_request_id.required` | 400 | `POST …/reports` | No `clientRequestId`, so a repeated request could not be told apart |
| `reports.client_request_id.reused` | 409 | `POST …/reports` | That `clientRequestId` was already used for a different report. Use a new one |
| `reports.range.invalid` | 400 | `POST …/reports` | `range` is not `7d`, `30d`, `90d` or `custom` |
| `reports.range.conflict` | 400 | the same | A preset range and dates together |
| `reports.range.dates_required` | 400 | the same | A custom range without both `from` and `to` |
| `reports.period.future` | 400 | the same | The period ends after today in the workspace's zone |
| `reports.period.reversed` | 400 | the same | `to` is before `from` |
| `reports.period.too_long` | 400 | the same | Longer than the longest period a report covers |
| `reports.period.too_early` | 400 | the same | Starts before the earliest date reports cover |
| `reports.page.invalid` | 400 | `GET …/reports/executions` | `page` or `pageSize` out of range |
| `reports.id.invalid` | 400 | `…/reports/executions/{id}` routes | An all-zero report id |
| `reports.file.not_ready` | 409 | `GET …/executions/{id}/download` | The report is still being generated |
| `reports.file.failed` | 409 | the same | The report failed, so there is no file |
| `reports.file.expired` | 410 | the same | The retention policy removed the file. Generate it again |
| `reports.file.missing` | 410 | the same | The record says a file exists and report storage does not have it. Generate it again; an operator should look |
| `reports.schedule.id.invalid` | 400 | `…/reports/schedules/{id}` routes | An all-zero schedule id |
| `reports.schedule.name.required` | 400 | schedule create and update | A schedule needs a name |
| `reports.schedule.name.too_long` | 400 | the same | Past the name bound; never truncated |
| `reports.schedule.name.control_characters` | 400 | the same | No control characters in a name |
| `reports.schedule.limit_reached` | 409 | `POST …/reports/schedules` | The workspace already has as many schedules as it may |
| `reports.schedule.enabled_required` | 400 | `PUT …/reports/schedules/{id}/enabled` | The request does not say whether the schedule is on or off |

A report or schedule written in a language other than English or Arabic keeps
`language.unsupported`, now also at the model's own check.

## The guarantee (Phase 37.5a)

Every `400`, `409`, `412` and `422` this API writes carries a `code`. The throw site
names the specific rule wherever this list has one; a refusal that reaches the
problem writer without one is given the generic code below, by
`ProblemCodes.ApplyFallback` on every problem body (exception handler,
`Results.Problem` and `Results.ValidationProblem` alike). A specific code is never
overwritten, and nothing else about the body changes: the status, `title`, the
English `detail` and any `errors` map stay exactly as they were. A client words a
refusal by `code`, falls back to the generic wording for a code it does not know,
and never shows `detail`.

Tests hold this in place (`ProblemCodeTests`): a source scan fails when a
`DomainValidationException` or `DomainStateException` in the knowledge, agent,
channel, role, invitation, team or billing folders (with the workspace, address,
account and plan value objects they are built from), and from Phase 38.7b the
customer, conversation, notification and report folders, or a knowledge file
refusal, has no code; another fails when a 400, 409 or 422 written by the customer,
conversation, notification or report endpoints themselves has none; another fails
when a published code is missing from this file; and HTTP checks prove the codes
reach the wire (`ProblemCodeTests`, `AreaProblemCodeApiTests`). Since Phase 38.7b
every area names the codes of its own rules. A refusal with no specific code yet
(for example an unknown analytics `range`) still gets the generic one, and keeps
it until it is named.

**No `errors` map in these areas.** None of the areas above answers with a
validation `errors` map (no endpoint returns `ValidationProblem`): each refusal is
one rule with one code, so no parallel `errorCodes` map exists. Should an endpoint
ever answer with `errors`, it keeps that map unchanged and gets `code`
`validation.failed` beside it, through the same guarantee.

| Code | Status | Meaning |
| --- | --- | --- |
| `validation.failed` | 400, 422 | The request's content was refused, and no more specific code exists for that rule yet |
| `state.conflict` | 409 | The resource's current state refused the request, and no more specific code exists yet |
| `precondition.failed` | 412 | `If-Match` names a version that is no longer current. Read again, then decide whether to reapply |
| `precondition.required` | 428 | A conditional write arrived without `If-Match` |
| `precondition.if_match.invalid` | 400 | `If-Match` could not be read as exactly one strong entity tag |

**In the OpenAPI document (Phase 38.7b, BA-5).** The `ProblemDetails` schema of
`/openapi/v1.json` (Development only) lists the extension members this API writes:
`code`, `codes`, `limit`, `limitUnit` (with its six values), `entitlement`,
`current`, `periodFrom`, `periodTo`, `traceId`, and the merged customer's
`customerId` and `canonicalCustomerId`; from Phase 39.1 also `field`. All are
optional: each appears only on the refusals this file names. `OpenApiDocumentContractTests`
reads the document and fails when one goes missing.

**`field` (Phase 39.1).** Where one request carries several values that the same
rule checks, the refusal names which one: `greeting`, `greetings.en` or
`greetings.ar` on the two `website-chat.greeting.*` codes. The code is the same for
all three; `field` tells a form which box to mark. Absent everywhere else.

**`limit` and `limitUnit` on a domain rule (Phase 39.1).** Besides the knowledge
file refusals, `account.display_name.too_long` carries `limit: 80` and
`limitUnit: "characters"`.

## Knowledge (Phase 37.5a)

**Refused files (row 17).** The status follows the reason exactly as before (413,
415 or 422; 400 only for a form without a file); the code names the rule. Where a
bound was broken, a numeric `limit` travels beside the code, in the rule's own
unit, so a client can say "more than 500 pages" in the reader's language without
reading the English sentence.

**`limitUnit` (Phase 38.7b, BA-12).** Beside every `limit` on a file refusal, and
only there, `limitUnit` says what the number counts: `bytes`, `pages`, `characters`,
`rows`, `columns` or `entries`. The two are present together or not at all. It
matters for `knowledge.file.archive_too_large`, whose `limit` is `entries` for a
DOCX with too many parts and `bytes` for one that expands too far; for the other
codes it restates the unit in the table below. An entitlement refusal's `limit` has
no `limitUnit`: its unit is the entitlement it names.

| Code | Status | `limit` | Meaning |
| --- | --- | --- | --- |
| `knowledge.file.multipart_required` | 415 | | The upload is not `multipart/form-data` |
| `knowledge.file.required` | 400 | | The form has no file, or an empty one |
| `knowledge.file.too_large` | 413 | bytes | Larger than the upload limit |
| `knowledge.file.unsupported_type` | 415 | | Not a PDF, DOCX, TXT or CSV |
| `knowledge.file.type_mismatch` | 422 | | The bytes contradict the name (a "PDF" that is text, text with binary in it) |
| `knowledge.file.empty` | 422 | | The file holds no bytes, or a CSV not even one line |
| `knowledge.file.not_utf8` | 422 | | A text or CSV file is not valid UTF-8 |
| `knowledge.file.damaged` | 422 | | The file could not be read as its format |
| `knowledge.file.encrypted` | 422 | | A password-protected PDF. Same status and detail as a damaged one; the fix is to unlock it |
| `knowledge.file.no_text` | 422 | | Read, and no text in it: a scan, images only, a CSV header without records |
| `knowledge.file.header_required` | 422 | | A CSV without a header row naming its columns |
| `knowledge.file.too_many_pages` | 422 | pages | A PDF past the page bound |
| `knowledge.file.too_many_characters` | 422 | characters | More text than one document may hold |
| `knowledge.file.too_many_rows` | 422 | rows | A CSV past the row bound |
| `knowledge.file.too_many_columns` | 422 | columns | A CSV past the column bound |
| `knowledge.file.cell_too_long` | 422 | characters | A CSV cell past the cell bound |
| `knowledge.file.archive_too_large` | 422 | `entries` or `bytes`, as `limitUnit` says | A DOCX with too many parts, or one that expands too far |
| `knowledge.file.name.required` | 400 | | The uploaded file has no name |
| `knowledge.file.name.unreadable` | 400 | | The file name could not be read |
| `knowledge.file.name.too_long` | 400 | | The file name is past its bound |

`knowledge.file.duplicate` and similar codes do not exist: nothing refuses a file
for being uploaded twice.

**Documents, assignments and retrieval.**

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `knowledge.title.required` | 400 | create, update | A document needs a title |
| `knowledge.title.too_long` | 400 | create, update | Past the title bound; never truncated |
| `knowledge.title.control_characters` | 400 | create, update | A title is one line without control characters |
| `knowledge.content.required` | 400 | create, update | A document needs some content |
| `knowledge.content.too_long` | 400 | create, update | Past the content bound; never truncated |
| `knowledge.content.control_characters` | 400 | create, update | Control characters other than tab and line feed |
| `knowledge.document.already_enabled` | 409 | enable | Already enabled |
| `knowledge.document.already_disabled` | 409 | disable | Already disabled |
| `knowledge.indexing.in_progress` | 409 | index | Another attempt for this document is running; nothing was sent |
| `knowledge.index.too_many_chunks` | 400 | index | The document would split into more pieces than allowed |
| `knowledge.source.missing` | 409 | replace the file | The document was typed, so there is no file to replace |
| `knowledge.assignment.document_unknown` | 400 | agent knowledge | A document id that is not in this workspace |
| `knowledge.assignment.too_many` | 400 | agent knowledge | More documents than one agent may draw on |
| `knowledge.search.question.required` | 400 | retrieval test | A question is required |
| `knowledge.search.question.too_long` | 400 | retrieval test | Past the question bound |
| `knowledge.page.invalid` | 400 | list | `page` or `pageSize` out of range |
| `knowledge.id.invalid` | 400 | any | An all-zero document id |

## AI agents (Phase 37.5a)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `ai-agents.name.required` | 400 | create, rename | A name is required |
| `ai-agents.name.too_long` | 400 | create, rename | Past the name bound; never truncated |
| `ai-agents.name.control_characters` | 400 | create, rename | No control characters in a name |
| `ai-agents.instructions.required` | 400 | create, instructions | Instructions are required |
| `ai-agents.instructions.too_long` | 400 | create, instructions | Past the instructions bound; never truncated |
| `ai-agents.instructions.control_characters` | 400 | create, instructions | Control characters other than tab, carriage return and line feed |
| `ai-agents.disable.draft` | 409 | disable | A draft was never active, so there is nothing to switch off |
| `ai-agents.page.invalid` | 400 | list | `page` or `pageSize` out of range |
| `ai-agents.id.invalid` | 400 | any | An all-zero agent id |
| `ai.message.invalid` | 400 | preview, chat | A message that is blank, too long or carries control characters |

## Website chat and the public chat (Phase 37.5a)

**Admin side (rows 21 and 22).**

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `website-chat.agent.required` | 400 | create | The request names no agent |
| `website-chat.agent.unusable` | 409 | create | The agent belongs elsewhere or already has a chat link |
| `website-chat.origin.invalid` | 400 | `PUT …/widget` | One website is not an origin (a path, a wildcard, plain text). The detail quotes the line |
| `website-chat.origin.too_many` | 400 | `PUT …/widget` | More websites than one widget may be allowed on |
| `website-chat.launcher_position.invalid` | 400 | `PUT …/widget` | Not `Left`, `Right`, `Start` or `End` |
| `website-chat.theme.invalid` | 400 | `PUT …/widget` | Not `System`, `Light` or `Dark` |
| `website-chat.accent_color.required` | 400 | `PUT …/widget` | An accent colour is required where one is given |
| `website-chat.accent_color.invalid` | 400 | `PUT …/widget` | Not a hex colour such as `#2563eb` |
| `website-chat.greeting.too_long` | 400 | `PUT …/widget` | Past the greeting bound (300); never truncated. From Phase 39.1 the body names the member in `field`: `greeting`, `greetings.en` or `greetings.ar` |
| `website-chat.greeting.control_characters` | 400 | `PUT …/widget` | Control characters other than line feed. `field` as above |
| `website-chat.public_key.invalid` | 400 | admin reads | A malformed public key |
| `website-chat.id.invalid` | 400 | any | An all-zero endpoint id |

There is no `website-chat.origin.duplicate`: a website listed twice is merged, as
before, and never refused.

**Public side.** The embedded widget's refusals are **not coded, by design**: an
unknown key, a website nobody approved and a withdrawn approval all answer the same
generic `404` (`type`, `title`, `status` and a per-request `traceId`; no `detail`,
no `code`), identical apart from the trace id, so sites and keys cannot be
enumerated (`embeddable-web-chat-widget.md` §10). A visitor's own message refusals are coded:

| Code | Status | Meaning |
| --- | --- | --- |
| `chat.message.required` | 400 | The message is blank |
| `chat.message.too_long` | 400 | Past the message bound; never truncated |
| `chat.message.in_progress` | 409 | This message is still being answered; nothing is retried |
| `chat.message.already_failed` | 409 | This message was attempted and failed; send a new one |

## WhatsApp (Phase 37.5a)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `whatsapp.agent.required` | 400 | create, update | The request names no agent |
| `whatsapp.agent.not_in_workspace` | 400 | create, update | The agent belongs to another workspace |
| `whatsapp.identifier.invalid` | 400 | create, update | A WhatsApp business account or phone number id that is not 1 to 64 digits |
| `whatsapp.display_phone_number.too_long` | 400 | create, update | Past the display number bound |
| `whatsapp.access_token.required` | 400 | credential | The access token is blank |
| `whatsapp.access_token.too_long` | 400 | credential | Past the token bound |
| `whatsapp.connection.in_service` | 409 | update | Take the connection out of service before changing its number or agent |
| `whatsapp.connection.access_token_missing` | 409 | activate | Add an access token before putting the connection into service |
| `whatsapp.phone_number.in_use` | 409 | create, update | The number is already connected on this deployment; not by whom |
| `whatsapp.conversation.not_on_whatsapp` | 409 | reply | The conversation did not come from WhatsApp |
| `whatsapp.message.required` | 400 | reply | An outbound message must carry text |
| `whatsapp.message.too_long` | 400 | reply | Past WhatsApp's text bound |
| `whatsapp.id.invalid` | 400 | any | An all-zero connection id |

## Workspace (Phase 37.5a)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `workspace.name.invalid` | 400 | `PUT /api/tenants/{id}/name`, `POST /api/tenants` | The name is blank, longer than 200 characters after trimming, or holds control characters. One code for all three, as the plan designed |
| `workspace.slug.taken` | 409 | `POST /api/tenants` | Another workspace already holds this slug |
| `workspace.slug.invalid` | 400 | `POST /api/tenants` | The slug is missing, the wrong length, or not lowercase letters, digits and single hyphens (Phase 37.5b; one code for every slug rule) |
| `workspace.id.invalid` | 400 | any | An all-zero workspace id (Phase 37.5b) |
| `workspace.time_zone.invalid` | 400 | `PUT /api/tenants/{id}/time-zone` | The zone is missing, longer than 64 characters, not an IANA name the runtime's zone database knows, not spelt exactly as that database spells it (`asia/riyadh` is refused), a `posix/` or `right/` alias, or not in PostgreSQL's `pg_timezone_names`. One code for every rule (Phase 37.6, row 34). The first four are checked before membership |

## Analytics (Phase 37.6)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `analytics.compare.invalid` | 400 | `GET …/analytics/dashboard` | `compare` is something other than `previous` (or empty). Checked before membership, so the answer says nothing about the workspace. An unknown `range` keeps its existing generic `validation.failed` |

## Roles and permissions (Phase 37.5b)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `roles.name.required` | 400 | create, rename | A role needs a name |
| `roles.name.too_long` | 400 | create, rename | Past the name bound; never truncated |
| `roles.name.control_characters` | 400 | create, rename | No control characters in a name |
| `roles.name.taken` | 409 | create, rename | Another role of this workspace already has this name, ignoring case |
| `roles.permission.invalid` | 400 | create, permissions | A permission code that is not written like one (`group.action`, lowercase) |
| `roles.permission.unknown` | 400 | create, permissions | A well-formed code that is not in the catalogue |
| `roles.system.protected` | 409 | rename, permissions | The Owner role is the protected system role: it is never renamed and always grants everything |
| `roles.id.invalid` | 400 | any | An all-zero role id |

## Invitations (Phase 37.5b)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `invitations.status.invalid` | 400 | `GET …/invitations?status=` | Not `Pending`, `Accepted`, `Revoked` or `Expired` (any letter case; a number is refused). Checked before membership, so the answer is the same for anybody |
| `invitations.role.unassignable` | 400 | `POST …/invitations` | A role id that is not a role of this workspace |
| `invitations.email.already_member` | 409 | `POST …/invitations` | The address already belongs to a member; change their roles instead |
| `invitations.email.already_invited` | 409 | `POST …/invitations` | An invitation for this address is already open; revoke it first |
| `invitations.revoke.closed` | 409 | `POST …/invitations/{id}/revoke` | The invitation was already accepted, revoked or expired |
| `invitations.no_longer_usable` | 410 | `POST /api/invitations/accept` | A real invitation that was used, withdrawn or expired, or whose inviter's authority no longer stands |
| `invitations.account.mismatch` | 403 | `POST /api/invitations/accept` | Signed in as somebody other than the invited address; neither address is named |
| `invitations.id.invalid` | 400 | any | An all-zero invitation id |

## Team (Phase 37.5b)

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `team.member.self` | 409 | `PUT …/members/{id}/roles` | Nobody changes their own access from team management |
| `team.member.owner_protected` | 409 | `PUT …/members/{id}/roles`, `POST …/invitations` | The Owner's access is not changed from team management, and the Owner role is never handed out there |
| `team.member.protected` | 409 | team management | Any other protected membership. No route raises it today; it is the general form of the two above |
| `team.role.unassignable` | 400 | `PUT …/members/{id}/roles` | A role id that is not a role of this workspace |

## Email addresses (Phase 37.5b)

The address rules, wherever an address is taken from a person (today, a new
invitation).

| Code | Status | Meaning |
| --- | --- | --- |
| `email.required` | 400 | No address |
| `email.too_long` | 400 | Past the address bound |
| `email.invalid_characters` | 400 | Spaces or control characters |
| `email.invalid` | 400 | Not an address this platform can use |

## Billing (Phase 37.5b)

The purchase refusals keep the codes they have carried since the billing step;
they are listed here so the whole set is in one place.

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `billing.offer_id.invalid` | 400 | `POST …/billing/checkout` | The request names no offer (an all-zero id) |
| `billing.checkout.unknownoffer` | 409 | checkout | No such offer |
| `billing.checkout.offernotavailable` | 409 | checkout | The offer is withdrawn |
| `billing.checkout.alreadysubscribed` | 409 | checkout | The workspace already has a subscription; manage it in the provider's portal |
| `billing.checkout.offernotpurchasable` | 409 | checkout | The offer cannot be bought on this deployment |
| `billing.portal.nocustomer` | 409 | `POST …/billing/portal` | The workspace has never bought anything, so there is nothing to manage |

## Internal consistency codes (Phase 37.5a)

These name invariants inside the model that no request this API accepts can break:
an aggregate without its workspace, an empty identity, an instant that is not a
point in time, a stored value of the wrong length. A client should never see one;
if it does, it is a defect, and the code tells an operator where to look.

| Code | Area |
| --- | --- |
| `ai-agents.tenant.required`, `ai-agents.instant.invalid` | AI agents |
| `knowledge.tenant.required`, `knowledge.instant.invalid`, `knowledge.source.invalid`, `knowledge.assignment.invalid`, `knowledge.fingerprint.invalid`, `knowledge.embedding_profile.invalid` | Knowledge |
| `website-chat.endpoint.invalid`, `website-chat.instant.invalid`, `chat.session.invalid`, `chat.access_token.invalid` | Website chat |
| `whatsapp.connection.invalid`, `whatsapp.instant.invalid`, `whatsapp.contact.invalid`, `whatsapp.provider_id.invalid`, `whatsapp.customer_identity.invalid`, `whatsapp.delivery.invalid`, `whatsapp.inbound.invalid` | WhatsApp |
| `roles.tenant.required`, `roles.assignment.invalid` | Roles (Phase 37.5b) |
| `invitations.tenant.required`, `invitations.email.required`, `invitations.inviter.required`, `invitations.expiry.invalid`, `invitations.role.required`, `invitations.role_grant.invalid`, `invitations.token_hash.invalid`, `invitations.email.not_sendable`, `invitations.email_token.invalid`, `invitations.accept.account_required`, `invitations.accept.closed`, `invitations.expire.not_due` | Invitations (37.5b). `invitations.accept.closed` is the aggregate's own guard; the accept route answers `410 invitations.no_longer_usable` first |
| `team.membership.invalid`, `users.id.invalid`, `accounts.email.invalid` | Team and accounts (37.5b) |
| `billing.checkout_intent.invalid`, `billing.customer.invalid`, `billing.event.invalid`, `billing.id.invalid`, `billing.offer.invalid`, `billing.provider_reference.invalid`, `billing.price.invalid`, `billing.subscription.invalid` | Billing (37.5b): records built from the provider's answers and the operator's catalogue |
| `plans.code.invalid`, `plans.name.invalid`, `plans.limit.invalid`, `plans.id.invalid`, `plans.assignment.invalid`, `plans.instant.invalid`, `plans.entitlement.unknown`, `plans.usage.invalid` | Plans (37.5b): the plan definitions behind billing and the entitlements |
| `customers.tenant.required`, `customers.instant.invalid`, `customers.display_name.required` | Customers (38.7b). `customers.display_name.required` is the name's own rule; the rename route clears a blank name instead of refusing it |
| `conversations.tenant.required`, `conversations.customer.required`, `conversations.sender.invalid`, `conversations.assignment.invalid`, `conversations.instant.invalid`, `conversations.value.invalid`, `conversations.handling_event.invalid`, `conversations.turn.invalid`, `conversations.turn.settled`, `conversations.reply.invalid`, `conversations.message_id.invalid` | Conversations (38.7b): the transcript, its turns, replies and handling events. `conversations.turn.settled` is the turn's own guard; the chat answers a repeated message with `chat.message.in_progress` or `chat.message.already_failed` first |
| `notifications.tenant.required`, `notifications.kind.unknown`, `notifications.retention.invalid`, `notifications.text.invalid`, `notifications.action.invalid`, `notifications.id.invalid`, `notifications.occurrence.invalid`, `notifications.email_id.invalid` | Notifications (38.7b): records the platform writes itself. `notifications.id.invalid` is the id's own rule; the route answers 404 for an all-zero id first |
| `reports.tenant.required`, `reports.artifact.invalid` | Reports (38.7b): executions, schedules and their stored files |

## Codes that existed before this list

| Code | Status | Where |
| --- | --- | --- |
| `antiforgery.invalid` | 400 | every antiforgery-checked endpoint |
| Entitlement codes (`entitlement.<kind>.exhausted`) | 409 | see `workspace-plans-entitlements-quotas.md`; the billing codes are listed above |
