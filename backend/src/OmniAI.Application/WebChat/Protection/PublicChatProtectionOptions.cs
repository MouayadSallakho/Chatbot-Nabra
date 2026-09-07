using System.Globalization;

namespace OmniAI.Application.WebChat.Protection;

/// <summary>
/// One counter's worth of policy: how many, over how long.
/// </summary>
/// <param name="Limit">The most actions allowed inside one window.</param>
/// <param name="Window">How long a window lasts.</param>
/// <remarks>
/// A pair rather than two loose numbers, so a limit can never be read with
/// somebody else's window. Every dimension of the protection policy is one of
/// these, which is also what lets the counter store take a list of them and
/// evaluate them together without knowing what any of them mean.
/// </remarks>
public sealed record RateLimitWindow(int Limit, TimeSpan Window)
{
    /// <summary>Validates one window, naming the setting that is wrong.</summary>
    /// <exception cref="InvalidOperationException">The limit or window is unusable.</exception>
    public void Validate(string name)
    {
        if (Limit < 1)
        {
            throw new InvalidOperationException(
                $"{PublicChatProtectionOptions.SectionName}:{name} must allow at least one "
                + $"request; {Limit.ToString(CultureInfo.InvariantCulture)} was configured. A "
                + "limit of zero would switch public chat off, which is what disabling the "
                + "endpoint is for.");
        }

        if (Window <= TimeSpan.Zero || Window > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException(
                $"{PublicChatProtectionOptions.SectionName}:{name} must use a window longer "
                + "than nothing and no longer than a day.");
        }
    }
}

/// <summary>
/// Everything this platform will let an anonymous stranger consume.
/// </summary>
/// <remarks>
/// <strong>Configuration, not constants, and validated at startup.</strong> These
/// numbers are the difference between a chat page that works and one that is a
/// billing incident, and they will need tuning per deployment long before there
/// is any notion of a plan. Scattering them through handlers would mean tuning
/// them meant a release; leaving them unvalidated would mean a mistyped window
/// silently ran at whatever this code happened to prefer.
/// <para>
/// <strong>Every value is non-secret.</strong> They belong in
/// <c>appsettings</c>, an environment variable or an orchestrator's config map,
/// and they are deliberately not exposed through any API — a public caller
/// learning the quotas learns exactly how to sit under them, and an administrator
/// has no screen for them yet because plan-aware quotas are a later phase.
/// </para>
/// <para>
/// <strong>The defaults are conservative and chosen for a support chat.</strong>
/// A real person asks a handful of questions over a few minutes. Every default
/// here is several times more generous than that and still far below what an
/// automated abuser needs to be worth the effort. They are written as a small
/// number of layered windows rather than one clever limit, because the threats
/// are genuinely different shapes: one machine hammering, a botnet spreading
/// itself thin, and a single legitimate-looking session running a script.
/// </para>
/// </remarks>
public sealed class PublicChatProtectionOptions
{
    /// <summary>Configuration section holding all of this.</summary>
    public const string SectionName = "PublicChatProtection";

    /// <summary>
    /// New conversations one network may start at one chat endpoint.
    /// </summary>
    /// <remarks>
    /// Five in five minutes. A person who reloads, changes their mind, or opens
    /// the chat on two tabs stays well inside it; a script creating conversations
    /// in a loop does not. Keyed per endpoint as well as per network so one
    /// business's visitors cannot exhaust another business's allowance.
    /// </remarks>
    public RateLimitWindow ConversationStartsPerClient { get; init; } =
        new(5, TimeSpan.FromMinutes(5));

    /// <summary>
    /// New conversations one chat endpoint will accept from everybody together.
    /// </summary>
    /// <remarks>
    /// <strong>The answer to a botnet.</strong> Every per-network limit is
    /// bypassed by using more networks, which is cheap; this one is not, because
    /// it does not care where the traffic comes from. Sixty in five minutes is
    /// far more than a small business's website produces and is the ceiling on how
    /// fast anybody can manufacture conversation rows in one workspace.
    /// <para>
    /// It is a real trade-off and worth naming: a genuinely popular site could
    /// reach this, and its visitors would be told to try again shortly. That is
    /// the correct failure while the alternative is an unbounded one, and the
    /// number is configuration precisely so a busy deployment can raise it
    /// deliberately.
    /// </para>
    /// </remarks>
    public RateLimitWindow ConversationStartsPerEndpoint { get; init; } =
        new(60, TimeSpan.FromMinutes(5));

    /// <summary>
    /// Public write requests one network may make, whatever they are.
    /// </summary>
    /// <remarks>
    /// The cheap outer boundary, checked before anything is looked up and before
    /// any credential is believed. It exists to stop a flood of requests that are
    /// going to be refused anyway from costing a database round trip each. Thirty
    /// a minute is more than any person types.
    /// </remarks>
    public RateLimitWindow RequestsPerClient { get; init; } = new(30, TimeSpan.FromMinutes(1));

    /// <summary>
    /// Messages one conversation may send, however valid its credential.
    /// </summary>
    /// <remarks>
    /// <strong>A legitimate session is still a rate limit.</strong> Somebody who
    /// opens the chat once and then drives it from a script has a perfectly good
    /// credential, and every message they send is a paid provider call. Ten a
    /// minute is a fast conversation and a slow attack.
    /// </remarks>
    public RateLimitWindow MessagesPerConversation { get; init; } =
        new(10, TimeSpan.FromMinutes(1));

