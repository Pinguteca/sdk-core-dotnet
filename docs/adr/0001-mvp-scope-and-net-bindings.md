# ADR 0001: MVP scope and .NET-specific bindings

- **Status:** Accepted
- **Date:** 2026-05-10
- **Deciders:** SDK team
- **Affects:** the root `Pinguteca.Sdk.Core` project and the test
  project layout for this repo.
- **Implements:** RFC 0002 (layered SDK architecture), RFC 0004
  (companion module naming), RFC 0005 (per-language wire protocol).

## Context

This is the first SDK in the family after Go. Four .NET-local
decisions need to be pinned before any code lands:

1. **Wire protocol binding.** Connect-for-.NET does not exist (RFC
   0005). The SDK must speak gRPC over HTTP/2 against the same
   Connect-Go server other SDKs reach via Connect.
2. **Target framework.** The .NET ecosystem has two current LTS
   lines (8 and 10) and consumers split between them.
3. **Repository layout.** Go shipped Layer 2 packages as a single
   root module with packages-as-directories. The .NET equivalent
   is one root project with feature-folder namespaces, not one
   project per feature.
4. **Test framework.** The scaffold pre-populates an xunit.v3
   placeholder, but the modern .NET 8+ alternative TUnit fits the
   single-TFM net10.0 stance better and is worth pinning
   explicitly rather than inheriting a placeholder.

The MVP scope is also pinned here so subsequent commits hit a
known landing zone instead of negotiating scope per concern.

## Decision

1. **Wire protocol: gRPC over HTTP/2.** The runtime is
   `Grpc.Net.Client` plus `Grpc.Tools` (codegen) plus
   `Google.Protobuf` (wire format), all exempt per RFC 0002. The
   SDK provides gRPC `ClientInterceptor` implementations rather
   than HttpClient handlers. HttpClient-level resilience
   (`Microsoft.Extensions.Http.Resilience`) is wrong here because
   it cannot see gRPC status; gRPC always returns HTTP 200 with
   status in trailers.

2. **Single target framework: `net10.0`.** .NET 8 to .NET 10
   migration is trivial for consumers, and multi-targeting doubles
   CI cost plus adds `#if NET10_0_OR_GREATER` noise. Consumers
   still on .NET 8 LTS upgrade before they consume this SDK.
   Revisit only if a stated consumer surfaces who cannot upgrade.

3. **Layout: one root project, feature-folder namespaces.**
   `src/Pinguteca.Sdk.Core/Pinguteca.Sdk.Core.csproj` contains
   `Errors/`, `Retry/`, `Timeouts/`, `Auth/`. Mirrors how Go's
   root module organises L2 packages as sibling directories.
   Single NuGet package ships. Companion L3 modules later get
   their own `Pinguteca.Sdk.Core.<Token>` projects under
   `companions/` (RFC 0004 naming).

4. **Test framework: TUnit.** Source-generator based, parallel by
   default, AOT-compatible, .NET 8+ native. Better fit than
   xunit.v3 for a single-TFM net10.0 project where reflection-
   based discovery and constructor-per-test ceremony pay no
   dividend. TUnit ships its own assertions and works with
   Shouldly or FluentAssertions if a test author prefers an
   ecosystem assertion library. The ecosystem-size argument for
   xunit (Verify, AutoFixture, etc.) does not bite because tests
   do not ship to consumers; only the SDK team feels the choice.

5. **MVP scope: errors, retry, timeouts, auth.** Each concern
   ships as a folder in the root project with a public surface
   summarised below. Anything else (idempotency, pagination,
   mTLS, correlation, OTel, logging, breaker, hedge, compression,
   pkcs12) is deferred. We expand to full RFC 0001 parity after
   the MVP proves out the wire-protocol binding and resilience
   model.

   - **Errors:** `SdkError` record with `Code` (mapped from
     `Grpc.Core.StatusCode`), optional `RetryInfo` payload,
     factory helpers. Acts as the typed boundary between gRPC and
     consumer code so consumers do not import `Grpc.Core`
     directly.
   - **Retry:** `RetryInterceptor` (gRPC `ClientInterceptor`
     subclass), `RetryOptions` (max attempts, base delay, max
     delay, retryable status set, optional `RetryInfo` honouring).
     Algorithm is exponential backoff with decorrelated jitter,
     identical curve to `sdk-core-go/retry`.
   - **Timeouts:** `TimeoutInterceptor` that sets
     `CallOptions.Deadline` from a per-client default and per-call
     override. No HttpClient-level timeout interactions.
   - **Auth:** `ITokenSource` abstraction; `StaticBearerTokenSource`
     for a fixed token; `ClientCredentialsTokenSource` for OAuth
     2.0 client_credentials (POST to token endpoint, in-memory
     cache, early refresh). `AuthInterceptor` attaches
     `Authorization: Bearer <token>` to each call. No MSAL (vendor-
     specific) and no `IdentityModel` dependency.

