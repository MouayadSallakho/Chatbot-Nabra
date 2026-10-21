using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;

namespace OmniAI.Api.IntegrationTests.Realtime;

/// <summary>
/// A signed-in member's connection to the workspace inbox hub, spoken at the
/// protocol level.
/// </summary>
/// <remarks>
/// <strong>Why not the SignalR client library.</strong> It is a separate package
/// this solution does not reference, and the part of SignalR these tests need is
/// small and documented: a JSON handshake, invocations and completions framed by
/// the ASCII record separator, over the same WebSocket a browser opens. Speaking
/// it directly means nothing between the test and the server is a double.
/// <para>
/// The connection carries the member's session cookie exactly as a browser's
/// would, issued by the real sign-in endpoint of the instance it connects to.
/// </para>
/// </remarks>
internal sealed class InboxHubClient : IAsyncDisposable
{
    private const char RecordSeparator = '\u001e';

    private const string HubPath = "/hubs/workspace-inbox";

    private readonly WebSocket _socket;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<Guid> _changed = Channel.CreateUnbounded<Guid>();
    private readonly Dictionary<string, TaskCompletionSource<string?>> _pending = [];
    private readonly Lock _sync = new();
    private readonly Task _reader;
    private int _nextInvocation;

    private InboxHubClient(WebSocket socket)
    {
        _socket = socket;
        _reader = Task.Run(ReadAsync);
    }

    /// <summary>
    /// Signs an existing account in on this instance and opens its hub connection.
    /// </summary>
    public static async Task<InboxHubClient> SignInAndConnectAsync(
        WebApplicationFactory<Program> instance,
        string email)
    {
        ArgumentNullException.ThrowIfNull(instance);

        CookieContainer cookies = new();

        using HttpClient client = instance.CreateDefaultClient(new CookieContainerHandler(cookies));

        using HttpResponseMessage signedIn =
            await ApiAuthentication.SignInAsync(client, email, ApiAuthentication.Password);

        signedIn.EnsureSuccessStatusCode();

        return await ConnectAsync(instance.Server, cookies.GetCookieHeader(client.BaseAddress!));
    }

    /// <summary>Opens a connection carrying these cookies and completes the handshake.</summary>
    public static async Task<InboxHubClient> ConnectAsync(TestServer server, string cookieHeader)
    {
        ArgumentNullException.ThrowIfNull(server);

        WebSocketClient sockets = server.CreateWebSocketClient();

        sockets.ConfigureRequest = request => request.Headers.Cookie = cookieHeader;

        WebSocket socket = await sockets.ConnectAsync(
            new UriBuilder(server.BaseAddress) { Scheme = "ws", Path = HubPath }.Uri,
            CancellationToken.None);

        await SendFrameAsync(socket, """{"protocol":"json","version":1}""");

        string handshake = await ReceiveFrameAsync(socket, CancellationToken.None);

        using (JsonDocument answer = JsonDocument.Parse(handshake))
        {
            if (answer.RootElement.TryGetProperty("error", out JsonElement error))
            {
                throw new InvalidOperationException($"Hub handshake refused: {error}");
            }
        }

        return new InboxHubClient(socket);
    }

    /// <summary>
    /// Calls a hub method and waits for its completion.
    /// </summary>
    /// <returns>The hub's error message, or <see langword="null"/> when it succeeded.</returns>
    public async Task<string?> InvokeAsync(string target, params object[] arguments)
    {
        string id;
        TaskCompletionSource<string?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_sync)
        {
            id = (++_nextInvocation).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _pending[id] = completion;
        }

        await SendFrameAsync(_socket, JsonSerializer.Serialize(new
        {
            type = 1,
            invocationId = id,
            target,
            arguments,
        }));

        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Waits for <c>conversationChanged</c> naming this conversation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when it arrived within the time allowed. Events for
    /// other conversations are skipped.
    /// </returns>
    public async Task<bool> WaitForChangeAsync(Guid conversationId, TimeSpan within)
    {
        using CancellationTokenSource timeout = new(within);

        try
        {
            while (true)
            {
                Guid changed = await _changed.Reader.ReadAsync(timeout.Token);

                if (changed == conversationId)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    string.Empty,
                    CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // The server may already have gone; the connection is over either way.
        }

        try
        {
            await _reader.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or WebSocketException or TimeoutException)
        {
            // The reader ends with the socket; how it ended is not under test.
        }

        _socket.Dispose();
        _stop.Dispose();
    }

    private async Task ReadAsync()
    {
        while (!_stop.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            string frames = await ReceiveFrameAsync(_socket, _stop.Token);

            foreach (string frame in frames.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                Dispatch(frame);
            }
        }
    }

    private void Dispatch(string frame)
    {
        using JsonDocument message = JsonDocument.Parse(frame);
        JsonElement root = message.RootElement;

        switch (root.GetProperty("type").GetInt32())
        {
            // An invocation from the server.
            case 1 when root.GetProperty("target").GetString() == "conversationChanged":
                _changed.Writer.TryWrite(root.GetProperty("arguments")[0].GetGuid());
                break;

            // The completion of one of ours.
            case 3:
                string id = root.GetProperty("invocationId").GetString() ?? string.Empty;
                string? error = root.TryGetProperty("error", out JsonElement failure)
                    ? failure.GetString()
                    : null;

                lock (_sync)
                {
                    if (_pending.Remove(id, out TaskCompletionSource<string?>? completion))
                    {
                        completion.TrySetResult(error);
                    }
                }

                break;

            // Pings, and anything this client has no use for.
            default:
                break;
        }
    }

    private static Task SendFrameAsync(WebSocket socket, string json) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(json + RecordSeparator),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

    /// <summary>Reads one whole WebSocket message, which may hold several frames.</summary>
    private static async Task<string> ReceiveFrameAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        using MemoryStream message = new();

        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return string.Empty;
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(message.ToArray()).TrimEnd(RecordSeparator);
    }
}
