using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Pinguteca.Sdk.Core.Caching;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

// TUnit.Assertions ships its own StringValue type; alias the protobuf
// one to disambiguate.
using ProtoStringValue = Google.Protobuf.WellKnownTypes.StringValue;

namespace Pinguteca.Sdk.Core.Caching.Tests;

public sealed class CachingInterceptorTests
{
    private static readonly Marshaller<ProtoStringValue> _marshaller = Marshallers.Create(
        (ProtoStringValue msg) => msg.ToByteArray(),
        (byte[] bytes) => ProtoStringValue.Parser.ParseFrom(bytes));

    private static readonly Method<ProtoStringValue, ProtoStringValue> _getMethod = new(
        MethodType.Unary,
        "svc.v1.Svc",
        "Get",
        _marshaller,
        _marshaller);

    private static readonly Method<ProtoStringValue, ProtoStringValue> _updateMethod = new(
        MethodType.Unary,
        "svc.v1.Svc",
        "Update",
        _marshaller,
        _marshaller);

    [Test]
    public async Task NullKeyScope_PassesThrough()
    {
        var calls = 0;
        var interceptor = new CachingInterceptor(new CachingOptions
        {
            Store = new MemoryCache(8),
            MethodConfig = new Dictionary<string, CacheSpec>
            {
                ["/svc.v1.Svc/Get"] = new CacheSpec { Ttl = TimeSpan.FromMinutes(1) },
            },
        });

        await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "a" }, _ =>
        {
            calls++;
            return Task.FromResult(new ProtoStringValue { Value = "from-server" });
        });
        await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "a" }, _ =>
        {
            calls++;
            return Task.FromResult(new ProtoStringValue { Value = "from-server" });
        });
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task UnconfiguredMethod_PassesThrough()
    {
        var calls = 0;
        var interceptor = new CachingInterceptor(new CachingOptions
        {
            Store = new MemoryCache(8),
            KeyScope = _ => "t",
            MethodConfig = new Dictionary<string, CacheSpec>
            {
                ["/svc.v1.Svc/Other"] = new CacheSpec { Ttl = TimeSpan.FromMinutes(1) },
            },
        });

        await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "a" }, _ =>
        {
            calls++;
            return Task.FromResult(new ProtoStringValue { Value = "v" });
        });
        await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "a" }, _ =>
        {
            calls++;
            return Task.FromResult(new ProtoStringValue { Value = "v" });
        });
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    public async Task MissThenHit_ReturnsCachedResponse()
    {
        var calls = 0;
        var interceptor = new CachingInterceptor(new CachingOptions
        {
            Store = new MemoryCache(8),
            KeyScope = _ => "tenant-a",
            MethodConfig = new Dictionary<string, CacheSpec>
            {
                ["/svc.v1.Svc/Get"] = new CacheSpec { Ttl = TimeSpan.FromMinutes(1) },
            },
        });

        var first = await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "k" }, _ =>
        {
            calls++;
            return Task.FromResult(new ProtoStringValue { Value = "from-server" });
        });
        var second = await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "k" }, _ =>
        {
            calls++;
            return Task.FromResult(new ProtoStringValue { Value = "should-not-be-called" });
        });
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(first.Value).IsEqualTo("from-server");
        await Assert.That(second.Value).IsEqualTo("from-server");
    }

    [Test]
    public async Task TenantIsolation_DifferentScopesUseDifferentEntries()
    {
        var calls = 0;
        var scope = "tenant-a";
        var interceptor = new CachingInterceptor(new CachingOptions
        {
            Store = new MemoryCache(8),
            KeyScope = _ => scope,
            MethodConfig = new Dictionary<string, CacheSpec>
            {
                ["/svc.v1.Svc/Get"] = new CacheSpec { Ttl = TimeSpan.FromMinutes(1) },
            },
        });

        await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "k" }, _ =>
        {
            calls++;
            return Task.FromResult(new ProtoStringValue { Value = scope });
        });
        scope = "tenant-b";
        var bResponse = await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "k" }, _ =>
        {
            calls++;
            return Task.FromResult(new ProtoStringValue { Value = scope });
        });

        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(bResponse.Value).IsEqualTo("tenant-b");
    }

    [Test]
    public async Task NegativeCaching_CachesNotFound()
    {
        var calls = 0;
        var store = new MemoryCache(8);
        var interceptor = new CachingInterceptor(new CachingOptions
        {
            Store = store,
            KeyScope = _ => "t",
            MethodConfig = new Dictionary<string, CacheSpec>
            {
                ["/svc.v1.Svc/Get"] = new CacheSpec
                {
                    Ttl = TimeSpan.FromMinutes(1),
                    NegativeTtl = TimeSpan.FromMinutes(1),
                },
            },
        });

        await Assert.That(async () =>
            await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "k" }, _ =>
            {
                calls++;
                return Task.FromException<ProtoStringValue>(
                    new RpcException(new Status(StatusCode.NotFound, "missing")));
            })).Throws<RpcException>();

        var key = CacheKey.Build("t", "/svc.v1.Svc/Get",
            CacheKey.SerializeRequest(new ProtoStringValue { Value = "k" }));
        var lookup = await store.GetAsync(key, CancellationToken.None);
        await Assert.That(lookup.Found).IsTrue();
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task WriteMethod_InvalidatesMatchingReads()
    {
        var store = new MemoryCache(8);
        var interceptor = new CachingInterceptor(new CachingOptions
        {
            Store = store,
            KeyScope = _ => "t",
            MethodConfig = new Dictionary<string, CacheSpec>
            {
                ["/svc.v1.Svc/Get"] = new CacheSpec { Ttl = TimeSpan.FromMinutes(1) },
                ["/svc.v1.Svc/Update"] = new CacheSpec { Invalidates = ["Get"] },
            },
        });

        await Invoke(interceptor, _getMethod, new ProtoStringValue { Value = "k" }, _ =>
            Task.FromResult(new ProtoStringValue { Value = "cached" }));
        await Invoke(interceptor, _updateMethod, new ProtoStringValue { Value = "k" }, _ =>
            Task.FromResult(new ProtoStringValue { Value = "ok" }));

        var key = CacheKey.Build("t", "/svc.v1.Svc/Get",
            CacheKey.SerializeRequest(new ProtoStringValue { Value = "k" }));
        var lookup = await store.GetAsync(key, CancellationToken.None);
        await Assert.That(lookup.Found).IsFalse();
    }

    // ---------- helpers ----------

    private static Task<TResponse> Invoke<TRequest, TResponse>(
        CachingInterceptor interceptor,
        Method<TRequest, TResponse> method,
        TRequest request,
        Func<TRequest, Task<TResponse>> producer)
        where TRequest : class
        where TResponse : class
    {
        var context = new ClientInterceptorContext<TRequest, TResponse>(
            method, host: null, new CallOptions());
        var call = interceptor.AsyncUnaryCall(request, context, (req, ctx) =>
            new AsyncUnaryCall<TResponse>(
                producer(req),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => [],
                () => { }));
        return call.ResponseAsync;
    }
}
