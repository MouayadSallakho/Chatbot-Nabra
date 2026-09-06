/**
 * The bound the role forms show a reader, mirrored from the Domain.
 *
 * <strong>The Domain remains the authority.</strong> This exists so a counter
 * can tick down while somebody types and so a form can refuse an obviously
 * over-long value without a round trip. The server still validates, and a
 * rejected value is reported in the server's own words rather than re-derived
 * here.
 *
 * Mirrors `TenantRoleName.MaxLength`.
 */
export const ROLE_NAME_MAX = 80;
