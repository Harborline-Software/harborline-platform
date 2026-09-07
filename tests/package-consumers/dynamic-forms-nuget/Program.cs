// Dynamic Forms capability vertical — engine half (ticket 073).
// Package-only: the single direct PackageReference is Harborline.Foundation.Forms.Engine; the
// application-derived F-20 parity corpus (cases.json) is the pinned source ledger. Validates and
// submits every corpus case through the IFormEngine caller interface, then cross-checks the
// packed reference-lane renderer's per-case verdicts (client-verdicts.json) against the
// engine's accepted values, error codes, pruned keys, and fail-closed submit gate.
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.SchemaValidation;

if (typeof(FormEngine).Assembly.GetName().Name != "Harborline.Foundation.Forms.Engine")
    throw new InvalidOperationException("Forms Engine assembly identity changed.");
if (typeof(IFormDefinitionStore).Assembly.GetName().Name != "Harborline.Foundation.Forms")
    throw new InvalidOperationException("Forms state assembly identity changed.");
if (typeof(ISchemaRegistry).Assembly.GetName().Name != "Harborline.Kernel.SchemaValidation")
    throw new InvalidOperationException("Schema validation assembly identity changed.");

using var corpus = JsonDocument.Parse(File.ReadAllText("cases.json"));
using var clientVerdicts = JsonDocument.Parse(File.ReadAllText("client-verdicts.json"));
var corpusRoot = corpus.RootElement;
var schemaProperties = corpusRoot.GetProperty("schema").GetProperty("properties")
    .EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

var schemas = new InMemorySchemaRegistry();
var schema = await schemas.RegisterAsync(corpusRoot.GetProperty("schema").GetRawText());
var definitionStore = new InMemoryFormDefinitionStore();

var caseRows = corpusRoot.GetProperty("cases").EnumerateArray().ToArray();
for (var index = 0; index < caseRows.Length; index += 1)
{
    await definitionStore.RegisterAndPublishAsync(BuildDefinition(
        corpusRoot,
        $"parity-{index}",
        schema.Id.Value,
        caseRows[index].GetProperty("rules").EnumerateArray().Select(value => value.GetString()!).ToArray()));
}

var sink = new RecordingProjectionSink();
var services = new ServiceCollection();
services.AddSingleton<IFormExecutionContextProvider, ConsumerExecutionContext>();
services.AddSingleton<IFormDefinitionStore>(definitionStore);
services.AddSingleton<IReuseResolver, IdentityReuseResolver>();
services.AddSingleton<ISchemaRegistry>(schemas);
services.AddSingleton<IFormFieldSecurity, PassThroughFieldSecurity>();
services.AddSingleton<IFormSensitiveReadAudit, NullReadAudit>();
services.AddSingleton(sink);
services.AddSingleton<IFormProjectionSink>(provider => provider.GetRequiredService<RecordingProjectionSink>());
services.AddHarborlineFormsEngineInMemorySubmissionStore(FormEngineHostEnvironment.Test);
services.AddHarborlineFormsEngine(FormEngineHostEnvironment.Test);
await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var engine = scope.ServiceProvider.GetRequiredService<IFormEngine>();

