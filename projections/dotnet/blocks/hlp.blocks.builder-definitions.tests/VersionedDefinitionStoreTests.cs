using System.Text.Json;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class VersionedDefinitionStoreTests
{
    [Theory]
    [InlineData(DefinitionKind.Views)]
    [InlineData(DefinitionKind.Reports)]
    [InlineData(DefinitionKind.Schedules)]
    [InlineData(DefinitionKind.DataExchange)]
    public async Task OneStorePublishesEveryRegistryWithoutLeakingDrafts(DefinitionKind kind)
    {
        var store = Store();
        var document = Document(kind);
        await store.SaveDraftAsync(document, 0, "draft");

        Assert.Null(await store.GetPublishedHeadAsync(document.Key));
        Assert.Null(await store.ResolvePublishedAsync(new(document.Key, document.VersionId)));

        var published = await store.PublishAsync(document.Key, document.VersionId, 1, "publish");
        Assert.Equal(DefinitionStatus.Published, published.Status);
        Assert.Equal(document, (await store.GetPublishedHeadAsync(document.Key))!.Document);
        Assert.Null(await store.GetPublishedHeadAsync(document.Key with { Tenant = "tenant-b" }));
    }

    [Fact]
    public async Task EqualSemanticVersionWithAnotherBodyRefusesBeforeAnyHistoryChange()
    {
        var store = Store();
        var original = Document();
        await store.SaveDraftAsync(original, 0, "draft-1");
        await store.PublishAsync(original.Key, original.VersionId, 1, "publish-1");
        var replacement = original with { VersionId = "version-b", BodyJson = "{\"gap\":2}" };
        await store.SaveDraftAsync(replacement, 2, "draft-2");

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.PublishAsync(replacement.Key, replacement.VersionId, 3, "publish-2"));

        AssertRefusal(error, "definition.version_conflict", "/version");
        Assert.Equal(3, (await store.ListHistoryAsync(original.Key)).Count);
        Assert.Equal(original.BodyJson, (await store.ResolvePublishedAsync(new(original.Key, original.VersionId)))!.Document.BodyJson);
    }

    [Fact]
    public async Task RestoreAppendsANewDraftAndPreservesThePublishedBytesAndPin()
    {
        var store = Store();
        var original = Document() with { BodyJson = "{ \"gap\": 1, \"caption\": \"Å\" }" };
        await store.SaveDraftAsync(original, 0, "draft");
        var published = await store.PublishAsync(original.Key, original.VersionId, 1, "publish");
        var pin = new DefinitionBinding(original.Key, original.VersionId);

        var restored = await store.RestoreAsDraftAsync(original.Key, original.VersionId,
            "version-b", "1.1.0", 2, "restore");
        var resolved = await store.ResolvePublishedAsync(pin);

        Assert.Equal(DefinitionStatus.Draft, restored.Status);
        Assert.Equal("version-a", restored.RestoredFromVersionId);
        Assert.Equal("version-b", restored.Document.VersionId);
        Assert.Equal("1.1.0", restored.Document.Version);
        Assert.Equal(original.BodyJson, restored.Document.BodyJson);
        Assert.Equal(published, resolved);
        Assert.Equal(original.BodyJson, resolved!.Document.BodyJson);
        Assert.Equal(new[] { 1L, 2L, 3L }, (await store.ListHistoryAsync(original.Key)).Select(item => item.Revision));
        Assert.Null(await store.ResolvePublishedAsync(new(original.Key, "version-b")));
        AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.ResolvePublishedAsync(new(original.Key, ""))), "definition.version_id_required", "/versionId");
    }

    [Fact]
    public async Task ConsumerPinStillResolvesTheOldBodyAfterTheHeadAdvances()
    {
        var store = Store();
        var original = Document();
        await store.SaveDraftAsync(original, 0, "draft-1");
        await store.PublishAsync(original.Key, original.VersionId, 1, "publish-1");
        var pin = new DefinitionBinding(original.Key, original.VersionId);
        var later = original with { VersionId = "version-b", Version = "2.0.0", BodyJson = "{\"gap\":2}" };
        await store.SaveDraftAsync(later, 2, "draft-2");
        await store.PublishAsync(later.Key, later.VersionId, 3, "publish-2");

        Assert.Equal(later, (await store.GetPublishedHeadAsync(original.Key))!.Document);
        Assert.Equal(original, (await store.ResolvePublishedAsync(pin))!.Document);
        Assert.Null(await store.ResolvePublishedAsync(pin with { Key = pin.Key with { DefinitionId = "different" } }));
    }

    [Fact]
    public async Task PublishPreservesMemberRefusalsAndDoesNotClampTheDraft()
    {
        var store = Store((document, phase) =>
        {
            using var body = JsonDocument.Parse(document.BodyJson);
            return phase == DefinitionAdmissionPhase.Publish && body.RootElement.GetProperty("gap").GetInt32() > 12
                ? [new("fixture.range", "/body/gap")] : [];
        });
        var invalid = Document() with { BodyJson = "{\"gap\":13}" };
        await store.SaveDraftAsync(invalid, 0, "draft");

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.PublishAsync(invalid.Key, invalid.VersionId, 1, "publish"));

        AssertRefusal(error, "fixture.range", "/body/gap");
        Assert.Equal(invalid.BodyJson, Assert.Single(await store.ListHistoryAsync(invalid.Key)).Document.BodyJson);
        Assert.Null(await store.GetPublishedHeadAsync(invalid.Key));
    }

    [Theory]
    [InlineData("99.bad.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0+bad..build")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0 ")]
    public async Task MalformedVersionsRefuseWithoutMutation(string version)
    {
        var store = Store();
        var invalid = Document() with { Version = version };
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(invalid, 0, "draft"));
        AssertRefusal(error, "definition.version_invalid", "/version");
        Assert.Empty(await store.ListHistoryAsync(invalid.Key));
    }

    [Fact]
    public async Task UnknownRegistryRefusesRatherThanUsingAnotherValidator()
    {
        var store = Store();
        var unknown = Document((DefinitionKind)999);
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(unknown, 0, "draft"));
        AssertRefusal(error, "definition.registry_unknown", "/registry");
    }

    [Fact]
    public async Task ARecognizedRegistryStillRequiresExplicitHostAdmission()
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>());
        var source = Document();

        AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(source, 0, "draft")), "definition.registry_unknown", "/registry");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvalidJsonRefusesWithoutAppendingHistory(string? body)
    {
        var store = Store();
        var source = Document() with { BodyJson = body! };

        AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(source, 0, "draft")), "definition.body_invalid", "/body");
        Assert.Empty(await store.ListHistoryAsync(source.Key));
    }

    [Fact]
    public async Task RestoreCannotPromoteADraftAsPublishedSource()
    {
        var store = Store();
        var source = Document();
        await store.SaveDraftAsync(source, 0, "draft");

        AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.RestoreAsDraftAsync(source.Key, source.VersionId, "version-b", "2.0.0", 1, "restore")),
            "definition.published_version_required", "/sourceVersionId");
        Assert.Single(await store.ListHistoryAsync(source.Key));
        Assert.Null(await store.ResolvePublishedAsync(new(source.Key, "version-b")));
    }

    [Fact]
    public async Task PublishedHeadUsesNumericSemverPrecedenceWithoutInt32Truncation()
    {
        var store = Store();
        var source = Document();
        var versions = new[] { "1.0.0-alpha.10", "1.0.0-alpha.2", "1.0.0+build.01", "2147483648.0.0", "2.0.0" };
        long revision = 0;
        for (int i = 0; i < versions.Length; i++)
        {
            var item = source with { Version = versions[i], VersionId = "version-" + i };
            await store.SaveDraftAsync(item, revision++, "draft-" + i);
            await store.PublishAsync(item.Key, item.VersionId, revision++, "publish-" + i);
            string expectedHead = i switch { 0 or 1 => "1.0.0-alpha.10", 2 => "1.0.0+build.01", _ => "2147483648.0.0" };
            Assert.Equal(expectedHead, (await store.GetPublishedHeadAsync(source.Key))!.Document.Version);
        }
    }

    [Fact]
    public async Task MutatingAReturnedHistoryArrayCannotReplaceAPublishedBody()
    {
        var store = Store();
        var source = Document();
        await store.SaveDraftAsync(source, 0, "draft");
        var published = await store.PublishAsync(source.Key, source.VersionId, 1, "publish");
        var history = await store.ListHistoryAsync(source.Key);
        if (history is IList<DefinitionRevision> list && !list.IsReadOnly)
            list[1] = published with { Document = source with { BodyJson = "{}" } };
        else if (history is DefinitionRevision[] array)
            array[1] = published with { Document = source with { BodyJson = "{}" } };

        Assert.Equal(source.BodyJson, (await store.ResolvePublishedAsync(new(source.Key, source.VersionId)))!.Document.BodyJson);
        Assert.Equal(published.Digest, (await store.ListHistoryAsync(source.Key))[1].Digest);
    }

    [Fact]
    public async Task PublishedVersionRejectsDraftEditsAndRestoreOverItsIdentity()
    {
        var store = Store();
        var source = Document();
        await store.SaveDraftAsync(source, 0, "draft");
        var published = await store.PublishAsync(source.Key, source.VersionId, 1, "publish");

        AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(source with { BodyJson = "{\"gap\":2}" }, 2, "edit")),
            "definition.version_immutable", "/versionId");
        AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.RestoreAsDraftAsync(source.Key, source.VersionId, source.VersionId, "2.0.0", 2, "restore")),
            "definition.version_conflict", "/versionId");

        Assert.Equal(published, await store.ResolvePublishedAsync(new(source.Key, source.VersionId)));
        Assert.Equal(2, (await store.ListHistoryAsync(source.Key)).Count);
    }

    [Fact]
    public async Task PublishCannotCommitTheSnapshotAfterAdmissionChangesTheDraft()
    {
        var source = Document();
        InMemoryVersionedDefinitionStore? store = null;
        store = Store((_, phase) =>
        {
            if (phase == DefinitionAdmissionPhase.Publish)
            {
                var edit = store!.SaveDraftAsync(source with { BodyJson = "{\"gap\":2}" }, 1, "edit-during-admission");
                Assert.True(edit.IsCompletedSuccessfully);
                Assert.Equal(2, edit.Result.Revision);
            }
            return [];
        });
        await store.SaveDraftAsync(source, 0, "draft");

        AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.PublishAsync(source.Key, source.VersionId, 1, "publish")),
            "definition.revision_conflict", "/expectedRevision");

        Assert.Null(await store.ResolvePublishedAsync(new(source.Key, source.VersionId)));
        var history = await store.ListHistoryAsync(source.Key);
        Assert.Equal(2, history.Count);
        Assert.Equal("{\"gap\":2}", history[1].Document.BodyJson);
        Assert.All(history, item => Assert.Equal(DefinitionStatus.Draft, item.Status));
    }

    [Fact]
    public async Task CancellationDuringAdmissionLeavesTheDraftAndHistoryUntouched()
    {
        using var cancellation = new CancellationTokenSource();
        var store = Store((_, phase) =>
        {
            if (phase == DefinitionAdmissionPhase.Publish) cancellation.Cancel();
            return [];
        });
        var source = Document();
        await store.SaveDraftAsync(source, 0, "draft");

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.PublishAsync(source.Key, source.VersionId, 1, "publish", cancellation.Token));

        Assert.Equal(source, Assert.Single(await store.ListHistoryAsync(source.Key)).Document);
        Assert.Null(await store.GetPublishedHeadAsync(source.Key));
    }

    [Fact]
    public async Task ReplayRequiresTheSameOperationFenceAndBody()
    {
        var store = Store();
        var source = Document();
        var first = await store.SaveDraftAsync(source, 0, "request");
        Assert.Equal(first, await store.SaveDraftAsync(source, 0, "request"));
        foreach (var (body, fence) in new[] { ("{\"gap\":2}", 0L), (source.BodyJson, 1L) })
            AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
                await store.SaveDraftAsync(source with { BodyJson = body }, fence, "request")),
                "definition.replay_conflict", "/requestId");
        AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.PublishAsync(source.Key, source.VersionId, 1, "request")),
            "definition.replay_conflict", "/requestId");
        Assert.Single(await store.ListHistoryAsync(source.Key));
        var published = await store.PublishAsync(source.Key, source.VersionId, 1, "publish");
        Assert.Equal(published, await store.PublishAsync(source.Key, source.VersionId, 1, "publish"));
        Assert.Equal(2, (await store.ListHistoryAsync(source.Key)).Count);
    }

    [Fact]
    public async Task ConcurrentEditsOfOneRevisionHaveOneWinnerAndLoseNoHistory()
    {
        var store = Store();
        var source = Document();
        await store.SaveDraftAsync(source, 0, "initial");
        async Task<bool> Edit(string body, string request)
        {
            try { await store.SaveDraftAsync(source with { BodyJson = body }, 1, request); return true; }
            catch (DefinitionRefusalException error)
            {
                AssertRefusal(error, "definition.revision_conflict", "/expectedRevision");
                return false;
            }
        }
        var outcomes = await Task.WhenAll(Task.Run(() => Edit("{\"gap\":2}", "a")),
            Task.Run(() => Edit("{\"gap\":3}", "b")));
        Assert.Single(outcomes, result => result);
        Assert.Equal(2, (await store.ListHistoryAsync(source.Key)).Count);
    }

    private static InMemoryVersionedDefinitionStore Store(DefinitionAdmission? admission = null)
        => new(Enum.GetValues<DefinitionKind>().ToDictionary(kind => kind,
            _ => admission ?? ((_, _) => Array.Empty<DefinitionRefusal>())));

    private static DefinitionDocument Document(DefinitionKind kind = DefinitionKind.Rules)
        => new(new("tenant-a", kind, "definition-a"), "version-a", "1.0.0", "{\"gap\":1}");

    private static void AssertRefusal(DefinitionRefusalException error, string code, string pointer)
    {
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal(code, refusal.Code);
        Assert.Equal(pointer, refusal.Pointer);
    }
}
