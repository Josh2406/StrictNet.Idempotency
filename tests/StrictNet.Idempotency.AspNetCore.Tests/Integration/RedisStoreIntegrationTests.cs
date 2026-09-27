using StackExchange.Redis;
using StrictNet.Idempotency.AspNetCore.Core;
using StrictNet.Idempotency.AspNetCore.Stores;
using System.Text;
using Testcontainers.Redis;

namespace StrictNet.Idempotency.AspNetCore.Tests.Integration
{
    [TestFixture]
    [Category("Integration")]
    public class RedisIdempotencyStoreIntegrationTests
    {
        private RedisContainer _redisContainer = null!;
        private ConnectionMultiplexer _redisConnection = null!;
        private RedisIdempotencyStore _store = null!;

        [OneTimeSetUp]
        public async Task GlobalSetup()
        {
            // 1. Define and start the Redis container
            _redisContainer = new RedisBuilder()
                .WithImage("redis:7.2")
                .Build();

            await _redisContainer.StartAsync();

            // 2. Connect StackExchange.Redis to the throwaway container
            string connectionString = _redisContainer.GetConnectionString();
            _redisConnection = await ConnectionMultiplexer.ConnectAsync(connectionString);

            // 3. Initialize the store being tested
            _store = new RedisIdempotencyStore(_redisConnection);
        }

        [OneTimeTearDown]
        public async Task GlobalTeardown()
        {
            if (_redisConnection != null)
            {
                await _redisConnection.DisposeAsync();
            }

            if (_redisContainer != null)
            {
                await _redisContainer.DisposeAsync();
            }
        }

        [Test]
        public async Task TryAcquireLockAsync_Concurrently_OnlyAllowsOneLock()
        {
            // Arrange
            string testKey = "integration-lock-key";
            TimeSpan timeout = TimeSpan.FromSeconds(5);

            // Act - Try to grab the exact same lock 3 times concurrently
            var lockTasks = Enumerable.Range(0, 3)
                .Select(_ => _store.TryAcquireLockAsync(testKey, timeout))
                .ToList();

            var results = await Task.WhenAll(lockTasks);

            // Assert - SETNX guarantees only one will succeed
            Assert.Multiple(() =>
            {
                Assert.That(results.Count(r => r == true), Is.EqualTo(1), "Only one lock should be acquired.");
                Assert.That(results.Count(r => r == false), Is.EqualTo(2), "The other two requests should be rejected.");
            });
        }

        [Test]
        public async Task SaveAndGetResponseAsync_RoundTripsDataCorrectly()
        {
            // Arrange
            string testKey = "integration-data-key";
            var originalResponse = new IdempotentResponse
            {
                StatusCode = 201,
                Body = Encoding.UTF8.GetBytes("Created via Integration Test"),
                Headers = new Dictionary<string, string[]> { { "X-Trace-Id", new[] { "abc-123" } } }
            };

            // Act
            await _store.SaveResponseAsync(testKey, originalResponse, TimeSpan.FromMinutes(1));
            var retrievedResponse = await _store.GetResponseAsync(testKey);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(retrievedResponse, Is.Not.Null);
                Assert.That(retrievedResponse!.StatusCode, Is.EqualTo(201));
                Assert.That(Encoding.UTF8.GetString(retrievedResponse.Body), Is.EqualTo("Created via Integration Test"));
                Assert.That(retrievedResponse.Headers["X-Trace-Id"][0], Is.EqualTo("abc-123"));
            });
        }
    }
}
