# Problem codes

Every refusal the API gives carries a stable `code` in its RFC 9457 problem body,
beside the status, the title and an English `detail`. **The code is the contract;
the detail is not.** The client words a refusal by its code, in the person's
language, and never shows `detail`. A code, once published, keeps its meaning.

Phase 37.3 started this list with the account endpoints (D-2, row 6b). Phase 37.5a
added the guarantee that every correctable refusal carries a code, and the codes of
the knowledge, agent, channel and workspace areas (row 28a); Phase 37.5b names the
codes of roles, invitations, team and billing.

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
| `auth.throttled` | 429 | `login`, `password/forgot`, `password/reset` | Too many failed sign-ins (for `login`, only failures count) or too many reset requests. `Retry-After` says, in whole seconds, when to try again; the body names no limit, window or address |
| `auth.throttle.unavailable` | 503 | `login`, `password/forgot`, `password/reset` | The rate limiter could not decide (store unreachable, or the client's network could not be established), so the request was refused rather than served unlimited |
| `language.unsupported` | 400 | `register`, `login`, `invitations/accept`, `POST …/invitations`, `PUT /api/me/language` | `language` is not `"en"` or `"ar"` |

When Identity reports several errors at once, `code` is the first and `codes` lists
them all, in order, for a form that shows each rule next to its field.

## Conversations (Phase 37.4)

All four are `400` and are checked before membership or permission, so the
answer is the same for anybody and says nothing about the workspace.

| Code | Status | Where | Meaning |
| --- | --- | --- | --- |
| `conversations.filter.invalid` | 400 | `GET …/conversations` | `filter` is not `needs-person` (or empty). Refused rather than ignored, so a misspelt filter never shows every conversation under a heading that promises a few |
| `transcript.cursor.conflict` | 400 | `GET …/conversations/{id}/messages` | `after` was combined with `before` or `tail=true`: a page cannot be read forwards and backwards at once |
| `transcript.include.invalid` | 400 | `GET …/conversations/{id}/messages` | `include` names something other than `events` |
| `conversations.read.sequence.invalid` | 400 | `POST …/conversations/{id}/read` | `sequence` is missing or negative |

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
`DomainValidationException` or `DomainStateException` in the knowledge, agent or
channel folders, or a knowledge file refusal, has no code; another fails when a
published code is missing from this file; and HTTP checks prove the codes reach
the wire. The areas of Phase 37.5b (roles, invitations, team, billing) answer the
generic codes until that step names theirs.

| Code | Status | Meaning |
| --- | --- | --- |
| `validation.failed` | 400, 422 | The request's content was refused, and no more specific code exists for that rule yet |
| `state.conflict` | 409 | The resource's current state refused the request, and no more specific code exists yet |
| `precondition.failed` | 412 | `If-Match` names a version that is no longer current. Read again, then decide whether to reapply |
| `precondition.required` | 428 | A conditional write arrived without `If-Match` |
| `precondition.if_match.invalid` | 400 | `If-Match` could not be read as exactly one strong entity tag |

## Knowledge (Phase 37.5a)

**Refused files (row 17).** The status follows the reason exactly as before (413,
415 or 422; 400 only for a form without a file); the code names the rule. Where a
bound was broken, a numeric `limit` travels beside the code, in the rule's own
unit, so a client can say "more than 500 pages" in the reader's language without
reading the English sentence.

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
| `knowledge.file.archive_too_large` | 422 | entries or bytes | A DOCX with too many parts, or one that expands too far |
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
| `website-chat.greeting.too_long` | 400 | `PUT …/widget` | Past the greeting bound; never truncated |
| `website-chat.greeting.control_characters` | 400 | `PUT …/widget` | Control characters other than line feed |
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

## Codes that existed before this list

| Code | Status | Where |
| --- | --- | --- |
| `antiforgery.invalid` | 400 | every antiforgery-checked endpoint |
| Entitlement and billing codes | 402, 409 | see `workspace-plans-entitlements-quotas.md` and `saas-subscription-billing.md` |
