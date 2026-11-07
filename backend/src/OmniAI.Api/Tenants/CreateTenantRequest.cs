namespace OmniAI.Api.Tenants;

/// <summary>
/// Request body for <c>POST /api/tenants</c>.
/// </summary>
/// <param name="Name">Display name of the business, for example <c>Acme Corporation</c>.</param>
/// <param name="Slug">Human-readable identifier, for example <c>acme</c>.</param>
/// <remarks>
/// Both properties are nullable strings and carry no validation attributes. That
/// is deliberate: <c>TenantName</c> and <c>TenantSlug</c> already own the rules,
/// and a <c>[Required]</c> or <c>[StringLength]</c> here would be a second copy
/// of them that can disagree with the first. What the transport layer does check
/// is transport-level only -- that the body is syntactically valid JSON of this
/// shape -- and a malformed body is rejected before this type exists.
/// <para>
/// The caller supplies both values explicitly. The slug is never derived from the
/// name, because guessing a slug would silently mint an identifier the caller did
/// not ask for and may not be able to change later. Neither value is truncated to
/// fit; an over-long value is rejected with an explanation instead.
/// </para>
/// <para>
/// There is no <c>id</c> and no <c>status</c> field, so neither can be chosen by
/// a caller.
/// </para>
/// </remarks>
public sealed record CreateTenantRequest(string? Name, string? Slug);

/// <summary>What renaming a workspace requires (Phase 37.5a).</summary>
/// <remarks>
/// The name only. It follows the rules a workspace is created under: required,
/// at most 200 characters after trimming, no control characters.
/// </remarks>
public sealed record RenameTenantRequest(string? Name);

/// <summary>The body of <c>PUT /api/tenants/{id}/time-zone</c> (Phase 37.6).</summary>
/// <param name="TimeZone">An IANA name, such as <c>Asia/Riyadh</c>, or <c>UTC</c>.</param>
public sealed record ChangeTenantTimeZoneRequest(string? TimeZone);
