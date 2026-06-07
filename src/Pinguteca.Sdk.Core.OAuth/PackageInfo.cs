namespace Pinguteca.Sdk.Core.OAuth;

/// <summary>
/// Marker for the OAuth companion package. Real types land in
/// follow-up PRs implementing the grant flows pinned by cross-SDK
/// RFC 0017. The marker keeps the assembly non-empty so NuGet pack
/// produces a usable artifact during the scaffolding window.
/// </summary>
internal static class PackageInfo
{
    internal const string RfcContract = "sdk-scaffold/docs/rfc/0017-oauth-grant-flows.md";
}
