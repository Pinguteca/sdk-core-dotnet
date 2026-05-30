# Pinguteca.Sdk.Core.Caching

Response-caching gRPC client interceptor for Pinguteca SDK clients.
Layer 3 companion to `Pinguteca.Sdk.Core`.

Cross-SDK contract pinned in
[RFC 0015](https://github.com/Pinguteca/sdk-scaffold/blob/main/docs/rfc/0015-caching-strategy.md):
schema-driven per-method opt-in, content-hashed cache keys
(SHA-256, FIPS 180-4 approved), default-deny tenant-scope
isolation, TTL plus ETag plus write-triggered invalidation, opt-in
stale-while-revalidate and negative caching, default-on
single-flight, and streaming pass-through.

Ships as L3 because realistic cache stores (Redis, etc.) are
third-party dependencies the core SDK does not require.

## Install

```sh
dotnet add package Pinguteca.Sdk.Core.Caching
```

## What ships

- `CachingInterceptor` wraps unary gRPC calls; streams pass through.
- `ICache` interface with `MemoryCache` LRU+TTL+SWR default.
- `CacheSpec` describing TTL, SWR, negative TTL, and write-method
  invalidation lists.
- `CachingOptions` with `KeyScope` hook (required, default-deny),
  `MethodConfig` map, and optional `ILogger` sink for hit/miss
  outcomes.

## Quickstart

```csharp
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.Caching;

var cache = new MemoryCache(1024);
var interceptor = new CachingInterceptor(new()
{
    Store = cache,
    KeyScope = ctx => "tenant-a",
    MethodConfig =
    {
        ["/user.v1.UserService/GetUser"]    = new CacheSpec { Ttl = TimeSpan.FromMinutes(1) },
        ["/user.v1.UserService/UpdateUser"] = new CacheSpec { Invalidates = ["GetUser"] },
    },
});

var channel = GrpcChannel.ForAddress("https://api.example.com");
var invoker = channel.Intercept(interceptor);
var client = new UserService.UserServiceClient(invoker);
```

Multi-tenant deployments wire `KeyScope` to extract a tenant
identifier from `ClientInterceptorContext.Options.Headers` or
application context. Single-tenant deployments wire
`KeyScope = _ => ""` to opt into empty scope with explicit
acknowledgement.

## Docs

<https://github.com/Pinguteca/sdk-core-dotnet>
