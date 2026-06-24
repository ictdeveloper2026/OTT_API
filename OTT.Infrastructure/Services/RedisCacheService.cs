using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace OTT.Infrastructure.Services;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class;
    Task RemoveAsync(string key);
    Task RemoveByPatternAsync(string pattern);
    Task<bool> ExistsAsync(string key);
    Task<long> IncrementAsync(string key, TimeSpan? expiry = null);
    Task SetStringAsync(string key, string value, TimeSpan? expiry = null);
    Task<string?> GetStringAsync(string key);

    // ── Write-behind buffers (high-frequency writes accumulated in Redis, flushed to SQL by a job) ──
    Task HashIncrementAsync(string key, string field, long value = 1);
    Task HashSetAsync(string key, string field, string value);
    Task<Dictionary<string, string>> HashGetAllAndClearAsync(string key);

    // ── Concurrent-stream slots (atomic, expiry-scored sorted set per user) ──
    /// <summary>
    /// Atomically prunes expired slots, then admits <paramref name="member"/> if the active slot
    /// count is below <paramref name="maxConcurrent"/> (re-admitting an existing member is always
    /// allowed). Returns the active slot count after admission, or -1 when at capacity.
    /// </summary>
    Task<long> TryAcquireStreamSlotAsync(string key, string member, int maxConcurrent, TimeSpan ttl);
    /// <summary>Refreshes an existing slot's expiry (heartbeat). No-op if the slot is gone.</summary>
    Task RenewStreamSlotAsync(string key, string member, TimeSpan ttl);
    /// <summary>Releases a slot immediately (clean stop / sign-out).</summary>
    Task ReleaseStreamSlotAsync(string key, string member);
}

public class RedisCacheService : IRedisCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisCacheService> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (!value.HasValue) return null;
            return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis GET error for key: {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            await _db.StringSetAsync(key, json, expiry ?? TimeSpan.FromMinutes(30));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SET error for key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis DELETE error for key: {Key}", key);
        }
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        try
        {
            var endpoints = _redis.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                var server = _redis.GetServer(endpoint);
                var keys = server.Keys(pattern: pattern).ToArray();
                if (keys.Length > 0)
                    await _db.KeyDeleteAsync(keys);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis PATTERN DELETE error: {Pattern}", pattern);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return await _db.KeyExistsAsync(key);
        }
        catch
        {
            return false;
        }
    }

    public async Task<long> IncrementAsync(string key, TimeSpan? expiry = null)
    {
        try
        {
            var val = await _db.StringIncrementAsync(key);
            if (expiry.HasValue && val == 1)
                await _db.KeyExpireAsync(key, expiry.Value);
            return val;
        }
        catch
        {
            return 0;
        }
    }

    public async Task SetStringAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            await _db.StringSetAsync(key, value, expiry ?? TimeSpan.FromMinutes(30));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SET STRING error for key: {Key}", key);
        }
    }

    public async Task<string?> GetStringAsync(string key)
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task HashIncrementAsync(string key, string field, long value = 1)
    {
        try { await _db.HashIncrementAsync(key, field, value); }
        catch (Exception ex) { _logger.LogError(ex, "Redis HINCRBY error for {Key}/{Field}", key, field); }
    }

    public async Task HashSetAsync(string key, string field, string value)
    {
        try { await _db.HashSetAsync(key, field, value); }
        catch (Exception ex) { _logger.LogError(ex, "Redis HSET error for {Key}/{Field}", key, field); }
    }

    // Atomic admit: ZREMRANGEBYSCORE (drop expired) → check capacity → ZADD → EXPIRE, in one
    // server-side script so two devices racing the last slot can't both win.
    private const string AcquireSlotScript = @"
        local now = tonumber(ARGV[1])
        local expireAt = tonumber(ARGV[2])
        local maxc = tonumber(ARGV[3])
        local member = ARGV[4]
        local ttl = tonumber(ARGV[5])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now)
        local exists = redis.call('ZSCORE', KEYS[1], member)
        if not exists and redis.call('ZCARD', KEYS[1]) >= maxc then
            return -1
        end
        redis.call('ZADD', KEYS[1], expireAt, member)
        redis.call('EXPIRE', KEYS[1], ttl)
        return redis.call('ZCARD', KEYS[1])";

    public async Task<long> TryAcquireStreamSlotAsync(string key, string member, int maxConcurrent, TimeSpan ttl)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var ttlSeconds = (long)ttl.TotalSeconds;
            var result = await _db.ScriptEvaluateAsync(AcquireSlotScript,
                new RedisKey[] { key },
                new RedisValue[] { now, now + ttlSeconds, maxConcurrent, member, ttlSeconds });
            return (long)result;
        }
        catch (Exception ex)
        {
            // Fail open: if Redis is unavailable, don't block paying customers from watching.
            _logger.LogError(ex, "Redis stream-slot acquire error for {Key}", key);
            return 1;
        }
    }

    public async Task RenewStreamSlotAsync(string key, string member, TimeSpan ttl)
    {
        try
        {
            var expireAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)ttl.TotalSeconds;
            // XX = only update if the member still exists; don't resurrect a released slot.
            await _db.SortedSetAddAsync(key, member, expireAt, SortedSetWhen.Exists);
            await _db.KeyExpireAsync(key, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis stream-slot renew error for {Key}", key);
        }
    }

    public async Task ReleaseStreamSlotAsync(string key, string member)
    {
        try { await _db.SortedSetRemoveAsync(key, member); }
        catch (Exception ex) { _logger.LogError(ex, "Redis stream-slot release error for {Key}", key); }
    }

    // Reads the whole buffer hash and deletes it so the flush job processes each batch once.
    // A handful of increments between read and delete may be lost — acceptable for view/progress counters.
    public async Task<Dictionary<string, string>> HashGetAllAndClearAsync(string key)
    {
        try
        {
            var entries = await _db.HashGetAllAsync(key);
            if (entries.Length == 0) return new();
            await _db.KeyDeleteAsync(key);
            return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis hash drain error for {Key}", key);
            return new();
        }
    }
}
