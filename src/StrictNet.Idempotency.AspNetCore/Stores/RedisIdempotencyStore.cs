using StackExchange.Redis;
using StrictNet.Idempotency.AspNetCore.Core;
using System.Text.Json;

namespace StrictNet.Idempotency.AspNetCore.Stores
{
    public class RedisIdempotencyStore : IIdempotencyStore
    {
        private readonly IDatabase _db;
        public RedisIdempotencyStore(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
        }

        public async Task<IdempotentResponse?> GetResponseAsync(string key, CancellationToken ct = default)
        {
            var data = await _db.StringGetAsync($"idemp:data:{key}");
            return data.HasValue ? JsonSerializer.Deserialize<IdempotentResponse>(data.ToString()) : null;
        }

        public Task ReleaseLockAsync(string key, CancellationToken ct = default)
        => _db.KeyDeleteAsync($"idemp:lock:{key}");

        public Task SaveResponseAsync(string key, IdempotentResponse response, TimeSpan expiry, CancellationToken ct = default)
        => _db.StringSetAsync($"idemp:data:{key}", JsonSerializer.Serialize(response), expiry);

        public Task<bool> TryAcquireLockAsync(string key, TimeSpan timeout, CancellationToken ct = default)
        => _db.StringSetAsync($"idemp:lock:{key}", "locked", timeout, When.NotExists);
    }
}
