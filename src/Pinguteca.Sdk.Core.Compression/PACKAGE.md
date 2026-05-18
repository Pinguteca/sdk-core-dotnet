# Pinguteca.Sdk.Core.Compression

Brotli and Zstd gRPC compression providers for Pinguteca SDK
clients. Layer 3 companion to `Pinguteca.Sdk.Core`.

Ships separately because Zstd requires a third-party implementation
(`ZstdSharp.Port`) and Layer 2 core stays zero-third-party beyond
the gRPC runtime itself. Cross-SDK contract pinned in
[RFC 0011](https://github.com/Pinguteca/sdk-scaffold/blob/main/docs/rfc/0011-compression-strategy.md).

## Install

```sh
dotnet add package Pinguteca.Sdk.Core.Compression
```

## What ships

- `BrotliCompressionProvider` over stdlib `BrotliStream` at
  `CompressionLevel.Optimal` (Brotli quality 4 in .NET 5+).
- `ZstdCompressionProvider` over `ZstdSharp.Port`.
- `CompressionDefaults.AddTo(GrpcChannelOptions)` to register both
  providers in one call alongside Grpc.Net.Client's built-in Gzip.
- `CompressionNames.Brotli` / `Zstd` / `Gzip` as canonical
  encoding-header constants.

## Quickstart

```csharp
using Grpc.Net.Client;
using Pinguteca.Sdk.Core.Compression;

var options = new GrpcChannelOptions();
CompressionDefaults.AddTo(options);

var channel = GrpcChannel.ForAddress("https://api.example.com", options);
```

Brotli is the default Send encoding; Zstd is registered for
Accept-Encoding negotiation when the server advertises support.

## Docs

<https://github.com/Pinguteca/sdk-core-dotnet>
