namespace OmniAI.Domain.AiAgents;

/// <summary>
/// Lifecycle state of an <see cref="AiAgent"/>.
/// </summary>
/// <remarks>
/// Three states, chosen because each answers a question the business already
/// has: is this agent still being written, is it switched on, or has it been
/// switched off.
/// <para>
/// Deliberately absent: <c>Deleted</c> and <c>Archived</c> are retention
/// concerns and a status is the wrong place to record them; <c>Training</c> and
/// <c>Publishing</c> describe asynchronous work that does not exist yet;
/// <c>Error</c> would mix a transient integration failure into a configuration
/// lifecycle; <c>Suspended</c> and <c>Trial</c> are billing concerns that belong
/// to the tenant, not to each of its agents. Inventing any of them now would
/// encode guesses about rules that have not been decided.
/// </para>
/// <para>
/// There is no zero member, so an uninitialised value is detectable rather than
/// silently meaning <see cref="Draft"/>.
/// </para>
/// </remarks>
public enum AiAgentStatus
{
    /// <summary>
    /// Being configured. The agent exists and is valid, but the tenant has not
    /// switched it on, so nothing should route customers to it.
    /// </summary>
    Draft = 1,

    /// <summary>The agent is switched on and available to handle work.</summary>
    Active = 2,

    /// <summary>
    /// Switched off after having been active. The agent and its configuration
    /// still exist and it can be switched on again.
    /// </summary>
    Disabled = 3,
}
