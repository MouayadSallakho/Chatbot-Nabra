using Microsoft.Extensions.Configuration;
using OmniAI.Infrastructure.Ai.OpenAi;

namespace OmniAI.Infrastructure.Ai.StandIn;

/// <summary>
/// Whether this process runs the local AI stand-in (Phase 37.2).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Opt-in, Development only, and decided at run time.</strong> The dev API
/// runs a Release publish, so a compile-time symbol could not be the switch.
/// <see cref="Variable"/> must be exactly <see cref="EnabledValue"/>, and the host
/// must be Development; anything else composes today's providers unchanged.
/// </para>
/// <para>
/// <strong>Refused, never ignored.</strong> The variable set outside Development,
/// set to anything but <see cref="EnabledValue"/>, or set alongside the live
/// OpenAI opt-in stops the process at startup with a message naming the variable.
/// Quietly ignoring any of those would leave somebody believing they are testing
/// one thing while the process does another.
/// </para>
/// <para>
/// The variable is a switch, not a secret: it carries no credential, and the
/// provider canary keeps passing with it present.
/// </para>
/// </remarks>
public static class LocalAiStandInSwitch
{
    /// <summary>The environment variable that asks for the stand-in.</summary>
    public const string Variable = "OMNIAI_DEV_AI_STANDIN";

    /// <summary>The only value that turns it on.</summary>
    public const string EnabledValue = "1";

    /// <summary>
    /// Reads the switch, refusing every combination that must not start.
    /// </summary>
    /// <param name="configuration">The process configuration.</param>
    /// <param name="development">Whether the host environment is Development.</param>
    /// <returns><see langword="true"/> when the stand-in is to be composed.</returns>
    /// <exception cref="InvalidOperationException">
    /// The variable is set outside Development, set to another value, or set
    /// together with the live OpenAI opt-in.
    /// </exception>
    public static bool IsEnabled(IConfiguration configuration, bool development)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? value = configuration[Variable];

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!development)
        {
            throw new InvalidOperationException(
                $"{Variable} is set, but this is not a Development environment. The local AI "
                + "stand-in exists only for developer machines and never answers customers "
                + "of a real deployment. Remove the variable to start.");
        }

        if (!string.Equals(value, EnabledValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Variable} must be exactly {EnabledValue} to turn the local AI stand-in on. "
                + "Remove it to start without the stand-in.");
        }

        if (string.Equals(
                configuration[OpenAiOptions.LiveProviderOptInVariable],
                OpenAiOptions.LiveProviderOptInValue,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{Variable} and {OpenAiOptions.LiveProviderOptInVariable} are both set. A "
                + "process uses either the local AI stand-in or the live provider, never both; "
                + "remove one of them.");
        }

        return true;
    }
}
