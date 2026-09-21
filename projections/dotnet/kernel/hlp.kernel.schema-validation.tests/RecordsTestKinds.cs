using Harborline.Foundation.FieldRuntime;

namespace Harborline.Kernel.SchemaValidation.Tests;

internal static class RecordsTestKinds
{
    internal static FieldKindRuntime Text { get; } = Create(new AdmittedFieldKind("text", "1.0.0", null));

    internal static FieldKindRuntime Create(params AdmittedFieldKind[] kinds)
        => new(new Harborline.Foundation.FieldRuntime.FieldKindRegistry(kinds));
}