var submittedCases = 0;
var refusedCases = 0;
EntityId? cleanInstance = null;
FormSubmitReceipt? cleanReceipt = null;
string? cleanDefinitionId = null;
JsonDocument? cleanCandidate = null;
for (var index = 0; index < caseRows.Length; index += 1)
{
    var row = caseRows[index];
    var name = row.GetProperty("name").GetString()!;
    var expected = row.GetProperty("expected");
    var expectedCodes = (expected.TryGetProperty("errorCodesServer", out var serverCodes)
            ? serverCodes
            : expected.GetProperty("errorCodes"))
        .EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal).ToArray();
    var expectedPruned = expected.GetProperty("prunedKeys")
        .EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal).ToArray();
    var definitionId = new FormDefinitionId($"parity-{index}");
    var candidate = JsonDocument.Parse(row.GetProperty("candidate").GetRawText());
    var candidateKeys = candidate.RootElement.EnumerateObject().Select(property => property.Name)
        .ToHashSet(StringComparer.Ordinal);

    var validation = await engine.ValidateAsync(definitionId, candidate);
    var actualCodes = validation.Errors
        .Select(error => error.Code.HasValue ? error.Code.Value! : "")
        .Order(StringComparer.Ordinal).ToArray();
    if (!actualCodes.SequenceEqual(expectedCodes))
        throw new InvalidOperationException(
            $"Case '{name}': packaged engine codes [{string.Join(",", actualCodes)}] != ledger [{string.Join(",", expectedCodes)}].");

    if (expectedCodes.Length == 0)
    {
        var receipt = await engine.SubmitAsync(new(definitionId, candidate, $"case-{index}", $"case-ref-{index}"));
        if (receipt.ProjectionStatus != FormProjectionStatus.Complete || receipt.ProjectionSkips.Count != 0)
            throw new InvalidOperationException($"Case '{name}': projection was not cleanly delivered.");
        var envelope = sink.Envelopes.Single(entry => entry.FormId == definitionId);
        if (envelope.CaseReference != $"case-ref-{index}" || envelope.SubmittedAt != receipt.SubmittedAt)
            throw new InvalidOperationException($"Case '{name}': outbox envelope lost the case reference or submit instant.");
        using var accepted = JsonDocument.Parse(envelope.ProtectedAcceptedValues.ToArray());
        var acceptedKeys = accepted.RootElement.EnumerateObject().Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var actualPruned = candidateKeys.Where(key => !acceptedKeys.Contains(key))
            .Order(StringComparer.Ordinal).ToArray();
        if (!actualPruned.SequenceEqual(expectedPruned))
            throw new InvalidOperationException(
                $"Case '{name}': packaged engine pruned [{string.Join(",", actualPruned)}] != ledger [{string.Join(",", expectedPruned)}].");
        submittedCases += 1;
        if (name.StartsWith("clean pass", StringComparison.Ordinal))
        {
            cleanInstance = receipt.InstanceId;
            cleanReceipt = receipt;
            cleanDefinitionId = definitionId.Value;
            cleanCandidate = candidate;
        }
    }
    else
    {
        try
        {
            await engine.SubmitAsync(new(definitionId, candidate, $"case-{index}-refused"));
            throw new InvalidOperationException($"Case '{name}': the packaged engine accepted an invalid submission.");
        }
        catch (FormEngineValidationException refusal)
        {
            var refusedCodes = refusal.Errors
                .Select(error => error.Code.HasValue ? error.Code.Value! : "")
                .Order(StringComparer.Ordinal).ToArray();
            if (!refusedCodes.SequenceEqual(expectedCodes))
                throw new InvalidOperationException($"Case '{name}': submit refusal codes diverged from validate codes.");
        }
        refusedCases += 1;
    }
}

if (cleanInstance is null || cleanReceipt is null || cleanDefinitionId is null || cleanCandidate is null)
    throw new InvalidOperationException("The corpus clean-pass case did not submit.");

// Idempotent replay: same key + same candidate returns the original receipt without redelivery.
var envelopesBeforeReplay = sink.Envelopes.Count;
var cleanIndex = Array.FindIndex(caseRows, row => row.GetProperty("name").GetString()!.StartsWith("clean pass", StringComparison.Ordinal));
var replay = await engine.SubmitAsync(new(new($"parity-{cleanIndex}"), cleanCandidate, $"case-{cleanIndex}", $"case-ref-{cleanIndex}"));
if (replay.InstanceId != cleanReceipt.InstanceId || replay.SubmittedAt != cleanReceipt.SubmittedAt
    || sink.Envelopes.Count != envelopesBeforeReplay)
    throw new InvalidOperationException("Idempotent replay did not return the original receipt without redelivery.");

// Render round-trip: the submitted instance renders back through the caller interface with the
// accepted values the reference lane's FormView contract consumes.
var rendered = await engine.RenderAsync(new(cleanDefinitionId), cleanInstance);
var renderedFields = rendered.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Name);
foreach (var property in cleanCandidate.RootElement.EnumerateObject())
{
    if (!renderedFields.TryGetValue(property.Name, out var field) || !field.Value.HasValue
        || field.Value.Value.GetRawText() != property.Value.GetRawText())
        throw new InvalidOperationException($"Rendered instance lost accepted value '{property.Name}'.");
}

// Cross-lane parity: the packed renderer's verdicts must agree with the packaged engine.
var verdictByName = clientVerdicts.RootElement.EnumerateArray()
    .ToDictionary(verdict => verdict.GetProperty("name").GetString()!, StringComparer.Ordinal);
