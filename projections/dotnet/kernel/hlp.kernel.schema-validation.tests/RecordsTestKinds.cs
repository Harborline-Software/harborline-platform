using Harborline.Kernel.SchemaValidation;

namespace Harborline.Kernel.SchemaValidation.Tests;

internal static class RecordsTestKinds
{
    internal static FieldKindRegistry Text { get; } = new([new("text", "1.0.0", null)]);
}
