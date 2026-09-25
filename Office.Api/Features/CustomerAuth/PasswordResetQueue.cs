using System.Threading.Channels;

namespace Office.Api.Features.CustomerAuth;

/// <summary>
/// "Forgot password" requests waiting for PasswordResetWorker. The endpoint only drops the
/// email in here and answers at once — whether the account exists, the database work and the
/// email all happen later, so neither the answer nor its timing tells an account apart.
/// In memory on purpose: the plain token is created in the worker and never persisted
/// anywhere (a Hangfire job's arguments would be). A restart loses queued requests; the
/// мизоҷ simply asks again. Bounded — a flood is dropped, not buffered without limit.
/// </summary>
public class PasswordResetQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <returns>False when the queue is full and the request was dropped.</returns>
    public bool TryEnqueue(string normalizedEmail) => _channel.Writer.TryWrite(normalizedEmail);

    public ChannelReader<string> Reader => _channel.Reader;
}
