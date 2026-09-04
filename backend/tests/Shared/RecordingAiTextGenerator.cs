using OmniAI.Application.Ai;

namespace OmniAI.Testing;

/// <summary>
/// A deterministic stand-in for an AI text generator.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than produced by a mocking framework, for the same reason
/// as the repository fakes: the contract has one method, this is shorter than the
/// setup a framework would need, and it can record the things that actually
/// matter -- what the request contained, how many times it was called, and which
/// cancellation token arrived. No mocking package is added to the solution.
/// </para>
/// <para>
/// <strong>It is never registered by the composition root.</strong> Production
/// resolves <c>UnconfiguredAiTextGenerator</c>, which refuses every request. This
/// type exists only inside test hosts, and the one API fixture that installs it
/// says so explicitly -- so nothing this returns can reach a real caller, and the
/// application cannot accidentally ship a component that invents answers.
/// </para>
/// <para>
/// It is deterministic on purpose: the same request always produces the same
/// reply, and no timing, randomness or network is involved. A test that asserted
/// on a real model's output would be asserting on something nobody controls.
/// </para>
/// <para>
/// Shared by the Application and API test projects through a linked compile item.
/// </para>
/// </remarks>
internal sealed class RecordingAiTextGenerator : IAiTextGenerator
{
    private readonly List<AiTextGenerationRequest> _requests = [];

    /// <summary>The reply every call returns.</summary>
    /// <remarks>
    /// Setting this to blank models a provider that answers with nothing usable:
    /// <c>AiTextGenerationResult.Create</c> refuses it, which is exactly the
    /// boundary under test. That is why this fake does not special-case emptiness
    /// itself -- doing so would test the fake instead of the contract.
    /// </remarks>
    public string Reply { get; set; } = "I can certainly help you choose.";

    /// <summary>When true, every call reports that no AI runtime is configured.</summary>
    public bool PretendUnavailable { get; set; }

    /// <summary>
    /// When true, every call returns <see langword="null"/> instead of a result.
    /// </summary>
    /// <remarks>
    /// The one contract breach no return type can prevent. It stands in for an
    /// adapter that returns nothing at all, and drives the handler's own guard.
    /// </remarks>
    public bool PretendNullResult { get; set; }

    /// <summary>How many times the generator was asked to produce anything.</summary>
    /// <remarks>
    /// The assertion that matters for tenant isolation: a cross-tenant request
    /// must leave this at zero, which proves the agent's instructions were never
    /// assembled into a request, let alone handed to something that would send
    /// them somewhere.
    /// </remarks>
    public int Calls { get; private set; }

    /// <summary>Every request received, in order.</summary>
    public IReadOnlyList<AiTextGenerationRequest> Requests => _requests;

    /// <summary>The most recent request, or null if there was none.</summary>
    public AiTextGenerationRequest? LastRequest => _requests.Count == 0 ? null : _requests[^1];

    /// <summary>The cancellation token the last call was given.</summary>
    /// <remarks>
    /// Recorded so a test can prove the caller's token reached the provider rather
    /// than being dropped and replaced with <c>default</c> -- the failure that
    /// would leave a network call running after the caller had given up.
    /// </remarks>
    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>Returns the generator to its initial state.</summary>
    /// <remarks>
    /// Needed because an API class fixture is shared by every test in its class,
    /// so a call count or a simulated failure left behind by one test would leak
    /// into the next. Each test resets first, which is what keeps them
    /// independent of the order they happen to run in.
    /// </remarks>
    public void Reset()
    {
        _requests.Clear();
        Calls = 0;
        LastCancellationToken = default;
        Reply = "I can certainly help you choose.";
        PretendUnavailable = false;
        PretendNullResult = false;
    }

    public Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastCancellationToken = cancellationToken;

        if (request is not null)
        {
            _requests.Add(request);
        }

        // Observed rather than ignored, so a cancelled caller is answered the way
        // a real network adapter would answer.
        cancellationToken.ThrowIfCancellationRequested();

        if (PretendUnavailable)
        {
            throw new AiRuntimeUnavailableException();
        }

        return Task.FromResult(PretendNullResult ? null! : AiTextGenerationResult.Create(Reply));
    }
}
