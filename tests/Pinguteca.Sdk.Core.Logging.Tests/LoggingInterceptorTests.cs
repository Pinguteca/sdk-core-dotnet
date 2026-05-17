using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Pinguteca.Sdk.Core.Logging;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Pinguteca.Sdk.Core.Logging.Tests;

public sealed class LoggingInterceptorTests
{
    private static readonly Method<string, string> _method = new(
        MethodType.Unary,
        "svc.v1.Svc",
        "Do",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    [Test]
    public async Task Constructor_NullOptions_Throws()
    {
        await Assert.That(() => new LoggingInterceptor(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullLogger_Throws()
    {
        await Assert.That(() => new LoggingInterceptor(new LoggingOptions()))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Success_EmitsOneRecordAtSuccessLevelWithRpcCodeOK()
    {
        var logger = new FakeLogger();
        var interceptor = new LoggingInterceptor(new LoggingOptions { Logger = logger });

        await InvokeAsync(interceptor, SuccessCall());

        await Assert.That(logger.Records.Count).IsEqualTo(1);
        var record = logger.Records[0];
        await Assert.That(record.Level).IsEqualTo(LogLevel.Information);
        await Assert.That(record.Get("rpc.system")).IsEqualTo("grpc");
        await Assert.That(record.Get("rpc.service")).IsEqualTo("svc.v1.Svc");
        await Assert.That(record.Get("rpc.method")).IsEqualTo("Do");
        await Assert.That(record.Get("rpc.code")).IsEqualTo("OK");
    }

    [Test]
    public async Task Failure_EmitsOneRecordAtErrorLevelWithStatusName()
    {
        var logger = new FakeLogger();
        var interceptor = new LoggingInterceptor(new LoggingOptions { Logger = logger });

        await Assert.That(async () =>
            await InvokeAsync(interceptor, FailingCall(new Status(StatusCode.Unavailable, "down"))))
            .ThrowsExactly<RpcException>();

        await Assert.That(logger.Records.Count).IsEqualTo(1);
        var record = logger.Records[0];
        await Assert.That(record.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(record.Get("rpc.code")).IsEqualTo("Unavailable");
        await Assert.That(record.Get("error")).IsTypeOf<string>();
    }

    [Test]
    public async Task RequestIdHeader_AppearsInRecord()
    {
        var logger = new FakeLogger();
        var interceptor = new LoggingInterceptor(new LoggingOptions { Logger = logger });
        var headers = new Metadata { { "x-request-id", "abc-123" } };

        await InvokeAsync(interceptor, SuccessCall(), headers);

        await Assert.That(logger.Records[0].Get("request.id")).IsEqualTo("abc-123");
    }

    [Test]
    public async Task TraceAndSpanId_CapturedFromActivity()
    {
        using var source = new ActivitySource($"test-{Guid.NewGuid():N}");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("test");

        var logger = new FakeLogger();
        var interceptor = new LoggingInterceptor(new LoggingOptions { Logger = logger });
        await InvokeAsync(interceptor, SuccessCall());

        await Assert.That(logger.Records[0].Get("trace.id")).IsEqualTo(activity!.TraceId.ToString());
        await Assert.That(logger.Records[0].Get("span.id")).IsEqualTo(activity.SpanId.ToString());
    }

    [Test]
    public async Task CallerHooks_AttrsAppendedToRecord()
    {
        var logger = new FakeLogger();
        var interceptor = new LoggingInterceptor(new LoggingOptions
        {
            Logger = logger,
            AddRequestAttrs = _ => new KeyValuePair<string, object?>[]
            {
                new("tenant.id", "t-1"),
            },
            AddResponseAttrs = _ => new KeyValuePair<string, object?>[]
            {
                new("bytes.returned", 42L),
            },
        });

        await InvokeAsync(interceptor, SuccessCall());

        await Assert.That(logger.Records[0].Get("tenant.id")).IsEqualTo("t-1");
        await Assert.That(logger.Records[0].Get("bytes.returned")).IsEqualTo(42L);
    }

    [Test]
    public async Task LogHeaders_Off_OmitsHeadersAttribute()
    {
        var logger = new FakeLogger();
        var interceptor = new LoggingInterceptor(new LoggingOptions { Logger = logger });
        var headers = new Metadata { { "authorization", "Bearer secret" } };

        await InvokeAsync(interceptor, SuccessCall(), headers);

        await Assert.That(logger.Records[0].Get("rpc.headers")).IsNull();
    }

    [Test]
    public async Task LogHeaders_On_IncludesRedactedHeaders()
    {
        var logger = new FakeLogger();
        var interceptor = new LoggingInterceptor(new LoggingOptions
        {
            Logger = logger,
            LogHeaders = true,
        });
        var headers = new Metadata
        {
            { "authorization", "Bearer secret" },
            { "x-trace-context", "visible-value" },
        };

        await InvokeAsync(interceptor, SuccessCall(), headers);

        var captured = (IReadOnlyDictionary<string, string>)logger.Records[0].Get("rpc.headers")!;
        await Assert.That(captured["authorization"]).IsEqualTo("[REDACTED]");
        await Assert.That(captured["x-trace-context"]).IsEqualTo("visible-value");
    }

    [Test]
    public async Task LogHeaders_On_SkipsBinaryMetadata()
    {
        var logger = new FakeLogger();
        var interceptor = new LoggingInterceptor(new LoggingOptions
        {
            Logger = logger,
            LogHeaders = true,
        });
        var headers = new Metadata { { "trace-id-bin", new byte[] { 1, 2, 3 } } };

        await InvokeAsync(interceptor, SuccessCall(), headers);

        var captured = (IReadOnlyDictionary<string, string>)logger.Records[0].Get("rpc.headers")!;
        await Assert.That(captured.ContainsKey("trace-id-bin")).IsFalse();
    }

    // ---------- helpers ----------

    private static async Task<string> InvokeAsync(
        LoggingInterceptor interceptor,
        AsyncUnaryCall<string> downstream,
        Metadata? headers = null)
    {
        var options = headers is null ? new CallOptions() : new CallOptions(headers: headers);
        var context = new ClientInterceptorContext<string, string>(_method, host: null, options);
        var call = interceptor.AsyncUnaryCall("req", context, (_, _) => downstream);
        try
        {
            return await call.ResponseAsync;
        }
        finally
        {
            call.Dispose();
        }
    }

    private static AsyncUnaryCall<string> SuccessCall() =>
        new(
            Task.FromResult("ok"),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => [],
            () => { });

    private static AsyncUnaryCall<string> FailingCall(Status status) =>
        new(
            Task.FromException<string>(new RpcException(status)),
            Task.FromResult(new Metadata()),
            () => status,
            () => [],
            () => { });
}

internal sealed class FakeLogger : ILogger
{
    public List<LogRecord> Records { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var attrs = state as IEnumerable<KeyValuePair<string, object?>>
            ?? Array.Empty<KeyValuePair<string, object?>>();
        Records.Add(new LogRecord(logLevel, attrs.ToList(), exception));
    }
}

internal sealed record LogRecord(
    LogLevel Level,
    IReadOnlyList<KeyValuePair<string, object?>> Attrs,
    Exception? Exception)
{
    public object? Get(string key)
    {
        foreach (var kv in Attrs)
        {
            if (kv.Key == key) return kv.Value;
        }
        return null;
    }
}
