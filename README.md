# StrictNet.Idempotency

[![Verify Build & Tests](https://github.com/Josh2406/StrictNet.Idempotency/actions/workflows/verify.yml/badge.svg)](https://github.com/Josh2406/StrictNet.Idempotency/actions/workflows/verify.yml)
[![NuGet](https://img.shields.io/nuget/v/StrictNet.Idempotency.AspNetCore.svg)](https://www.nuget.org/packages/StrictNet.Idempotency.AspNetCore)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%208%20%7C%2010-512BD4)](#requirements)

A lightweight, high-performance idempotency middleware for ASP.NET Core APIs. It prevents duplicate side effects from retried or concurrent requests by caching responses and coordinating access with a distributed lock, keyed off a client-supplied `Idempotency-Key` header.

## Why

Clients retry. Load balancers retry. Mobile apps on flaky networks retry. Without idempotency protection, a retried `POST /payments` or `POST /orders` can create the same resource twice. StrictNet.Idempotency solves this by:

- **Replaying the original response** for a request whose key it has already seen, instead of executing the handler again.
- **Locking in-flight requests** so that two concurrent calls with the same key don't both reach your business logic.
- **Rejecting concurrent duplicates** with `409 Conflict` while the original request is still processing.

## Features

- 🔒 **Distributed locking** via Redis, safe across multiple API instances.
- ⚡ **In-memory store** for local development, testing, and single-instance deployments.
- 🧩 **Drop-in middleware** — a few lines in `Program.cs`, no changes to your controllers or minimal API handlers.
- ⚙️ **Configurable** header name, cache duration, lock timeout, conflict behavior, and fail-open/fail-closed error handling.
- 🎯 **Multi-target** support for .NET 6, .NET 8, and .NET 10.

## Requirements

| | |
|---|---|
| Target frameworks | .NET 6.0, .NET 8.0, .NET 10.0 |
| Distributed store | Redis (via [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis)) — optional, only required for `AddRedisIdempotencyStore` |

## Installation

```bash
dotnet add package StrictNet.Idempotency.AspNetCore
```

## Quick Start

### 1. Register a store and the middleware

Choose exactly one store. Registering both throws an `InvalidOperationException` at startup.

```csharp
using StrictNet.Idempotency.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdempotency(options =>
{
    options.HeaderName = "Idempotency-Key";       // default
    options.CacheDuration = TimeSpan.FromHours(24); // how long a response is replayed
    options.LockTimeout = TimeSpan.FromSeconds(5);  // max wait for an in-flight duplicate
    options.Return409OnConflict = true;             // reject concurrent duplicates
    options.FailOpen = false;                       // fail closed if the store is unavailable
});

// Production: distributed locking backed by Redis
builder.Services.AddRedisIdempotencyStore(builder.Configuration.GetConnectionString("Redis")!);

// Local dev / single-node / tests: in-process store
// builder.Services.AddInMemoryIdempotencyStore();

var app = builder.Build();

app.UseIdempotency();

app.MapPost("/payments", HandlePayment);

app.Run();
```

### 2. Send the header from the client

```http
POST /payments HTTP/1.1
Idempotency-Key: 8f14e45f-ceea-467e-9959-b8f7d5cbea1a
Content-Type: application/json

{ "amount": 4999, "currency": "usd" }
```

- **First request** with a given key runs the handler normally, and the response (status code, headers, and body) is cached for `CacheDuration`.
- **Repeated request** with the same key, after the first has completed, returns the cached response without re-executing the handler.
- **Concurrent request** with the same key, while the first is still in flight, receives `409 Conflict` (when `Return409OnConflict` is `true`) instead of running the handler.
- Requests without the header are passed straight through — idempotency is opt-in per client, not enforced globally.

Only responses with a `2xx` status code are cached; error responses are not stored, so a client can safely retry a failed request with the same key.

## Configuration Reference

`IdempotencyOptions`, configured via `AddIdempotency`:

| Option | Default | Description |
|---|---|---|
| `HeaderName` | `"Idempotency-Key"` | The request header inspected for an idempotency key. Requests without it bypass the middleware entirely. |
| `CacheDuration` | `24 hours` | How long a successful response is retained and replayed for the same key. |
| `LockTimeout` | `5 seconds` | How long the middleware waits to acquire the lock for a key before treating it as a conflict. |
| `Return409OnConflict` | `true` | When `true`, a concurrent duplicate request receives `409 Conflict`. When `false`, the request is simply dropped without a response body being written. |
| `FailOpen` | `false` | When `true`, any unhandled error from the idempotency store (e.g. Redis unavailable) allows the request through to your handler unprotected. When `false` (default), the error propagates and the request fails — fail closed. |

## Storage Backends

### In-memory (`AddInMemoryIdempotencyStore`)

Backed by `ConcurrentDictionary` and `SemaphoreSlim`. Fast and dependency-free, but state is local to the process — it does **not** coordinate across multiple instances and is cleared on restart. Intended for local development, testing, and single-node deployments.

### Redis (`AddRedisIdempotencyStore`)

Backed by [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis). Locks use `SET NX` with an expiry so a crashed instance can't hold a key forever; cached responses are stored as JSON with a TTL matching `CacheDuration`. Safe to use across any number of API instances sharing the same Redis server. Recommended for production.

You can implement `IIdempotencyStore` yourself to plug in a different backend (e.g. SQL Server, DynamoDB):

```csharp
public interface IIdempotencyStore
{
    Task<bool> TryAcquireLockAsync(string key, TimeSpan timeout, CancellationToken ct = default);
    Task ReleaseLockAsync(string key, CancellationToken ct = default);
    Task<IdempotentResponse?> GetResponseAsync(string key, CancellationToken ct = default);
    Task SaveResponseAsync(string key, IdempotentResponse response, TimeSpan expiry, CancellationToken ct = default);
}
```

## Building and Testing

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

The test suite covers unit tests for the middleware pipeline, concurrency validation against the in-memory store, and integration tests against a real Redis instance via Testcontainers (requires Docker to be available on the host).

## Contributing

Issues and pull requests are welcome. Please open an issue to discuss significant changes before submitting a PR, and follow the established code style with tests for behavioral changes.

## License

Licensed under the [MIT License](LICENSE).
