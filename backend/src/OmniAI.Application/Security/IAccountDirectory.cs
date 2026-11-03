using OmniAI.Domain.Users;

namespace OmniAI.Application.Security;

/// <summary>
/// The few facts about an account that product code is allowed to know.
/// </summary>
/// <remarks>
/// <strong>An account is not a user.</strong> The identity provider owns
/// credentials, confirmation flags, lockout counters and security stamps; none of
/// that is any of the product's business, and none of it appears here. What the
/// product genuinely needs is an address: to show an administrator who is on
/// their team, and to decide whether the person accepting an invitation is the
/// person it was written to.
/// <para>
/// <strong>Why the email is read here rather than from the session.</strong> A
/// signed-in caller arrives with claims, and a claim is a snapshot taken when the
/// session began -- it can be older than the account. Binding an invitation to a
/// stale claim would mean an address changed after signing in still opens
/// invitations written to the old one. So the address is read from the account
/// store at the moment it matters. It is never taken from the request body at
/// all: that would let a caller nominate who they are.
/// </para>
/// <para>
/// <strong>There is no lookup by address, and there is no search.</strong> This
/// platform deliberately publishes no way to ask whether a given email has an
/// account, because that turns every workspace into a directory of everybody who
/// has ever registered. Invitations name addresses precisely so that no such
/// question has to be asked.
/// </para>
/// </remarks>
public interface IAccountDirectory
{
    /// <summary>The address an account signs in with.</summary>
    /// <returns>
    /// <see langword="null"/> when there is no such account, or it has no usable
    /// address.
    /// </returns>
    Task<EmailAddress?> FindEmailAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The addresses of several accounts at once (Phase 37.5b), with the same rule
    /// as <see cref="FindEmailAsync"/>.
    /// </summary>
    /// <returns>
    /// One entry per account that exists and has a usable address; the others are
    /// simply absent.
    /// </returns>
    /// <remarks>One statement for a whole page of rows that each name an account.</remarks>
    Task<IReadOnlyDictionary<UserId, EmailAddress>> FindEmailsAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The language the person reads what the server sends them in (Phase 37.3).
    /// </summary>
    /// <returns>Their choice, or null when they have none or the account is gone.</returns>
    Task<UserLanguage?> FindLanguageAsync(UserId userId, CancellationToken cancellationToken = default);
}
