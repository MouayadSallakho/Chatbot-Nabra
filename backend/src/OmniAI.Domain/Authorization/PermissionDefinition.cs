namespace OmniAI.Domain.Authorization;

/// <summary>
/// One capability in the platform catalogue: its code, and enough description
/// for a person to decide whether to grant it.
/// </summary>
/// <param name="Code">
/// The security contract. This, and only this, is compared when a request is
/// authorised.
/// </param>
/// <param name="Group">
/// The product area this capability belongs to. Used to group the checkboxes in
/// the role editor so a person reads eight related permissions rather than one
/// undifferentiated list. It carries no authorisation meaning: two permissions in
/// the same group imply nothing about each other.
/// </param>
/// <param name="Label">
/// The short name a person sees, for example <c>Edit AI agents</c>.
/// </param>
/// <param name="Description">
/// One sentence saying what somebody holding this permission can actually do.
/// Written for the person granting it, not for a developer -- a role editor that
/// shows only codes makes an administrator guess, and guessing about permissions
/// is how too much gets granted.
/// </param>
/// <remarks>
/// <strong>The metadata is deliberately not part of any decision.</strong>
/// Authorisation compares <paramref name="Code"/> values; nothing reads the
/// label, the description or the group to decide anything. That separation is
/// what makes it safe for this text to change -- rewording a description can
/// never alter who may do what.
/// <para>
/// <strong>The text lives in the Domain because the capability does.</strong>
/// "What can be done in this product" is a business statement, and the sentence
/// describing it belongs beside the code rather than in a parallel table that
/// somebody has to remember to update. The cost is that these strings are
/// English: when this platform is localised, the catalogue supplies the key and
/// the presentation layer supplies the wording. That is recorded as a deferred
/// concern rather than solved speculatively.
/// </para>
/// </remarks>
public sealed record PermissionDefinition(
    PermissionCode Code,
    string Group,
    string Label,
    string Description);
