using System.Text.Json;
using Harborline.Contracts.Fields;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Reads the control declarations shipped with this library, never a host's copies.</summary>
internal static class LayoutReleasedFieldControls
{
    internal static IReadOnlyList<LayoutFieldControlDescriptor> Read()
    {
        using var stream = typeof(LayoutReleasedFieldControls).Assembly.GetManifestResourceStream("Harborline.Layout.ReleasedFieldControls")
            ?? throw new InvalidOperationException("The released Layout field-control register is missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("controls").EnumerateArray().Select(control =>
            new LayoutFieldControlDescriptor(
                control.GetProperty("id").GetString()!,
                Array.AsReadOnly(control.GetProperty("valueShapes").EnumerateArray()
                    .Select(shape => Enum.Parse<FieldScalarValueShape>(shape.GetString()!)).ToArray()),
                control.TryGetProperty("parameterSchema", out var schema) ? schema.Clone() : null))
            .ToArray();
    }
}
