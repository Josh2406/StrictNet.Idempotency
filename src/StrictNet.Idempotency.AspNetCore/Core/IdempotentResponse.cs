namespace StrictNet.Idempotency.AspNetCore.Core
{
    public class IdempotentResponse
    {
        public int StatusCode { get; set; }
        public Dictionary<string, string[]> Headers { get; set; } = new();
        public byte[] Body { get; set; } = Array.Empty<byte>();
    }
}
