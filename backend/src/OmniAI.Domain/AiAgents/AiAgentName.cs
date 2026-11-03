namespace OmniAI.Domain.AiAgents;

/// <summary>
/// The business-facing display name of an AI agent, for example
/// <c>Sales Assistant</c> or <c>Customer Support</c>.
/// </summary>
/// <remarks>
/// A validated type rather than a bare <see cref="string"/>, so the "non-blank,
/// trimmed, bounded" rule is enforced once at construction instead of at every
/// call site that happens to remember it.
/// <para>
/// <b>Uniqueness is not a rule here, and none was invented.</b> Two tenants may
/// each have a <c>Sales Assistant</c>, and nothing currently forbids one tenant
/// from having two agents with the same display name. That is deliberate: no
/// product requirement asks for uniqueness, a name is a label rather than an
/// identifier -- <see cref="AiAgentId"/> is the identifier -- and a uniqueness
/// rule invented now would have to be guessed at (global? per tenant?
/// case-insensitive?) and would then be expensive to relax. The Domain also
/// could not enforce it: knowing whether a name is taken requires knowledge of
/// every stored agent, which is the same reasoning recorded for
/// <c>TenantSlug</c>.
/// </para>
/// </remarks>
public sealed record AiAgentName
{
    /// <summary>
    /// Maximum length, in UTF-16 code units, measured after trimming.
    /// </summary>
    /// <remarks>
    /// Shorter than <c>TenantName</c>'s 200 on purpose, because the two are
    /// different kinds of name. A tenant name is a real legal or trading name the
    /// business already has, including long non-Latin forms and corporate
    /// suffixes. An agent name is a short label the tenant invents to tell its own
    /// agents apart, and it will appear in lists, pickers and tabs where anything
    /// long is truncated by the interface anyway. 120 sits far above any realistic
    /// label -- <c>Customer Support Assistant</c> is 26 -- while keeping display
    /// and storage cost bounded.
    /// </remarks>
    public const int MaxLength = 120;

    private AiAgentName(string value) => Value = value;

    /// <summary>The normalised name.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and normalises a display name. Surrounding whitespace is
    /// trimmed; nothing else is altered, so a name that breaks a rule is rejected
    /// rather than quietly repaired.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// The value is null, blank, longer than <see cref="MaxLength"/>, or contains
    /// a control character.
    /// </exception>
    public static AiAgentName Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("AI agent name is required.")
            {
                Code = "ai-agents.name.required",
            };
        }

        string normalised = value.Trim();

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"AI agent name must be at most {MaxLength} characters; " +
                $"the supplied name has {normalised.Length}. It is not truncated.")
            {
                Code = "ai-agents.name.too_long",
            };
        }

        // Control characters serve no purpose in a display name -- including line
        // breaks and tabs, which a single-line label has no use for -- and would
        // corrupt lists, logs and rendered output. This is stricter than
        // AgentInstructions on purpose: interior formatting is meaningful there
        // and meaningless here.
        foreach (char character in normalised)
        {
            if (char.IsControl(character))
            {
                throw new DomainValidationException(
                    "AI agent name must not contain control characters.")
                {
                    Code = "ai-agents.name.control_characters",
                };
            }
        }

        return new AiAgentName(normalised);
    }

    public override string ToString() => Value;
}
