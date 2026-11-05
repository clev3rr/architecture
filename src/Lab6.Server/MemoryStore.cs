using System.Collections.Concurrent;
using System.Security.Cryptography;
using Lab6.Crypto;

namespace Lab6.Server;

public sealed class MemoryStore
{
    private readonly ConcurrentDictionary<int, RsaKeys> keys = new();
    private readonly ConcurrentDictionary<Guid, ChannelSession> sessions = new();
    private int nextId;

    public MemoryStore(IRsaService rsa)
    {
        for (var i = 0; i < 10; i++) AddKey(rsa.GenerateCryptoKeys());
    }

    public int KeyCount => keys.Count;
    public int AddKey(RsaKeys pair)
    {
        var id = Interlocked.Increment(ref nextId);
        keys[id] = pair;
        return id;
    }
    public RsaKeys? GetKey(int id) => keys.GetValueOrDefault(id);
    public KeyValuePair<int, RsaKeys> RandomKey()
    {
        var snapshot = keys.ToArray();
        return snapshot[RandomNumberGenerator.GetInt32(snapshot.Length)];
    }
    public bool AddSession(Guid id, ChannelSession session)
    {
        foreach (var item in sessions)
            if (item.Value.ExpiresAt <= DateTimeOffset.UtcNow) sessions.TryRemove(item.Key, out _);
        return sessions.TryAdd(id, session);
    }
    public ChannelSession? GetSession(Guid id) => sessions.GetValueOrDefault(id);
}

public sealed class ChannelSession(AesKey key, DateTimeOffset expiresAt)
{
    public AesKey Key { get; } = key;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public long LastSequence { get; set; }
    public object SyncRoot { get; } = new();
}
