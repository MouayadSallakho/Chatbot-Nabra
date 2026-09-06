namespace OmniAI.Domain.Authorization;

/// <summary>
/// The name a workspace gives one of its roles, for example
/// <c>Support agent</c>.
/// </summary>
/// <remarks>
/// A validated type rather than a bare <see cref="string"/>, so the "non-blank,
/// trimmed, bounded, no control characters" rule is enforced once at construction
/// instead of at every call site that happens to remember it.
/// <para>
/// <strong>It carries its own normalised form, and that is what uniqueness is
/// decided on.</strong> A workspace with a role called <c>Support</c> must not
/// also be able to have one called <c>support</c> or <c>SUPPORT</c>: three names
/// that are visually distinct only to a machine would be three sets of
/// permissions somebody has to keep straight, and picking the wrong one is a
/// silent authorisation mistake. <see cref="Normalised"/> is what the database's
/// unique index is built on, so the rule is enforced where concurrent creations
/// meet rather than only where they are checked.
/// </para>
/// <para>
/// <strong>Uniqueness is per tenant, never global.</strong> Two workspaces may
/// each have a <c>Support agent</c> role, and must -- they are different
/// businesses with different rules, and a name one of them takes cannot be
/// allowed to affect the other. The unique index carries <c>tenant_id</c> for
/// exactly that reason.
/// </para>
/// <para>
/// The Domain cannot decide uniqueness itself: knowing whether a name is taken
/// requires knowledge of every stored role in a tenant. That is the same
/// reasoning already recorded for <c>TenantSlug</c>, and it lands the same way --
/// a unique index in PostgreSQL, surfaced through the Application as a conflict.
/// </para>
/// </remarks>
public sealed record TenantRoleName
{
    /// <summary>
    /// Longest permitted name, measured after trimming.
    /// </summary>
    /// <remarks>
    /// Shorter than <c>TenantName</c>'s 200 and shorter than an agent name's 120,
    /// because a role name is the shortest kind of label in the product: it names
    /// a job inside one workspace and is read in a table column and beside a
    /// checkbox group. <c>Customer support supervisor</c> is 29 characters, so 80
    /// sits far above anything realistic while keeping the column and every list
    /// that renders it bounded.
    /// </remarks>
    public const int MaxLength = 80;

    private TenantRoleName(string value, string normalised)
    {
        Value = value;
        Normalised = normalised;
    }

    /// <summary>The name as the workspace typed it, trimmed.</summary>
    /// <remarks>
    /// Case is preserved because it is what a person reads: forcing
    /// <c>Support agent</c> to lowercase in the list would make the product look
    /// broken. Only the comparison is case-insensitive.
    /// </remarks>
    public string Value { get; }

    /// <summary>
    /// The form uniqueness is decided on: upper-invariant.
    /// </summary>
    /// <remarks>
    /// Upper rather than lower, matching the convention ASP.NET Core Identity
    /// already established in this schema for <c>normalized_email</c> and
    /// <c>normalized_user_name</c>. There is no technical difference; agreeing
    /// with the neighbouring columns means nobody has to remember which table
    /// normalises which way.
    /// </remarks>
    public string Normalised { get; }

    /// <summary>
    /// Validates and normalises a role name. Surrounding whitespace is trimmed;
    /// nothing else is altered, so a name that breaks a rule is rejected rather
    /// than quietly repaired.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// The value is null, blank, whitespace-only, longer than
    /// <see cref="MaxLength"/>, or contains a control character.
    /// </exception>
    public static TenantRoleName Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("Role name is required.");
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"Role name must be at most {MaxLength} characters; " +
                $"the supplied name has {trimmed.Length}. It is not truncated.");
        }

        // Control characters serve no purpose in a single-line label -- including
        // line breaks and tabs -- and would corrupt lists, logs and rendered
        // output. The same rule AiAgentName applies, for the same reason.
        foreach (char character in trimmed)
        {
            if (char.IsControl(character))
            {
                throw new DomainValidationException(
                    "Role name must not contain control characters.");
            }
        }

        // ToUpperInvariant, not ToUpper: the normalised form is a storage key and
        // must not depend on the machine's culture. A name normalised under one
        // culture and compared under another would let a duplicate through.
        return new TenantRoleName(trimmed, trimmed.ToUpperInvariant());
    }

    public override string ToString() => Value;
}
