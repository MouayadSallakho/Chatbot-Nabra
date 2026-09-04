using Microsoft.Extensions.Primitives;
using OmniAI.Application.Concurrency;

namespace OmniAI.Api.AiAgents;

/// <summary>
/// The <c>ETag</c> and <c>If-Match</c> contract for the AI agent endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Deliberately not a conditional-request engine.</strong> There is no
/// middleware, no filter, no <c>If-None-Match</c>, no <c>If-Unmodified-Since</c>,
/// no caching, no content negotiation and no reflection. It reads one header the
/// one way these six endpoints need it read, and every rule below is a decision
/// rather than a subset of RFC 9110 that happened to get implemented.
/// </para>
/// <para>
/// The accepted contract for AI agent mutations:
/// </para>
/// <list type="table">
/// <item>
/// <term>Header absent or empty</term>
/// <description><c>428 Precondition Required</c>. The mutation is not attempted.</description>
/// </item>
/// <item>
/// <term><c>*</c></term>
/// <description>
/// <c>400</c>. A wildcard means "if the resource exists at all", which is exactly
/// the guarantee these endpoints must not accept: it would let an edit computed
/// from a screen opened an hour ago overwrite everything since, which is the
/// lost update this whole mechanism exists to prevent. Refused rather than
/// silently downgraded to an unconditional write.
/// </description>
/// </item>
/// <item>
/// <term><c>W/"..."</c></term>
/// <description>
/// <c>400</c>. A weak tag asserts semantic equivalence, not byte equality, and
/// RFC 9110 does not permit it for <c>If-Match</c>. Two revisions of an agent
/// that differ only in instructions could legitimately share a weak tag, and
/// honouring one here would reintroduce the lost update through the front door.
/// </description>
/// </item>
/// <item>
/// <term>More than one tag</term>
/// <description>
/// <c>400</c>. A list means "any of these will do", which cannot express the
/// exact-version precondition required here. Not supported in this phase.
/// </description>
/// </item>
/// <item>
/// <term>Not a quoted string</term>
/// <description><c>400</c>. It is not an entity tag.</description>
/// </item>
/// <item>
/// <term>A single strong tag</term>
/// <description>
/// Accepted. Its contents are handed on as an opaque token and never inspected
/// here.
/// </description>
/// </item>
/// </list>
/// <para>
/// <strong>A well-formed tag whose contents this platform did not issue is not a
/// <c>400</c>.</strong> It is a syntactically valid precondition that simply does
/// not match, so it fails as a precondition -- <c>412</c> -- decided further in
/// where versions are compared. The distinction is deliberate: <c>400</c> says
/// the request is broken and the client should change how it builds requests;
/// <c>412</c> says the client is out of date and should re-read. Only the second
/// is true of a stale or unrecognised token, and only the second describes an
/// action that recovers.
/// </para>
/// </remarks>
internal static class EntityTags
{
    /// <summary>Formats a version as a strong entity tag.</summary>
    /// <remarks>
    /// Strong, never <c>W/</c>: the value identifies one exact stored revision,
    /// which is the only thing a lost-update precondition can be built on.
    /// </remarks>
    public static string Format(ConcurrencyToken version) => $"\"{version.Value}\"";

    /// <summary>
    /// Reads the <c>If-Match</c> precondition from a mutation request.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="expected">The version the caller says it is changing.</param>
    /// <returns>
    /// <see langword="null"/> when the header is usable; otherwise the Problem
    /// Details response to send instead of performing the mutation.
    /// </returns>
    public static IResult? ReadIfMatch(HttpRequest request, out ConcurrencyToken expected)
    {
        expected = default;

        StringValues header = request.Headers.IfMatch;

        // Absent entirely. Not an error in the request's construction -- it is a
        // required precondition the caller has not supplied, which is what 428
        // exists to say. Answered before the agent is looked up, so it reveals
        // nothing about whether the agent exists.
        if (header.Count == 0)
        {
            return PreconditionRequired();
        }

        // More than one header line is a list, whatever the individual values are.
        if (header.Count > 1)
        {
            return Malformed("Exactly one If-Match entity tag is required; a list was supplied.");
        }

        string? value = header[0]?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return PreconditionRequired();
        }

        if (value == "*")
        {
            return Malformed(
                "If-Match: * is not accepted for this operation. It would permit an update " +
                "based on any earlier version of the agent, which is what this precondition " +
                "exists to prevent. Supply the exact entity tag the agent was read at.");
        }

        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            return Malformed(
                "A weak entity tag is not accepted for this operation. Supply the strong " +
                "entity tag exactly as it was returned, including the surrounding quotes.");
        }

        // A comma inside a single header line is still a list.
        if (value.Contains(',', StringComparison.Ordinal))
        {
            return Malformed("Exactly one If-Match entity tag is required; a list was supplied.");
        }

        if (value.Length < 2
            || !value.StartsWith('"')
            || !value.EndsWith('"'))
        {
            return Malformed(
                "If-Match must be a quoted entity tag, exactly as it was returned in the " +
                "ETag response header.");
        }

        string opaque = value[1..^1];

        if (opaque.Length == 0 || opaque.Contains('"', StringComparison.Ordinal))
        {
            return Malformed("If-Match must be a quoted entity tag with a non-empty value.");
        }

        expected = ConcurrencyToken.From(opaque);

        return null;
    }

    /// <summary>
    /// <c>428 Precondition Required</c>: the caller must say which version it is
    /// changing.
    /// </summary>
    /// <remarks>
    /// Not <c>400</c>, and not a silent unconditional write. The request is
    /// perfectly well-formed; what is missing is the caller's statement of what it
    /// believed it was editing, and without that the server cannot tell a
    /// deliberate change from one computed against state that has since moved on.
    /// The detail explains the fix rather than only naming the fault.
    /// </remarks>
    private static IResult PreconditionRequired() =>
        Results.Problem(
            title: "Precondition required",
            detail:
                "This operation requires an If-Match header carrying the entity tag from the " +
                "ETag response header of the agent you read. It prevents an edit based on an " +
                "out-of-date view from silently overwriting somebody else's change.",
            statusCode: StatusCodes.Status428PreconditionRequired);

    private static IResult Malformed(string detail) =>
        Results.Problem(
            title: "Invalid If-Match header",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}
