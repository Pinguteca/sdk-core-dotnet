using System.Net.Http.Json;

namespace Pinguteca.Sdk.Core.IntegrationTests.Fixtures;

/// <summary>
/// Thin client over FauxRPC's built-in <c>stubs.v1.StubsService</c>,
/// reached through the Connect HTTP/JSON form so the test project
/// avoids pulling in a second proto module and its codegen. Every
/// FauxRPC instance exposes this service on the same port as the
/// data plane; no extra container flags are needed.
/// </summary>
public static class FauxRpcStubs
{
    /// <summary>
    /// Adds a stub that makes the named RPC return the supplied gRPC
    /// error code on every invocation. <paramref name="methodTarget"/>
    /// is the fully-qualified <c>{package}.{Service}/{Method}</c>
    /// string the FauxRPC registry uses internally.
    /// </summary>
    public static async Task AddErrorStubAsync(
        Uri baseAddress,
        string methodTarget,
        string errorCode,
        string message,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            stubs = new[]
            {
                new
                {
                    @ref = new { target = methodTarget },
                    error = new { code = errorCode, message = message },
                },
            },
        };
        await PostAsync(baseAddress, "/stubs.v1.StubsService/AddStubs", payload, cancellationToken);
    }

    /// <summary>
    /// Clears every stub previously registered on the running FauxRPC
    /// instance. Call from test teardown to keep stub state isolated
    /// across test classes that share a container.
    /// </summary>
    public static async Task RemoveAllStubsAsync(
        Uri baseAddress,
        CancellationToken cancellationToken = default)
    {
        await PostAsync(baseAddress, "/stubs.v1.StubsService/RemoveAllStubs", new { }, cancellationToken);
    }

    private static async Task PostAsync(
        Uri baseAddress,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = baseAddress };
        using var response = await http.PostAsJsonAsync(path, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
