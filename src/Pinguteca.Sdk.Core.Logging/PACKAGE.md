# Pinguteca.Sdk.Core.Logging

Canonical-log + wide-event gRPC interceptor for Pinguteca SDK
clients. Layer 3 companion to `Pinguteca.Sdk.Core` that targets
`Microsoft.Extensions.Logging.ILogger`.

Ships separately because structured-logging integration is
ecosystem-native: each SDK in the family binds to its language's
de-facto logger interface (slog for Go, MEL for .NET, SLF4J for
JVM, `tracing` for Rust, etc.) rather than mandating one. Cross-SDK
contract pinned in
[RFC 0010](https://github.com/Pinguteca/sdk-scaffold/blob/main/docs/rfc/0010-structured-logging.md).

## Install

```sh
dotnet add package Pinguteca.Sdk.Core.Logging
```

## What ships

- `LoggingInterceptor` emits one structured record per RPC at
  completion. No per-step debug noise.
- Attributes follow OTel RPC semantic conventions:
  `rpc.system`, `rpc.service`, `rpc.method`, `rpc.duration_ms`,
  `rpc.code`, `request.id`, `trace.id`, `span.id`, `error`.
- Default redaction list masks `Authorization`, `Cookie`,
  `Set-Cookie`, `Proxy-Authorization`, `X-Api-Key` when header
  logging is enabled.
- `AddRequestAttrs` / `AddResponseAttrs` hooks for business-domain
  attributes (`tenant.id`, `actor.id`, etc.).

## Quickstart

```csharp
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Pinguteca.Sdk.Core.Logging;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
var logger = loggerFactory.CreateLogger("rpc");

var channel = GrpcChannel.ForAddress("https://api.example.com");
var invoker = channel.Intercept(new LoggingInterceptor(new() { Logger = logger }));

var client = new YourService.YourServiceClient(invoker);
```

## Docs

<https://github.com/Pinguteca/sdk-core-dotnet>
