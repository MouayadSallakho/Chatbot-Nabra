using Microsoft.AspNetCore.Mvc;

namespace OmniAI.Api.ErrorHandling;

/// <summary>
/// The codes the API layer itself assigns, and the guarantee that every refusal
/// a client can correct carries one (Phase 37.5a, row 28).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The code is the contract; the detail is not.</strong> A client words a
/// refusal by its <c>code</c>, in the reader's language, and never shows
/// <c>detail</c>. Most codes are named at the throw site, where the rule is
/// known; these are the ones only the HTTP edge can name, plus the two generic
/// codes the guarantee falls back to.
/// </para>
/// <para>
/// <strong>The guarantee.</strong> <see cref="ApplyFallback"/> runs on every
/// problem body this API writes, through <c>CustomizeProblemDetails</c>: the
/// exception handler, <c>Results.Problem</c> and <c>Results.ValidationProblem</c>
/// all go through the same writer. A 400 or 422 that reached it without a code
/// is given <c>validation.failed</c>, a 409 <c>state.conflict</c> and a 412
/// <c>precondition.failed</c>. A specific code is never overwritten, and nothing
/// else about the body changes: the status, the title, the English detail and
/// any <c>errors</c> map stay exactly as they were.
/// </para>
/// <para>
/// Every code here, and every code named at a throw site, is listed in
/// <c>docs/architecture/problem-codes.md</c>, and a test fails when the two
/// disagree.
/// </para>
/// </remarks>
internal static class ProblemCodes
{
    /// <summary>The extension member that carries the code.</summary>
    public const string Member = "code";

    /// <summary>A refusal of the request's content with no more specific code.</summary>
    public const string ValidationFailed = "validation.failed";

    /// <summary>A refusal by the state of the resource with no more specific code.</summary>
    public const string StateConflict = "state.conflict";

    /// <summary>The <c>If-Match</c> tag no longer matches the resource (412).</summary>
    public const string PreconditionFailed = "precondition.failed";

    /// <summary>A conditional write arrived without <c>If-Match</c> (428).</summary>
    public const string PreconditionRequired = "precondition.required";

    /// <summary>The <c>If-Match</c> header could not be read as one entity tag (400).</summary>
    public const string IfMatchInvalid = "precondition.if_match.invalid";

    /// <summary>
    /// Gives a correctable refusal its generic code when nothing more specific was set.
    /// </summary>
    public static void ApplyFallback(ProblemDetails problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        if (problem.Extensions.TryGetValue(Member, out object? existing)
            && existing is string { Length: > 0 })
        {
            return;
        }

        string? fallback = problem.Status switch
        {
            StatusCodes.Status400BadRequest => ValidationFailed,
            StatusCodes.Status422UnprocessableEntity => ValidationFailed,
            StatusCodes.Status409Conflict => StateConflict,
            StatusCodes.Status412PreconditionFailed => PreconditionFailed,
            _ => null,
        };

        if (fallback is not null)
        {
            problem.Extensions[Member] = fallback;
        }
    }
}
