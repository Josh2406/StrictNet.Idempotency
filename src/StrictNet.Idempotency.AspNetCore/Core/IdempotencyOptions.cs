namespace StrictNet.Idempotency.AspNetCore.Core
{
    public class IdempotencyOptions
    {
        public string HeaderName { get; set; } = "Idempotency-Key";
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(24);
        public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public bool Return409OnConflict { get; set; } = true;
        public bool FailOpen { get; set; } = false;
    }
}
