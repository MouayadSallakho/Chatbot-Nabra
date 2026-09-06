using OmniAI.Domain.Invitations;

namespace OmniAI.Application.Invitations;

/// <summary>
/// Minting and recognising invitation secrets.
/// </summary>
/// <remarks>
/// <strong>An interface because the Application must not do cryptography, not
/// because anybody intends to swap it.</strong> Choosing an RNG, a digest and an
/// encoding is a decision about primitives, and primitives live in
/// Infrastructure beside the other implementation choices. What the use cases
/// need to know is narrower and stated here: a token can be created, and a token
/// somebody presents can be turned into the same digest that was stored.
/// <para>
/// <strong>There is deliberately no way to go backwards.</strong> No method takes
/// an invitation and returns its token, because none could: the raw value exists
/// only inside the request that created it. That is not a limitation to work
/// around later -- it is the property that makes a copy of the database useless
/// for joining workspaces.
/// </para>
/// </remarks>
public interface IInvitationTokens
{
    /// <summary>Creates a fresh secret and the digest to store beside it.</summary>
    /// <remarks>
    /// Both come back together because they must correspond, and returning only
    /// the raw value would leave every caller to hash it -- which is how one
    /// caller eventually hashes it differently.
    /// </remarks>
    NewInvitationToken Create();

    /// <summary>
    /// The digest of a token somebody has presented.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the value could not be a token this platform
    /// issued -- wrong length, wrong alphabet, missing. Refusing to hash it means
    /// a malformed guess never reaches the database at all.
    /// </returns>
    InvitationTokenHash? Recognise(string? token);
}

/// <summary>A newly minted invitation secret.</summary>
/// <param name="Token">
/// The raw value. It is returned to the inviter once and never stored, logged or
/// published again.
/// </param>
/// <param name="Hash">What is written to the database in its place.</param>
public sealed record NewInvitationToken(string Token, InvitationTokenHash Hash);
