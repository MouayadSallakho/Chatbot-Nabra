using Microsoft.AspNetCore.Identity;

namespace OmniAI.Infrastructure.Identity;

/// <summary>
/// A person who can sign in to the admin application.
/// </summary>
/// <remarks>
/// <strong>This is a credential record, not a Domain concept.</strong> It lives
/// in Infrastructure with the rest of the identity provider, and nothing above
/// this layer names it: the Application and the Domain know only
/// <see cref="Domain.Users.UserId"/>, which is a value. That separation is what
/// keeps password hashes, security stamps and lockout counters out of the
/// business model, and it is asserted by guard tests rather than left to
/// discipline.
/// <para>
/// <strong>Keyed by <see cref="Guid"/>, not by an integer.</strong>
/// <c>IdentityUser</c>'s default key is a string holding a GUID, which would have
/// stored every user id as text and made the membership foreign key a
/// <c>varchar</c> pointing at a <c>uuid</c>-shaped platform. Sequential integers
/// were rejected outright: they disclose how many accounts exist and how quickly
/// they are being created, and make one account's id guessable from another's.
/// The ids are allocated as UUIDv7 through <see cref="Domain.Users.UserId.New"/>,
/// so accounts sort by creation time in the index exactly like every other
/// identifier in this platform.
/// </para>
/// <para>
/// <strong>No properties are added.</strong> <c>IdentityUser&lt;Guid&gt;</c>
/// already carries fifteen columns, and Phase 12 needs none beyond them. A
/// created-at timestamp, a display name or a profile field would each be a column
/// nothing reads -- and user profile management is explicitly a later phase, so
/// guessing at its shape now would only produce something to migrate away from.
/// The subclass exists so the type is this platform's own and those fields have
/// somewhere to land when they are actually required.
/// </para>
/// <para>
/// <strong>Phase 37.3 added the first one: <see cref="Language"/>.</strong> It is
/// the only preference the server holds (D22), so it is a column here rather than
/// a preferences table.
/// </para>
/// </remarks>
public sealed class OmniAiUser : IdentityUser<Guid>
{
    /// <summary>
    /// <c>en</c>, <c>ar</c>, or null when the person has not chosen. What the server
    /// writes to them is in this language, and in English while it is null.
    /// </summary>
    /// <remarks>
    /// Kept as the stored code, like every other column on this type; the
    /// Application reads it as <see cref="Domain.Users.UserLanguage"/> through
    /// <see cref="IdentityAccountDirectory"/>.
    /// </remarks>
    public string? Language { get; set; }

    /// <summary>
    /// The name colleagues see beside the email, or null (Phase 39.1, note 7).
    /// </summary>
    /// <remarks>
    /// Kept as the stored text, like <see cref="Language"/>; written only through
    /// <see cref="Domain.Users.MemberDisplayName"/>, which trims, collapses and
    /// bounds it. One per account, edited by its owner.
    /// </remarks>
    public string? DisplayName { get; set; }
}
