/**
 * The minimum password length, mirroring the server's policy.
 *
 * <strong>A copy, and only for immediate feedback.</strong> The server owns the
 * policy and re-checks it; this exists so somebody typing a short password is
 * told before they submit rather than after a round trip. If the two ever
 * disagree, the server wins and its refusal is what appears.
 *
 * Length is the whole policy. There is deliberately no rule demanding a digit
 * or a symbol: those push everybody towards the same predictable substitutions
 * while adding very little, and a longer passphrase is both stronger and easier
 * to remember.
 *
 * Shared by registration and setting a new password, so the two can never
 * disagree with each other.
 */
export const PASSWORD_MIN_LENGTH = 12;
