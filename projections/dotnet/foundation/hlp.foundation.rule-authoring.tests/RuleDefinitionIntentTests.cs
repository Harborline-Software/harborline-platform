using System.Text.Json.Nodes;

using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Registry;
using Harborline.Foundation.RuleEngine.Skins;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

public sealed class RuleDefinitionIntentTests
{
    [Fact]
    public void AuthoredSourceDoesNotRequireAConsumerResolutionPolicy()
    {
        var source = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(FormulaDocument()))!.AsObject();
        source.Remove("versionPolicy");

        var result = RuleIntentValidator.ValidateJson(source.ToJsonString(), RuleIntentPhase.Author);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        Assert.True(JsonNode.DeepEquals(source,
            JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(result.Document!))));
    }

    [Theory]
    [InlineData(RuleIntentPhase.Author)]
    [InlineData(RuleIntentPhase.Publish)]
    [InlineData(RuleIntentPhase.Persisted)]
    public void ConsumerResolutionPolicyIsAnUnknownSourceMember(RuleIntentPhase phase)
    {
        var source = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(FormulaDocument()))!;
        source["versionPolicy"] = new JsonObject { ["kind"] = "Latest", ["version"] = null };
        var diagnostic = Assert.Single(RuleIntentValidator.ValidateJson(source.ToJsonString(), phase).Diagnostics);
        Assert.Equal(RuleDefinitionCodes.UnknownMember, diagnostic.Code);
        Assert.Equal("/versionPolicy", diagnostic.Location);
        Assert.Equal(phase, diagnostic.Phase);
    }

    [Theory]
    [InlineData("1e300")]
    [InlineData("-1e300")]
    [InlineData("0")]
    public void FiniteProvenanceNumbersRemainAuthoredNumbers(string number)
    {
        string source = RuleDefinitionCodec.SerializeCanonical(FormulaDocument())
            .Replace("\"provenance\":{", "\"provenance\":{\"finite\":" + number + ",", StringComparison.Ordinal);
        var result = RuleIntentValidator.ValidateJson(source, RuleIntentPhase.Author);
        Assert.True(result.IsValid);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source),
            JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(result.Document!))));
    }

    [Fact]
    public void TypedNonfiniteProvenanceRefusesAtTheEscapedAuthoredPointer()
    {
        var document = FormulaDocument();
        document.Envelope.Provenance["a/b~c"] = new JsonArray(double.PositiveInfinity);
        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Publish);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleDefinitionCodes.InvalidDocument, diagnostic.Code);
        Assert.Equal("/envelope/provenance/a~1b~0c/0", diagnostic.Location);
    }

    [Fact]
    public void DuplicateMembersTakePrecedenceOverNonfiniteProvenance()
    {
        string source = RuleDefinitionCodec.SerializeCanonical(FormulaDocument())
            .Replace("\"provenance\":{", "\"provenance\":{\"overflow\":1e400,\"duplicate\":0,\"duplicate\":1,", StringComparison.Ordinal);
        var diagnostic = Assert.Single(RuleIntentValidator.ValidateJson(source, RuleIntentPhase.Author).Diagnostics);
        Assert.Equal(RuleDefinitionCodes.DuplicateMember, diagnostic.Code);
        Assert.Equal("/envelope/provenance/duplicate", diagnostic.Location);
    }

    [Fact]
    public void FormulaDefinitionRoundTripsCanonicalProviderNeutralJson()
    {
        var document = FormulaDocument(new FormulaExpr.Binary(
            ArithOps.Add,
            new FormulaExpr.Ref("field.amount"),
            new FormulaExpr.Literal("2", ColumnValueType.Number)));

        string canonical = RuleDefinitionCodec.SerializeCanonical(document);
        var parsed = RuleDefinitionCodec.Parse(canonical, RuleIntentPhase.Author);

        Assert.True(parsed.IsValid);
        Assert.NotNull(parsed.Document);
        Assert.Equal(canonical, RuleDefinitionCodec.SerializeCanonical(parsed.Document));
        Assert.Equal("invoice-total", parsed.Document.Envelope.Id);
        Assert.Equal("1.2.3", parsed.Document.Envelope.Version.ToString());
        Assert.DoesNotContain("versionPolicy", RuleDefinitionCodec.SerializeCanonical(parsed.Document), StringComparison.Ordinal);
        Assert.IsType<FormulaDraft>(parsed.Document.Draft);
    }

    [Fact]
    public void DecisionTableDefinitionRoundTripsCanonicalProviderNeutralJson()
    {
        var table = RuleSeeds.BlankTableDraft();
        string columnId = table.Columns[0].Id;
        var document = Document(table with
        {
            Rows = new[]
            {
                new TableRow(
                    "row-1",
                    new Dictionary<string, TableCell>
                    {
                        [columnId] = new TableCell.Range("0", "100"),
                    },
                    "low",
                    1),
            },
            NoMatch = new NoMatchPosture.Default("high"),
        });

        string canonical = RuleDefinitionCodec.SerializeCanonical(document);
        var parsed = RuleDefinitionCodec.Parse(canonical, RuleIntentPhase.Author);

        Assert.True(parsed.IsValid);
        Assert.Equal(canonical, RuleDefinitionCodec.SerializeCanonical(parsed.Document!));
        Assert.IsType<DecisionTableDraft>(parsed.Document!.Draft);
    }

    [Theory]
    [InlineData("1.0.0-alpha.10")]
    [InlineData("1.0.0+build.01")]
    [InlineData("2147483648.0.0")]
    public void CodecPreservesGenericStoreVersionLabelsWithoutAnInt32OnlyParser(string version)
    {
        var source = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(FormulaDocument()))!.AsObject();
        source["envelope"]!["version"] = version;

        var result = RuleDefinitionCodec.Parse(source.ToJsonString(), RuleIntentPhase.Author);

        Assert.True(result.IsValid);
        Assert.Equal(version, result.Document!.Envelope.Version.ToString());
        var roundTrip = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(result.Document))!;
        Assert.Equal(version, roundTrip["envelope"]!["version"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("tier", "not-a-tier", RuleDefinitionCodes.InvalidTier, "/tier")]
    [InlineData("scope", "not-a-scope", RuleDefinitionCodes.InvalidScope, "/draft/scope")]
    [InlineData("outputType", "not-an-action", RuleDefinitionCodes.InvalidAction, "/draft/outputType")]
    public void MalformedClosedDiscriminantsRefuseWithoutNormalization(
        string member,
        string malformed,
        string expectedCode,
        string expectedLocation)
    {
        var json = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(FormulaDocument()))!.AsObject();
        if (member == "tier") json[member] = malformed;
        else json["draft"]![member] = malformed;

        var result = RuleDefinitionCodec.Parse(json.ToJsonString(), RuleIntentPhase.Author);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal(expectedLocation, diagnostic.Location);
        Assert.Equal(RuleIntentPhase.Author, diagnostic.Phase);
        Assert.Null(result.Document);
    }

    [Fact]
    public void MalformedCellKindAndNumericEndpointRefuseAtTheirJsonPointers()
    {
        var table = RuleSeeds.BlankTableDraft();
        string columnId = table.Columns[0].Id;
        var document = Document(table with
        {
            Rows = new[]
            {
                new TableRow(
                    "row-1",
                    new Dictionary<string, TableCell>
                    {
                        [columnId] = new TableCell.Range("0", "100"),
                    },
                    "low",
                    0),
            },
            NoMatch = new NoMatchPosture.Default("high"),
        });
        var badKind = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(document))!.AsObject();
        badKind["draft"]!["rows"]![0]!["cells"]![columnId]!["kind"] = "mystery";
        var badEndpoint = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(document))!.AsObject();
        badEndpoint["draft"]!["rows"]![0]!["cells"]![columnId]!["hi"] = "not-a-number";

        var kindResult = RuleDefinitionCodec.Parse(badKind.ToJsonString(), RuleIntentPhase.Publish);
        var endpointResult = RuleDefinitionCodec.Parse(badEndpoint.ToJsonString(), RuleIntentPhase.Publish);

        Assert.Collection(kindResult.Diagnostics, diagnostic =>
        {
            Assert.Equal(RuleDefinitionCodes.InvalidCellKind, diagnostic.Code);
            Assert.Equal($"/draft/rows/0/cells/{columnId}/kind", diagnostic.Location);
        });
        Assert.Collection(endpointResult.Diagnostics, diagnostic =>
        {
            Assert.Equal(RuleDefinitionCodes.InvalidNumericEndpoint, diagnostic.Code);
            Assert.Equal($"/draft/rows/0/cells/{columnId}/hi", diagnostic.Location);
        });
    }

    [Theory]
    [InlineData("retentionClass")]
    [InlineData("legalHold")]
    public void PolicyOwnedEnvelopeMembersAreNotAuthored(string member)
    {
        var json = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(FormulaDocument()))!.AsObject();
        json["envelope"]![member] = "tenant-choice";

        var result = RuleDefinitionCodec.Parse(json.ToJsonString(), RuleIntentPhase.Author);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleDefinitionCodes.UnknownMember, diagnostic.Code);
        Assert.Equal($"/envelope/{member}", diagnostic.Location);
        Assert.Null(result.Document);
    }

    [Fact]
    public void AnUndeclaredCellCannotDisappearDuringLowering()
    {
        var table = RuleSeeds.BlankTableDraft();
        var document = Document(table with
        {
            Rows = new[]
            {
                new TableRow("row-1", new Dictionary<string, TableCell>
                {
                    [table.Columns[0].Id] = new TableCell.Any(),
                    ["missing/column"] = new TableCell.Compare(CompareOps.Eq, "restricted"),
                }, "first", 0),
            },
            NoMatch = new NoMatchPosture.Default("otherwise"),
        });

        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Publish);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("rule.skin.decision_table_bad_cell", diagnostic.Code);
        Assert.Equal("/draft/rows/0/cells/missing~1column", diagnostic.Location);
    }

    [Fact]
    public void DuplicateWireMembersRefuseInsteadOfSelectingTheLastValue()
    {
        string json = RuleDefinitionCodec.SerializeCanonical(FormulaDocument());
        json = json.Replace("\"tier\":\"JsonLogic\"", "\"tier\":\"PowerFx\",\"tier\":\"JsonLogic\"", StringComparison.Ordinal);

        var result = RuleIntentValidator.ValidateJson(json, RuleIntentPhase.Publish);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleDefinitionCodes.DuplicateMember, diagnostic.Code);
        Assert.Equal("/tier", diagnostic.Location);
    }

    [Fact]
    public void AuthorAndPublishUseTheSameDiagnosticContract()
    {
        var json = JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(FormulaDocument()))!.AsObject();
        json["draft"]!["scope"] = "not-a-scope";

        var author = RuleIntentValidator.ValidateJson(json.ToJsonString(), RuleIntentPhase.Author);
        var publish = RuleIntentValidator.ValidateJson(json.ToJsonString(), RuleIntentPhase.Publish);

        var authorDiagnostic = Assert.Single(author.Diagnostics);
        var publishDiagnostic = Assert.Single(publish.Diagnostics);
        Assert.Equal(authorDiagnostic.Code, publishDiagnostic.Code);
        Assert.Equal(authorDiagnostic.Location, publishDiagnostic.Location);
        Assert.Equal(RuleIntentPhase.Author, authorDiagnostic.Phase);
        Assert.Equal(RuleIntentPhase.Publish, publishDiagnostic.Phase);
    }

    [Fact]
    public void LimitsComeFromTheEngineSchemaContract()
    {
        var actual = RuleIntentSchema.Current.Limits;
        var engine = RuleEngineLimits.Default;

        Assert.Equal(engine.MaxGraphNodes, actual.MaxGraphNodes);
        Assert.Equal(engine.MaxTableRowsPerAggregate, actual.MaxTableRowsPerAggregate);
        Assert.Equal(engine.MaxDependencyDepth, actual.MaxDependencyDepth);
        Assert.Equal(engine.MaxReferencesPerRule, actual.MaxReferencesPerRule);
        Assert.Equal(engine.MaxAstNodes, actual.MaxAstNodes);
        Assert.Equal(engine.MaxLiteralLength, actual.MaxLiteralLength);
        Assert.Equal(engine.StepBudget, actual.StepBudget);
        Assert.Equal(engine.WallClockCeiling, actual.WallClockCeiling);
    }

    [Fact]
    public void LiteralAtTheLimitPassesAndOnePastItRefusesAtTheExpression()
    {
        var atLimit = FormulaDocument(new FormulaExpr.Literal(new string('x', 4_096), ColumnValueType.Text));
        var overLimit = FormulaDocument(new FormulaExpr.Literal(new string('x', 4_097), ColumnValueType.Text));

        var accepted = RuleIntentValidator.Validate(atLimit, RuleIntentPhase.Publish);
        var refused = RuleIntentValidator.Validate(overLimit, RuleIntentPhase.Publish);

        Assert.True(accepted.IsValid);
        var diagnostic = Assert.Single(refused.Diagnostics);
        Assert.Equal(RuleEngineCodes.CompileLiteralTooLong, diagnostic.Code);
        Assert.Equal("/draft/expression", diagnostic.Location);
    }

    [Fact]
    public void InvalidPersistedSourceDiagnosesInsteadOfClamping()
    {
        var document = FormulaDocument(new FormulaExpr.Literal(new string('x', 4_097), ColumnValueType.Text));

        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Persisted);

        Assert.False(result.IsValid);
        Assert.Null(result.Document);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleEngineCodes.CompileLiteralTooLong, diagnostic.Code);
        Assert.Equal(RuleIntentPhase.Persisted, diagnostic.Phase);
        Assert.Equal(4_097, Assert.IsType<FormulaExpr.Literal>(Assert.IsType<FormulaDraft>(document.Draft).Expression).Value.Length);
    }

    [Fact]
    public void IntentAdmits256AstNodesAndRefuses257WithoutChangingTheSource()
    {
        static FormulaExpr Sum(FormulaExpr first)
        {
            FormulaExpr Build(int start, int count)
            {
                if (count == 1)
                    return start == 0 ? first : new FormulaExpr.Literal("1", ColumnValueType.Number);
                int left = count / 2;
                return new FormulaExpr.Binary(ArithOps.Add, Build(start, left), Build(start + left, count - left));
            }
            return Build(0, 86);
        }
        // A literal is one node; a var is two. Each binary adds an object and array.
        var atLimit = FormulaDocument(Sum(new FormulaExpr.Literal("1", ColumnValueType.Number)));
        var overLimit = FormulaDocument(Sum(new FormulaExpr.Ref("field.amount")));
        string source = RuleDefinitionCodec.SerializeCanonical(overLimit);

        Assert.True(RuleIntentValidator.Validate(atLimit, RuleIntentPhase.Publish).IsValid);
        var result = RuleIntentValidator.Validate(overLimit, RuleIntentPhase.Publish);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleEngineCodes.CompileAstTooLarge, diagnostic.Code);
        Assert.Equal("/draft/expression", diagnostic.Location);
        Assert.Equal(source, RuleDefinitionCodec.SerializeCanonical(overLimit));
    }

    [Theory]
    [InlineData("regex")]
    [InlineData("map")]
    [InlineData("not-an-operator")]
    public void UnsupportedFormulaOperatorsRefuseAtTheAuthoredOperator(string op)
    {
        var document = FormulaDocument(new FormulaExpr.Binary(op,
            new FormulaExpr.Literal("1", ColumnValueType.Number),
            new FormulaExpr.Literal("2", ColumnValueType.Number)));

        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Publish);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleEngineCodes.CompileInvalidExpression, diagnostic.Code);
        Assert.Equal("/draft/expression/op", diagnostic.Location);
    }

    [Theory]
    [InlineData(ColumnValueType.Number, "not-a-number")]
    [InlineData(ColumnValueType.Number, "Infinity")]
    [InlineData(ColumnValueType.Number, "NaN")]
    [InlineData(ColumnValueType.Boolean, "tru")]
    [InlineData(ColumnValueType.Boolean, "TRUE")]
    public void MalformedTypedLiteralsRefuseInsteadOfChangingTheirMeaning(ColumnValueType type, string value)
    {
        var document = FormulaDocument(new FormulaExpr.Literal(value, type));

        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Author);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleEngineCodes.CompileInvalidExpression, diagnostic.Code);
        Assert.Equal("/draft/expression/value", diagnostic.Location);
    }

    [Fact]
    public void NonterminalWildcardIsAnAdvisoryLintMatterNotAnAdmissionRefusal()
    {
        var table = RuleSeeds.BlankTableDraft();
        var document = Document(table with
        {
            Rows = new[]
            {
                new TableRow("wildcard", new Dictionary<string, TableCell>(), "first", 10),
                new TableRow("shadowed", new Dictionary<string, TableCell>
                {
                    [table.Columns[0].Id] = new TableCell.Range("0", "1"),
                }, "second", 1),
            },
            NoMatch = new NoMatchPosture.CatchAll(),
        });

        Assert.True(RuleIntentValidator.Validate(document, RuleIntentPhase.Publish).IsValid);
    }

    [Fact]
    public void AJsonLogicSkinCannotBeRelabeledAsTheKernelJsonSchemaTier()
    {
        var document = FormulaDocument() with { Tier = RuleDefinitionTier.JsonSchema };

        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Publish);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleEngineCodes.CompileUnsupportedTier, diagnostic.Code);
        Assert.Equal("/tier", diagnostic.Location);
    }

    [Fact]
    public void UnsupportedIfComparisonRefusesAtTheAuthoredOperator()
    {
        var document = FormulaDocument(new FormulaExpr.If(
            new FormulaCondition(new FormulaExpr.Literal("1", ColumnValueType.Number),
                "regex", new FormulaExpr.Literal("1", ColumnValueType.Number)),
            new FormulaExpr.Literal("yes", ColumnValueType.Text),
            new FormulaExpr.Literal("no", ColumnValueType.Text)));

        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Author);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuleEngineCodes.CompileInvalidExpression, diagnostic.Code);
        Assert.Equal("/draft/expression/when/op", diagnostic.Location);
    }

    [Theory]
    [InlineData(ColumnValueType.Number, "not-a-number")]
    [InlineData(ColumnValueType.Boolean, "tru")]
    public void MalformedTypedComparisonCannotWidenATableCondition(ColumnValueType type, string value)
    {
        var table = RuleSeeds.BlankTableDraft() with
        {
            Columns = new[] { new ConditionColumn("input", "field.amount", type) },
            Rows = new[]
            {
                new TableRow("conditional", new Dictionary<string, TableCell>
                {
                    ["input"] = new TableCell.Compare(CompareOps.Eq, value),
                }, "match", 0),
            },
            NoMatch = new NoMatchPosture.Default("otherwise"),
        };

        var result = RuleIntentValidator.Validate(Document(table), RuleIntentPhase.Publish);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(SkinCodes.DecisionTableBadCell, diagnostic.Code);
        Assert.Equal("/draft/rows/0/cells/input/value", diagnostic.Location);
    }

    [Fact]
    public void UnsupportedTierAndSelfCycleRefuseBeforePersistence()
    {
        var unsupported = FormulaDocument() with { Tier = RuleDefinitionTier.PowerFx };
        var cycle = FormulaDocument(new FormulaExpr.Ref("field.total"));
        cycle = cycle with
        {
            Draft = ((FormulaDraft)cycle.Draft) with
            {
                Inputs = new[] { new FormulaInputDecl("total", "field.total", ColumnValueType.Number) },
            },
        };

        var unsupportedResult = RuleIntentValidator.Validate(unsupported, RuleIntentPhase.Publish);
        var cycleResult = RuleIntentValidator.Validate(cycle, RuleIntentPhase.Publish);

        Assert.Equal(RuleEngineCodes.CompileUnsupportedTier, Assert.Single(unsupportedResult.Diagnostics).Code);
        Assert.Equal("/tier", Assert.Single(unsupportedResult.Diagnostics).Location);
        Assert.Equal(RuleEngineCodes.CompileCycle, Assert.Single(cycleResult.Diagnostics).Code);
        Assert.Equal("/draft/expression", Assert.Single(cycleResult.Diagnostics).Location);
    }

    private static RuleDefinitionDocument FormulaDocument(FormulaExpr? expression = null)
    {
        var draft = RuleSeeds.BlankFormulaDraft() with
        {
            ScopeTarget = "total",
            Inputs = new[] { new FormulaInputDecl("amount", "field.amount", ColumnValueType.Number) },
            Expression = expression ?? new FormulaExpr.Ref("field.amount"),
        };
        return Document(draft);
    }

    private static RuleDefinitionDocument Document(RuleDraft draft)
        => new(
            new RuleDefinitionEnvelope(
                "invoice-total",
                "1.2.3",
                "tenant-a",
                "domain-package",
                new JsonObject { ["kind"] = "package", ["id"] = "finance" },
                new[] { "records.invoice@2.0.0" }),
            "Invoice total",
            RuleDefinitionTier.JsonLogic,
            draft);
}