var comparedVerdicts = 0;
for (var index = 0; index < caseRows.Length; index += 1)
{
    var row = caseRows[index];
    var name = row.GetProperty("name").GetString()!;
    if (!verdictByName.TryGetValue(name, out var verdict))
        throw new InvalidOperationException($"The reference lane produced no verdict for case '{name}'.");
    var expected = row.GetProperty("expected");
    var expectedCodes = (expected.TryGetProperty("errorCodesServer", out var serverCodes)
            ? serverCodes
            : expected.GetProperty("errorCodes"))
        .EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
    var expectedPruned = expected.GetProperty("prunedKeys")
        .EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
    var selectedRules = row.GetProperty("rules").EnumerateArray().Select(value => value.GetString()!)
        .ToHashSet(StringComparer.Ordinal);
    var clientHidden = verdict.GetProperty("hiddenFields").EnumerateArray()
        .Select(value => value.GetString()!).ToArray();
    foreach (var hidden in clientHidden)
    {
        if (!expectedPruned.Contains(hidden))
            throw new InvalidOperationException(
                $"Case '{name}': renderer hid '{hidden}' but the packaged engine did not prune it.");
    }
    var clientSaveBlocked = verdict.GetProperty("saveBlocked").GetBoolean();
    var serverRuleBlocked = expectedCodes.Overlaps(selectedRules);
    if (clientSaveBlocked != serverRuleBlocked)
        throw new InvalidOperationException(
            $"Case '{name}': renderer save gate ({clientSaveBlocked}) disagrees with engine rule blockers ({serverRuleBlocked}).");
    comparedVerdicts += 1;
}
var ruleHiddenVerdict = verdictByName.Single(entry => entry.Key.Contains("rule-HIDDEN required field", StringComparison.Ordinal)).Value;
if (!ruleHiddenVerdict.GetProperty("hiddenFields").EnumerateArray().Select(value => value.GetString()).SequenceEqual(["legalName"]))
    throw new InvalidOperationException("Renderer and engine disagree on the rule-hidden required field.");
var hiddenSectionVerdict = verdictByName.Single(entry => entry.Key.Contains("HIDDEN SECTION", StringComparison.Ordinal)).Value;
var hiddenSectionExpectedPruned = caseRows
    .Single(row => row.GetProperty("name").GetString()!.Contains("HIDDEN SECTION", StringComparison.Ordinal))
    .GetProperty("expected").GetProperty("prunedKeys").EnumerateArray()
    .Select(value => value.GetString()!).Where(schemaProperties.Contains).Order(StringComparer.Ordinal).ToArray();
if (!hiddenSectionVerdict.GetProperty("hiddenFields").EnumerateArray().Select(value => value.GetString()!)
        .Order(StringComparer.Ordinal).SequenceEqual(hiddenSectionExpectedPruned))
    throw new InvalidOperationException("Renderer and engine disagree on the hidden section's pruned fields.");

Console.WriteLine(
    $"DYNAMIC_FORMS_PACKAGE_PASS: packaged Forms Engine validated {caseRows.Length} App parity-corpus cases through IFormEngine, submitted {submittedCases} with ledger-exact pruning, refused {refusedCases} fail-closed, replayed idempotently, rendered the submitted instance round-trip, and agreed with {comparedVerdicts} packed reference-lane verdicts");

static FormDefinition BuildDefinition(JsonElement corpus, string id, string schemaId, IReadOnlyCollection<string> selectedRules)
{
    var fields = corpus.GetProperty("schema").GetProperty("properties").EnumerateObject()
        .ToDictionary(
            property => property.Name,
            property => new FieldOverlay(InternationalizedText.FromInvariant(property.Name)),
            StringComparer.Ordinal);
    var sections = corpus.GetProperty("sections").EnumerateArray().Select(section => new FormSection(
        section.GetProperty("id").GetString()!,
        InternationalizedText.FromInvariant(section.GetProperty("id").GetString()!),
        section.GetProperty("fields").EnumerateArray().Select(value => value.GetString()!).ToArray(),
        new([Harborline.Contracts.Authorization.RoleReference.Domain("admin")], [Harborline.Contracts.Authorization.RoleReference.Domain("admin")]),
        Items: section.TryGetProperty("items", out var items)
            ? items.EnumerateArray().Select(ToItem).ToArray()
            : null)).ToArray();
    var catalog = corpus.GetProperty("ruleCatalog");
    var rules = selectedRules.Select(ruleId => ToRule(catalog.GetProperty(ruleId))).ToArray();
    var pages = corpus.GetProperty("pages").EnumerateArray().Select(page =>
    {
        // A page check may only reference a rule the case declares; the store's overlay
        // validation refuses dangling references, and the corpus omits the rule on purpose
        // for cases that exercise the schema tier alone.
        var declaredChecks = page.TryGetProperty("checks", out var checks)
            ? checks.EnumerateArray().Select(value => value.GetString()!).Where(selectedRules.Contains).ToArray()
            : [];
        return new FormPage(
            page.GetProperty("id").GetString()!,
            InternationalizedText.FromInvariant(page.GetProperty("id").GetString()!),
            page.GetProperty("sections").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            page.TryGetProperty("visibleWhen", out var guard) ? guard.GetString() : null,
            declaredChecks.Length > 0 ? declaredChecks : null);
    }).ToArray();
    var now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
    return new(new(id), new(1, 0, 0), FormDefinitionStatus.Draft, new("tenant-vertical"), IdentityRef.System,
        new(schemaId), new(fields, sections, rules, Pages: pages), null, now, now);
}