    /// <summary>
    /// New AI generation attempts one workspace will make in a burst.
    /// </summary>
    /// <remarks>
    /// The short edge of the workspace safety budget: it caps how fast money can
    /// be spent, whoever is spending it and however many conversations they
    /// spread themselves across.
    /// </remarks>
    public RateLimitWindow GenerationsPerWorkspaceBurst { get; init; } =
        new(30, TimeSpan.FromMinutes(1));

    /// <summary>
    /// New AI generation attempts one workspace will make in an hour.
    /// </summary>
    /// <remarks>
    /// The sustained edge. A burst limit alone permits burst-after-burst forever;
    /// this is what makes a slow, patient attacker bounded too.
    /// </remarks>
    public RateLimitWindow GenerationsPerWorkspaceSustained { get; init; } =
        new(600, TimeSpan.FromHours(1));

    /// <summary>
    /// New AI generation attempts one workspace will make in a day.
    /// </summary>
    /// <remarks>
    /// The hard safety ceiling, and the one an operator would actually notice on
    /// an invoice. Five thousand attempts is a busy real day for a single support
    /// chat and an unmistakable anomaly for a quiet one.
    /// </remarks>
    public RateLimitWindow GenerationsPerWorkspaceDaily { get; init; } =
        new(5_000, TimeSpan.FromHours(24));

    /// <summary>
    /// How long an anonymous visitor's credential opens their conversation.
    /// </summary>
    /// <remarks>
    /// <strong>The one setting here that is not a counter, and it belongs with
    /// them.</strong> Both answer the same question — how much may an anonymous
    /// stranger consume, and for how long — and splitting them across two
    /// sections would mean an operator tightening abuse policy in one place and
    /// leaving credential lifetime in another.
    /// <para>
    /// A day: long enough that somebody who leaves the tab open over lunch, or
    /// comes back after a meeting, is still in their conversation; short enough
    /// that a credential copied from a shared machine is worthless by tomorrow.
    /// It is absolute and never extended by activity — see
    /// <c>WebChatSession</c> for why sliding expiry would defeat the purpose.
    /// </para>
    /// </remarks>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Prefix every protection counter's name begins with.
    /// </summary>
    /// <remarks>
    /// Carries a schema version, so a future change to what a counter means can
    /// start counting afresh rather than inheriting values with the old meaning.
    /// Also what lets parallel test classes share one Redis without colliding.
    /// </remarks>
    public string KeyPrefix { get; init; } = "omniai:pcp:v1";

    /// <summary>
    /// Checks the whole policy, naming whichever setting is wrong.
    /// </summary>
    /// <remarks>
    /// At startup rather than on the first public request, so a mistyped limit is
    /// a deployment failure an operator sees rather than a rate limit a customer
    /// discovers. The relationships between the three workspace windows are
    /// checked as well as each one on its own: a daily ceiling below the hourly
    /// allowance is not a stricter policy, it is a policy where two settings
    /// disagree and only one of them can be doing anything.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The policy is not usable.</exception>
    public void Validate()
    {
        ConversationStartsPerClient.Validate(nameof(ConversationStartsPerClient));
        ConversationStartsPerEndpoint.Validate(nameof(ConversationStartsPerEndpoint));
        RequestsPerClient.Validate(nameof(RequestsPerClient));
        MessagesPerConversation.Validate(nameof(MessagesPerConversation));
        GenerationsPerWorkspaceBurst.Validate(nameof(GenerationsPerWorkspaceBurst));
        GenerationsPerWorkspaceSustained.Validate(nameof(GenerationsPerWorkspaceSustained));
        GenerationsPerWorkspaceDaily.Validate(nameof(GenerationsPerWorkspaceDaily));

        RequireAscending(
            nameof(GenerationsPerWorkspaceBurst),
            GenerationsPerWorkspaceBurst,
            nameof(GenerationsPerWorkspaceSustained),
            GenerationsPerWorkspaceSustained);

        RequireAscending(
            nameof(GenerationsPerWorkspaceSustained),
            GenerationsPerWorkspaceSustained,
            nameof(GenerationsPerWorkspaceDaily),
            GenerationsPerWorkspaceDaily);

        if (SessionLifetime <= TimeSpan.Zero || SessionLifetime > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SessionLifetime)} must be longer than nothing and no "
                + "longer than thirty days. An anonymous credential that outlives a month is "
                + "not a session.");
        }

        if (string.IsNullOrWhiteSpace(KeyPrefix))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(KeyPrefix)} cannot be blank.");
        }
    }

    /// <summary>
    /// Insists a wider window is at least as permissive as the narrower one it
    /// contains.
    /// </summary>
    private static void RequireAscending(
        string narrowerName,
        RateLimitWindow narrower,
        string widerName,
        RateLimitWindow wider)
    {
        if (wider.Window <= narrower.Window)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{widerName} must cover a longer window than {narrowerName}.");
        }

        if (wider.Limit < narrower.Limit)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{widerName} allows fewer attempts than {narrowerName} does over "
                + "a shorter window, so the narrower limit could never be reached. One of the "
                + "two is not the limit it appears to be.");
        }
    }
}
