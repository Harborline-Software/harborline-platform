using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contract = Harborline.Contracts.Forms;
using State = Harborline.Foundation.Forms.Models;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Kernel.SchemaValidation;

namespace Harborline.Foundation.Forms.Engine;

public sealed class FormEngine : IFormEngine
{
    private readonly IFormExecutionContextProvider _contexts;
    private readonly Harborline.Foundation.Forms.IFormDefinitionStore _definitions;
    private readonly Harborline.Foundation.Forms.IReuseResolver _reuse;
    private readonly ISchemaRegistry _schemas;
    private readonly IFormFieldSecurity _security;
    private readonly IFormSensitiveReadAudit _readAudit;
    private readonly IFormSubmissionTransactionStore _submissions;
    private readonly IFormProjectionSink _projections;
    private readonly FormEngineOptions _options;
    private readonly TimeProvider _clock;

    public FormEngine(
        IFormExecutionContextProvider contexts,
        Harborline.Foundation.Forms.IFormDefinitionStore definitions,
        Harborline.Foundation.Forms.IReuseResolver reuse,
        ISchemaRegistry schemas,
        IFormFieldSecurity security,
        IFormSensitiveReadAudit readAudit,
        IFormSubmissionTransactionStore submissions,
        IFormProjectionSink projections,
        FormEngineOptions? options = null,
        TimeProvider? clock = null)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _reuse = reuse ?? throw new ArgumentNullException(nameof(reuse));
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
        _security = security ?? throw new ArgumentNullException(nameof(security));
        _readAudit = readAudit ?? throw new ArgumentNullException(nameof(readAudit));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _projections = projections ?? throw new ArgumentNullException(nameof(projections));
        _options = options ?? new FormEngineOptions();
        _options.Validate();
        _clock = clock ?? TimeProvider.System;
    }

    public async ValueTask<Contract.FormView> RenderAsync(
        State.FormDefinitionId formId,
        Harborline.Foundation.Assets.Common.EntityId? instanceId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequiredScopeAsync(FormEngineAction.Read, cancellationToken).ConfigureAwait(false);
        var definition = await LoadEffectiveAsync(scope, formId, cancellationToken).ConfigureAwait(false);
        if (instanceId is null)
        {
            using var empty = JsonDocument.Parse("{}");
            var sensitive = definition.Overlay.Fields.Where(row => row.Value.PiiSensitivity == State.PiiSensitivity.Sensitive).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
            var rules = EvaluateRenderRules(definition, empty, cancellationToken);
            return FormContractMapper.ToView(scope, definition, empty, sensitive, sensitive, rules);
        }

        var submission = await ProviderAsync(() => _submissions.GetAsync(scope.Tenant, instanceId.Value, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (submission is null) throw new FormEngineNotFoundException();
        var read = await ProviderAsync(
            () => _security.ReadAsync(scope, definition, submission, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        using var projection = await ProviderAsync(
            () => BuildReadProjectionAsync(scope, submission, read, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        using var ruleCandidate = CandidateWithoutSensitiveFields(
            projection.Candidate, projection.SensitiveFields);
        var rulesResult = EvaluateRenderRules(definition, ruleCandidate, cancellationToken);
        return FormContractMapper.ToView(
            scope,
            definition,
            projection.Candidate,
            projection.SensitiveFields,
            projection.WithheldFields,
            rulesResult);
    }

    public async ValueTask<Contract.ValidationResult> ValidateAsync(
        State.FormDefinitionId formId,
        JsonDocument candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var scope = await RequiredScopeAsync(FormEngineAction.Validate, cancellationToken).ConfigureAwait(false);
        State.FormDefinition definition;
        try { definition = await LoadEffectiveAsync(scope, formId, cancellationToken).ConfigureAwait(false); }
        catch (FormEngineNotFoundException)
        {
            return Invalid(new Contract.ValidationError { JsonPointer = "", Message = "The form was not found.", Kind = Contract.ValidationErrorKind.NotFound, Code = "form.engine.not-found" });
        }

        using var evaluation = await EvaluateAsync(scope, definition, candidate, cancellationToken).ConfigureAwait(false);
        return new Contract.ValidationResult { IsValid = evaluation.Errors.Count == 0, Errors = evaluation.Errors };
    }

    public async ValueTask<FormSubmitReceipt> SubmitAsync(FormSubmitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        var scope = await RequiredScopeAsync(FormEngineAction.Submit, cancellationToken).ConfigureAwait(false);
        var definition = await LoadEffectiveAsync(scope, request.FormId, cancellationToken).ConfigureAwait(false);
        using var evaluation = await EvaluateAsync(scope, definition, request.Candidate, cancellationToken).ConfigureAwait(false);
        if (evaluation.Errors.Count > 0) throw new FormEngineValidationException(evaluation.Errors);

        var instant = _clock.GetUtcNow();
        var instanceId = new Harborline.Foundation.Assets.Common.EntityId("harborline", "forms", Guid.NewGuid().ToString("N"));
        var protectedResult = await ProviderAsync(
            () => _security.ProtectAsync(
                scope, definition, instanceId, evaluation.AcceptedCandidate, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var fingerprint = Fingerprint(evaluation.AcceptedCandidate.RootElement);
        var auditPayload = BuildSubmissionAuditPayload(definition, evaluation.AcceptedCandidate, protectedResult, instant);
        var outboxId = Guid.NewGuid().ToString("N");
        var receipt = new FormSubmitReceipt(instanceId, instant, FormProjectionStatus.Pending, Array.Empty<FormProjectionSkip>());
        var commit = new FormSubmissionCommit(
            request.IdempotencyKey,
            new(instanceId, scope.Tenant, scope.PartyId, scope.ActorId, definition.Id, definition.Version, fingerprint, protectedResult.ProtectedCandidate, instant),
            new(Guid.NewGuid().ToString("N"), instanceId, scope.Tenant, scope.ActorId, auditPayload, instant),
            new(outboxId, instanceId, scope.Tenant, scope.PartyId, scope.ActorId, definition.Id, definition.Version,
                request.CaseReference, protectedResult.ProtectedCandidate.ToArray(), instant),
            receipt);
        var committed = await ProviderAsync(() => _submissions.CommitAsync(commit, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (committed.Disposition == FormSubmissionCommitDisposition.Conflict) throw new FormEngineIdempotencyConflictException();
        if (committed.Disposition == FormSubmissionCommitDisposition.Replayed) return committed.Receipt!;

        try
        {
            var delivered = await _projections.DeliverAsync(commit.Projection, cancellationToken).ConfigureAwait(false);
            await _submissions.CompleteProjectionAsync(outboxId, delivered.Skips, cancellationToken).ConfigureAwait(false);
            return receipt with { ProjectionStatus = FormProjectionStatus.Complete, ProjectionSkips = delivered.Skips.ToArray() };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { await _submissions.ReleaseProjectionLeaseAsync(outboxId, CancellationToken.None).ConfigureAwait(false); }
            catch { }
            throw;
        }
        catch
        {
            await _submissions.RetryProjectionAsync(outboxId, "form.engine.projection-pending", CancellationToken.None).ConfigureAwait(false);
            return receipt;
        }
    }

    public async ValueTask<FormProjectionRecoveryResult> RecoverProjectionsAsync(
        int maximumDeliveries,
        CancellationToken cancellationToken = default)
    {
        if (maximumDeliveries is <= 0 or > FormEngineOptions.MaximumRecoveryBatch) throw new ArgumentOutOfRangeException(nameof(maximumDeliveries));
        _ = await RequiredScopeAsync(FormEngineAction.RecoverProjections, cancellationToken).ConfigureAwait(false);
        return await ProviderAsync(
            () => FormProjectionRecovery.RecoverAsync(_submissions, _projections, maximumDeliveries, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<State.FormDefinition> LoadEffectiveAsync(FormExecutionScope scope, State.FormDefinitionId id, CancellationToken cancellationToken)
    {
        State.FormDefinition? definition;
        try { definition = await _definitions.GetCurrentPublishedAsync(scope.Tenant, id, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new FormEngineProviderUnavailableException(ex); }
        if (definition is null) throw new FormEngineNotFoundException();
        try
        {
            var effective = (await _reuse.ResolveAsync(definition, cancellationToken).ConfigureAwait(false)).Effective;
            if (effective.Overlay.Sections.Any(section => ContainsReference(section.Items)))
                throw new FormEngineValidationException([new Contract.ValidationError
                {
                    JsonPointer = "",
                    Message = "A reusable form reference could not be resolved.",
                    Kind = Contract.ValidationErrorKind.Schema,
                    Code = "unresolved-reference",
                }]);
            return effective;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FormEngineException) { throw; }
        catch (Harborline.Foundation.Forms.Exceptions.ReuseResolutionException) { throw; }
        catch (Exception ex) { throw new FormEngineProviderUnavailableException(ex); }
    }

    private static bool ContainsReference(IReadOnlyList<State.FormItem>? items) =>
        items?.Any(item => item.Kind == State.FormItemKind.Reference || ContainsReference(item.Items)) == true;

    private async ValueTask<FormCandidateEvaluation> EvaluateAsync(FormExecutionScope scope, State.FormDefinition definition, JsonDocument candidate, CancellationToken cancellationToken)
    {
        try { return await FormCandidateEvaluator.EvaluateAsync(scope, definition, candidate, _schemas, _options.MaximumCandidateBytes, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is RuleEngineTimeoutException or TimeoutException) { throw new FormEngineResourceBoundException(ex); }
        catch (RuleCompilationException ex) { throw new FormEngineValidationException([new Contract.ValidationError { JsonPointer = "", Message = "A form rule could not be compiled.", Kind = Contract.ValidationErrorKind.Schema, Code = ex.Code }]); }
        catch (Exception ex) { throw new FormEngineProviderUnavailableException(ex); }
    }

    private static RuleEvaluationResult? EvaluateRenderRules(State.FormDefinition definition, JsonDocument candidate, CancellationToken cancellationToken)
    {
        if (definition.Overlay.Rules.Count == 0 || candidate.RootElement.ValueKind != JsonValueKind.Object) return null;
        try
        {
            var compiled = RuleCompiler.Compile(definition.Overlay.Rules.Select(FormContractMapper.ToContractRule).ToArray());
            return compiled.RuleCount == 0 ? null : new FormRuleGraph(compiled).EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse(candidate.RootElement.GetRawText())!.AsObject()), cancellationToken);
        }
        catch (Exception ex) when (ex is RuleCompilationException or RuleEngineTimeoutException) { throw new FormEngineProviderUnavailableException(ex); }
    }

    private async ValueTask<FormReadProjection> BuildReadProjectionAsync(
        FormExecutionScope scope,
        FormSubmissionRecord submission,
        FormReadableCandidate read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        var candidate = new JsonObject();
        var sensitive = new HashSet<string>(StringComparer.Ordinal);
        var withheld = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in read.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.FieldName) || !seen.Add(field.FieldName))
                throw new InvalidOperationException("Forms security returned an invalid or duplicate field decision.");
            if (field.RequiredAudits.Any(audit =>
                    !string.Equals(audit.FieldName, field.FieldName, StringComparison.Ordinal)))
                throw new InvalidOperationException("Forms security returned an audit for a different field.");

            if (field.IsSensitive) sensitive.Add(field.FieldName);
            var decryptGrants = field.RequiredAudits.Where(audit =>
                audit.Kind == FormSensitiveReadAuditKind.DecryptOnRender &&
                audit.Outcome == FormSensitiveReadAuditOutcome.Granted).ToArray();

            var validPlaintext = field.Disposition == FormFieldReadDisposition.Plaintext &&
                                 !field.IsSensitive &&
                                 field.ProjectedValue.HasValue &&
                                 field.RequiredAudits.Count == 0;
            var validWithheld = field.Disposition == FormFieldReadDisposition.Withheld &&
                                field.IsSensitive &&
                                !field.ProjectedValue.HasValue &&
                                decryptGrants.Length == 0;
            var validGrant = field.Disposition == FormFieldReadDisposition.DecryptGranted &&
                             field.IsSensitive &&
                             field.ProjectedValue.HasValue &&
                             decryptGrants.Length == 1 &&
                             !string.IsNullOrWhiteSpace(decryptGrants[0].DecryptCapabilityId) &&
                             string.Equals(
                                 decryptGrants[0].Permission,
                                 FormEnginePermissions.DecryptSensitive,
                                 StringComparison.Ordinal) &&
                             string.Equals(
                                 decryptGrants[0].Purpose,
                                 FormEnginePermissions.DecryptOnRenderPurpose,
                                 StringComparison.Ordinal);
            if (!validPlaintext && !validWithheld && !validGrant)
                throw new InvalidOperationException("Forms security returned an inconsistent field decision.");

            foreach (var audit in field.RequiredAudits.Where(audit =>
                         audit.Outcome != FormSensitiveReadAuditOutcome.Granted))
            {
                try
                {
                    await AppendReadAuditAsync(
                        scope, submission.InstanceId, audit, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }
            }

            if (validWithheld)
            {
                withheld.Add(field.FieldName);
                continue;
            }

            if (validGrant)
            {
                try
                {
                    await AppendReadAuditAsync(
                        scope, submission.InstanceId, decryptGrants[0], cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    withheld.Add(field.FieldName);
                    continue;
                }
            }

            candidate[field.FieldName] = JsonNode.Parse(field.ProjectedValue!.Value.GetRawText());
        }

        return new(
            JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(candidate)),
            sensitive,
            withheld);
    }

    private ValueTask AppendReadAuditAsync(
        FormExecutionScope scope,
        Harborline.Foundation.Assets.Common.EntityId instanceId,
        FormSensitiveReadAuditEvent audit,
        CancellationToken cancellationToken) =>
        _readAudit.AppendAsync(
            new(scope.Tenant, instanceId, scope.ActorId, audit, _clock.GetUtcNow()),
            cancellationToken);

    private static JsonDocument CandidateWithoutSensitiveFields(
        JsonDocument candidate,
        IReadOnlySet<string> sensitiveFields)
    {
        var node = JsonNode.Parse(candidate.RootElement.GetRawText())!.AsObject();
        foreach (var field in sensitiveFields) node.Remove(field);
        return JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(node));
    }

    private sealed record FormReadProjection(
        JsonDocument Candidate,
        IReadOnlySet<string> SensitiveFields,
        IReadOnlySet<string> WithheldFields) : IDisposable
    {
        public void Dispose() => Candidate.Dispose();
    }

    private async ValueTask<FormExecutionScope> RequiredScopeAsync(FormEngineAction action, CancellationToken cancellationToken)
    {
        try
        {
            var scope = await _contexts.GetRequiredAsync(action, cancellationToken).ConfigureAwait(false);
            if (scope.Tenant == default || scope.PartyId == Guid.Empty || string.IsNullOrWhiteSpace(scope.ActorId) || scope.Roles is null) throw new FormEngineDeniedException();
            return scope with { Roles = scope.Roles.Distinct(StringComparer.Ordinal).ToArray() };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FormExecutionTenantUnavailableException) when (action == FormEngineAction.Read) { throw new FormEngineNotFoundException(); }
        catch (FormExecutionTenantUnavailableException) { throw new FormEngineDeniedException(); }
        catch (FormEngineDeniedException) { throw; }
        catch { throw new FormEngineDeniedException(); }
    }

    private static async ValueTask<T> ProviderAsync<T>(Func<ValueTask<T>> operation, CancellationToken cancellationToken)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FormEngineException) { throw; }
        catch (Exception ex) { throw new FormEngineProviderUnavailableException(ex); }
    }

    private static Contract.ValidationResult Invalid(params Contract.ValidationError[] errors) => new() { IsValid = false, Errors = errors };

    private byte[] BuildSubmissionAuditPayload(
        State.FormDefinition definition,
        JsonDocument acceptedCandidate,
        FormProtectionResult protectedResult,
        DateTimeOffset submittedAt)
    {
        var acceptedNames = acceptedCandidate.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var protectedNames = protectedResult.SensitiveFields.Order(StringComparer.Ordinal).ToArray();
        var payload = new JsonObject
        {
            ["op"] = "form-instance-mint",
            ["form"] = definition.Id.Value,
            ["version"] = definition.Version.ToString(),
            ["encryptedFields"] = new JsonArray(protectedNames.Select(value => JsonValue.Create(value)).ToArray()),
            ["operation"] = "submit",
            ["definitionId"] = definition.Id.Value,
            ["definitionVersion"] = definition.Version.ToString(),
            ["schemaRef"] = definition.SchemaRef.Value,
            ["engineVersion"] = _options.EngineVersion,
            ["acceptedFields"] = new JsonArray(acceptedNames.Select(value => JsonValue.Create(value)).ToArray()),
            ["protectedFields"] = new JsonArray(protectedNames.Select(value => JsonValue.Create(value)).ToArray()),
            ["binding"] = new JsonObject
            {
                ["schemaRef"] = definition.SchemaRef.Value,
                ["definitionId"] = definition.Id.Value,
                ["definitionVersion"] = definition.Version.ToString(),
                ["engineVersion"] = _options.EngineVersion,
                ["localeChain"] = new JsonArray(_options.LocaleChain.Select(value => JsonValue.Create(value)).ToArray()),
                ["submittedAt"] = submittedAt,
            },
        };

        if (RequiresFullProjectionSnapshot(definition.Overlay))
        {
            payload["snapshot"] = new JsonObject
            {
                ["mode"] = "full-projection",
                ["capturedAt"] = submittedAt,
                ["projection"] = JsonNode.Parse(protectedResult.ProtectedCandidate.AsSpan()),
            };
        }

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    /// <summary>The coding system a capture-policy tag declares.</summary>
    public const string CapturePolicySystem = "harborline/capture-policy";

    /// <summary>
    /// The pre-rename spelling of <see cref="CapturePolicySystem"/> (ticket 288 slice 2). The policy
    /// vocabulary is unchanged -- the same capture-as-shown / compliance-grade codes under the
    /// Harborline identity -- and an overlay authored before the rename still asks for a full
    /// projection snapshot, so it is accepted on read. New overlays are authored with
    /// <see cref="CapturePolicySystem"/>.
    /// </summary>
    public const string LegacyCapturePolicySystem = "shipyard/capture-policy";

    private static bool IsCapturePolicy(string system) =>
        string.Equals(system, CapturePolicySystem, StringComparison.Ordinal)
        || string.Equals(system, LegacyCapturePolicySystem, StringComparison.Ordinal);

    private static bool RequiresFullProjectionSnapshot(State.HarborlineOverlay overlay)
    {
        static bool ContainsCaptureTag(State.AspectOverlay? aspects) => aspects?.Classification?.Tags.Any(tag =>
            IsCapturePolicy(tag.System) &&
            (string.Equals(tag.Code, "capture-as-shown", StringComparison.Ordinal) ||
             string.Equals(tag.Code, "compliance-grade", StringComparison.Ordinal))) == true;

        return ContainsCaptureTag(overlay.Aspects) ||
               overlay.Sections.Any(section => ContainsCaptureTag(section.Aspects)) ||
               overlay.Fields.Values.Any(field => ContainsCaptureTag(field.Aspects));
    }

    private static string Fingerprint(JsonElement element)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, element);
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(row => row.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var value in element.EnumerateArray()) WriteCanonical(writer, value); writer.WriteEndArray();
                break;
            default: element.WriteTo(writer); break;
        }
    }
}
