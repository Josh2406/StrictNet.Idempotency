using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using StrictNet.Idempotency.AspNetCore.Core;

namespace StrictNet.Idempotency.AspNetCore
{
    public class IdempotencyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IdempotencyOptions _options;

        public IdempotencyMiddleware(RequestDelegate next, IOptions<IdempotencyOptions> options)
        {
            _next = next;
            _options = options.Value;
        }

        public async Task InvokeAsync(HttpContext context, IIdempotencyStore store)
        {
            if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var keyValues))
            {
                await _next(context);
                return;
            }

            string key = keyValues.ToString();

            try
            {
                var cached = await store.GetResponseAsync(key, context.RequestAborted);
                if (cached != null)
                {
                    context.Response.StatusCode = cached.StatusCode;
                    foreach (var h in cached.Headers) context.Response.Headers[h.Key] = h.Value;
                    await context.Response.Body.WriteAsync(cached.Body, 0, cached.Body.Length);
                    return;
                }

                if (!await store.TryAcquireLockAsync(key, _options.LockTimeout, context.RequestAborted))
                {
                    if (_options.Return409OnConflict)
                    {
                        context.Response.StatusCode = 409;
                        await context.Response.WriteAsync("Conflict: Request in progress.");
                    }
                    return;
                }

                try
                {
                    var originalBody = context.Response.Body;
                    using var memoryStream = new MemoryStream();
                    context.Response.Body = memoryStream;

                    await _next(context);

                    var headers = context.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray());

                    if (context.Response.StatusCode is >= 200 and < 300)
                    {
                        var responseToCache = new IdempotentResponse
                        {
                            StatusCode = context.Response.StatusCode,
                            Headers = headers!,
                            Body = memoryStream.ToArray()
                        };
                        await store.SaveResponseAsync(key, responseToCache, _options.CacheDuration, context.RequestAborted);
                    }

                    memoryStream.Position = 0;
                    await memoryStream.CopyToAsync(originalBody);
                    context.Response.Body = originalBody;
                }
                finally
                {
                    await store.ReleaseLockAsync(key, context.RequestAborted);
                }
            }
            catch
            {
                if (_options.FailOpen) await _next(context); else throw;
            }
        }
    }
}
