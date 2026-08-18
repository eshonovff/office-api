using System.Collections.Concurrent;

namespace Office.Api.Channels.Meta;

public interface IOAuthNonceTracker
{
    /// <summary>True агар nonce бори аввал бошад (истифода иҷозат аст); false агар такрор (replay).</summary>
    bool TryConsume(string nonce, DateTimeOffset expiresAt, DateTimeOffset now);
}

/// <summary>
/// Ҳимояи якмаротибагии state — лои дуюм дар паҳлӯи <see cref="OAuthStateCodec"/> (он танҳо
/// имзо ва мӯҳлатро месанҷад, аммо бе ҳолат ду маротиба validate шудани як state-и то ҳол
/// эътиборнокро блок карда наметавонад). Дар хотира: nonce-ҳо худашон кӯтоҳмуддатанд.
/// </summary>
public sealed class OAuthNonceTracker : IOAuthNonceTracker
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);

    public bool TryConsume(string nonce, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        PruneExpired(now);
        return _consumed.TryAdd(nonce, expiresAt);
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var (key, exp) in _consumed)
            if (exp <= now)
                _consumed.TryRemove(key, out _);
    }
}
