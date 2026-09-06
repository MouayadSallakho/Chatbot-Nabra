namespace OmniAI.Application.Security;

/// <summary>
/// Raised when a use case that requires a signed-in person is reached without one.
/// </summary>
/// <remarks>
/// <strong>This should be unreachable over HTTP, and that is why it exists.</strong>
/// Every protected endpoint is already behind authorization, so an anonymous
/// request is refused by the pipeline long before a handler runs. This type is
/// the second layer: if an endpoint were ever mapped without its authorization
/// requirement -- a one-line mistake in a route group that no compiler would
/// catch -- the use case still refuses rather than executing for nobody.
/// <para>
/// <strong>It is deliberately distinct from "you are not a member".</strong> The
/// two answer different questions and must produce different statuses. Not
/// authenticated is <c>401</c>: the caller has not proved who they are, and doing
/// so may change the outcome. Not a member is <c>404</c>: the caller has proved
/// who they are, and for them that resource does not exist. Collapsing the first
/// into the second would tell an unauthenticated client that its session is fine
/// and the resource is gone, which is the opposite of what it needs to do next.
/// </para>
/// <para>
/// Transport-neutral by construction: no status code, no <c>ProblemDetails</c>,
/// no HTTP vocabulary. The API decides that this means <c>401</c>. It carries no
/// identifier, no claim and no session detail -- there is nothing here to leak.
/// </para>
/// </remarks>
public sealed class NotAuthenticatedException : Exception
{
    /// <summary>The single message this type ever carries.</summary>
    /// <remarks>
    /// Fixed and content-free on purpose. It reaches a client as the
    /// <c>detail</c> of a <c>401</c>, so it must say what to do and nothing about
    /// what was attempted.
    /// </remarks>
    private const string DefaultMessage = "This operation requires a signed-in user.";

    public NotAuthenticatedException()
        : base(DefaultMessage)
    {
    }

    public NotAuthenticatedException(string message)
        : base(message)
    {
    }

    public NotAuthenticatedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
