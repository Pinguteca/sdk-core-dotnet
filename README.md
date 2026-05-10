# sdk-core-dotnet

Layer 2 interceptor library for Pinguteca SDKs in .NET

## Stack

- **.NET 10** with central package management and NuGet lockfiles
- **mise** for tool versions and tasks (`mise.toml`, `mise.ci.toml`)
- **Cocogitto** for conventional commits and semantic versioning
- **prek** for pre-commit hooks (secrets, lockfiles, formatting)
- **Renovate** consuming `github>Pinguteca/renovate-config`
- **Octo STS** for short-lived per-workflow OIDC tokens

## Getting started

```bash
mise install                # installs every tool defined in mise.toml
prek install                # installs git hooks
mise run restore            # restore NuGet packages (also generates lockfile)
mise run build              # build everything
mise run test               # run tests
```

## Common tasks

| Task | Alias | Description |
|------|-------|-------------|
| `mise run build` | `b` | Build the project |
| `mise run test` | `t` | Run tests |
| `mise run lint` |  | Run all linters |
| `mise run secret:scan` | `ss` | Kingfisher secret scan |
| `mise run release:sbom` | `rsbom` | Generate CycloneDX SBOM |
| `mise run bump` |  | Tag the next semver version |

## Release

Push a conventional commit, then `mise run bump`. The release workflow
takes over: SBOM, signatures,
NuGet packages,and a GitHub Release with SLSA L3 provenance.

## License

See [LICENSE](./LICENSE). Pinguteca holds copyright.
