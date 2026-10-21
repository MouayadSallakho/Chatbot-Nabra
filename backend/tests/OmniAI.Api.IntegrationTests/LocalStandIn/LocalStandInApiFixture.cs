using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Infrastructure.Ai.StandIn;

namespace OmniAI.Api.IntegrationTests.LocalStandIn;

/// <summary>
/// The hosted application with the local AI stand-in switched on, exactly as
/// the dev stack starts it (Phase 37.2).
/// </summary>
/// <remarks>
/// <strong>Nothing is replaced.</strong> The switch is a setting, so the
/// composition root composes the stand-in itself, and every reply, index and
/// ledger row below goes through the real handlers, the real retriever over
/// pgvector and the real ledger. Only the reply delay is set to zero, so the
/// suite does not wait a second per turn.
/// </remarks>
public sealed class LocalStandInApiFixture : RoleApiFixture
{
    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LocalAiStandInSwitch.Variable] = LocalAiStandInSwitch.EnabledValue,
            ["LocalAiStandIn:ReplyDelayMilliseconds"] = "0",
        };
}