## Consequences

### Positive

- One NuGet package for the MVP keeps the publish path simple
  while RFC 0004 naming is exercised on the companion side later.
- gRPC interceptor binding is the idiomatic .NET shape and
  consumers familiar with gRPC adopt the SDK without a new
  mental model.
- Owning retry, timeout, and auth keeps the algorithm identical
  across SDKs (RFC 0002 doctrine). `Microsoft.Extensions.Http.Resilience`
  stays available for the consumer's *non-SDK* HTTP traffic, just
  not wired into the gRPC channel.
- Single TFM avoids `#if` clutter and halves the CI matrix.
- TUnit's source-gen discovery removes reflection startup cost
  and gives parallel-by-default for free; in an SDK that will
  grow to hundreds of small unit tests this compounds.

### Negative

- Consumers on .NET 8 LTS cannot consume the SDK until they
  upgrade. Documented in the README.
- Single root project means a single version bump applies to
  every L2 concern. Acceptable while the surface is small;
  revisit if it ever forces a breaking change in one area to
  ride alongside an unrelated one.
- gRPC error model in trailers means consumers piping requests
  through HTTP-only middleware (custom logging, certain proxies)
  see `200 OK` with no body and lose the error. Documented in
  RFC 0005; same trade for every gRPC-fallback SDK.
- TUnit's ecosystem is smaller than xunit's. If a future test
  needs Verify-style snapshot testing or AutoFixture-style
  generators, the team either uses TUnit-friendly equivalents
  (Verify supports TUnit; AutoFixture works framework-agnostic)
  or accepts a small per-test glue layer.

### Neutral

- The L2 allow-list CI guard (RFC 0003 contract) is not yet wired
  for .NET. Tracked as a follow-up ADR; the manual allow list at
  this point is the RFC 0002 table.

## Alternatives considered

- **Multi-target `net8.0;net10.0`.** Considered. Rejected by the
  user on grounds that .NET 8 to .NET 10 migration is trivial for
  consumers; doubled CI cost is not worth the marginal reach.
- **One project per concern from day one.** Considered. Adds
  multi-project release coordination before there is anything to
  release. Mirrors Go's L2 organisation instead.
- **Wrap `Microsoft.Extensions.Http.Resilience` for retry.**
  Rejected: it operates on HTTP status, which for gRPC is always
  200, so it cannot see retryable gRPC codes.
- **Adopt MSAL for OAuth.** Rejected: MSAL is Azure AD specific.
  RFC 0002 calls out non-vendor IdPs (Keycloak, Auth0, Entra ID
  via OIDC, etc.); a focused client_credentials implementation
  serves them all uniformly.
- **xunit.v3 instead of TUnit.** Considered. Larger ecosystem but
  no consumer-facing impact (tests stay in-repo). TUnit's
  source-gen and parallel-by-default outweigh ecosystem size for
  this SDK.
- **NUnit.** Considered. Older than xunit, similar reflection-
  based discovery, no clear advantage over the two above.

## Revisit when

- A real consumer on .NET 8 LTS reports they cannot upgrade.
  Switch to multi-target and accept the CI cost.
- A concern in the root project (e.g. retry) ships a breaking
  change while the others are stable. Split that concern into
  its own project at that point.
- A Connect-for-.NET runtime publishes a stable 1.0. Migrate
  per RFC 0005's future-possibilities section.
- TUnit ecosystem gap forces non-trivial glue for a real test
  need. Revisit the framework choice if the workaround tax
  exceeds the productivity gain.

## References

- RFC 0002 (layered SDK architecture), RFC 0004 (companion
  naming), RFC 0005 (per-language wire protocol).
- `Grpc.Net.Client` documentation:
  https://learn.microsoft.com/aspnet/core/grpc/client
- `Grpc.Core.Api` interceptor types:
  https://learn.microsoft.com/dotnet/api/grpc.core.interceptors
- TUnit: https://github.com/thomhurst/TUnit
- xunit.v3 (rejected): https://xunit.net/docs/getting-started/v3/getting-started
