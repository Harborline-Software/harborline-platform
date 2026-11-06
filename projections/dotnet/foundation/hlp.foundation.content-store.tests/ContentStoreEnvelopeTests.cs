using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

// The stated formats, checked by reference code written here from DES-0054 eng-10, Q20, Q25, Q28 and R-0136, never by reading
// the store's helpers: the HKDF labels, the wrap, the segment nonce (7-byte prefix, 4-byte big-endian index, 1-byte last flag),
// the fixity digest, and range arithmetic against a pattern where every byte says where it came from.
public sealed class ContentStoreEnvelopeTests
{
    private static readonly TenantId Tenant = new("envelope-tenant");
    private static readonly byte[] TenantKey = SHA256.HashData(Encoding.UTF8.GetBytes("envelope"));
    private static readonly byte[] Pattern = [.. Enumerable.Range(0, (65_536 * 2) + 10).Select(index => (byte)((index % 95) + 0x20))];

    private static InMemoryContentStore Store(IContentStoreStorage? backing = null, long itemLimit = 300_000) =>
        new(new(itemLimit, 1_000_000, 1_000_000, TimeSpan.FromMinutes(1)), new Keys(), new Clock(), backing ?? new InMemoryContentStoreStorage());

    private static async Task<ContentReference> Commit(InMemoryContentStore store, byte[] bytes) =>
        await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(bytes), "text/plain", null), new("record-1", "0"), "a.txt");

    private static async Task<byte[]> Read(ContentDelivery delivery)
    {
        await using var body = delivery.Body;
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static byte[] Subkey(string info)
    {
        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, TenantKey, key, Encoding.UTF8.GetBytes(Tenant.Value), Encoding.ASCII.GetBytes(info));
        return key;
    }

    [Fact]
    public async Task Eng10_the_stored_item_opens_under_the_stated_labels_wrap_and_segment_nonces()
    {
        var backing = new InMemoryContentStoreStorage();
        var store = Store(backing);
        var reference = await Commit(store, Pattern);
        var item = await store.StorageItemAsync(Tenant, reference.ContentId);

        Assert.Equal("hmac-sha256:" + Convert.ToHexStringLower(HMACSHA256.HashData(Subkey("harborline-content-store:content-id:v1"), Pattern)), reference.ContentId.Value);

        // The data-key-wrap subkey opens only the epoch key, and the epoch key opens the data key (eng-10, ADR 0116 decision 1).
        var epoch = (await backing.Epochs.GetAsync(Tenant, item.EpochId))!;
        var epochKey = new byte[32];
        using (var subkey = new AesGcm(Subkey("harborline-content-store:data-key-wrap:v1"), 16))
        {
            subkey.Decrypt(epoch.WrapNonce, epoch.WrappedKey, epoch.WrapTag, epochKey,
                Encoding.UTF8.GetBytes("harborline-content-store:epoch-key:v1|" + epoch.EpochId.ToString("N") + "|" + Tenant.Value));
            Assert.ThrowsAny<CryptographicException>(() =>
                subkey.Decrypt(item.WrapNonce, item.WrappedDataKey, item.WrapTag, new byte[32], Encoding.ASCII.GetBytes(reference.ContentId.Value)));
        }

        var dataKey = new byte[32];
        using (var wrap = new AesGcm(epochKey, 16))
        {
            wrap.Decrypt(item.WrapNonce, item.WrappedDataKey, item.WrapTag, dataKey, Encoding.ASCII.GetBytes(reference.ContentId.Value));
        }

        using var aead = new AesGcm(dataKey, 16);
        using var opened = new MemoryStream();
        for (var index = 0; index < 3; index++)
        {
            var segment = (await backing.Bytes.ReadSegmentAsync(item.StageId, index))!;
            var nonce = new byte[12];
            item.NoncePrefix.CopyTo(nonce, 0);
            nonce[7] = (byte)(index >> 24);
            nonce[8] = (byte)(index >> 16);
            nonce[9] = (byte)(index >> 8);
            nonce[10] = (byte)index;
            nonce[11] = index == 2 ? (byte)1 : (byte)0;
            var clear = new byte[segment.Ciphertext.Length];
            aead.Decrypt(nonce, segment.Ciphertext, segment.Tag, clear);
            opened.Write(clear);
        }

        Assert.Equal(7, item.NoncePrefix.Length);
        Assert.Equal(65_536, item.SegmentSize);
        Assert.Equal(Pattern, opened.ToArray());
        Assert.Null(await backing.Bytes.ReadSegmentAsync(item.StageId, 3));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(65_530, 65_540)]
    [InlineData(65_536, 65_536)]
    [InlineData(70_000, 70_010)]
    [InlineData(65_535, 131_080)]
    [InlineData(131_072, 131_081)]
    public async Task Eng8_a_range_answers_exactly_those_bytes_wherever_it_falls_in_the_segments(int start, int end)
    {
        var store = Store();
        var reference = await Commit(store, Pattern);
        var delivery = await store.ReadAsync(Tenant, reference, true, new ContentByteRange(start, end));
        Assert.Equal(206, delivery.StatusCode);
        Assert.Equal(Pattern[start..(end + 1)], await Read(delivery));
    }

    [Fact]
    public async Task Cc7_the_fixity_digest_is_the_stated_length_prefixed_hmac()
    {
        var store = Store();
        var reference = await Commit(store, "fixity"u8.ToArray());
        var export = await store.ExportAsync(Tenant, [reference], true);
        var id = Encoding.ASCII.GetBytes(reference.ContentId.Value);
        var input = new byte[4 + id.Length + 6];
        BinaryPrimitives.WriteUInt32BigEndian(input, (uint)id.Length);
        id.CopyTo(input, 4);
        "fixity"u8.CopyTo(input.AsSpan(4 + id.Length));
        Assert.Equal(HMACSHA256.HashData(export.FixityKey, input), Assert.Single(export.Package.Items).Digest);
    }

    [Theory]
    [InlineData("application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 })]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 })]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20 })]
    public async Task Eng3_each_signature_is_stored_and_served_as_its_type_with_or_without_a_declaration(string expected, byte[] bytes)
    {
        var store = Store();
        var undeclared = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(bytes), null, null), new("record-1", "0"), "a");
        Assert.Equal(expected, (await store.ReadAsync(Tenant, undeclared, true)).ContentType);
        var declared = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(bytes), expected, null), new("record-2", "0"), "b");
        Assert.Equal(expected, declared.MediaType);
    }

    [Theory]
    [InlineData(null, "text/plain")]
    [InlineData("text/plain", "text/plain")]
    [InlineData("text/csv", "text/csv")]
    public async Task Eng3_text_takes_its_type_from_the_declaration(string? declared, string expected)
    {
        var store = Store();
        var reference = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream("a,b"u8.ToArray()), declared, null), new("record-1", "0"), "a");
        Assert.Equal(expected, reference.MediaType);
    }

    [Fact]
    public async Task Eng3_text_that_merely_contains_a_webp_marker_is_text()
    {
        var store = Store();
        var reference = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream("XXXXYYYYWEBPVP8 "u8.ToArray()), null, null), new("record-1", "0"), "a");
        Assert.Equal("text/plain", reference.MediaType);
    }

    [Fact]
    public async Task Ck9_every_remaining_refusal_keeps_its_stated_message()
    {
        var store = Store();
        var binary = new byte[] { 0x00, 0x01, 0x02 };
        Assert.Equal("The detected media type is not allowed.",
            (await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(binary), null, null))).Message);
        Assert.Equal("The declared media type disagrees with the bytes.",
            (await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream("%PDF-1.7"u8.ToArray()), "image/png", null))).Message);
        Assert.Equal("The declared text type is on bytes that are not text.",
            (await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(binary), "text/csv", null))).Message);
        Assert.StartsWith("A field size limit must be positive.",
            (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.PutAsync(Tenant, new MemoryStream(binary), null, null, field: new("zero", 0, null)))).Message, StringComparison.Ordinal);
        Assert.Equal("No readable content exists for that reference.", new ContentNotFoundException().Message);
        Assert.Equal("Content integrity verification failed.", new ContentIntegrityException(Guid.Empty, new("r", "e"), 0, new InvalidOperationException()).Message);
        Assert.Equal("Every content-store measurement must be supplied and positive; none has a default.",
            Assert.Throws<ArgumentException>(() => new ContentStoreOptions(0, 1, 1, TimeSpan.FromMinutes(1)).Validate()).Message);

        var reference = await Commit(store, "fixity"u8.ToArray());
        var export = await store.ExportAsync(Tenant, [reference], true);
        Assert.Equal("An exported item failed its per-export fixity check.",
            Assert.Throws<InvalidDataException>(() => InMemoryContentStore.VerifyExport(export.Package, new byte[32])).Message);
        var shortKey = new InMemoryContentStore(new(100, 100, 100, TimeSpan.FromMinutes(1)), new ShortKey(), new Clock(), new InMemoryContentStoreStorage());
        Assert.Equal("A tenant content key must be 32 bytes.",
            (await Assert.ThrowsAsync<InvalidOperationException>(async () => await shortKey.PutAsync(Tenant, new MemoryStream(binary), null, null))).Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hmac-sha256:")]
    [InlineData("HMAC-SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("hmac-sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaG")]
    [InlineData("hmac-sha256:Aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Ck2_a_content_id_is_only_the_stated_form(string value)
    {
        var refused = Assert.ThrowsAny<ArgumentException>(() => new ContentId(value));
        if (value.Length != 0) Assert.StartsWith("A content id is hmac-sha256: followed by 64 lower-case hex digits.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ck2_a_null_content_id_and_a_default_one_are_handled()
    {
        Assert.Throws<ArgumentNullException>(() => new ContentId(null!));
        Assert.Equal(string.Empty, default(ContentId).ToString());
        Assert.Throws<ArgumentNullException>(() => ContentStoreRunRegistration.Register(null!));
    }

    [Fact]
    public async Task Q34_a_healthy_full_read_opens_no_transaction_at_all()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, "healthy"u8.ToArray());
        var begun = backing.TransactionsBegun;
        Assert.Equal("healthy"u8.ToArray(), await Read(await store.ReadAsync(Tenant, reference, true)));
        Assert.Equal(begun, backing.TransactionsBegun);
    }

    private sealed class Keys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => TenantKey;
    }

    private sealed class ShortKey : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => new byte[16];
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
