using Microsoft.Extensions.Hosting;
using Testcontainers.Redis;
using StrictNet.Idempotency.AspNetCore.Extensions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace StrictNet.Idempotency.AspNetCore.Tests.Integration
{
    [TestFixture]
    [Category("Integration")]
    public class MiddlewareEndToEndTests
    {
        private RedisContainer _redisContainer = null!;
        private IHost _host = null!;
        private HttpClient _client = null!;
        private int _handlerExecutionCount = 0;

        [OneTimeSetUp]
        public async Task Setup()
        {
            // 1. Start the throwaway Redis container
            _redisContainer = new RedisBuilder().WithImage("redis:7.2").Build();
            await _redisContainer.StartAsync();

            // 2. Build a lightweight Test Server (Bypasses WebApplicationFactory & .sln issues)
            _host = await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            // Register your StrictNet middleware
                            services.AddIdempotency(opt => { opt.HeaderName = "Idempotency-Key"; });
                            services.AddRedisIdempotencyStore(_redisContainer.GetConnectionString());
                        })
                        .Configure(app =>
                        {
                            // Insert the middleware into the pipeline
                            app.UseIdempotency();

                            // Setup a dummy endpoint to test against
                            app.Run(async context =>
                            {
                                Interlocked.Increment(ref _handlerExecutionCount);
                                context.Response.StatusCode = 200;
                                await context.Response.WriteAsync("Disbursement Processed");
                            });
                        });
                })
                .StartAsync();

            // 3. Get the HttpClient hooked up to our in-memory test pipeline
            _client = _host.GetTestClient();
        }

        [OneTimeTearDown]
        public async Task Teardown()
        {
            _client?.Dispose();
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
            if (_redisContainer != null) await _redisContainer.DisposeAsync();
        }

        [Test]
        public async Task SequentialDuplicate_ReturnsCachedResponse_ExecutesHandlerOnce()
        {
            // Arrange
            var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/disburse");
            request1.Headers.Add("Idempotency-Key", "e2e-test-key-1");

            var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/disburse");
            request2.Headers.Add("Idempotency-Key", "e2e-test-key-1");

            // Act
            var response1 = await _client.SendAsync(request1);
            var response2 = await _client.SendAsync(request2);

            var body1 = await response1.Content.ReadAsStringAsync();
            var body2 = await response2.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response1.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
                Assert.That(response2.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));

                Assert.That(body1, Is.EqualTo("Disbursement Processed"));
                Assert.That(body2, Is.EqualTo("Disbursement Processed"));

                Assert.That(_handlerExecutionCount, Is.EqualTo(1), "Handler should only run once.");
            });
        }
    }
}
