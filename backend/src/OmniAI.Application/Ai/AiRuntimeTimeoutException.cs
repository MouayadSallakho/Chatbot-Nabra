namespace OmniAI.Application.Ai;

/// <summary>
/// Raised when the AI runtime did not answer in time.
/// </summary>
/// <remarks>
/// <para>
/// Added in the phase that integrated a real provider, because a real provider
/// introduced a state the platform previously could not reach: a request that was
/// accepted, was never refused, and simply did not come back. Phase 8 had no
/// network call, so nothing could be slow.
/// </para>
/// <para>
/// <strong>Distinct from <see cref="AiRuntimeUnavailableException"/> on purpose.</strong>
/// "Unavailable" means the platform could not get the work started — no provider
/// configured, credentials rejected, capacity refused. This means the work
/// probably <em>did</em> start and the answer did not arrive inside the
/// deployment's own patience. A caller that can tell them apart knows whether to
/// try a shorter question or simply to try again; an operator reading logs knows
/// whether to look at configuration or at latency.
/// </para>
/// <para>
/// <strong>It is not caller cancellation.</strong> If the caller gave up, that is
/// an <see cref="OperationCanceledException"/> and never this type — turning
/// somebody closing a browser tab into an upstream fault would make an ordinary
/// event look like an incident, and would corrupt any latency signal built on
/// this.
/// </para>
/// <para>
/// <strong>Nothing retries.</strong> A timeout is the single most tempting thing
/// to retry and the most expensive to retry blindly: the first request may still
/// be running and still being billed, so a second one doubles the cost of a
/// situation that is already going badly. Resilience will be designed once real
/// provider behaviour has been observed.
/// </para>
/// <para>
/// Transport-neutral and provider-neutral by construction: no status code, no
/// <c>ProblemDetails</c>, no vendor exception type, no endpoint, no configured
/// duration. The message reaches clients, so it says what happened and nothing
/// about who or where.
/// </para>
/// </remarks>
public sealed class AiRuntimeTimeoutException : Exception
{
    /// <summary>The single message every occurrence carries.</summary>
    private const string TimedOut =
        "The AI runtime did not respond in time. No answer was produced.";

    public AiRuntimeTimeoutException()
        : base(TimedOut)
    {
    }

    public AiRuntimeTimeoutException(string message)
        : base(message)
    {
    }

    public AiRuntimeTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
