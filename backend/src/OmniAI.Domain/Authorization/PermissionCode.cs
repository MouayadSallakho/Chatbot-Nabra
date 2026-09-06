namespace OmniAI.Domain.Authorization;

/// <summary>
/// The name of one capability this platform recognises, for example
/// <c>ai-agents.edit</c>.
/// </summary>
/// <remarks>
/// <strong>This is the security contract.</strong> Everything else about a
/// permission -- its label, its description, the group it is shown under -- is
/// there so a person can understand what they are granting. Only this value is
/// ever compared when a request is authorised, and nothing in the authorisation
/// path reads anything else.
/// <para>
/// <strong>A string, not an integer.</strong> An integer permission id would make
/// the meaning of stored data depend on a declaration order that no database row
/// records: renumbering, reordering or inserting a member would silently
/// reinterpret every grant already written. A code is self-describing -- a row
/// saying <c>ai-agents.edit</c> means the same thing in five years, in a backup,
/// and in a log line, without anything else to consult.
/// </para>
/// <para>
/// <strong>Independent of HTTP.</strong> No code is derived from a route, a
/// method, an endpoint name or a handler type, and nothing is discovered by
/// reflection. A permission is a product capability; a route is one way to reach
/// it. Tying the two together would mean that renaming a URL silently changed who
/// could do what, and that a capability reachable from two routes needed two
/// permissions.
/// </para>
/// <para>
/// <strong>Shape is validated here; recognition is not.</strong> This type
/// answers "is this a well-formed permission code". Whether the platform actually
/// has such a capability is a different question with a different answer source --
/// <see cref="PlatformPermissions"/> -- and merging them would make it impossible
/// to read a code out of the database at all once a capability was retired.
/// </para>
/// <para>
/// A readonly record struct over a canonicalised string, following the same
/// design as the identifier value types: a private constructor, so the only way
/// to obtain one is through a factory that has already validated it.
/// </para>
/// </remarks>
public readonly record struct PermissionCode : IComparable<PermissionCode>
{
    /// <summary>
    /// Longest permitted code.
    /// </summary>
    /// <remarks>
    /// Comfortably above anything the catalogue holds -- the longest today is
    /// <c>ai-agents.lifecycle</c> at 19 -- while keeping the stored column
    /// bounded. It is a limit on a machine-readable identifier rather than on
    /// prose, so a generous ceiling costs nothing and a value approaching it is
    /// almost certainly a mistake.
    /// </remarks>
    public const int MaxLength = 64;

    private const char GroupSeparator = '.';
    private const char Hyphen = '-';

    private PermissionCode(string value) => Value = value;

    /// <summary>The canonical, lowercase code.</summary>
    public string Value { get; }

    /// <summary>True for the uninitialised <c>default</c> value.</summary>
    /// <remarks>
    /// C# always permits <c>default(PermissionCode)</c> for a struct, and its
    /// value is null. Code that accepts one from outside should reject an empty
    /// one rather than let it reach a comparison, where it would match another
    /// default rather than any real grant.
    /// </remarks>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>
    /// Validates and canonicalises a permission code.
    /// </summary>
    /// <remarks>
    /// The grammar is deliberately narrow: two or more segments separated by a
    /// single dot, each segment made of lowercase letters, digits and interior
    /// hyphens. Surrounding whitespace is trimmed and the value is lowercased,
    /// because case is not meaningful in an identifier and <c>Roles.Manage</c>
    /// and <c>roles.manage</c> must never become two different grants. Nothing
    /// else is repaired -- a code containing a space, an underscore or an
    /// accented letter is rejected rather than rewritten into a neighbouring
    /// capability.
    /// </remarks>
    /// <exception cref="DomainValidationException">
    /// The value is null, blank, too long, or breaks the grammar.
    /// </exception>
    public static PermissionCode Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("Permission code is required.");
        }

        // ToLowerInvariant, not ToLower: the result must not depend on the
        // machine's culture, and this solution runs with invariant globalisation.
        string normalised = value.Trim().ToLowerInvariant();

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"Permission code must be at most {MaxLength} characters; " +
                $"the supplied code has {normalised.Length}. It is not truncated.");
        }

        if (normalised[0] is GroupSeparator or Hyphen
            || normalised[^1] is GroupSeparator or Hyphen)
        {
            throw new DomainValidationException(
                $"Permission code must not start or end with '.' or '-'; '{normalised}' does.");
        }

        int separators = 0;

        for (int index = 0; index < normalised.Length; index++)
        {
            char character = normalised[index];

            if (character == GroupSeparator)
            {
                separators++;
            }
            else if (!IsAllowedCharacter(character))
            {
                throw new DomainValidationException(
                    "Permission code may contain only lowercase letters, digits, hyphens and " +
                    $"dots; '{normalised}' contains '{character}'.");
            }

            if (index > 0 && IsSeparator(character) && IsSeparator(normalised[index - 1]))
            {
                throw new DomainValidationException(
                    $"Permission code must not contain adjacent '.' or '-' characters; " +
                    $"'{normalised}' does.");
            }
        }

        if (separators == 0)
        {
            throw new DomainValidationException(
                "Permission code must name a group and an action separated by a dot, " +
                $"for example 'ai-agents.edit'; '{normalised}' does not.");
        }

        return new PermissionCode(normalised);
    }

    /// <summary>
    /// Reads a permission code without throwing.
    /// </summary>
    /// <remarks>
    /// For the boundaries that receive a code from outside and must answer with a
    /// refusal rather than an exception -- a client that sent something malformed
    /// gets the same answer as a client that sent something unrecognised, which
    /// is what stops the two being told apart.
    /// </remarks>
    public static bool TryCreate(string? value, out PermissionCode code)
    {
        try
        {
            code = Create(value);

            return true;
        }
        catch (DomainValidationException)
        {
            code = default;

            return false;
        }
    }

    /// <summary>
    /// Orders codes by their canonical value.
    /// </summary>
    /// <remarks>
    /// Ordinal, so the order does not depend on the machine's culture. It exists
    /// because every list of permissions this platform publishes is ordered:
    /// a set with no order would make responses differ between requests that
    /// granted exactly the same thing, which is noise in a diff and in a test.
    /// </remarks>
    public int CompareTo(PermissionCode other) =>
        string.CompareOrdinal(Value, other.Value);

    public static bool operator <(PermissionCode left, PermissionCode right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(PermissionCode left, PermissionCode right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(PermissionCode left, PermissionCode right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(PermissionCode left, PermissionCode right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() => Value;

    private static bool IsSeparator(char character) =>
        character is GroupSeparator or Hyphen;

    // A hand-written scan rather than a regular expression: the grammar is three
    // character ranges, so this is clearer at a glance, allocates nothing, and
    // presents no backtracking surface. It matches the approach TenantSlug takes
    // for the same reason.
    private static bool IsAllowedCharacter(char character) =>
        character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or Hyphen;
}
