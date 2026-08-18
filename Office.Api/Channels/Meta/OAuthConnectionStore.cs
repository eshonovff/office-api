using System.Collections.Concurrent;

namespace Office.Api.Channels.Meta;

public interface IOAuthConnectionStore
{
    Guid Create(OAuthConnectionSession session);
    OAuthConnectionSession? TryGet(Guid connectionId, DateTimeOffset now);
}

/// <summary>
/// Нигоҳдории муваққатии натиҷаи /callback (token-ҳо дар дохил) то операторон account-ро
/// дар /connect интихоб кунад. Дар хотира — як instance-и .NET (алгуи лоиҳа), TTL-и кӯтоҳ
/// (мувофиқи мӯҳлати state, ~10 дақ) ин кофист, DB лозим нест.
/// </summary>
public sealed class OAuthConnectionStore : IOAuthConnectionStore
{
    private readonly ConcurrentDictionary<Guid, OAuthConnectionSession> _sessions = new();

    public Guid Create(OAuthConnectionSession session)
    {
        var id = Guid.CreateVersion7();
        _sessions[id] = session;
        return id;
    }

    public OAuthConnectionSession? TryGet(Guid connectionId, DateTimeOffset now)
    {
        if (!_sessions.TryGetValue(connectionId, out var session))
            return null;

        if (session.ExpiresAt <= now)
        {
            _sessions.TryRemove(connectionId, out _);
            return null;
        }

        return session;
    }
}
