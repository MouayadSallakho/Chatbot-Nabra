using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Security;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Identity;

/// <summary>
/// Reads the one account fact the product is allowed to know: an address.
/// </summary>
/// <remarks>
/// <strong>The Identity boundary in one direction only.</strong> This is where an
/// ASP.NET Core Identity user becomes an
/// <see cref="OmniAI.Domain.Users.EmailAddress"/> and nothing else. No credential,
/// no confirmation flag, no lockout state and no security stamp crosses into the
/// Application, and no Identity type is named in its contract.
/// <para>
/// <strong>Both stored forms are read, and neither is recomputed.</strong>
/// Identity keeps the address as typed and a normalised copy it matches on; the
/// Domain's <c>EmailAddress</c> keeps exactly the same pair. Rebuilding the
/// canonical form here would mean this platform decided who somebody is by a rule
/// the identity provider had not agreed to -- so the stored normalisation is
/// carried across as it stands.
/// </para>
/// <para>
/// <strong>Reads directly, without <c>UserManager</c>.</strong> This asks one
/// question about one row; a <c>UserManager</c> would bring password hashing,
/// lockout policy and token providers into a lookup that needs none of them, and
/// would put a service capable of changing credentials within reach of code that
/// only wants to display an address.
/// </para>
/// </remarks>
internal sealed class IdentityAccountDirectory(OmniAiDbContext context) : IAccountDirectory
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<EmailAddress?> FindEmailAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId.Value)
            .Select(user => new { user.Email, user.NormalizedEmail })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (account?.Email is null)
        {
            return null;
        }

        // An account with no normalised form is one Identity never wrote through
        // its own path. Canonicalising it here would invent a comparison the
        // provider has not made, so it is treated as unusable for matching --
        // which fails an invitation acceptance closed rather than open.
        if (string.IsNullOrEmpty(account.NormalizedEmail))
        {
            return null;
        }

        return EmailAddress.FromStored(account.Email, account.NormalizedEmail);
    }

    public async Task<IReadOnlyDictionary<UserId, EmailAddress>> FindEmailsAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return new Dictionary<UserId, EmailAddress>();
        }

        List<Guid> ids = [.. userIds.Select(id => id.Value).Distinct()];

        var accounts = await _context.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.Email, user.NormalizedEmail })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The same rule as FindEmailAsync: no address, or no normalised form, is
        // treated as no usable address.
        return accounts
            .Where(account => account.Email is not null && !string.IsNullOrEmpty(account.NormalizedEmail))
            .ToDictionary(
                account => UserId.From(account.Id),
                account => EmailAddress.FromStored(account.Email!, account.NormalizedEmail!));
    }

    public async Task<IReadOnlyDictionary<UserId, AccountPerson>> FindPeopleAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return new Dictionary<UserId, AccountPerson>();
        }

        List<Guid> ids = [.. userIds.Select(id => id.Value).Distinct()];

        var accounts = await _context.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.Email, user.NormalizedEmail, user.DisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The same address rule as FindEmailsAsync.
        return accounts
            .Where(account => account.Email is not null && !string.IsNullOrEmpty(account.NormalizedEmail))
            .ToDictionary(
                account => UserId.From(account.Id),
                account => new AccountPerson(
                    EmailAddress.FromStored(account.Email!, account.NormalizedEmail!),
                    account.DisplayName));
    }

    public async Task<UserLanguage?> FindLanguageAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        string? code = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId.Value)
            .Select(user => user.Language)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return UserLanguages.FromStored(code);
    }
}
