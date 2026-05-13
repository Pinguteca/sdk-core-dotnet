# ADR 0002: Embedded debug symbols, no snupkg

- **Status:** Accepted
- **Date:** 2026-05-13
- **Deciders:** SDK team
- **Affects:** `Directory.Build.props` debug settings, NuGet pack output,
  GitHub Release asset list.

## Context

The .NET SDK can ship debug symbols three ways:

1. **`<DebugType>portable</DebugType>`** (default for .NET Core).
   A separate `.pdb` file is produced next to the DLL. Not included in
   the nupkg by default; consumers get no debug info.
2. **`<IncludeSymbols>true</IncludeSymbols>` with `<SymbolPackageFormat>snupkg</SymbolPackageFormat>`.**
   The `.pdb` ships as a separate `.snupkg` package on nuget.org's
   symbol server. Consumers must wire the nuget.org symbol server in
   Visual Studio or Rider to step into SDK code.
3. **`<DebugType>embedded</DebugType>`.** The PDB is embedded inside the
   DLL itself. One file ships, debug info travels with it, no consumer
   configuration required.

The decision matters because the SDK is a Layer 2 interceptor library:
consumers will hit retry, auth, and timeout code paths in their own
debuggers and benefit from stepping into the SDK. Symbol-server-gated
debug info loses that audience the day someone forgets to enable the
server.

The first release (v0.1.0) shipped without a `.snupkg` because the
csproj never enabled it; the release workflow's `*.snupkg` glob also
silently matched nothing. Fixing the gap requires picking a posture
now rather than enabling snupkg-and-symbol-server out of inertia.

## Decision

1. **Embed PDBs in the assembly.** Set in `Directory.Build.props`:
   ```xml
   <DebugType>embedded</DebugType>
   <DebugSymbols>true</DebugSymbols>
   ```
   Every output assembly (Pinguteca.Sdk.Core today, companions later)
   carries its PDB. SourceLink stays on so the embedded PDB references
   GitHub source URLs.

2. **Do not enable `IncludeSymbols`.** No `.snupkg` is produced or
   uploaded. The release workflow drops the `*.snupkg` glob from its
   asset list.

3. **Accept the DLL-size cost.** For the current MVP surface the
   embedded PDB grows the assembly by tens of kilobytes; the order of
   magnitude is irrelevant for consumers who already pull tens of MB
   of gRPC and protobuf runtime.

4. **Apply at the scaffold level.** Template's
   `Directory.Build.props.jinja` carries the same two properties so
   every future .NET SDK ships with embedded debug by default.

## Consequences

### Positive

- Consumers step into SDK code with zero configuration. No "configure
  symbol server" support tickets.
- Single shipped artifact per assembly. Simpler supply-chain audit:
  one nupkg per release, no parallel symbol package to verify.
- SourceLink works out of the box because embedded PDBs always carry
  the source-document table.
- Removes one CI failure source (the `*.snupkg` glob that matched no
  files and made softprops warn).

### Negative

- DLL ships with debug info to every consumer whether they want it or
  not. Cannot strip post-publish.
- Slightly larger NuGet download. Negligible at SDK scale; would
  matter for large libraries (out of scope here).
- Enterprise consumers running an internal symbol cache lose a
  centralised symbol-source story. The embedded PDB still works with
  their tooling but does not feed their cache. Acceptable until a real
  consumer asks.

### Neutral

- Reversing the decision is a one-line change plus a CI tweak. Low
  switching cost if a future signal points to snupkg.

## Alternatives considered

- **Stay on portable PDB (default), no symbols in nupkg.** Rejected.
  Loses source-level debugging on shipped packages, which is the worst
  default for an SDK consumers expect to debug.
- **`IncludeSymbols` plus `snupkg`.** Rejected. Microsoft's own
  guidance has moved away from snupkg-by-default toward embedded for
  smaller libraries. Adds publish surface (the symbol upload step) and
  loses zero-config debugging for the long tail of consumers who
  never configure the nuget.org symbol server.
- **`<DebugType>full</DebugType>`.** Windows-only legacy format. Out
  of scope.

## Revisit when

- DLL size becomes a stated problem (consumer running on size-
  constrained edge, AOT publish, container image audit). Switch back
  to snupkg at that point.
- An enterprise consumer requires symbol-server-driven debugging
  workflow (e.g. internal SymStore). Layer snupkg on top of embedded
  rather than replacing.

## References

- Microsoft Learn, "Including Symbols in NuGet packages":
  https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg
- SourceLink and embedded PDBs:
  https://github.com/dotnet/sourcelink
- ADR 0001 (.NET MVP scope) for the broader binding context.
