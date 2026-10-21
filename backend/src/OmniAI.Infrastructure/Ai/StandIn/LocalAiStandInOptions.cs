using System.Globalization;
using Microsoft.Extensions.Configuration;
using OmniAI.Domain.Knowledge;
using OmniAI.Infrastructure.Knowledge;

namespace OmniAI.Infrastructure.Ai.StandIn;

/// <summary>
/// The fixed names and the one setting of the local AI stand-in.
/// </summary>
/// <remarks>
/// Everything here is a constant except the reply delay, which exists so the
/// typing state shows in a browser and can be set to zero in tests.
/// </remarks>
internal sealed class LocalAiStandInOptions
{
    /// <summary>Where the delay is read from.</summary>
    public const string SectionName = "LocalAiStandIn";

    /// <summary>The model name its ledger rows carry.</summary>
    public const string Model = "local-stand-in";

    /// <summary>
    /// The embedding profile of the vectors it makes.
    /// </summary>
    /// <remarks>
    /// Its own profile, so retrieval, which compares only vectors of the active
    /// profile, can never mix these with real embeddings, and a document indexed
    /// here reads as needing an index once a real provider is configured.
    /// </remarks>
    public static EmbeddingProfileId ProfileId { get; } =
        EmbeddingProfileId.Create($"dev:standin:{EmbeddingOptions.Dimensions}");

    /// <summary>A customer message containing this fails like an unavailable provider.</summary>
    public const string FailMarker = "[[fail]]";

    /// <summary>A customer message containing this answers <see cref="SlowDelay"/> later.</summary>
    public const string SlowMarker = "[[slow]]";

    /// <summary>The extra wait <see cref="SlowMarker"/> asks for.</summary>
    public static TimeSpan SlowDelay { get; } = TimeSpan.FromSeconds(10);

    public const int DefaultReplyDelayMilliseconds = 1_000;

    /// <summary>How long every reply waits before it is produced.</summary>
    public int ReplyDelayMilliseconds { get; init; } = DefaultReplyDelayMilliseconds;

    public TimeSpan ReplyDelay => TimeSpan.FromMilliseconds(ReplyDelayMilliseconds);

    /// <summary>Reads the delay, refusing a value that is not a sensible wait.</summary>
    public static LocalAiStandInOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? value = configuration[$"{SectionName}:{nameof(ReplyDelayMilliseconds)}"];

        if (string.IsNullOrWhiteSpace(value))
        {
            return new LocalAiStandInOptions();
        }

        if (!int.TryParse(value.Trim(), CultureInfo.InvariantCulture, out int milliseconds)
            || milliseconds is < 0 or > 60_000)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(ReplyDelayMilliseconds)} must be a whole number of "
                + "milliseconds between 0 and 60000.");
        }

        return new LocalAiStandInOptions { ReplyDelayMilliseconds = milliseconds };
    }
}
