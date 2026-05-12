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

## Docs

Full documentation, ADRs, and roadmap live in the repository:
<https://github.com/Pinguteca/sdk-core-dotnet>.
