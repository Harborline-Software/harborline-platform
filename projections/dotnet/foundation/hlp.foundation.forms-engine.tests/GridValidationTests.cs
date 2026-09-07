using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class GridValidationTests
{
    private static readonly RuleDefinition BalanceRule = new(
        "debits-equal-credits",
        RuleTier.JsonLogic,
        RuleScope.Table,
        "lines/sum/debit",
        "{\"==\":[{\"var\":\"table.sum(lines.debit)\"},{\"var\":\"table.sum(lines.credit)\"}]}",
        RuleActionKind.Validate);

    [Fact]
    public async Task Unbalanced_entry_blocks_with_the_stable_code_anchored_to_the_table()
    {
        using var result = await EvaluateAsync("""{"lines":[{"debit":100,"credit":0}]}""");

        var balance = Assert.Single(result.Errors, IsBalanceError);
        Assert.Equal("/lines", balance.JsonPointer);
    }

    [Fact]
    public async Task Balanced_entry_does_not_block()
    {
        using var result = await EvaluateAsync("""{"lines":[{"debit":100,"credit":0},{"debit":0,"credit":100}]}""");

        Assert.DoesNotContain(result.Errors, IsBalanceError);
    }

    [Fact]
    public async Task Balanced_fractional_cents_money_entry_does_not_block()
    {
        using var result = await EvaluateAsync(
            """{"lines":[{"debit":"100.10","credit":"0"},{"debit":"200.20","credit":"0"},{"debit":"0","credit":"300.30"}]}""");

        Assert.DoesNotContain(result.Errors, IsBalanceError);
    }

    [Fact]
    public async Task Fractional_cents_money_entry_off_by_one_cent_blocks()
    {
        using var result = await EvaluateAsync(
            """{"lines":[{"debit":"100.10","credit":"0"},{"debit":"0","credit":"100.11"}]}""");

        var balance = Assert.Single(result.Errors, IsBalanceError);
        Assert.Equal("/lines", balance.JsonPointer);
    }

    private static bool IsBalanceError(Harborline.Contracts.Forms.ValidationError error) =>
        error.Code.HasValue && error.Code.Value == BalanceRule.Id;

    private static async Task<FormCandidateEvaluation> EvaluateAsync(string candidateJson)
    {
        var schemas = new InMemorySchemaRegistry();
        var schema = await schemas.RegisterAsync(
            """{"type":"object","properties":{"lines":{"type":"array","items":{"type":"object","properties":{"debit":{},"credit":{}},"required":["debit","credit"],"additionalProperties":false}}},"required":["lines"],"additionalProperties":false}""");
        var definition = JournalDefinition(schema.Id.Value);
        using var candidate = JsonDocument.Parse(candidateJson);
        return await FormCandidateEvaluator.EvaluateAsync(
            new(new TenantId("tenant:acme"), Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice", ["tenant:admin"]),
            definition,
            candidate,
            schemas,
            FormEngineOptions.DefaultMaximumCandidateBytes,
            CancellationToken.None);
    }

    private static FormDefinition JournalDefinition(string schemaId)
    {
        var section = new FormSection(
            "entry",
            InternationalizedText.FromInvariant("Entry"),
            Array.Empty<string>(),
            new([Harborline.Contracts.Authorization.RoleReference.Domain("*")], [Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin")]),
            Items: [FormItem.OfCollection("lines", [FormItem.OfField("debit"), FormItem.OfField("credit")])]);
        var fields = new[] { "debit", "credit" }.ToDictionary(
            field => field,
            field => new FieldOverlay(InternationalizedText.FromInvariant(field)),
            StringComparer.Ordinal);
        return new(
            new("journal-entry"),
            new(1, 0, 0),
            FormDefinitionStatus.Published,
            new("tenant:acme"),
            IdentityRef.System,
            new(schemaId),
            new(fields, [section], [BalanceRule]),
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
    }
}