static FormItem ToItem(JsonElement item)
{
    var kind = item.GetProperty("kind").GetString();
    return kind switch
    {
        "field" => FormItem.OfField(item.GetProperty("key").GetString()!),
        "group" => FormItem.OfGroup(
            item.GetProperty("key").GetString()!,
            item.GetProperty("items").EnumerateArray().Select(ToItem).ToArray()),
        _ => throw new InvalidOperationException($"Unsupported corpus item kind '{kind}'."),
    };
}

static RuleDefinition ToRule(JsonElement rule) => new(
    rule.GetProperty("id").GetString()!,
    Enum.Parse<RuleTier>(rule.GetProperty("tier").GetString()!),
    Enum.Parse<RuleScope>(rule.GetProperty("scope").GetString()!),
    rule.GetProperty("scopeTarget").GetString()!,
    rule.GetProperty("expression").GetString()!,
    Enum.Parse<RuleActionKind>(rule.GetProperty("action").GetString()!));

sealed class ConsumerExecutionContext : IFormExecutionContextProvider
{
    private static readonly Harborline.Contracts.Authorization.RoleReference Admin =
        Harborline.Contracts.Authorization.RoleReference.Domain("admin");
    private static readonly Harborline.Contracts.Authorization.RoleVocabulary Vocabulary =
        Harborline.Contracts.Authorization.RoleVocabulary.FromApi([new(
            Guid.Parse("bbbd7893-58b7-41ec-96dd-1953def39fe1"), Admin, "Admin",
            new(Harborline.Contracts.Authorization.RoleOwnerKind.Package, "dynamic-forms-package-consumer"), false)]);
    private static readonly FormExecutionScope Scope = new(
        new TenantId("tenant-vertical"),
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "alice",
        ["admin"],
        Vocabulary,
        new Harborline.Contracts.Authorization.HeldRoleSet([Admin]));

    public ValueTask<FormExecutionScope> GetRequiredAsync(
        FormEngineAction action, CancellationToken cancellationToken = default) => ValueTask.FromResult(Scope);
}

sealed class IdentityReuseResolver : IReuseResolver
{
    public ValueTask<ResolvedFormDefinition> ResolveAsync(FormDefinition definition, CancellationToken ct = default) =>
        ValueTask.FromResult(new ResolvedFormDefinition(definition, new Dictionary<string, ReuseProvenance>()));
}

sealed class PassThroughFieldSecurity : IFormFieldSecurity
{
    public ValueTask<FormProtectionResult> ProtectAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        EntityId instanceId,
        JsonDocument acceptedCandidate,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new FormProtectionResult(
            JsonSerializer.SerializeToUtf8Bytes(acceptedCandidate.RootElement),
            new HashSet<string>(StringComparer.Ordinal)));

    public ValueTask<FormReadableCandidate> ReadAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        FormSubmissionRecord submission,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(submission.ProtectedAcceptedCandidate.ToArray());
        var decisions = document.RootElement.EnumerateObject()
            .Select(property => new FormFieldReadDecision(
                property.Name, FormFieldReadDisposition.Plaintext, property.Value.Clone(), false, []))
            .ToArray();
        return ValueTask.FromResult(new FormReadableCandidate(decisions));
    }
}

sealed class NullReadAudit : IFormSensitiveReadAudit
{
    public ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

sealed class RecordingProjectionSink : IFormProjectionSink
{
    public List<FormProjectionEnvelope> Envelopes { get; } = [];

    public ValueTask<FormProjectionDeliveryResult> DeliverAsync(
        FormProjectionEnvelope envelope, CancellationToken cancellationToken = default)
    {
        Envelopes.Add(envelope);
        return ValueTask.FromResult(new FormProjectionDeliveryResult([]));
    }
}
