using System.Collections.Generic;
using System.Net.Http;

namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Configuration for <see cref="LocalEndpointBrokerSource"/>. The
/// blessed Broker-mode transport per cross-SDK RFC 0019: POST a
/// form to a broker-supplied HTTP endpoint (typically a sidecar on
/// loopback) and parse the response as an OAuth
/// <see cref="TokenResponse"/>.
/// </summary>
public sealed class LocalEndpointBrokerConfig
{
    /// <summary>HTTP client used for broker requests. Required.</summary>
    public required HttpClient HttpClient { get; init; }

    /// <summary>
    /// Broker endpoint URL. Must use https or a loopback host. The
    /// loopback allowance lets local sidecar transports skip TLS.
    /// </summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Optional target audience appended to the request body.</summary>
    public string? Audience { get; init; }

    /// <summary>Optional scopes appended to the request body.</summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>
    /// Optional extra form parameters passed verbatim. Useful for
    /// broker-specific identifiers (workload labels, namespaces).
    /// </summary>
    public IReadOnlyDictionary<string, string>? AdditionalParameters { get; init; }

    /// <summary>
    /// Upper bound on how long a broker-issued token stays cached.
    /// When null the default of 30 seconds applies (RFC 0019).
    /// Consumers that know their broker guarantees longer validity
    /// raise this.
    /// </summary>
    public TimeSpan? MaxCacheDuration { get; init; }
}
