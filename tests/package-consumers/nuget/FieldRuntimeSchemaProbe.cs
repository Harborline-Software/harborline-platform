using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Harborline.Contracts.Fields;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;

internal static class FieldRuntimeSchemaProbe
{
    internal static async Task VerifyAsync()
    {
        var runtime = new FieldKindRuntime(new FieldKindRegistry(new[]
        {
            new AdmittedFieldKind("amount", "1.0.0", null, FieldScalarValueShape.Number),
        }));
        var narrow = Project(runtime, "3");
        var wide = Project(runtime, "4");

        foreach (var reverse in new[] { false, true })
        {
            var schemas = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
            var first = await schemas.RegisterAsync(reverse ? wide : narrow);
            var second = await schemas.RegisterAsync(reverse ? narrow : wide);
            var narrowId = reverse ? second.Id : first.Id;
            var wideId = reverse ? first.Id : second.Id;
            if (narrowId == wideId)
                throw new InvalidOperationException("Packed schemas lost the complete field-kind identity.");

            await RefusesAsync(schemas, narrowId, "123.4", "field.total_digits_exceeded");
            await RefusesAsync(schemas, narrowId, "12.30", "field.fraction_digits_exceeded");
            if (!(await ValidateAsync(schemas, narrowId, "12.3")).IsValid
                || !(await ValidateAsync(schemas, wideId, "123.4")).IsValid)
                throw new InvalidOperationException("Packed schema bindings did not retain independent kind limits.");
        }

        var refusedMissingRuntime = false;
        try { await new InMemorySchemaRegistry().RegisterAsync(narrow); }
        catch (InvalidSchemaException) { refusedMissingRuntime = true; }
        if (!refusedMissingRuntime)
            throw new InvalidOperationException("Packed schema registration ignored an executable kind binding.");

        Console.WriteLine("FIELD_RUNTIME_SCHEMA_PACKAGE_PASS: distinct kind identities, executable limits, original numeric spelling, runtime-required admission");
    }

    private static string Project(IFieldKindRuntime runtime, string totalDigits)
    {
        var kind = runtime.Bind(new FieldKindReference("amount", "1.0.0", new Dictionary<string, string>
        {
            ["total_digits"] = totalDigits,
            ["fraction_digits"] = "1",
        }), "/kind");
        return "{\"type\":\"object\",\"properties\":{\"amount\":" + kind.JsonSchema.GetRawText() + "}}";
    }

    private static ValueTask<SchemaValidationResult> ValidateAsync(ISchemaRegistry schemas, SchemaId id, string number)
        => schemas.ValidateAsync(id, Encoding.UTF8.GetBytes("{\"amount\":" + number + "}"));

    private static async Task RefusesAsync(ISchemaRegistry schemas, SchemaId id, string number, string code)
    {
        var result = await ValidateAsync(schemas, id, number);
        if (result.IsValid || !result.Errors.Any(error => error.Code == code && error.JsonPointer == "/amount"))
            throw new InvalidOperationException("Packed field binding failed to return " + code + " at /amount.");
    }
}
