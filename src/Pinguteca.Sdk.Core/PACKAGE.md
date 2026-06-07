# Pinguteca.Sdk.Core

Layer 2 gRPC client interceptors for Pinguteca SDKs in .NET. Speaks
gRPC over HTTP/2 against the Connect-Go server other SDKs reach via
the Connect protocol.

## Install

```sh
dotnet add package Pinguteca.Sdk.Core
```

## What ships

- `RetryInterceptor` with full or decorrelated jitter, server-supplied
  `retry-after` honouring, and a sensible retryable-status set.
- `TimeoutInterceptor` that stamps a default deadline on unary calls
  unless the caller already set one.
- `AuthInterceptor` with `StaticBearerTokenSource` and
  `ClientCredentialsTokenSource` (OAuth 2.0 client_credentials, in-
  memory cache, proactive refresh).
- `OtelInterceptor` opens a client-kind `Activity` per unary call and
  stamps OpenTelemetry RPC semantic conventions on it. Auto-parents to
  `Activity.Current` via `AsyncLocal`.
- `SdkError` and `SdkErrorCode` as the typed boundary over
  `Grpc.Core.RpcException`.

## Quickstart

```csharp
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.Auth;
using Pinguteca.Sdk.Core.Retry;
using Pinguteca.Sdk.Core.Timeouts;

var channel = GrpcChannel.ForAddress("https://api.example.com");

var invoker = channel
    .Intercept(new TimeoutInterceptor(new() { Default = TimeSpan.FromSeconds(5) }))
    .Intercept(new RetryInterceptor())
    .Intercept(new AuthInterceptor(new()
    {
        Source = new ClientCredentialsTokenSource(new()
        {
            TokenUrl = new Uri("https://idp.example.com/oauth2/token"),
            ClientId = "your-client-id",
            ClientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET")!,
        }),
    }));

var client = new YourService.YourServiceClient(invoker);
```

## OpenTelemetry and Aspire

`OtelInterceptor` owns an `ActivitySource` named `Pinguteca.Sdk.Core`
by default (override via `OtelOptions.ActivitySourceName`). To collect
spans, subscribe that source on your tracer provider. For Aspire users
this is the entire integration surface:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Pinguteca.Sdk.Core"));
```

`Activity.Current` propagation is implicit. `ActivitySource.StartActivity`
auto-parents to the ambient activity via `AsyncLocal`, so spans created
upstream (ASP.NET Core, MassTransit, the Aspire dashboard's incoming
request span) become parents of the SDK call without explicit wiring.
W3C `traceparent` is injected into outgoing gRPC metadata by the
gRPC client instrumentation; no SDK-side propagator is needed.

Span shape per unary call:

- Name: full gRPC method (`package.Service/Method`).
- Kind: `Client`.
- Tags: `rpc.system=grpc`, `rpc.service`, `rpc.method`,
  `rpc.grpc.status_code`.
- Status: `Ok` on success, `Error` with the gRPC status detail on
  `RpcException`.

The interceptor sits outermost in the chain by design (see RFC 0008
in the cross-SDK scaffold), so retries, breaker trips, idempotency
key generation, and token refreshes all appear as descendants of one
SDK span. In Aspire's structured trace view this renders as a single
collapsible RPC frame with each interceptor's work nested below.

### HttpClient instrumentation overlap

If you also enable `AddHttpClientInstrumentation()` (Aspire does this
by default for `HttpClient`-backed channels), expect a nested
`CLIENT` span: the SDK span carries RPC semantics, the HttpClient span
carries HTTP semantics. This is the OpenTelemetry-recommended layering
and is not double-counting. Pin
`OpenTelemetry.Instrumentation.Http` >= 1.9.0 so the HttpClient
instrumentation does not overwrite the `traceparent` header the gRPC
layer already injected.

## Docs

Full documentation, ADRs, and roadmap live in the repository:
<https://github.com/Pinguteca/sdk-core-dotnet>.
