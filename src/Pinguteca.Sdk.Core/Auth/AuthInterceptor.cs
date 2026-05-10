using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Errors;

namespace Pinguteca.Sdk.Core.Auth;

/// <summary>
/// gRPC client interceptor that attaches a credential to every
/// outgoing unary call. Streaming calls also get the credential at
/// stream-open. Failure to obtain a token surfaces as an
/// <see cref="SdkError"/> with
/// <see cref="SdkErrorCode.Unauthenticated"/>, matching the
/// behaviour the rest of the SDK family follows.
///
/// Client-side only. Registering this interceptor on a handler
/// would attach the bearer header to inbound requests, which is
/// meaningless on the server side.
/// </summary>
public sealed class AuthInterceptor : Interceptor
{
    private readonly AuthOptions _options;

    public AuthInterceptor(AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Source);
        if (string.IsNullOrEmpty(options.HeaderName))
        {
            throw new ArgumentException("HeaderName must be non-empty.", nameof(options));
        }
        _options = options;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var response = AttachAndCallAsync(request, context, continuation);
        return new AsyncUnaryCall<TResponse>(
            response,
            response.ContinueWith(_ => new Metadata(), TaskScheduler.Default),
            () => response.IsCompletedSuccessfully ? Status.DefaultSuccess : Status.DefaultCancelled,
            () => [],
            () => { });
    }

    private async Task<TResponse> AttachAndCallAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        var procedure = $"/{context.Method.ServiceName}/{context.Method.Name}";
        if (_options.Skip is { } skip && skip(procedure))
        {
            return await continuation(request, context).ResponseAsync.ConfigureAwait(false);
        }

        string token;
        try
        {
            token = await _options.Source
                .GetTokenAsync(context.Options.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SdkError(
                SdkErrorCode.Unauthenticated,
                "auth: failed to obtain token: " + ex.Message,
                retryAfter: null,
                source: null);
        }

        var headers = context.Options.Headers ?? [];
        var newHeaders = new Metadata();
        foreach (var entry in headers)
        {
            newHeaders.Add(entry);
        }
        newHeaders.Add(_options.HeaderName, _options.FormatHeader(token));

        var newOptions = context.Options.WithHeaders(newHeaders);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            newOptions);

        try
        {
            return await continuation(request, newContext).ResponseAsync.ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            throw SdkError.FromRpcException(ex);
        }
    }
}
