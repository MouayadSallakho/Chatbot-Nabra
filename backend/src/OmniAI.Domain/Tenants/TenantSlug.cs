namespace OmniAI.Domain.Tenants;

/// <summary>
/// Stable, human-readable identifier for a tenant, for example <c>acme-syria</c>.
/// </summary>
/// <remarks>
/// The slug is intended to appear in places people read and type -- URLs, and
/// potentially a subdomain -- so its grammar is deliberately the DNS label
/// grammar: lowercase letters, digits and interior hyphens. Adopting that
/// grammar now costs nothing and avoids minting slugs that later turn out to be
/// unusable as a host name.
/// <para>
/// <b>Uniqueness is not enforced here.</b> Two <see cref="TenantSlug"/> instances
/// with the same value are equal, but the Domain cannot know whether a value is
/// already taken -- that requires knowledge of every stored tenant. A static
/// registry inside the Domain would be a lie: it would be process-local,
/// unusable across instances, and would break the moment the platform runs on
/// more than one node. Uniqueness is therefore a persistence-layer guarantee
/// (a unique index) surfaced through the Application layer, and is recorded as
/// such in the multi-tenancy ADR.
/// </para>
/// </remarks>
public sealed record TenantSlug
{
    /// <summary>
    /// Shortest permitted slug. Three characters keeps slugs typeable and
    /// readable while leaving one- and two-character values free, since those are
    /// the ones most likely to be wanted for platform-level routes later.
    /// </summary>
    public const int MinLength = 3;

    /// <summary>
    /// Longest permitted slug: the 63-character limit of a single DNS label, so
    /// any accepted slug can be used as a subdomain without further truncation.
    /// </summary>
    public const int MaxLength = 63;

    private const char Hyphen = '-';

    private TenantSlug(string value) => Value = value;

    /// <summary>The canonical, lowercase slug.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and canonicalises a slug. Surrounding whitespace is trimmed and
    /// the value is lowercased, because case is not meaningful in a slug and
    /// <c>Acme</c> and <c>acme</c> must not become two different tenants.
    /// No other repair is attempted: a value containing spaces, underscores,
    /// accented letters or stray hyphens is rejected rather than rewritten, so a
    /// mistake surfaces instead of silently becoming a different tenant's slug.
    /// </summary>
    /// <exception cref="DomainValidationException">The value breaks any slug rule.</exception>
    public static TenantSlug Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("Tenant slug is required.")
            {
                Code = "workspace.slug.invalid",
            };
        }

        // ToLowerInvariant, not ToLower: the result must not depend on the
        // machine's culture. The solution also runs with invariant globalisation.
        string normalised = value.Trim().ToLowerInvariant();

        if (normalised.Length is < MinLength or > MaxLength)
        {
            throw new DomainValidationException(
                $"Tenant slug must be between {MinLength} and {MaxLength} characters; " +
                $"'{normalised}' has {normalised.Length}.")
            {
                Code = "workspace.slug.invalid",
            };
        }

        if (normalised[0] == Hyphen || normalised[^1] == Hyphen)
        {
            throw new DomainValidationException(
                $"Tenant slug must not start or end with a hyphen; '{normalised}' does.")
            {
                Code = "workspace.slug.invalid",
            };
        }

        for (int index = 0; index < normalised.Length; index++)
        {
            char character = normalised[index];

            if (!IsAllowedCharacter(character))
            {
                throw new DomainValidationException(
                    $"Tenant slug may contain only lowercase letters, digits and hyphens; " +
                    $"'{normalised}' contains '{character}'.")
                {
                    Code = "workspace.slug.invalid",
                };
            }

            if (index > 0 && character == Hyphen && normalised[index - 1] == Hyphen)
            {
                throw new DomainValidationException(
                    $"Tenant slug must not contain consecutive hyphens; '{normalised}' does.")
                {
                    Code = "workspace.slug.invalid",
                };
            }
        }

        return new TenantSlug(normalised);
    }

    // A hand-written scan rather than a regular expression: the grammar is three
    // character ranges, so this is clearer at a glance, allocates nothing, and
    // presents no backtracking surface.
    private static bool IsAllowedCharacter(char character) =>
        character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or Hyphen;

    public override string ToString() => Value;
}
