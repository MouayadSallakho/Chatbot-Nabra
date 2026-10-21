using System.Globalization;
using System.Text;
using OmniAI.Application.Ai;
using OmniAI.Application.Ai.Usage;
using OmniAI.Domain.Ai;

namespace OmniAI.Infrastructure.Ai.StandIn;

/// <summary>
/// The local AI stand-in's text generator (Phase 37.2).
/// </summary>
/// <remarks>
/// <para>
/// <strong>In process, no network, no key.</strong> It opens no socket and reads
/// no credential. It is composed only on a Development host that asked for it
/// (<see cref="LocalAiStandInSwitch"/>).
/// </para>
/// <para>
/// <strong>Nobody can mistake it for an answer.</strong> Every reply starts with
/// "Local test reply:" or «رد تجريبي محلي:», in the language of the customer's
/// latest message, says that no AI model wrote it, quotes that message and names
/// the knowledge passages it was given. The same request always gets the same
/// reply.
/// </para>
/// <para>
/// <strong>Failures on demand.</strong> A latest message containing
/// <see cref="LocalAiStandInOptions.FailMarker"/> fails exactly like an
/// unavailable provider: the ledger row is settled as failed and
/// <see cref="AiRuntimeUnavailableException"/> is thrown.
/// <see cref="LocalAiStandInOptions.SlowMarker"/> adds
/// <see cref="LocalAiStandInOptions.SlowDelay"/> to the usual delay.
/// </para>
/// <para>
/// <strong>Accounted like any provider call</strong>, as
/// <see cref="AiProvider.DevelopmentStandIn"/> with model
/// <see cref="LocalAiStandInOptions.Model"/>. The counts it reports are word
/// counts, not tokens, and the built-in zero rate prices them at nothing.
/// </para>
/// </remarks>
internal sealed class LocalStandInTextGenerator(
    LocalAiStandInOptions options,
    IAiProviderUsageLedger ledger,
    TimeProvider clock)
    : IAiTextGenerator
{
    /// <summary>At most this many passages are named in a reply.</summary>
    internal const int NamedPassages = 3;

    private const int QuoteLength = 160;

    private const int PassageQuoteLength = 100;

    private readonly LocalAiStandInOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    private readonly IAiProviderUsageLedger _ledger =
        ledger ?? throw new ArgumentNullException(nameof(ledger));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        AiUsageContext usage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(usage);

        cancellationToken.ThrowIfCancellationRequested();

        string latest = LatestCustomerMessage(request);

        AiProviderUsageId operation = await _ledger
            .BeginAsync(
                usage,
                AiProvider.DevelopmentStandIn,
                AiOperationType.TextGeneration,
                ModelName.Create(LocalAiStandInOptions.Model),
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            TimeSpan delay = DelayFor(latest);

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Nothing was produced and nobody was paid, so this one is settled as
            // failed rather than unknown: unlike a real provider, the stand-in
            // knows it did no work.
            await _ledger.FailAsync(operation, CancellationToken.None).ConfigureAwait(false);

            throw;
        }

        if (latest.Contains(LocalAiStandInOptions.FailMarker, StringComparison.OrdinalIgnoreCase))
        {
            await _ledger.FailAsync(operation, CancellationToken.None).ConfigureAwait(false);

            throw new AiRuntimeUnavailableException();
        }

        string reply = Compose(latest, request.Grounding);

        int inputWords = InputWords(request);
        int outputWords = LocalStandInText.CountWords(reply);

        await _ledger
            .SucceedAsync(
                operation,
                ProviderTokenUsage.Create(
                    inputTokens: inputWords,
                    outputTokens: outputWords,
                    totalTokens: inputWords + outputWords),
                ModelName.Create(LocalAiStandInOptions.Model),
                CancellationToken.None)
            .ConfigureAwait(false);

        return AiTextGenerationResult.Create(reply);
    }

    /// <summary>How long a request with this latest message waits.</summary>
    internal TimeSpan DelayFor(string latest) =>
        latest.Contains(LocalAiStandInOptions.SlowMarker, StringComparison.OrdinalIgnoreCase)
            ? _options.ReplyDelay + LocalAiStandInOptions.SlowDelay
            : _options.ReplyDelay;

    /// <summary>The reply for this message and these passages.</summary>
    internal static string Compose(string latest, IReadOnlyList<GroundingItem> grounding)
    {
        bool arabic = LocalStandInText.IsArabic(latest);

        StringBuilder reply = new();

        reply.AppendLine(arabic
            ? "رد تجريبي محلي: كتب هذا الرد بديلٌ محلي للاختبار، وليس نموذج ذكاء اصطناعي."
            : "Local test reply: this answer was written by a local test stand-in, not by an AI model.");

        reply.AppendLine(arabic
            ? $"رسالتك: «{Quote(latest, QuoteLength)}»"
            : $"Your message: \"{Quote(latest, QuoteLength)}\"");

        if (grounding.Count == 0)
        {
            reply.Append(arabic
                ? "لا يوجد في المعرفة مقطع يطابق رسالتك."
                : "No knowledge passage matched your message.");

            return reply.ToString();
        }

        reply.AppendLine(arabic
            ? string.Create(CultureInfo.InvariantCulture, $"مقاطع من المعرفة تطابق رسالتك ({grounding.Count}):")
            : string.Create(CultureInfo.InvariantCulture, $"Knowledge passages that match your message ({grounding.Count}):"));

        int named = Math.Min(grounding.Count, NamedPassages);

        for (int index = 0; index < named; index++)
        {
            GroundingItem item = grounding[index];

            string line = arabic
                ? string.Create(CultureInfo.InvariantCulture, $"{index + 1}. {item.Title}: «{Quote(item.Text, PassageQuoteLength)}»")
                : string.Create(CultureInfo.InvariantCulture, $"{index + 1}. {item.Title}: \"{Quote(item.Text, PassageQuoteLength)}\"");

            if (index < named - 1 || grounding.Count > named)
            {
                reply.AppendLine(line);
            }
            else
            {
                reply.Append(line);
            }
        }

        if (grounding.Count > named)
        {
            int more = grounding.Count - named;

            reply.Append(arabic
                ? string.Create(CultureInfo.InvariantCulture, $"ومقاطع أخرى عددها {more}.")
                : string.Create(CultureInfo.InvariantCulture, $"And {more} more."));
        }

        return reply.ToString();
    }

    /// <summary>The customer's latest message, which the markers are read from.</summary>
    private static string LatestCustomerMessage(AiTextGenerationRequest request)
    {
        for (int index = request.Turns.Count - 1; index >= 0; index--)
        {
            if (request.Turns[index].Role == AiTurnRole.User)
            {
                return request.Turns[index].Text;
            }
        }

        return request.Turns[^1].Text;
    }

    private static int InputWords(AiTextGenerationRequest request) =>
        LocalStandInText.CountWords(request.Instructions)
        + request.Turns.Sum(turn => LocalStandInText.CountWords(turn.Text))
        + request.Grounding.Sum(item =>
            LocalStandInText.CountWords(item.Title) + LocalStandInText.CountWords(item.Text));

    /// <summary>One line of at most <paramref name="length"/> characters.</summary>
    private static string Quote(string text, int length)
    {
        string single = string.Join(' ', text.Split(
            (char[])['\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (single.Length <= length)
        {
            return single;
        }

        int cut = length;

        // Never between the two halves of a surrogate pair.
        if (char.IsHighSurrogate(single[cut - 1]))
        {
            cut--;
        }

        return single[..cut].TrimEnd() + "…";
    }
}
