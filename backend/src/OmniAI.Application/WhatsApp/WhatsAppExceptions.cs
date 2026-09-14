using OmniAI.Application.Concurrency;

namespace OmniAI.Application.WhatsApp;

/// <summary>
/// A WhatsApp connection was changed between being read and being written.
/// </summary>
public sealed class WhatsAppConnectionVersionMismatchException(ConcurrencyToken expected)
    : Exception(
        "This WhatsApp connection was changed somewhere else after it was read. "
        + "Reload it and try again.")
{
    /// <summary>The version the caller presented.</summary>
    public ConcurrencyToken Expected { get; } = expected;
}

/// <summary>
/// A phone number id is already connected somewhere on this deployment.
/// </summary>
/// <remarks>
/// <strong>Deployment-wide, not workspace-wide, and the message says so
/// carefully.</strong> Telling an administrator that another workspace has
/// claimed the number would disclose something about a workspace they have no
/// relationship with. What they are told is that the number is already
/// connected — which is true, actionable, and reveals nothing.
/// </remarks>
public sealed class WhatsAppPhoneNumberInUseException()
    : Exception(
        "That phone number is already connected. A WhatsApp number can belong to "
        + "one connection at a time, because an incoming message has to reach "
        + "exactly one workspace.");

/// <summary>
/// A conversation was asked to send to WhatsApp and did not come from there.
/// </summary>
public sealed class ConversationNotOnWhatsAppException()
    : Exception("This conversation did not come from WhatsApp.");
