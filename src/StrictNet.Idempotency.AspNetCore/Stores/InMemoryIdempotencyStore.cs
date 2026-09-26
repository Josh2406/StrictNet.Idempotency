using StrictNet.Idempotency.AspNetCore.Core;
using System.Collections.Concurrent;

namespace StrictNet.Idempotency.AspNetCore.Stores
{
    public class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly ConcurrentDictionary<string, IdempotentResponse> _cache = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public Task<IdempotentResponse?> GetResponseAsync(string k, CancellationToken ct = default)
        => Task.FromResult(_cache.TryGetValue(k, out var r) ? r : null);

        public Task ReleaseLockAsync(string key, CancellationToken ct = default)
        {
            if (_locks.TryGetValue(key, out var sem)) sem.Release();
            return Task.CompletedTask;
        }

        public Task SaveResponseAsync(string k, IdempotentResponse r, TimeSpan e, CancellationToken ct = default)
        {
            _cache[k] = r;
            return Task.CompletedTask;
        }

        public async Task<bool> TryAcquireLockAsync(string key, TimeSpan timeout, CancellationToken ct = default)
        {
            var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            return await sem.WaitAsync(timeout, ct);
        }
    }
}