using System.Net.Sockets;
using StackExchange.Redis;
using OmniAI.Infrastructure.WebChat.Protection;

namespace OmniAI.Infrastructure.IntegrationTests.WebChat;

/// <summary>
/// Phase 34: which failures of the protection store refuse a public write as
/// "protection unavailable" (503) rather than escaping as a server error.
/// </summary>
/// <remarks>
/// Found in the Redis-pause drill: with the store accepting TCP but never
/// answering, the script call threw a bare <see cref="IOException"/> over a
/// <see cref="SocketException"/> and the visitor received 500. The request was
/// still refused before anything was written (fail-closed held); the contract that
/// an unusable store is a 503 did not.
/// </remarks>
public sealed class ProtectionStoreFailureTests
{
    public static TheoryData<Exception> StoreFailures => new()
    {
        new IOException("Unable to read data from the transport connection", new SocketException((int)SocketError.OperationAborted)),
        new RedisException("server error"),
        new TimeoutException(),
        new SocketException((int)SocketError.ConnectionRefused),
        new ObjectDisposedException("multiplexer"),
    };

    [Theory]
    [MemberData(nameof(StoreFailures))]
    public void A_store_that_cannot_answer_is_protection_unavailable(Exception failure) =>
        Assert.True(RedisPublicChatProtection.IsProtectionStoreFailure(failure));

    [Fact]
    public void A_cancellation_nobody_asked_for_is_the_store_failing_but_an_abandoned_request_is_not()
    {
        // Seen in the same drill: the client cancels in-flight commands when it
        // abandons a stalled connection.
        Assert.True(RedisPublicChatProtection.IsProtectionStoreFailure(new TaskCanceledException(), CancellationToken.None));

        using CancellationTokenSource abandoned = new();
        abandoned.Cancel();

        Assert.False(RedisPublicChatProtection.IsProtectionStoreFailure(new TaskCanceledException(), abandoned.Token));
        Assert.False(RedisPublicChatProtection.IsProtectionStoreFailure(new OperationCanceledException(abandoned.Token), abandoned.Token));
    }

    [Fact]
    public void A_bug_in_this_code_is_not_dressed_up_as_an_outage()
    {
        Assert.False(RedisPublicChatProtection.IsProtectionStoreFailure(new InvalidOperationException()));
        Assert.False(RedisPublicChatProtection.IsProtectionStoreFailure(new ArgumentException("bad key")));
        Assert.False(RedisPublicChatProtection.IsProtectionStoreFailure(new FormatException()));
    }
}
