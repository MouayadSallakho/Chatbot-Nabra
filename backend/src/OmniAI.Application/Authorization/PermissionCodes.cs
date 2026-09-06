using OmniAI.Domain.Authorization;

namespace OmniAI.Application.Authorization;

/// <summary>
/// Turns the permission codes a client sent into codes this platform will act on.
/// </summary>
/// <remarks>
/// <strong>One place, so the rules cannot diverge between the create and the
/// replace.</strong> Both operations take a list of strings from a request body,
/// and both must apply exactly the same reading of it: null means an empty set, a
/// malformed code and an unrecognised code are equally refused, and the result is
/// ordered and deduplicated. Two implementations of that would eventually be two
/// different contracts on two endpoints editing the same thing.
/// <para>
/// <strong>Recognition is not checked here.</strong> This converts strings into
/// well-formed codes; whether the platform has such a capability is decided by
/// <see cref="TenantRole"/> when the set is applied, so an unrecognised code
/// cannot become a stored grant however it arrived. Checking it twice would put
/// the catalogue rule in two places and invite them to disagree; checking it only
/// here would leave the aggregate willing to grant anything it was handed.
/// </para>
/// <para>
/// Both failures surface as the same <c>DomainValidationException</c> and
/// therefore the same <c>400</c>. That is deliberate: a client that sent
/// <c>ai-agents.edti</c> and one that sent <c>billing.manage</c> have both sent
/// something this platform will not grant, and distinguishing them would let a
/// caller enumerate the catalogue by probing -- which the catalogue endpoint
/// already publishes to anyone entitled to see it, so the distinction would buy
/// nothing and cost a disclosure rule.
/// </para>
/// </remarks>
internal static class PermissionCodes
{
    /// <summary>
    /// Reads a request's permission list.
    /// </summary>
    /// <param name="codes">
    /// The codes as the client sent them. <see langword="null"/> is read as an
    /// empty set rather than refused -- a role that grants nothing is a legitimate
    /// thing to ask for, and a client omitting the field means exactly that.
    /// </param>
    /// <remarks>
    /// <strong>Duplicates are deduplicated, not rejected.</strong> A list naming
    /// the same capability twice expresses the same wish as naming it once; there
    /// is no reading under which it means something else, so refusing it would be
    /// pedantry a client has to write code to avoid. The stored result is
    /// identical either way, and the database's composite key guarantees it. This
    /// is the contract, and it is stated so a client can rely on it rather than
    /// discover it.
    /// </remarks>
    /// <exception cref="Domain.DomainValidationException">
    /// A code is blank or breaks the permission-code grammar. Raised by
    /// <see cref="PermissionCode.Create"/>, not re-implemented here.
    /// </exception>
    public static IReadOnlyList<PermissionCode> Read(IReadOnlyList<string?>? codes) =>
        codes is null
            ? []
            : [.. codes.Select(PermissionCode.Create).Distinct().Order()];
}
