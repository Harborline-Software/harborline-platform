using System.Diagnostics.Metrics;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Content-free, per-tenant counts of definition admission refusals (DES-0013 ck-8).</summary>
public static class DefinitionRefusalTelemetry
{
    /// <summary>The meter hosts subscribe to; emitting requires no OpenTelemetry SDK.</summary>
    public const string MeterName = "Harborline.Definitions";
    /// <summary>The counter of individual refusals.</summary>
    public const string InstrumentName = "definition.refusals";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Records only refusals with an available opaque tenant and definition id.</summary>
    public static void Record(string? tenantId, string? definitionId, DefinitionRefusalException refusal)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(definitionId)) return;
        Counter<long> refusals;
        try
        {
            refusals = Meter.CreateCounter<long>(InstrumentName);
        }
        catch (Exception)
        {
            // Publication callbacks can throw; retry on the next refusal without poisoning a type initializer.
            return;
        }
        foreach (var item in refusal.Refusals)
        {
            try
            {
                refusals.Add(1,
                    new KeyValuePair<string, object?>("tenant_id", tenantId),
                    new KeyValuePair<string, object?>("stage", refusal.Stage.ToString()),
                    new KeyValuePair<string, object?>("definition_id", definitionId),
                    new KeyValuePair<string, object?>("code", item.Code));
            }
            catch (Exception)
            {
                // A diagnostic listener must never replace an admission refusal.
            }
        }
    }
}
