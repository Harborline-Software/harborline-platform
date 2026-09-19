using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;
using Harborline.Foundation.Forms.Models;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Forms.Engine.Security;
using Harborline.Foundation.Forms.Engine.Persistence;
using Xunit;
using Harness = Harborline.Foundation.Forms.Engine.Tests.FormEngineOrchestrationTests.Harness;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class SharedFieldBindingTests
{
    [Fact]
    public async Task Query_picker_and_submission_share_the_predicate_and_readable_membership()
    {
        var records = Enumerable.Range(0, 40000).Select(index => new FieldDomainMember(
            index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), "record-" + index,
            JsonSerializer.SerializeToElement(new { active = true }))).ToList();
        records.Add(new("inactive", "excluded", JsonSerializer.SerializeToElement(new { active = false })));
        records.Add(new("hidden", "restricted", JsonSerializer.SerializeToElement(new { active = true })));
        var host = new FieldHost
        {
            Domain = new(RecordQuery: new("postcodes", "{\"var\":\"field.active\"}")),
            Records = new Dictionary<string, IReadOnlyList<FieldDomainMember>> { ["postcodes"] = records },
            ReadableValues = new HashSet<string>(records.Select(record => record.Value).Where(value => value != "hidden"), StringComparer.Ordinal),
        };
        var harness = await Create(host, hint: "textarea", security: new PlaintextSecurity());
        var field = (await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections[0].Fields.Single(value => value.Name == "name");
        Assert.Equal("RecordPicker", field.ControlHint.Value);
        Assert.Equal(40000, field.PermittedValues.Value!.Count);
        Assert.DoesNotContain("hidden", field.PermittedValues.Value);
        Assert.DoesNotContain("inactive", field.PermittedValues.Value);
        foreach (var value in new[] { "inactive", "hidden", "outside" })
        {
            using var candidate = JsonDocument.Parse(JsonSerializer.Serialize(new { name = value }));
            var error = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
                await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, value)));
            Assert.Contains(error.Errors, refusal => refusal.Code.HasValue && refusal.Code.Value == "field.value_outside_domain");
        }
        Assert.Equal(0, (await harness.Store.CountsAsync()).Submissions);
        using var permitted = JsonDocument.Parse("{\"name\":\"39999\"}");
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, permitted, "permitted"));
        var stored = await harness.Store.GetAsync(host.Tenant, receipt.InstanceId);
        using var persisted = JsonDocument.Parse(stored!.ProtectedAcceptedCandidate);
        Assert.Equal("39999", persisted.RootElement.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("[12.3,1.2]", null)]
    [InlineData("[12.30,1.2]", "field.fraction_digits_exceeded")]
    [InlineData("[]", "field.required")]
    [InlineData("[12.3]", "field.minimum_count")]
    [InlineData("[12.3,1.2,2.3]", "field.maximum_count")]
    public async Task Repeated_values_enforce_multiplicity_and_original_scalar_semantics(string json, string? code)
    {
        var host = new FieldHost { Domain = null, Scalar = FieldScalarValueShape.Number,
            MinimumCount = 2, MaximumCount = 2,
            Parameters = new Dictionary<string, string> { ["fraction_digits"] = "1" } };
        var harness = await Create(host, security: new PlaintextSecurity());
        using var candidate = JsonDocument.Parse("{\"name\":" + json + "}");
        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);
        if (code is null)
        {
            Assert.True(result.IsValid);
            var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "repeated"));
            var stored = await harness.Store.GetAsync(host.Tenant, receipt.InstanceId);
            using var persisted = JsonDocument.Parse(stored!.ProtectedAcceptedCandidate);
            Assert.Equal(json, persisted.RootElement.GetProperty("name").GetRawText());
        }
        else
        {
            Assert.Contains(result.Errors, error => error.Code.HasValue && error.Code.Value == code
                && error.JsonPointer == (code == "field.fraction_digits_exceeded" ? "/name/0" : "/name"));
            await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
                await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "repeated")));
            Assert.Equal(0, (await harness.Store.CountsAsync()).Submissions);
        }
        Assert.Equal(json, candidate.RootElement.GetProperty("name").GetRawText());
    }

    [Fact]
    public async Task Three_value_domain_carries_runtime_radio_choice_through_real_render()
    {
        var host = new FieldHost
        {
            Domain = new(LiteralValues: ["open", "closed", "pending"]),
            ReadableValues = new HashSet<string>(["open", "closed", "pending"], StringComparer.Ordinal),
        };
        var harness = await Create(host, hint: "textarea");

        var view = await harness.Engine.RenderAsync(harness.Definition.Id, null);
        var field = view.Sections[0].Fields.Single(item => item.Name == "name");

        Assert.Equal("RadioGroup", field.ControlHint.Value);
        Assert.Equal(new[] { "open", "closed", "pending" }, field.PermittedValues.Value);
    }

    [Fact]
    public async Task Bound_required_floor_refuses_an_absent_submission_value()
    {
        var harness = await Create(new FieldHost { Domain = null }, security: new PlaintextSecurity());
        using var candidate = JsonDocument.Parse("{}");

        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.JsonPointer == "/name");
        await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "absent-required")));
        Assert.Equal(0, (await harness.Store.CountsAsync()).Submissions);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("outside")]
    public async Task Bound_domain_refuses_unreadable_or_nonmember_submission_values(string value)
    {
        var harness = await Create(new FieldHost(), name: "a~/b", security: new PlaintextSecurity());
        using var candidate = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { ["a~/b"] = value }));

        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code.HasValue
            && error.Code.Value == "field.value_outside_domain" && error.JsonPointer == "/a~0~1b");
        await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "outside-domain")));
        Assert.Equal(0, (await harness.Store.CountsAsync()).Submissions);
        Assert.Equal(value, candidate.RootElement.GetProperty("a~/b").GetString());
    }

    [Fact]
    public async Task Literal_options_keep_taxonomy_authority_and_shared_editor_after_narrowing()
    {
        var host = new FieldHost { Domain = new(TaxonomyScheme: new("scheme", "1")) };
        var harness = await Create(host, metadata: new("text", true, options: ["allowed", "hidden"]));
        var field = (await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections[0].Fields[0];
        Assert.Equal(new[] { "allowed" }, field.PermittedValues.Value);
        Assert.Equal("SingleValue", field.ControlHint.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_overlay_and_declared_role_gates_withhold_domain_and_editor(bool declaredGate)
    {
        var host = new FieldHost { ReadRoles = declaredGate ? ["owner"] : [] };
        var harness = await Create(host, transform: definition => declaredGate ? definition : definition with
        {
            Overlay = definition.Overlay with { Fields = definition.Overlay.Fields.ToDictionary(p => p.Key,
                p => p.Value with { FieldReadRoles = [RoleReference.Domain("owner")] }) },
        });
        var field = (await harness.Engine.RenderAsync(harness.Definition.Id, null)).Sections[0].Fields[0];
        Assert.False(field.IsReadable);
        Assert.False(field.PermittedValues.HasValue);
        Assert.False(field.ControlHint.HasValue);
    }

    [Fact]
    public async Task Shared_kind_validates_the_pruned_candidate_that_submit_really_persists()
    {
        var host = new FieldHost { Domain = null, Scalar = FieldScalarValueShape.Number,
            Parameters = new Dictionary<string, string> { ["fraction_digits"] = "1" } };
        var harness = await Create(host, security: new PlaintextSecurity(), transform: definition => definition with
        {
            Overlay = definition.Overlay with { Rules = [new("hide", RuleTier.JsonLogic, RuleScope.Field, "secret", "false", RuleActionKind.Visibility)] },
        });
        using var candidate = JsonDocument.Parse("{\"name\":12.3,\"secret\":12.30}");
        Assert.True((await harness.Engine.ValidateAsync(harness.Definition.Id, candidate)).IsValid);
        var receipt = await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "pruned"));
        var stored = await harness.Store.GetAsync(harness.Definition.Tenant, receipt.InstanceId);
        using var persisted = JsonDocument.Parse(stored!.ProtectedAcceptedCandidate);
        Assert.Equal("12.3", persisted.RootElement.GetProperty("name").GetRawText());
        Assert.False(persisted.RootElement.TryGetProperty("secret", out _));
        Assert.Equal("12.30", candidate.RootElement.GetProperty("secret").GetRawText());
    }

    [Fact]
    public async Task Ordinary_unbound_schema_still_validates_and_never_uses_authored_hint()
    {
        var harness = await Harness.CreateAsync(definitionFactory: (schema, tenant) =>
        {
            var definition = Harness.CreateDefinition(schema, tenant);
            return definition with { Overlay = definition.Overlay with { Fields = definition.Overlay.Fields.ToDictionary(
                p => p.Key, p => p.Value with { ControlHint = "invented" }) } };
        });
        using var invalid = JsonDocument.Parse("{\"name\":\"\"}");
        Assert.False((await harness.Engine.ValidateAsync(harness.Definition.Id, invalid)).IsValid);
        using var valid = JsonDocument.Parse("{\"name\":\"value\"}");
        Assert.True((await harness.Engine.ValidateAsync(harness.Definition.Id, valid)).IsValid);
        var view = await harness.Engine.RenderAsync(harness.Definition.Id, null);
        Assert.All(view.Sections[0].Fields, f => Assert.False(f.ControlHint.HasValue));
    }

    [Theory]
    [InlineData("invented")]
    [InlineData("textarea")]
    public async Task Render_uses_readable_domain_and_ignores_authored_control(string hint)
    {
        var host = new FieldHost();
        var harness = await Create(host, hint: hint);
        var view = await harness.Engine.RenderAsync(harness.Definition.Id, null);
        var field = Assert.Single(view.Sections).Fields.Single(f => f.Name == "name");
        Assert.Equal("SingleValue", field.ControlHint.Value);
        var wire = JsonSerializer.SerializeToElement(field);
        Assert.Equal(new[] { "allowed" }, wire.GetProperty("permittedValues").EnumerateArray().Select(v => v.GetString()));
        var roundTrip = Harborline.Contracts.Forms.FormsJson.Deserialize<Harborline.Contracts.Forms.FormViewField>(
            Harborline.Contracts.Forms.FormsJson.Serialize(field));
        Assert.Equal(new[] { "allowed" }, roundTrip.PermittedValues.Value);
        Assert.Equal(new[] { "alice" }, host.Actors.Distinct());
        var secret = view.Sections[0].Fields.Single(f => f.Name == "secret");
        Assert.False(secret.ControlHint.HasValue);
        Assert.False(JsonSerializer.SerializeToElement(secret).TryGetProperty("permittedValues", out _));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("tenant")]
    [InlineData("field")]
    public async Task Configured_binding_fails_closed(string mismatch)
    {
        var harness = await Create(new FieldHost { Mismatch = mismatch });
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await harness.Engine.RenderAsync(harness.Definition.Id, null));
        Assert.Equal("field.binding_unresolved", Assert.Single(error.Refusals).Code);
    }

    [Theory]
    [InlineData("minimum", "0")]
    [InlineData("maxLength", "99")]
    public async Task Bound_runtime_rejects_member_kind_limit_redeclaration(string code, string value)
    {
        var harness = await Create(new FieldHost(), metadata: new("text", true, [new(code, value)]));
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await harness.Engine.RenderAsync(harness.Definition.Id, null));
        Assert.Equal("field.kind_limit_redeclared", Assert.Single(error.Refusals).Code);
    }

    [Theory]
    [InlineData(false, "allowed", "field.requirement_dropped")]
    [InlineData(true, "outside", "field.value_domain_widened")]
    public async Task Authored_refinement_cannot_drop_floor_or_widen_complete_membership(bool required, string option, string code)
    {
        var harness = await Create(new FieldHost(), metadata: new("text", required, options: [option]));
        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await harness.Engine.RenderAsync(harness.Definition.Id, null));
        Assert.Equal(code, Assert.Single(error.Refusals).Code);
    }

    [Theory]
    [InlineData("12.30", "fraction_digits", "1", "field.fraction_digits_exceeded")]
    [InlineData("123.4", "maximum", "100", "field.above_maximum")]
    [InlineData("123.4", "total_digits", "3", "field.total_digits_exceeded")]
    [InlineData("\"éé\"", "max_bytes", "3", "field.max_bytes_exceeded")]
    public async Task Validation_refuses_original_value_through_shared_kind(string json, string parameter, string limit, string code)
    {
        var host = new FieldHost { Scalar = parameter == "max_bytes" ? FieldScalarValueShape.Text : FieldScalarValueShape.Number,
            Parameters = new Dictionary<string, string> { [parameter] = limit }, Domain = null };
        var harness = await Create(host, name: "a~/b");
        var original = "{\"a~/b\":" + json + "}";
        using var candidate = JsonDocument.Parse(original);
        var result = await harness.Engine.ValidateAsync(harness.Definition.Id, candidate);
        Assert.Contains(result.Errors, e => e.Code.HasValue && e.Code.Value == code && e.JsonPointer == "/a~0~1b");
        Assert.Equal(original, candidate.RootElement.GetRawText());
        await Assert.ThrowsAsync<FormEngineValidationException>(async () => await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, "refused")));
        Assert.Equal(0, (await harness.Store.CountsAsync()).Submissions);
    }

    internal static Task<Harness> Create(FieldHost host, string hint = "wrong", FormFieldAuthoringMetadata? metadata = null, string name = "name",
        Func<FormDefinition, FormDefinition>? transform = null, IFormFieldSecurity? security = null)
        => Harness.CreateAsync(schemaJson: "{\"type\":\"object\"}", fieldBindings: host, security: security,
            fieldKinds: new FieldKindRuntime(new FieldKindRegistry([new("declared", "1", null, host.Scalar)])),
            fieldDomains: new ValueDomainRuntime(host, host),
            definitionFactory: (schema, tenant) =>
            {
                host.Name = name;
                host.SchemaRef = schema;
                var definition = Harness.CreateDefinition(schema, tenant);
                definition = definition with
                {
                    Overlay = definition.Overlay with
                    {
                        Fields = new Dictionary<string, FieldOverlay> { [name] = new(InternationalizedText.FromInvariant("Field"), ControlHint: hint),
                            ["secret"] = new(InternationalizedText.FromInvariant("Secret"), ControlHint: hint, PiiSensitivity: PiiSensitivity.Sensitive) },
                        Sections = [new("main", InternationalizedText.FromInvariant("Main"), [name, "secret"], new([], []))],
                    },
                    Authoring = metadata is null ? null : new(new Dictionary<string, FormFieldAuthoringMetadata> { [name] = metadata }),
                };
                return transform?.Invoke(definition) ?? definition;
            });

    private sealed class PlaintextSecurity : IFormFieldSecurity
    {
        public ValueTask<FormProtectionResult> ProtectAsync(FormExecutionScope scope, FormDefinition definition, EntityId instanceId,
            JsonDocument acceptedCandidate, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new FormProtectionResult(JsonSerializer.SerializeToUtf8Bytes(acceptedCandidate.RootElement), new HashSet<string>()));
        public ValueTask<FormReadableCandidate> ReadAsync(FormExecutionScope scope, FormDefinition definition, FormSubmissionRecord submission,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class FieldHost : IFormFieldBindingSource, IFieldDomainSource, IFieldDomainSnapshot, IFieldDomainReadAuthority
    {
        public string? Mismatch { get; init; }
        public string Name { get; set; } = "name";
        public string SchemaRef { get; set; } = "";
        public FieldScalarValueShape Scalar { get; init; } = FieldScalarValueShape.Text;
        public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
        public ValueDomainDefinition? Domain { get; init; } = new(LiteralValues: ["allowed", "hidden"]);
        public IReadOnlyList<string> ReadRoles { get; init; } = [];
        public int MinimumCount { get; init; }
        public int? MaximumCount { get; init; } = 1;
        public IReadOnlyDictionary<string, IReadOnlyList<FieldDomainMember>> Records { get; init; }
            = new Dictionary<string, IReadOnlyList<FieldDomainMember>>();
        public IReadOnlySet<string> ReadableValues { get; init; } = new HashSet<string>(["allowed"], StringComparer.Ordinal);
        public List<string> Actors { get; } = [];
        public TenantId Tenant { get; } = new("tenant-engine");
        public string Revision => "pinned-1";
        public bool IsComplete => true;
        public ValueTask<FormSchemaFieldBindings?> ResolveAsync(TenantId tenant, string schemaRef, CancellationToken cancellationToken = default)
        {
            if (tenant != Tenant || schemaRef != SchemaRef) return ValueTask.FromResult<FormSchemaFieldBindings?>(null);
            var fields = new Dictionary<string, FieldBindingDefinition> { [Name] = new(new("declared", "1", Parameters), new(true, MinimumCount, MaximumCount, ReadRoles, Domain)),
                ["secret"] = new(new("declared", "1", Parameters), new(false, 0, 1, [], Domain)) };
            if (Mismatch == "field") fields.Remove(Name);
            return ValueTask.FromResult<FormSchemaFieldBindings?>(Mismatch == "missing" ? null : new(Mismatch == "tenant" ? new("foreign") : tenant,
                Mismatch == "schema" ? "different" : schemaRef, fields));
        }
        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId tenant, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Tenant, tenant);
            return ValueTask.FromResult<IFieldDomainSnapshot>(this);
        }
        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId) => Records.GetValueOrDefault(recordTypeId);
        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme)
            => scheme == new TaxonomySchemeReference("scheme", "1") ? [new("allowed", "a", default), new("hidden", "b", default)] : null;
        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain, FieldDomainMember member, CancellationToken cancellationToken = default)
        {
            Actors.Add(scope.Principal);
            return ValueTask.FromResult(scope.Principal == "alice" && (Domain?.TaxonomyScheme is not null && domain.LiteralValues is not null || ReadableValues.Contains(member.Value)));
        }
    }
}
