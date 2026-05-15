namespace Pinguteca.Sdk.Core.Otel;

/// <summary>
/// Knobs for <see cref="OtelInterceptor"/>. The activity source name
/// drives the <see cref="System.Diagnostics.ActivitySource"/> consumers
/// subscribe to via the OpenTelemetry SDK
/// (<c>AddSource("Pinguteca.Sdk.Core")</c> by default).
/// </summary>
public sealed class OtelOptions
{
    public string ActivitySourceName { get; init; } = "Pinguteca.Sdk.Core";
}
