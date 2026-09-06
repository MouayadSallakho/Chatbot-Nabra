using OmniAI.Application.Ai;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.PreviewAiAgent;

/// <summary>
/// Tries an AI agent out with one message, without changing or storing anything.
/// </summary>
/// <remarks>
/// <para>
/// The first use case in the platform that reaches an AI runtime, and it exists to
/// prove the shape of that path end to end: a stored agent's configuration, a
/// provider-independent request, a capability contract, a reply. Every provider
/// integration will arrive behind the same contract this handler already calls.
/// </para>
/// <para>
/// <strong>Read-only.</strong> No aggregate method is invoked, no
/// <c>UpdateAsync</c> is called, and there is no <c>TimeProvider</c> dependency
/// because nothing here has a timestamp to stamp. A preview cannot change
/// <c>updatedAt</c>, cannot change an agent's version, and therefore needs no
/// precondition -- there is nothing for a conditional request to protect.
/// </para>
/// <para>
/// <strong>Stateless.</strong> No conversation is created, no message is stored,
/// and neither the question nor the answer is written anywhere. A preview is
/// somebody testing a configuration, not a customer interaction, and recording it
/// would put staff experiments into the same history as real conversations while
/// silently accumulating other people's text.
/// </para>
/// </remarks>
public sealed class PreviewAiAgentHandler(
    IAiAgentRepository agents,
    TenantAccessGuard access,
    IAiTextGenerator generator)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAccessGuard _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly IAiTextGenerator _generator =
        generator ?? throw new ArgumentNullException(nameof(generator));

    /// <summary>Generates a reply from the agent's stored instructions.</summary>
    /// <returns>
    /// The generated reply, or <see langword="null"/> when the caller does not
    /// belong to this tenant, or when this tenant has no agent with that id --
    /// including when the agent exists under a different tenant. All three are
    /// indistinguishable to the caller, deliberately.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>The order of these steps is the security property.</strong> Access
    /// is resolved before any instruction text is read, before a request is
    /// assembled, and before the generator is reached -- first by membership, then
    /// by the tenant-scoped lookup. A caller naming another tenant's agent gets
    /// not-found without a single character of that agent's configuration being
    /// loaded into a request, so a cross-tenant preview cannot become a way to
    /// extract somebody's instructions through a model's answer, and cannot spend
    /// this platform's AI budget on their data.
    /// </para>
    /// <para>
    /// Input validation runs first, before the lookup, matching every other handler
    /// in this slice. It discloses nothing: a malformed message is refused
    /// identically whether the agent exists, belongs to somebody else, or was never
    /// real, so the ordering cannot be used to probe for agents.
    /// </para>
    /// <para>
    /// <strong>Status is not consulted.</strong> A draft, an active and a disabled
    /// agent all preview. See the architecture note on why lifecycle state governs
    /// live operation rather than administrative testing.
    /// </para>
    /// </remarks>
    /// <exception cref="Domain.DomainValidationException">An identifier is empty.</exception>
    /// <exception cref="AiTurnValidationException">The message is unusable.</exception>
    /// <exception cref="AiRuntimeUnavailableException">No AI runtime is configured.</exception>
    /// <exception cref="AiResponseInvalidException">The runtime answered with nothing usable.</exception>
    public async Task<AiAgentPreview?> HandleAsync(
        PreviewAiAgentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentId agentId = AiAgentId.From(command.AiAgentId);

        // Validated here, where a person can be told what is wrong with what they
        // typed, rather than by a provider rejecting it later in its own words.
        AiTurn question = AiTurn.FromUser(command.Message);

        // Membership before anything that costs money. This endpoint is the one
        // place in the platform where a request reaches a paid third party, so a
        // caller outside the tenant must be stopped here -- not merely denied the
        // answer afterwards. Nothing below this line runs for a non-member: the
        // agent is never read, its instructions are never loaded, and the
        // generator is never called, so somebody else's workspace cannot be used
        // to spend this deployment's provider budget or to have its instructions
        // inferred from a reply.
        if (!await _access.IsCurrentUserMemberAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        VersionedAiAgent? stored = await _agents
            .FindAsync(tenantId, agentId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            // Nothing has been sent anywhere. The generator has not been touched,
            // and no instructions exist in this request to send.
            return null;
        }

        // The instructions come from the agent that was just proven to belong to
        // this tenant, and from nowhere else. The command has no field that could
        // contribute to them.
        AiTextGenerationRequest request = AiTextGenerationRequest.Create(
            stored.Agent.Instructions.Value,
            [question]);

        AiTextGenerationResult result = await _generator
            .GenerateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        // The result type refuses blank text at construction, so a badly behaved
        // adapter cannot build one. It could still return null, which no contract
        // can prevent, and a null dereference here would surface as an unexplained
        // fault rather than as the upstream breach it is.
        if (result is null)
        {
            throw new AiResponseInvalidException();
        }

        return new AiAgentPreview(result.Text);
    }
}
