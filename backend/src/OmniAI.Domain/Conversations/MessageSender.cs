namespace OmniAI.Domain.Conversations;

/// <summary>
/// Who produced a message.
/// </summary>
/// <remarks>
/// This describes authorship at business level; it is not a foreign key and does
/// not imply that an AI agent or human agent entity exists. Which specific agent
/// or human wrote a message is a separate concern that arrives with those
/// aggregates. No provider-specific sender types appear here.
/// There is no zero member, so an uninitialised value is detectable.
/// </remarks>
public enum MessageSender
{
    /// <summary>The end customer talking to the business.</summary>
    Customer = 1,

    /// <summary>The platform's AI answering on the tenant's behalf.</summary>
    AiAgent = 2,

    /// <summary>A human working for the tenant.</summary>
    HumanAgent = 3,

    /// <summary>The platform itself, recording something that happened.</summary>
    System = 4,
}
