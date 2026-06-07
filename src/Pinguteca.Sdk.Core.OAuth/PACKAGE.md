# Pinguteca.Sdk.Core.OAuth

Layer 3 OAuth 2.0 companion for the Pinguteca .NET SDK. Provides
the grant-flow surface pinned in cross-SDK RFC 0017: machine-to-
machine `client_credentials`, interactive `authorization_code` with
PKCE, OIDC discovery, and mTLS-bound token requests (RFC 8705).

## Install

```sh
dotnet add package Pinguteca.Sdk.Core.OAuth
```

`Pinguteca.Sdk.Core` is pulled in transitively. No other runtime
dependency.

## Scope

In scope per RFC 0017:

- `client_credentials` grant (RFC 6749 section 4.4).
- `authorization_code` grant with PKCE S256 (RFC 6749 section 4.1
  + RFC 7636). PKCE is mandatory; the `plain` method is not
  available.
- OIDC discovery (RFC 8414).
- mTLS at the token endpoint (RFC 8705), composable with both
  grants.

Explicitly out of scope: `password`, `implicit`, `device_code`,
`token_exchange`, `private_key_jwt`, `jwt_bearer`, and standalone
`refresh_token`. See RFC 0017 for rationale.

## Status

This release ships the project scaffold only. The grant flow
implementations land in subsequent releases on per-package tags
(`oauth-v0.x.y`). Track progress in the repository roadmap.

## Docs

Full documentation, the cross-SDK contract, and the changelog live
in the repository:
<https://github.com/Pinguteca/sdk-core-dotnet>.
