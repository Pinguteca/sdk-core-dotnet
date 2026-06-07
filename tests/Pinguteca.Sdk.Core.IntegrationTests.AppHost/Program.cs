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

builder.AddContainer("fauxrpc", FauxRpcImage)
    .WithBindMount(
        source: Path.Combine(AppContext.BaseDirectory, "schema", "contract.binpb"),
        target: "/schema.binpb",
        isReadOnly: true)
    .WithArgs("run", "--addr=0.0.0.0:6660", "--schema=/schema.binpb")
    .WithHttpEndpoint(targetPort: 6660, name: "rpc");

builder.Build().Run();
