# Pinguteca.Sdk.Core.Hedge

Hedged-requests gRPC interceptor for Pinguteca SDK clients. Layer 3
companion to `Pinguteca.Sdk.Core`.

Ships separately because parallel-attempt orchestration is
ecosystem-native (Tasks plus Channels in .NET, goroutines plus
channels in Go, Tokio mpsc in Rust, etc.) and because hedge is an
opt-in tail-latency tool that multiplies backend load. Cross-SDK
contract pinned in
[RFC 0013](https://github.com/Pinguteca/sdk-scaffold/blob/main/docs/rfc/0013-hedged-requests.md).

## Install

```sh
dotnet add package Pinguteca.Sdk.Core.Hedge
```

## What ships

- `HedgeInterceptor` runs up to N parallel attempts of the same RPC
  and returns the first successful response, cancelling the others.
- Default policy: 3 total attempts, 50 ms stagger, hedges only
  methods that return `HedgeEligibility.NoSideEffects` from the
  caller-supplied `IsHedgeEligible` hook. `Idempotent` methods are
  skipped unless `HedgeIdempotent = true`.
- LAST observed error returned on all-fail (most-recent-state).
- Streaming RPCs pass through unchanged.

## Quickstart

```csharp
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.Hedge;

var channel = GrpcChannel.ForAddress("https://api.example.com");
var invoker = channel.Intercept(new HedgeInterceptor(new()
{
    IsHedgeEligible = method => method.EndsWith("/Read")
        ? HedgeEligibility.NoSideEffects
        : HedgeEligibility.Unknown,
}));

var client = new YourService.YourServiceClient(invoker);
```

Hedge attempts count as retry attempts to the outer retry
interceptor; lower `RetryOptions.MaxAttempts` accordingly to keep
total request volume bounded.

## Docs

<https://github.com/Pinguteca/sdk-core-dotnet>
