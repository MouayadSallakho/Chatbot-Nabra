using System.Globalization;

namespace OmniAI.Domain.Plans;

/// <summary>
/// How much of something a plan allows, or that it does not say.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Unlimited is absence, never a large number.</strong> Encoding it as
/// <c>-1</c>, <c>999999999</c> or <c>int.MaxValue</c> puts a sentinel into
/// arithmetic that will eventually be compared, summed or shown: a percentage
/// against <c>int.MaxValue</c> is always nought, a progress bar against it is
/// always empty, and somebody eventually reads "999999999" off a screen. Here
/// there is simply no number, and every consumer has to decide what to do about
/// that.
/// </para>
/// <para>
/// <strong>Zero is a real limit, and it is not unlimited.</strong> A plan that
/// allows no WhatsApp connections is expressible and immediately exhausted.
/// </para>
/// </remarks>
public readonly record struct PlanLimit
{
    private PlanLimit(int? maximum) => Maximum = maximum;

    /// <summary>The cap, or <see langword="null"/> when this plan does not cap it.</summary>
    public int? Maximum { get; }

    /// <summary>Whether this plan places no cap on the resource.</summary>
    public bool IsUnlimited => Maximum is null;

    /// <summary>A plan that does not cap the resource at all.</summary>
    public static PlanLimit Unlimited => new(null);

    /// <summary>A finite cap. Zero is allowed and means "none of these".</summary>
    /// <exception cref="DomainValidationException">The cap is negative.</exception>
    public static PlanLimit Of(int maximum) =>
        maximum < 0
            ? throw new DomainValidationException(
                "A plan limit cannot be negative. Leave it unset to mean unlimited.")
            : new PlanLimit(maximum);

    /// <summary>Reads a configured or stored value, where null means unlimited.</summary>
    public static PlanLimit From(int? maximum) =>
        maximum is int value ? Of(value) : Unlimited;

    /// <summary>
    /// Whether one more of this resource is allowed, given how many exist.
    /// </summary>
    /// <remarks>
    /// <c>&gt;=</c> rather than <c>&gt;</c>: at the cap, the next one is refused.
    /// A workspace at 5 of 5 may keep its five and may not have a sixth.
    /// </remarks>
    public bool AllowsAnother(long current) => IsUnlimited || current < Maximum!.Value;

    /// <summary>How many remain, or <see langword="null"/> when unlimited.</summary>
    public long? Remaining(long current) =>
        IsUnlimited ? null : Math.Max(0, Maximum!.Value - current);

    public override string ToString() =>
        IsUnlimited
            ? "unlimited"
            : Maximum!.Value.ToString(CultureInfo.InvariantCulture);
}
