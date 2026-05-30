# Pinguteca.Sdk.Ergo

Layer 1.5 ergonomic primitive kit for Pinguteca SDK clients in
.NET. Companion to `Pinguteca.Sdk.Core`.

Cross-SDK contract pinned in
[RFC 0016](https://github.com/Pinguteca/sdk-scaffold/blob/main/docs/rfc/0016-layer-1-5-ergonomic-api.md):
ships the building blocks per-service L1.5 resource methods rely
on. No service-specific code lives here; resource methods are
written against this kit once their `api-surface.yaml` exists.

## Install

```sh
dotnet add package Pinguteca.Sdk.Ergo --prerelease
```

## What ships

- `ComposedOp` orchestrates multi-RPC operations under one L1.5
  entry point. Derives per-leg idempotency keys (`{op_id}/{leg}`)
  and threads a correlation id through every leg via gRPC
  metadata headers.
- `Operation<T>` long-running-operation poller with full-jitter
  backoff (RFC 0006), server `retry-after` override, total wait
  bounded by the caller's `CancellationToken`.
- `IdGenerator` generates 128-bit hex identifiers via
  `RandomNumberGenerator.Fill` (FIPS-approved CSPRNG).

## Quickstart

```csharp
using Pinguteca.Sdk.Ergo;
using Grpc.Core;

// In an L1.5 resource method:
public async Task<File> UploadAsync(string name, Stream data, CallOptions options)
{
    var op = ComposedOp.New();
    var session = await op.RunAsync(options,
        opts => _client.CreateUploadSessionAsync(new() { Name = name }, opts).ResponseAsync);
    // ... stream chunks (next leg) ... finalize (next leg)
    return ...;
}
```

Stability: pre-release while Layer 1.5 takes shape. Tag releases
as `v0.x-alpha` until the primitive set proves stable across more
than one consumer-facing service.

## Docs

<https://github.com/Pinguteca/sdk-core-dotnet>
