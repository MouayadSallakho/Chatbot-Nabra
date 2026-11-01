# Problem codes

Every refusal the API gives carries a stable `code` in its RFC 9457 problem body,
beside the status, the title and an English `detail`. **The code is the contract;
the detail is not.** The client words a refusal by its code, in the person's
language, and never shows `detail`. A code, once published, keeps its meaning.

Phase 37.3 started this list with the account endpoints (D-2, row 6b). Phase 37.5a
brings the remaining areas onto the same mechanism (row 28).

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

## Codes that existed before this list

| Code | Status | Where |
| --- | --- | --- |
| `antiforgery.invalid` | 400 | every antiforgery-checked endpoint |
| Entitlement and billing codes | 402, 409 | see `workspace-plans-entitlements-quotas.md` and `saas-subscription-billing.md` |
