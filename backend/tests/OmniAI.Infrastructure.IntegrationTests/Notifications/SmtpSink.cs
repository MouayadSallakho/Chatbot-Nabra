using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OmniAI.Infrastructure.IntegrationTests.Notifications;

/// <summary>How the sink answers the conversation.</summary>
internal enum SmtpSinkMode
{
    /// <summary>Accepts every message.</summary>
    Accept = 1,

    /// <summary>Refuses every recipient with a 550.</summary>
    RejectRecipient = 2,

    /// <summary>Takes the whole message, then drops the connection instead of answering.</summary>
    DropAfterData = 3,
}

/// <summary>
/// A deterministic SMTP server on the loopback interface, for tests.
/// </summary>
/// <remarks>
/// Just enough of RFC 5321 for MailKit to hand a message over: a greeting, EHLO,
/// MAIL, RCPT, DATA and QUIT. It records every connection, every envelope
/// recipient and every raw message, so a test can prove exactly what reached the
/// wire — and, by counting connections, that nothing did. Nothing leaves the
/// machine and nothing is written to disk.
/// </remarks>
internal sealed class SmtpSink : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<SinkMessage> _messages = [];
    private readonly Task _accepting;
    private int _connections;

    public SmtpSink(SmtpSinkMode mode = SmtpSinkMode.Accept)
    {
        Mode = mode;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public SmtpSinkMode Mode { get; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Connections => Volatile.Read(ref _connections);

    public IReadOnlyList<SinkMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();

        try
        {
            await _accepting;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }

        _stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(_stopping.Token);

            Interlocked.Increment(ref _connections);

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
            await using StreamWriter writer = new(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

            List<string> recipients = [];
            string? from = null;

            await writer.WriteLineAsync("220 sink.local ESMTP test sink");

            while (await reader.ReadLineAsync() is string line)
            {
                string verb = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();

                switch (verb)
                {
                    case "EHLO":
                        await writer.WriteLineAsync("250-sink.local");
                        await writer.WriteLineAsync("250 8BITMIME");
                        break;

                    case "HELO":
                        await writer.WriteLineAsync("250 sink.local");
                        break;

                    case "MAIL":
                        from = line;
                        await writer.WriteLineAsync("250 OK");
                        break;

                    case "RCPT":
                        if (Mode == SmtpSinkMode.RejectRecipient)
                        {
                            await writer.WriteLineAsync("550 No such user");
                        }
                        else
                        {
                            recipients.Add(line);
                            await writer.WriteLineAsync("250 OK");
                        }

                        break;

                    case "DATA":
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");

                        StringBuilder data = new();

                        while (await reader.ReadLineAsync() is string content && content != ".")
                        {
                            data.Append(content).Append("\r\n");
                        }

                        if (Mode == SmtpSinkMode.DropAfterData)
                        {
                            // The message arrived; the answer never does.
                            return;
                        }

                        lock (_messages)
                        {
                            _messages.Add(new SinkMessage(from ?? string.Empty, [.. recipients], data.ToString()));
                        }

                        await writer.WriteLineAsync("250 OK queued");
                        break;

                    case "RSET":
                    case "NOOP":
                        await writer.WriteLineAsync("250 OK");
                        break;

                    case "QUIT":
                        await writer.WriteLineAsync("221 Bye");
                        return;

                    default:
                        await writer.WriteLineAsync("502 Not implemented");
                        break;
                }
            }
        }
    }
}

/// <summary>One message as it crossed the wire.</summary>
internal sealed record SinkMessage(string MailFrom, IReadOnlyList<string> Recipients, string Raw)
{
    /// <summary>The header block: everything before the first blank line.</summary>
    public string Headers => Raw[..Math.Max(0, Raw.IndexOf("\r\n\r\n", StringComparison.Ordinal))];
}
