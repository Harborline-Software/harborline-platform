using System.Text;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;

using Xunit;

using static Harborline.Foundation.Documents.Tests.TemplateFixtures;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>
/// T-572 slice 3 (T-724 rulings 85 to 88): a template's envelope contract is checked against the host's window,
/// which the host binding carries as a required member, at author, publish, install and the persisted read.
/// </summary>
public sealed class TemplateContractWindowTests
{
    public static TheoryData<int?, int?, string> Refused => new()
    {
        { null, null, "definition.contract.missing" },
        { 1, -1, "definition.contract.missing" },
        { 2, 0, "definition.contract.out_of_window" },
        { 1, 1, "definition.contract.out_of_window" },
    };

    private static TemplateDefinition With(int? major, int? minor)
        => Template() with { Envelope = Template().Envelope with { Contract = major is null ? null : new(major.Value, minor!.Value) } };

    [Theory(DisplayName = "T-572 slice 3: a template whose contract is missing or outside the window is refused at author and publish at /envelope/contract")]
    [MemberData(nameof(Refused))]
    public void AuthorAndPublishRefuse(int? major, int? minor, string code)
    {
        var template = With(major, minor);
        var expected = new DefinitionRefusal(code, "/envelope/contract");
        var body = Encoding.UTF8.GetString(TemplateDefinitionJson.SerializeCanonical(template));

        Assert.Equal([expected], TemplateDefinitionAdmission.AdmitCatalogueBody(Tenant, "template.invoice", "1.0.0", body, publishing: false, Surfaces()));
        Assert.Equal([expected], TemplateDefinitionAdmission.AdmitCatalogueBody(Tenant, "template.invoice", "1.0.0", body, publishing: true, Surfaces()));
        var published = Assert.Throws<DefinitionRefusalException>(() => TemplateDefinitionAdmission.ValidateForPublish(template, Surfaces()));
        Assert.Equal(DefinitionAdmissionPhase.Publish, published.Stage);
        Assert.Equal([expected], published.Refusals);
        Assert.Equal([expected], Assert.Throws<DefinitionRefusalException>(() => TemplatePack.Export(template, Surfaces())).Refusals);
    }

    [Theory(DisplayName = "T-572 slice 3: a pack template whose contract is missing or outside the window is refused by name at install, and its valid sibling still installs")]
    [MemberData(nameof(Refused))]
    public async Task InstallRefuses(int? major, int? minor, string code)
    {
        var valid = TemplatePack.Export(Template(), Surfaces());
        var node = JsonNode.Parse(valid.Content)!;
        var envelope = node["envelope"]!.AsObject();
        envelope.Remove("contract");
        if (major is not null) envelope["contract"] = new JsonObject { ["major"] = major, ["minor"] = minor };
        envelope["version"] = "2.0.0";
        var bad = new TemplatePackEntry("template.invoice", "2.0.0", Encoding.UTF8.GetBytes(node.ToJsonString()));
        var published = new List<TemplateDefinition>();

        var outcomes = await TemplatePack.InstallAsync([bad, valid], new(Tenant, Provenance, Surfaces(),
            _ => ValueTask.FromResult<TemplateDefinition?>(null),
            template => { published.Add(template); return ValueTask.CompletedTask; }));

        Assert.Equal(TemplateInstallOutcomeKind.Refused, outcomes[0].Kind);
        Assert.Equal([new DefinitionRefusal(code, "/envelope/contract")], outcomes[0].Refusals);
        Assert.Equal(TemplateInstallOutcomeKind.Published, outcomes[1].Kind);
        Assert.Equal("1.0.0", Assert.Single(published).Envelope.Version);
    }

    [Theory(DisplayName = "T-572 slice 3: a stored template whose contract is missing or outside the window is refused at the persisted read before render")]
    [MemberData(nameof(Refused))]
    public void PersistedReadRefuses(int? major, int? minor, string code)
    {
        var refused = Assert.Throws<DefinitionRefusalException>(() => TemplateDefinitionAdmission.ValidatePersisted(With(major, minor), Surfaces()));
        Assert.Equal(DefinitionAdmissionPhase.Render, refused.Stage);
        Assert.Equal([new DefinitionRefusal(code, "/envelope/contract")], refused.Refusals);
    }

    [Fact(DisplayName = "T-572 slice 3: the check reads the host binding's window, not a fixed value; the seed's {1, 0} is admitted at every stage")]
    public void TheHostBindingSuppliesTheWindow()
    {
        Assert.Empty(TemplateDefinitionAdmission.Validate(Template(), Surfaces()));
        TemplateDefinitionAdmission.ValidatePersisted(Template(), Surfaces());
        var newer = Surfaces() with { ContractWindow = new DefinitionContractWindow(1, 1, 1) };
        Assert.Empty(TemplateDefinitionAdmission.Validate(With(1, 1), newer));
        Assert.Equal([new DefinitionRefusal("definition.contract.out_of_window", "/envelope/contract")],
            TemplateDefinitionAdmission.Validate(Template(), Surfaces() with { ContractWindow = new DefinitionContractWindow(2, 0, 2) }));
    }
}
