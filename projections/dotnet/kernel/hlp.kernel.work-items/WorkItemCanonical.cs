using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Kernel.WorkItems;

internal static class WorkItemCanonical
{
    public static string CreateFingerprint(string tenantId, CreateWorkItemRequest request) => Hash(new
    {
        operation = "create",
        tenantId,
        request.Id,
        request.SubjectRef,
        request.DefinitionKey,
        request.DefinitionVersion,
        request.InitialStep,
        status = (int)request.InitialStatus,
        state = NormalizeJson(request.StateJson),
        basis = NormalizeJson(request.BasisJson),
        outcomes = request.AllowedOutcomes
            .OrderBy(outcome => outcome.FromStep, StringComparer.Ordinal)
            .ThenBy(outcome => outcome.Id, StringComparer.Ordinal)
            .Select(outcome => new
            {
                outcome.Id,
                outcome.FromStep,
                outcome.NextStep,
                status = (int)outcome.NextStatus,
                outcome.IsLoopBack,
            }).ToArray(),
    });

    public static string TransitionFingerprint(string tenantId, TransitionWorkItemRequest request) => Hash(new
    {
        operation = "transition",
        tenantId,
        request.Id,
        request.ExpectedVersion,
        request.OutcomeId,
        result = NormalizeJson(request.ResultJson),
    });

    public static string StepKey(string tenantId, WorkItemSnapshot snapshot) => $"wi1:{Hash(new
    {
        tenantId,
        workItemId = snapshot.Id,
        snapshot.Iteration,
        step = snapshot.CurrentStep,
    })}";

    public static string NormalizeJson(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        using var document = JsonDocument.Parse(value);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, document.RootElement);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Hash(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("Unsupported JSON token in work-item content.");
        }
    }
}
