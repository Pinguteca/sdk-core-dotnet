// FauxRPC harness AppHost for sdk-core-dotnet integration tests.
//
// RFC 0018 pins the image source as docker.io/sudorandom/fauxrpc and
// mandates digest-only pinning. The image is sourced from Docker Hub
// but resolves identically through Podman, containerd, or any other
// OCI-compatible runtime; consult the runtime's docs for how Aspire's
// DOCKER_HOST setting maps to its socket.

const string FauxRpcImage =
    "docker.io/sudorandom/fauxrpc@sha256:edad8a3d20aab1ae85ca4e80082b0d6b83df7d397628bd67ca37e2bc658aad05";
// Tag at digest pin time: v0.20.1 (2026-05-29).
// Release notes: https://github.com/sudorandom/fauxrpc/releases

var builder = DistributedApplication.CreateBuilder(args);

// Extract the embedded FauxRPC descriptor to a temp path the OCI
// runtime can bind-mount. Embedding it in the AppHost assembly lets
// the path stay valid in both standalone runs and test-host runs
// (where the AppHost DLL lives inside the test project's output).
var schemaPath = ExtractEmbeddedSchema();

builder.AddContainer("fauxrpc", FauxRpcImage)
    .WithBindMount(source: schemaPath, target: "/schema.binpb", isReadOnly: true)
    .WithArgs("run", "--addr=0.0.0.0:6660", "--schema=/schema.binpb")
    .WithHttpEndpoint(targetPort: 6660, name: "rpc");

builder.Build().Run();

static string ExtractEmbeddedSchema()
{
    var assembly = typeof(Program).Assembly;
    using var stream = assembly.GetManifestResourceStream("contract.binpb")
        ?? throw new InvalidOperationException(
            "Embedded contract.binpb not found in AppHost assembly. " +
            "Rebuild after `mise run proto:descriptor`.");

    // Unique path per extraction. TUnit runs test classes in parallel
    // within an assembly, so two concurrent AppHost instances would
    // otherwise race on a shared temp file and one would see
    // "file in use" mid-write. Process exit reaps temp files; an
    // explicit delete here would risk pulling the mount out from
    // under the still-running container.
    var schemaPath = Path.Combine(
        Path.GetTempPath(),
        $"pinguteca-sdk-core-dotnet-fauxrpc-schema-{Guid.NewGuid():N}.binpb");
    using var file = File.Create(schemaPath);
    stream.CopyTo(file);
    return schemaPath;
}
