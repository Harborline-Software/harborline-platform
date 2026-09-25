using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>T-591 (owner ruling Q16): the shared refusal envelope carries its stage, and a refusal may carry a target.</summary>
public sealed class RefusalContractTests
{
    private static readonly DefinitionDocument Draft = new(new("tenant-a", DefinitionKind.Rules, "rule-a"), "v1", "1.0.0", "{}");

    private static InMemoryVersionedDefinitionStore Store(DefinitionAdmission admission)
        => new(Enum.GetValues<DefinitionKind>().ToDictionary(kind => kind, _ => admission));

    [Fact(DisplayName = "DES-0018 §7 refusal stage: the envelope names the admission stage it refused at, author or publish, with every refusal the member returned")]
    public async Task Refusal_envelope_carries_its_stage()
    {
        var authoring = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await Store((_, phase) => phase == DefinitionAdmissionPhase.Author
                ? [new("a.one", "/x"), new("a.two", "/y")] : []).SaveDraftAsync(Draft, 0, "r1"));
        Assert.Equal(DefinitionAdmissionPhase.Author, authoring.Stage);
        Assert.Equal(["/x", "/y"], authoring.Refusals.Select(refusal => refusal.Pointer));

        var store = Store((_, phase) => phase == DefinitionAdmissionPhase.Publish ? [new("p.one", "/z")] : []);
        await store.SaveDraftAsync(Draft, 0, "r1");
        var publishing = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.PublishAsync(Draft.Key, "v1", 1, "r2"));
        Assert.Equal(DefinitionAdmissionPhase.Publish, publishing.Stage);

        Assert.Equal(["Author", "Publish", "Install"], Enum.GetNames<DefinitionAdmissionPhase>());
        Assert.Null(new DefinitionRefusal("c", "/p").Target);
    }
}
