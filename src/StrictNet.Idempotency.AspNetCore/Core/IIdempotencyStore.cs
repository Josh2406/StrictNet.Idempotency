namespace StrictNet.Idempotency.AspNetCore.Core
{
    public interface IIdempotencyStore
    {
        Task<bool> TryAcquireLockAsync(string key, TimeSpan timeout, CancellationToken ct = default);
        Task ReleaseLockAsync(string key, CancellationToken ct = default);
        Task<IdempotentResponse?> GetResponseAsync(string key, CancellationToken ct = default);
        Task SaveResponseAsync(string key, IdempotentResponse response, TimeSpan expiry, CancellationToken ct = default);
    }
}
