using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using StrictNet.Idempotency.AspNetCore.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrictNet.Idempotency.AspNetCore.Tests.Unit
{
    [TestFixture]
    public class IdempotencyMiddlewareTests
    {
        private Mock<IIdempotencyStore> _storeMock;
        private IdempotencyOptions _options;
        private DefaultHttpContext _context;

        [SetUp]
        public void Setup()
        {
            _storeMock = new Mock<IIdempotencyStore>();
            _options = new IdempotencyOptions();
            _context = new DefaultHttpContext();

            _context.Response.Body = new MemoryStream();
        }

        private IdempotencyMiddleware CreateMiddleware(RequestDelegate next)
        {
            var optionsMock = new Mock<IOptions<IdempotencyOptions>>();
            optionsMock.Setup(o => o.Value).Returns(_options);
            return new IdempotencyMiddleware(next, optionsMock.Object);
        }

        [Test]
        public async Task InvokeAsync_NoHeader_BypassesMiddleware()
        {
            // Arrange
            var nextCalled = false;
            var middleware = CreateMiddleware(innerContext =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            // Act
            await middleware.InvokeAsync(_context, _storeMock.Object);

            // Assert
            Assert.That(nextCalled, Is.True);
            _storeMock.Verify(s => s.GetResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task InvokeAsync_CachedResponseExists_ReturnsCachedAndShortCircuits()
        {
            // Arrange
            _context.Request.Headers[_options.HeaderName] = "test-key-123";
            var cachedResponse = new IdempotentResponse
            {
                StatusCode = 201,
                Body = Encoding.UTF8.GetBytes("Cached Body"),
                Headers = new Dictionary<string, string[]> { { "X-Custom", new[] { "Value" } } }
            };

            _storeMock.Setup(s => s.GetResponseAsync("test-key-123", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(cachedResponse);

            var nextCalled = false;
            var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

            // Act
            await middleware.InvokeAsync(_context, _storeMock.Object);
            _context.Response.Body.Position = 0;
            using var reader = new StreamReader(_context.Response.Body);
            var bodyString = await reader.ReadToEndAsync();


            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(nextCalled, Is.False, "Handler should not be called on cache hit.");
                Assert.That(_context.Response.StatusCode, Is.EqualTo(201));
                Assert.That(_context.Response.Headers["X-Custom"].ToString(), Is.EqualTo("Value"));
                Assert.That(bodyString, Is.EqualTo("Cached Body"));
            });
        }

        [Test]
        public async Task InvokeAsync_LockFails_Returns409Conflict()
        {
            // Arrange
            _context.Request.Headers[_options.HeaderName] = "test-key-123";
            _options.Return409OnConflict = true;

            _storeMock.Setup(s => s.GetResponseAsync("test-key-123", It.IsAny<CancellationToken>()))
                      .ReturnsAsync((IdempotentResponse?)null);

            _storeMock.Setup(s => s.TryAcquireLockAsync("test-key-123", _options.LockTimeout, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(false);

            var middleware = CreateMiddleware(_ => Task.CompletedTask);

            // Act
            await middleware.InvokeAsync(_context, _storeMock.Object);

            // Assert
            Assert.That(_context.Response.StatusCode, Is.EqualTo(409));
        }

        [Test]
        public async Task InvokeAsync_LockSucceeds_ExecutesNextAndCachesResponse()
        {
            // Arrange
            _context.Request.Headers[_options.HeaderName] = "test-key-123";

            _storeMock.Setup(s => s.GetResponseAsync("test-key-123", It.IsAny<CancellationToken>()))
                      .ReturnsAsync((IdempotentResponse?)null);

            _storeMock.Setup(s => s.TryAcquireLockAsync("test-key-123", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(true);

            var middleware = CreateMiddleware(async innerContext =>
            {
                innerContext.Response.StatusCode = 200;
                await innerContext.Response.WriteAsync("New Response");
            });

            // Act
            await middleware.InvokeAsync(_context, _storeMock.Object);

            // Assert
            _storeMock.Verify(s => s.SaveResponseAsync(
                "test-key-123",
                It.Is<IdempotentResponse>(r => r.StatusCode == 200 && Encoding.UTF8.GetString(r.Body) == "New Response"),
                _options.CacheDuration,
                It.IsAny<CancellationToken>()), Times.Once);

            _storeMock.Verify(s => s.ReleaseLockAsync("test-key-123", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void InvokeAsync_StoreThrows_FailClosed_ThrowsException()
        {
            // Arrange
            _context.Request.Headers[_options.HeaderName] = "test-key-123";
            _options.FailOpen = false;

            _storeMock.Setup(s => s.GetResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ThrowsAsync(new Exception("Redis connection failed"));

            var middleware = CreateMiddleware(_ => Task.CompletedTask);

            // Act & Assert
            Assert.ThrowsAsync<Exception>(async () => await middleware.InvokeAsync(_context, _storeMock.Object));
        }

        [Test]
        public async Task InvokeAsync_StoreThrows_FailOpen_BypassesIdempotency()
        {
            // Arrange
            _context.Request.Headers[_options.HeaderName] = "test-key-123";
            _options.FailOpen = true;

            _storeMock.Setup(s => s.GetResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ThrowsAsync(new Exception("Redis connection failed"));

            var nextCalled = false;
            var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

            // Act
            await middleware.InvokeAsync(_context, _storeMock.Object);

            // Assert
            Assert.That(nextCalled, Is.True, "Should fail open and proceed to the next middleware.");
        }
    }
}
