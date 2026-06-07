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

## Quickstart

### client_credentials, discovery-driven

```csharp
using Pinguteca.Sdk.Core.OAuth;

using var http = new HttpClient();
using var tokens = await ClientCredentialsTokenSource.FromIssuerAsync(new ClientCredentialsFromIssuerConfig
{
    Issuer = new Uri("https://idp.example.com"),
    ClientId = "your-client-id",
    ClientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET"),
    Scopes = new[] { "api:read" },
    HttpClient = http,
});

string accessToken = await tokens.GetTokenAsync(cancellationToken);
```

`ClientCredentialsTokenSource` implements
`Pinguteca.Sdk.Core.Auth.IRotatingTokenSource`, so the existing
`AuthInterceptor` and `RotationInterceptor` consume it without
changes.

### authorization_code with PKCE

```csharp
using Pinguteca.Sdk.Core.OAuth;

using var http = new HttpClient();
var flow = await AuthorizationCodeFlow.FromIssuerAsync(new AuthorizationCodeFromIssuerConfig
{
    Issuer = new Uri("https://idp.example.com"),
    ClientId = "your-client-id",
    ClientSecret = null,                     // public client + PKCE
    AuthMode = ClientAuthMode.None,
    RedirectUri = new Uri("https://app.example.com/callback"),
    Scopes = new[] { "openid", "profile" },
    HttpClient = http,
});

// 1) Bounce the user to the authorization endpoint.
var pkce = PkcePair.Generate();
string state = Guid.NewGuid().ToString("N");
Uri redirectTo = flow.BuildAuthorizationUrl(state, pkce);
// (your web app persists `pkce.Verifier` and `state` alongside the
//  user's session, then returns a redirect to `redirectTo`)

// 2) On callback, exchange the code for tokens.
TokenResponse tokens = await flow.ExchangeAsync(receivedCode, pkce.Verifier);

// 3) Build a TokenSource that auto-refreshes via the refresh token.
using var source = new AuthorizationCodeTokenSource(flow, tokens);
string accessToken = await source.GetTokenAsync(cancellationToken);
```

`state` is mandatory and consumer-owned; the SDK rejects empty
values rather than generating one because state binds to consumer-
side session state.

### mTLS at the token endpoint (RFC 8705)

Build an `HttpClient` whose `SocketsHttpHandler` presents the
client cert at the TLS layer. The OAuth companion does not host
the cert plumbing; `Pinguteca.Sdk.Core.Mtls.MtlsHelper` does, and
the OAuth package consumes whatever `HttpClient` you hand it.

```csharp
using Pinguteca.Sdk.Core.Mtls;
using Pinguteca.Sdk.Core.OAuth;

SslClientAuthenticationOptions ssl =
    MtlsHelper.ConfigFromP12("client.pfx", "p12-password");
SocketsHttpHandler handler = MtlsHelper.Transport(ssl);
using var http = new HttpClient(handler);

using var tokens = await ClientCredentialsTokenSource.FromIssuerAsync(new ClientCredentialsFromIssuerConfig
{
    Issuer = new Uri("https://idp.example.com"),
    ClientId = "your-client-id",
    ClientSecret = null,
    AuthMode = ClientAuthMode.Mtls,
    HttpClient = http,
});
```

`FromIssuerAsync` reads the IdP's discovery document and, when the
server advertises `mtls_endpoint_aliases.token_endpoint`, routes
the token request to that alias (per RFC 8705 section 5). The
authorization endpoint stays on the top-level URL because mTLS
gates the back-channel exchange, not the user-agent redirect.

`AuthorizationCodeFlow.FromIssuerAsync` does the same mTLS routing
when configured with `ClientAuthMode.Mtls`.

## Docs

Full documentation, the cross-SDK contract, and the changelog live
in the repository:
<https://github.com/Pinguteca/sdk-core-dotnet>.
