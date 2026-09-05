namespace OmniAI.Infrastructure.Ai.OpenAi;

/// <summary>
/// Non-secret configuration for the OpenAI adapter.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a deployment choice rather than a business fact, which is
/// why none of it reaches the Domain, the Application or the HTTP contract. An
/// agent is what the business configured it to be; which model answers on its
/// behalf, how long the platform waits and how much output it will pay for are
/// decisions the operator makes.
/// </para>
/// <para>
/// <strong>The API key is deliberately not here.</strong> It is read once at
/// registration from the <c>OPENAI_API_KEY</c> environment variable and handed
/// straight to the client. Binding it into an options object would put it in a
/// type that is easy to log, easy to serialise into diagnostics, and easy to
/// dump wholesale into a configuration endpoint. Keeping the secret out of the
/// settings shape means a future <c>appsettings.json</c> cannot accidentally
/// carry one either.
/// </para>
/// </remarks>
internal sealed class OpenAiOptions
{
    /// <summary>Configuration section these values are bound from.</summary>
    public const string SectionName = "OpenAI";

    /// <summary>Environment variable holding the API key. Never bound into this type.</summary>
    public const string ApiKeyVariable = "OPENAI_API_KEY";

    /// <summary>
    /// The model that answers preview requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A deployment setting, overridable through <c>OpenAI:Model</c>. It is
    /// deliberately not a property of <c>AiAgent</c>, of the generation request or
    /// of the preview API contract: per-agent model selection is a product feature
    /// with its own design, and putting a vendor's catalogue into the Domain would
    /// make one supplier's naming part of this platform's business vocabulary.
    /// </para>
    /// <para>
    /// The default is the low-cost model this phase was asked to use for preview
    /// and smoke traffic, because preview is staff testing a configuration rather
    /// than customer-facing work.
    /// </para>
    /// </remarks>
    public string Model { get; set; } = DefaultModel;

    /// <summary>
    /// Upper bound on the tokens one preview answer may generate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A cost and runaway-output safety bound, not a model context limit
    /// and not a Domain rule.</strong> It says nothing about what the model can
    /// read or produce; it says what this platform is willing to pay for in a
    /// single preview.
    /// </para>
    /// <para>
    /// 800 tokens is roughly 600 words — several paragraphs, comfortably more
    /// than any customer-service reply needs, and far short of the runaway
    /// generation that turns one stuck request into a real bill. Preview is also
    /// the most exposed AI surface the platform has: it is unauthenticated today,
    /// so a bound that keeps a single call cheap matters more here than anywhere
    /// else.
    /// </para>
    /// <para>
    /// A response cut off at this bound still returns whatever was generated. The
    /// alternative — refusing a long answer outright — would spend the money and
    /// then throw the result away.
    /// </para>
    /// </remarks>
    public int MaxOutputTokens { get; set; } = DefaultMaxOutputTokens;

    /// <summary>
    /// How long to wait for the provider before giving up, in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 30 seconds. Preview is interactive: somebody is watching a spinner, and a
    /// wait longer than this is indistinguishable from a hang. It is generous
    /// enough for a normal completion at the configured output bound and short
    /// enough that a stalled upstream does not hold a request thread, and the
    /// person waiting, indefinitely.
    /// </para>
    /// <para>
    /// Explicitly set rather than left to a default, because an unbounded wait is
    /// how a slow dependency becomes an outage: requests accumulate faster than
    /// they retire until nothing is served at all.
    /// </para>
    /// <para>
    /// This is the transport's own timeout. It is separate from the caller's
    /// cancellation token, which still propagates and still wins — a caller that
    /// gives up after two seconds is not made to wait thirty.
    /// </para>
    /// </remarks>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>The model used when nothing overrides it.</summary>
    public const string DefaultModel = "gpt-5.6-luna";

    /// <summary>The output bound used when nothing overrides it.</summary>
    public const int DefaultMaxOutputTokens = 800;

    /// <summary>The provider timeout used when nothing overrides it.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>The configured timeout as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>
    /// Throws if the configured values could not produce a working call.
    /// </summary>
    /// <remarks>
    /// Checked at registration rather than on the first request, so a mistyped
    /// setting is a startup failure an operator sees immediately instead of a
    /// runtime failure a customer discovers. These are deployment errors, not
    /// caller errors, so they are argument failures rather than one of the AI
    /// runtime types.
    /// </remarks>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(Model)} must name a model.");
        }

        if (MaxOutputTokens <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxOutputTokens)} must be greater than zero.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(TimeoutSeconds)} must be greater than zero.");
        }
    }
}
