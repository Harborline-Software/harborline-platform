using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

public sealed class ContentStoreStagedPrincipalTests
{
    [Theory]
    [InlineData("tenant-a", "another-principal")]
    [InlineData("tenant-b", "uploader")]
    public async Task Another_principal_or_tenant_gets_staging_expired_and_leaves_the_stage_untouched(string tenant, string actor)
    {
        // ADR-0142 decision 3: a stage is bound to the authenticated put actor and tenant.
        var storage = new InMemoryContentStoreStorage();
        var store = Store(storage);
        var stage = await store.PutAsync(new("tenant-a"), new MemoryStream("evidence"u8.ToArray()), "text/plain", null,
            ContentStoreWriteEvidence.New("uploader"));
        var before = Assert.Single(await storage.Bookkeeping.StagedCreatedAtOrBeforeAsync(DateTimeOffset.MaxValue));

        var refusal = await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(
            new(tenant), stage, new("record", "evidence"), "evidence.txt", ContentStoreWriteEvidence.New(actor)));

        Assert.Equal("content.staging-expired", refusal.Code);
        Assert.Equal("content-store", refusal.Definition);
        Assert.Empty(refusal.Detail);
        Assert.Equal(before, Assert.Single(await storage.Bookkeeping.StagedCreatedAtOrBeforeAsync(DateTimeOffset.MaxValue)));
        Assert.Equal(0, store.CommittedCount);
        // The original principal can still commit: the rejected attempt neither consumes nor destroys the stage.
        var reference = await store.CommitReferenceAsync(new("tenant-a"), stage, new("record", "evidence"), "evidence.txt",
            ContentStoreWriteEvidence.New("uploader"));
        Assert.Equal(8, reference.Length);
        Assert.Equal(0, store.StagedCount);
        Assert.Equal(1, store.CommittedCount);
    }

    [Fact]
    public async Task Committed_stage_is_single_use_even_for_the_same_principal()
    {
        var store = Store(new());
        var stage = await store.PutAsync(new("tenant-a"), new MemoryStream("evidence"u8.ToArray()), "text/plain", null,
            ContentStoreWriteEvidence.New("uploader"));
        await store.CommitReferenceAsync(new("tenant-a"), stage, new("record", "evidence"), "one.txt", ContentStoreWriteEvidence.New("uploader"));
        var error = await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(new("tenant-a"),
            stage, new("another-record", "evidence"), "two.txt", ContentStoreWriteEvidence.New("uploader")));
        Assert.Equal("content.staging-expired", error.Code);
        Assert.Equal(1, store.CommittedCount);
        Assert.Equal(0, store.StagedCount);
    }

    private static InMemoryContentStore Store(InMemoryContentStoreStorage storage) =>
        new(new(100, 1000, 10000, TimeSpan.FromHours(1)), new Keys(), TimeProvider.System, storage);
    private sealed class Keys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenant) => SHA256.HashData(Encoding.UTF8.GetBytes("test:" + tenant.Value));
    }
}
