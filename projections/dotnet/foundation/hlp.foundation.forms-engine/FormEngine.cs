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
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.Core;

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
    private readonly FormFieldBinding? _fieldBinding;
    private readonly IFormSubmitGateAccess? _submitGates;

    public FormEngine(
        IFormExecutionContextProvider contexts,
        Harborline.Foundation.Forms.IFormDefinitionStore definitions,
        Harborline.Foundation.Forms.IReuseResolver reuse,
        ISchemaRegistry schemas,
        IFormFieldSecurity security,
        IFormSensitiveReadAudit readAudit,
        IFormSubmissionTransactionStore submissions,
        IFormProjectionSink projections,
        FormEngineOptions? options,
        TimeProvider clock,
        IFormFieldBindingSource? fieldBindings = null,
        Harborline.Contracts.Fields.IFieldKindRuntime? fieldKinds = null,
        Harborline.Contracts.Fields.IFieldDomainRuntime? fieldDomains = null,
        IFormSubmitGateAccess? submitGates = null)
    {
        _submitGates = submitGates;
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
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (fieldBindings is not null)
            _fieldBinding = new(fieldBindings, fieldKinds ?? throw new ArgumentNullException(nameof(fieldKinds)), fieldDomains ?? throw new ArgumentNullException(nameof(fieldDomains)));
    }

    public async ValueTask<Contract.FormView> RenderAsync(
        State.FormDefinitionId formId,
        Harborline.Foundation.Assets.Common.EntityId? instanceId,
        CancellationToken cancellationToken = default)
    {
        FormExecutionScope scope;
        var submitImpliesRead = false;
        try { scope = await RequiredScopeAsync(FormEngineAction.Read, cancellationToken).ConfigureAwait(false); }
        catch (FormEngineDeniedException) when (instanceId is null)
        {
            // forms-eng-8 (L342, L358): permission to submit implies reading the blank form, never a submission.
            scope = await RequiredScopeAsync(FormEngineAction.Submit, cancellationToken).ConfigureAwait(false);
            submitImpliesRead = true;
        }
        var head = await LoadPublishedAsync(scope, formId, cancellationToken).ConfigureAwait(false);
        if (submitImpliesRead) await RequireSubmitGateAsync(scope, head, cancellationToken).ConfigureAwait(false);
        var definition = await ResolveEffectiveAsync(head, cancellationToken).ConfigureAwait(false);
        var instant = _clock.GetUtcNow();
        var bindings = _fieldBinding is null ? null : await _fieldBinding.ResolveAsync(scope, definition, cancellationToken);
        if (instanceId is null)
        {
            using var empty = JsonDocument.Parse("{}");
            var sensitive = definition.Overlay.Fields.Where(row => row.Value.PiiSensitivity == State.PiiSensitivity.Sensitive).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
            var rules = EvaluateRenderRules(definition, empty, instant, cancellationToken);
            var hiddenPages = FormCandidateEvaluator.HiddenPages(definition, empty, rules, instant, cancellationToken);
            return FormContractMapper.ToView(scope, definition, empty, sensitive, sensitive, rules, bindings, hiddenPages);
        }

        var submission = await ProviderAsync(() => _submissions.GetAsync(scope.Tenant, instanceId.Value, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (submission is null) throw new FormEngineNotFoundException();
        var read = await ProviderAsync(
            () => _security.ReadAsync(scope, definition, submission, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        using var projection = await ProviderAsync(
            () => BuildReadProjectionAsync(scope, submission, read, instant, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        using var ruleCandidate = CandidateWithoutSensitiveFields(
            projection.Candidate, projection.SensitiveFields);
        var rulesResult = EvaluateRenderRules(definition, ruleCandidate, instant, cancellationToken);
        return FormContractMapper.ToView(
            scope,
            definition,
            projection.Candidate,
            projection.SensitiveFields,
            projection.WithheldFields,
            rulesResult, bindings,
            FormCandidateEvaluator.HiddenPages(definition, ruleCandidate, rulesResult, instant, cancellationToken));
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

        using var evaluation = await EvaluateAsync(scope, definition, candidate, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return new Contract.ValidationResult { IsValid = evaluation.Errors.Count == 0, Errors = evaluation.Errors };
    }

    public async ValueTask<FormSubmitReceipt> SubmitAsync(FormSubmitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        var scope = await RequiredScopeAsync(FormEngineAction.Submit, cancellationToken).ConfigureAwait(false);
        var head = await LoadPublishedAsync(scope, request.FormId, cancellationToken).ConfigureAwait(false);
        await RequireSubmitGateAsync(scope, head, cancellationToken).ConfigureAwait(false);
        var definition = await ResolveEffectiveAsync(head, cancellationToken).ConfigureAwait(false);
        FormSubmissionCommit? commit = null;
        var transaction = await ProviderAsync(
            () => KernelTransactionBoundary.ExecutePreparedAsync(
                async token =>
                {
                    var instant = _clock.GetUtcNow();
                    using var evaluation = await EvaluateAsync(scope, definition, request.Candidate, instant, token).ConfigureAwait(false);
                    if (evaluation.Errors.Count > 0) throw new FormEngineValidationException(evaluation.Errors);

                    var instanceId = new Harborline.Foundation.Assets.Common.EntityId("harborline", "forms", Guid.NewGuid().ToString("N"));
                    var protectedResult = await ProviderAsync(
                        () => _security.ProtectAsync(scope, definition, instanceId, evaluation.AcceptedCandidate, token), token).ConfigureAwait(false);
                    var fingerprint = Fingerprint(evaluation.AcceptedCandidate.RootElement);
                    var auditPayload = BuildSubmissionAuditPayload(definition, evaluation.AcceptedCandidate, protectedResult, instant);
                    var outboxId = Guid.NewGuid().ToString("N");
                    var receipt = new FormSubmitReceipt(instanceId, instant, FormProjectionStatus.Pending, Array.Empty<FormProjectionSkip>());
                    commit = new FormSubmissionCommit(
                        request.IdempotencyKey,
                        new(instanceId, scope.Tenant, scope.PartyId, scope.ActorId, definition.Id, definition.Version, fingerprint, protectedResult.ProtectedCandidate, instant),
                        new(Guid.NewGuid().ToString("N"), instanceId, scope.Tenant, scope.ActorId, auditPayload, instant),
                        new(outboxId, instanceId, scope.Tenant, scope.PartyId, scope.ActorId, definition.Id, definition.Version,
                            request.CaseReference, protectedResult.ProtectedCandidate.ToArray(), instant),
                        receipt);
                    return new KernelCommand<FormSubmissionCommit>(
                        new(instanceId.ToString(), request.IdempotencyKey, fingerprint),
                        commit,
                        new(commit.Audit.AuditId, commit.Audit.ActorId, commit.Audit.RecordedAt, commit.Audit.Payload));
                },
                new FormSubmissionKernelTransactionPort(_submissions),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var committed = transaction.Value
            ?? throw new InvalidOperationException(transaction.Refusal?.Code ?? "The kernel transaction did not return a result.");
        if (committed.Disposition == FormSubmissionCommitDisposition.Conflict) throw new FormEngineIdempotencyConflictException();
        if (committed.Disposition == FormSubmissionCommitDisposition.Replayed) return committed.Receipt!;
        var createdCommit = commit ?? throw new InvalidOperationException("The committed Forms submission was not prepared.");

        try
        {
            var delivered = await _projections.DeliverAsync(createdCommit.Projection, cancellationToken).ConfigureAwait(false);
            await _submissions.CompleteProjectionAsync(createdCommit.Projection.OutboxId, delivered.Skips, cancellationToken).ConfigureAwait(false);
            return createdCommit.Receipt with { ProjectionStatus = FormProjectionStatus.Complete, ProjectionSkips = delivered.Skips.ToArray() };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { await _submissions.ReleaseProjectionLeaseAsync(createdCommit.Projection.OutboxId, CancellationToken.None).ConfigureAwait(false); }
            catch { }
            throw;
        }
        catch
        {
            await _submissions.RetryProjectionAsync(createdCommit.Projection.OutboxId, "form.engine.projection-pending", CancellationToken.None).ConfigureAwait(false);
            return createdCommit.Receipt;
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
        => await ResolveEffectiveAsync(await LoadPublishedAsync(scope, id, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    private async ValueTask<State.FormDefinition> LoadPublishedAsync(FormExecutionScope scope, State.FormDefinitionId id, CancellationToken cancellationToken)
    {
        State.FormDefinition? definition;
        try { definition = await _definitions.GetCurrentPublishedAsync(scope.Tenant, id, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new FormEngineProviderUnavailableException(ex); }
        return definition ?? throw new FormEngineNotFoundException();
    }

    // forms-eng-2: the form's own gate is checked on the published head, before reuse, bindings or the candidate
    // are resolved. A declared gate with no host port, or any port failure, refuses (deny by default).
    private async ValueTask RequireSubmitGateAsync(FormExecutionScope scope, State.FormDefinition head, CancellationToken cancellationToken)
    {
        if (head.SubmitGate is not { } gate) return;
        bool satisfied;
        try { satisfied = gate.IsWellFormed && _submitGates is not null && await _submitGates.SatisfiesAsync(scope, head, gate, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { satisfied = false; }
        if (!satisfied) throw new FormEngineDeniedException();
    }

    private async ValueTask<State.FormDefinition> ResolveEffectiveAsync(State.FormDefinition definition, CancellationToken cancellationToken)
    {
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

    private async ValueTask<FormCandidateEvaluation> EvaluateAsync(FormExecutionScope scope, State.FormDefinition definition, JsonDocument candidate, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        try
        {
            var bindings = _fieldBinding is null ? null : await _fieldBinding.ResolveAsync(scope, definition, cancellationToken);
            var evaluation = await FormCandidateEvaluator.EvaluateAsync(scope, definition, candidate, _schemas, _options.MaximumCandidateBytes, instant, cancellationToken).ConfigureAwait(false);
            if (bindings is null || evaluation.AcceptedCandidate.RootElement.ValueKind != JsonValueKind.Object) return evaluation;
            var errors = evaluation.Errors.ToList();
            foreach (var (name, binding) in bindings)
            {
                evaluation.AcceptedCandidate.RootElement.TryGetProperty(name, out var value);
                errors.AddRange(_fieldBinding!.Validate(binding, value, FormFieldBinding.Pointer(name)).Select(refusal => new Contract.ValidationError
                {
                    Code = refusal.Code, JsonPointer = refusal.JsonPointer, Message = refusal.Message, Kind = Contract.ValidationErrorKind.Schema,
                }));
            }
            return evaluation with { Errors = errors };
        }
        catch (Harborline.Contracts.Fields.FieldAdmissionException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is RuleEngineTimeoutException or TimeoutException) { throw new FormEngineResourceBoundException(ex); }
        catch (RuleCompilationException ex) { throw new FormEngineValidationException([new Contract.ValidationError { JsonPointer = "", Message = "A form rule could not be compiled.", Kind = Contract.ValidationErrorKind.Schema, Code = ex.Code }]); }
        catch (Exception ex) { throw new FormEngineProviderUnavailableException(ex); }
    }

    private RuleEvaluationResult? EvaluateRenderRules(State.FormDefinition definition, JsonDocument candidate, DateTimeOffset instant, CancellationToken cancellationToken)
    {
        if (definition.Overlay.Rules.Count == 0 || candidate.RootElement.ValueKind != JsonValueKind.Object) return null;
        try
        {
            var compiled = RuleCompiler.Compile(definition.Overlay.Rules.Select(FormContractMapper.ToContractRule).ToArray());
            return compiled.RuleCount == 0 ? null : new FormRuleGraph(compiled, new PinnedClock(instant), FormsExpressionEnvironment.Admitted.For(EvaluationPhase.Render)).EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse(candidate.RootElement.GetRawText())!.AsObject()), cancellationToken);
        }
        catch (Exception ex) when (ex is RuleCompilationException or RuleEngineTimeoutException) { throw new FormEngineProviderUnavailableException(ex); }
    }

    private async ValueTask<FormReadProjection> BuildReadProjectionAsync(
        FormExecutionScope scope,
        FormSubmissionRecord submission,
        FormReadableCandidate read,
        DateTimeOffset instant,
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
                        scope, submission.InstanceId, audit, instant, cancellationToken).ConfigureAwait(false);
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
                        scope, submission.InstanceId, decryptGrants[0], instant, cancellationToken).ConfigureAwait(false);
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
        DateTimeOffset instant,
        CancellationToken cancellationToken) =>
        _readAudit.AppendAsync(
            new(scope.Tenant, instanceId, scope.ActorId, audit, instant),
            cancellationToken);

    private sealed class PinnedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

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
