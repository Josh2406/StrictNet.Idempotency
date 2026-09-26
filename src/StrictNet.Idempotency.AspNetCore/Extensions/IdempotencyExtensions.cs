using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using StrictNet.Idempotency.AspNetCore.Core;
using StrictNet.Idempotency.AspNetCore.Stores;

namespace StrictNet.Idempotency.AspNetCore.Extensions
{
    public static class IdempotencyExtensions
    {
        /// <summary>
        /// Registers the core idempotency configuration.
        /// </summary>
        public static IServiceCollection AddIdempotency(this IServiceCollection services, Action<IdempotencyOptions>? configure = null)
        {
            if (configure != null)
            {
                services.Configure(configure);
            }

            return services;
        }

        /// <summary>
        /// Registers the InMemory store for testing or single-node deployments.
        /// </summary>
        public static IServiceCollection AddInMemoryIdempotencyStore(this IServiceCollection services)
        {
            EnsureNoStoreRegistered(services);
            services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
            return services;
        }

        /// <summary>
        /// Registers the Redis store for production distributed locking.
        /// </summary>
        public static IServiceCollection AddRedisIdempotencyStore(this IServiceCollection services, string redisConnectionString)
        {
            EnsureNoStoreRegistered(services);
            services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
            return services;
        }

        /// <summary>
        /// Inserts the Idempotency middleware into the HTTP request pipeline.
        /// </summary>
        public static IApplicationBuilder UseIdempotency(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<IdempotencyMiddleware>();
        }

        private static void EnsureNoStoreRegistered(IServiceCollection services)
        {
            if (services.Any(s => s.ServiceType == typeof(IIdempotencyStore)))
            {
                throw new InvalidOperationException("An idempotency store has already been registered. You cannot register both InMemory and Redis stores simultaneously.");
            }
        }
    }
}
