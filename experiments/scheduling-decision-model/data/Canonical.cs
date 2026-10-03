using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harborline.Blocks.Scheduling.Planning;

namespace Harborline.Experiments.SchedulingDecisionModel.Data;

/// <summary>Canonical JSON for a profile: every collection sorted by ordinal id, so the hash is order-independent.</summary>
public static class Canonical
{
    public static string Json(SchedulingProfile p)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("profileVersion", "sdm.unit-resource.v1");
            w.WriteString("profileId", p.ProfileId);
            w.WriteString("inputVersion", p.InputVersion);

            w.WriteStartArray("activities");
            foreach (var a in p.Activities.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var window = p.TimeWindows.Single(t => t.ActivityId == a.Id);
                w.WriteStartObject();
                w.WriteString("id", a.Id);
                w.WriteNumber("duration", a.DurationSlots);
                w.WriteNumber("earliestStart", window.EarliestStartSlot);
                w.WriteNumber("latestStart", window.LatestStartSlot);
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("resources");
            foreach (var r in p.Resources.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("id", r.Id);
                w.WriteStartArray("capabilities");
                foreach (var c in r.Capabilities.Order(StringComparer.Ordinal))
                {
                    w.WriteStringValue(c);
                }

                w.WriteEndArray();
                w.WriteStartArray("availableSlots");
                foreach (var s in r.AvailableSlots.Order())
                {
                    w.WriteNumberValue(s);
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("requirements");
            foreach (var q in p.ResourceRequirements.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("id", q.Id);
                w.WriteString("activity", q.ActivityId);
                w.WriteString("capability", q.Capability);
                w.WriteNumber("quantity", q.Quantity);
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("precedence");
            foreach (var c in p.Precedence.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                w.WriteStartObject();
                w.WriteString("id", c.Id);
                w.WriteString("before", c.BeforeActivityId);
                w.WriteString("after", c.AfterActivityId);
                w.WriteNumber("gap", c.MinimumGapSlots);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
