using System.Net;
using System.Text;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// An HTTP handler that answers OpenAI calls from a script instead of a network.
/// </summary>
/// <remarks>
/// <para>
/// The test seam for the whole OpenAI adapter. It is deliberately placed at the
/// <em>bottom</em> of the stack rather than at the top: the real
/// <c>ResponsesClient</c>, the real pipeline, the real request serialisation and
/// the real response deserialisation all run, and only the socket is replaced.
/// </para>
/// <para>
/// That matters. A test that substituted <c>IAiTextGenerator</c> would prove
/// nothing about the adapter, and a test that substituted the client would prove
/// nothing about what this platform actually puts on the wire. Here the assertion
/// can be made against the exact JSON OpenAI would have received — which is the
/// only way to be sure instructions are not concatenated, that <c>store</c> is
/// false, and that no tool ever appears.
/// </para>
/// <para>
/// <strong>No network, no account, no cost.</strong> Every test using this runs
/// offline with a dummy credential.
/// </para>
/// </remarks>
internal sealed class CannedOpenAiTransport : HttpMessageHandler
{
    private readonly List<string> _bodies = [];

    /// <summary>The HTTP status to answer with.</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>The body to answer with.</summary>
    public string ResponseBody { get; set; } = OpenAiCannedResponses.Completed("Hello there.");

    /// <summary>
    /// When set, the handler throws this instead of answering.
    /// </summary>
    /// <remarks>
    /// Used to reproduce a transport-level timeout, which arrives as a
    /// <c>TaskCanceledException</c> that did not come from the caller's token.
    /// </remarks>
    public Func<Exception>? ThrowInstead { get; set; }

    /// <summary>
    /// When true, the handler waits on the caller's token before answering.
    /// </summary>
    /// <remarks>
    /// Proves the caller's cancellation actually reaches the socket rather than
    /// being dropped somewhere in the pipeline.
    /// </remarks>
    public bool ObserveCancellation { get; set; }

    /// <summary>How many HTTP calls were made.</summary>
    /// <remarks>
    /// The assertion that proves retries are off: a failing status must produce
    /// exactly one call, not the four the SDK would make by default.
    /// </remarks>
    public int Calls { get; private set; }

    /// <summary>Every request body sent, in order.</summary>
    public IReadOnlyList<string> Bodies => _bodies;

    /// <summary>The most recent request body.</summary>
    public string LastBody => _bodies.Count == 0 ? string.Empty : _bodies[^1];

    /// <summary>Whether the last request carried an Authorization header.</summary>
    /// <remarks>
    /// Presence only. The value is never read, never stored and never printed.
    /// </remarks>
    public bool SentAuthorizationHeader { get; private set; }

    /// <summary>The request URI of the last call.</summary>
    public Uri? LastRequestUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Calls++;
        LastRequestUri = request.RequestUri;
        SentAuthorizationHeader = request.Headers.Authorization is not null
            || request.Headers.Contains("Authorization");

        if (request.Content is not null)
        {
            _bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        if (ObserveCancellation)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        if (ThrowInstead is not null)
        {
            throw ThrowInstead();
        }

        return new HttpResponseMessage(Status)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
        };
    }
}
