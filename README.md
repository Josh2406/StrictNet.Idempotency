# StrictNet.Idempotency.AspNetCore

StrictNet.Idempotency.AspNetCore provides a small, framework-agnostic idempotency execution pipeline for ASP.NET Core applications. It helps prevent duplicate processing of requests by honoring a client-supplied idempotency key, optionally returning a cached response for repeated requests and coordinating concurrent requests with per-key locks.

Supported target frameworks
- .NET 6
- .NET 8
- .NET 10

Features
- Middleware that detects an idempotency key header and short-circuits when a cached response is available.
- Pluggable IIdempotencyStore implementations (InMemory and Redis provided).
- Per-key locking to avoid duplicate execution for concurrent requests.
- Configurable behavior: header name, cache duration, lock timeout, conflict handling, and fail-open.

Quickstart

1. Add the package to your project

If you are using the project in the same solution, add a project reference. The NuGet package is published, you can install it instead:

dotnet add package StrictNet.Idempotency.AspNetCore

2. Register services and middleware (Program.cs)

using StrictNet.Idempotency.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// register idempotency options (optional)
builder.Services.AddIdempotency(opts =>
{
	// defaults shown; change as needed
	opts.HeaderName = "Idempotency-Key";
	opts.CacheDuration = TimeSpan.FromHours(24);
	opts.LockTimeout = TimeSpan.FromSeconds(5);
	opts.Return409OnConflict = true;
	opts.FailOpen = false;
});

// choose a store implementation
// For single-node / development:
builder.Services.AddInMemoryIdempotencyStore();

// For distributed scenarios (Redis):
// builder.Services.AddRedisIdempotencyStore("localhost:6379");

var app = builder.Build();

// insert middleware into the pipeline
app.UseIdempotency();

// ... your endpoints

app.Run();

How it works
- The middleware inspects the configured header (default: `Idempotency-Key`). If no header is present the request passes through as normal.
- If a cached response exists for the key, the middleware immediately writes the cached status code, headers and body and returns.
- If no cached response exists, the middleware attempts to acquire a per-key lock using the configured store. If it cannot acquire the lock and `Return409OnConflict` is true, a 409 response is returned.
- On successful execution (HTTP 2xx), the middleware captures response headers and body and saves them to the configured store for the configured cache duration.

Configuration / IdempotencyOptions
- `HeaderName` (string): request header to read a key from (default: `Idempotency-Key`).
- `CacheDuration` (TimeSpan): TTL for cached responses (default: 24 hours).
- `LockTimeout` (TimeSpan): maximum time to wait to acquire a per-key lock (default: 5s).
- `Return409OnConflict` (bool): whether to return 409 when a lock cannot be acquired (default: true).
- `FailOpen` (bool): when true, internal idempotency errors will allow the request to be processed normally; when false exceptions will bubble (default: false).

Provided stores
- `InMemoryIdempotencyStore`: useful for local development and single-node deployments. No external dependencies.
- `RedisIdempotencyStore`: recommended for distributed deployments. Uses `StackExchange.Redis` `IConnectionMultiplexer` internally.

Notes about Redis
- When using Redis in production, provide a resilient connection string and monitor connection health. TTL and locking semantics are implemented using Redis primitives; ensure clocks and network latency are accounted for when tuning `LockTimeout` and `CacheDuration`.

Testing
- Unit tests and integration tests are included in the `tests` project. Run them with:

dotnet test tests/StrictNet.Idempotency.AspNetCore.Tests

- Integration tests that exercise Redis use Testcontainers and require Docker to be available on the host.

Development
- Build the solution: `dotnet build`
- Run tests: `dotnet test`

Contributing
- Contributions are welcome. Please open issues for bugs or feature requests, and submit pull requests for fixes and features. Follow the established code style and add tests for behavioral changes.

License
- See LICENSE for license terms.

Contact
- For questions open an issue in the repository.

