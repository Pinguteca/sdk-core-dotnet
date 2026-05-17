using System;
using System.Collections.Generic;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Pinguteca.Sdk.Core.Logging;

/// <summary>
/// Knobs for <see cref="LoggingInterceptor"/>. Defaults mirror the
/// cross-SDK contract pinned in
/// <c>sdk-scaffold/docs/rfc/0010-structured-logging.md</c>:
/// one canonical record per RPC at completion, request-id header
/// <c>X-Request-ID</c>, message <c>"rpc"</c>, success at Information
/// and failure at Error, headers redacted on the standard list when
/// <see cref="LogHeaders"/> opts in.
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>Default canonical-record message.</summary>
    public const string DefaultMessage = "rpc";

    /// <summary>Default request-id header name. Matches the Go SDK.</summary>
    public const string DefaultRequestIdHeader = "x-request-id";

    /// <summary>Required structured logger sink.</summary>
    public ILogger Logger { get; init; } = null!;

    /// <summary>Canonical-record message. Defaults to <c>"rpc"</c>.</summary>
    public string Message { get; init; } = DefaultMessage;

    /// <summary>
    /// Request-id metadata header read into <c>request.id</c>. Lookup
    /// is case-insensitive; the default name follows gRPC metadata's
    /// lowercase canonicalisation.
    /// </summary>
    public string RequestIdHeader { get; init; } = DefaultRequestIdHeader;

    /// <summary>
    /// Header names whose values are masked when <see cref="LogHeaders"/>
    /// is true. Case-insensitive. Defaults to the standard sensitive
    /// list from RFC 0010.
    /// </summary>
    public IReadOnlyCollection<string> RedactHeaders { get; init; } = DefaultRedactHeaders;

    /// <summary>
    /// When true, the canonical record includes a <c>rpc.headers</c>
    /// attribute holding every request header with redaction applied.
    /// Off by default because the headers blob is large and adds
    /// little value once <c>request.id</c> is captured.
    /// </summary>
    public bool LogHeaders { get; init; }

    /// <summary>
    /// Log level used for successful calls. Default
    /// <see cref="LogLevel.Information"/>.
    /// </summary>
    public LogLevel SuccessLevel { get; init; } = LogLevel.Information;

    /// <summary>
    /// Log level used for failed calls. Default <see cref="LogLevel.Error"/>.
    /// </summary>
    public LogLevel ErrorLevel { get; init; } = LogLevel.Error;

    /// <summary>
    /// Hook for caller-supplied request attributes
    /// (e.g. <c>tenant.id</c>, <c>actor.id</c>). Runs before the call;
    /// returned attrs are appended to the canonical record. Hooks must
    /// not emit separate log lines.
    /// </summary>
    public Func<LoggingRequestContext, IEnumerable<KeyValuePair<string, object?>>>? AddRequestAttrs { get; init; }

    /// <summary>
    /// Hook for caller-supplied response attributes. Runs after the
    /// call, regardless of success or failure.
    /// </summary>
    public Func<LoggingResponseContext, IEnumerable<KeyValuePair<string, object?>>>? AddResponseAttrs { get; init; }

    /// <summary>
    /// Default sensitive-header list from RFC 0010. Lowercase to match
    /// gRPC metadata canonicalisation.
    /// </summary>
    public static readonly IReadOnlyCollection<string> DefaultRedactHeaders = new[]
    {
        "authorization",
        "cookie",
        "set-cookie",
        "proxy-authorization",
        "x-api-key",
    };
}

/// <summary>Inputs to <see cref="LoggingOptions.AddRequestAttrs"/>.</summary>
public sealed record LoggingRequestContext(
    string Service,
    string Method,
    Metadata? Headers);

/// <summary>Inputs to <see cref="LoggingOptions.AddResponseAttrs"/>.</summary>
public sealed record LoggingResponseContext(
    string Service,
    string Method,
    StatusCode Code,
    Exception? Error);
