using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ExecutionRuntime;
using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

// Tests named Clause_<ids>_... are the DES-0054 §9 verification clauses, one test per clause; the Trait carries the
// clause's first id so the design record can point its probe at test:<name>.
public sealed class ContentStoreTests
{
    private static readonly TenantId TenantA = new("tenant-a");
    private static readonly TenantId TenantB = new("tenant-b");
    private static readonly byte[] Hello = "hello"u8.ToArray();

    [Fact, Trait("DES-0054", "content-store-ck-7")]
    public void Clause_ck7_eng1_put_one_byte_over_the_effective_limit_refuses_too_large_with_detail_and_stages_nothing()
    {
        var store = Store(itemLimit: 100);
        var field = new FieldNarrowing("field:inspection.photos", 4, null);
        var refusal = Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream(Hello), "text/plain", null, field));
        Assert.Equal(ContentRefusals.TooLarge, refusal.Code);
        Assert.Equal("field:inspection.photos", refusal.Definition);
        Assert.Equal(4, refusal.Detail["limit"]);
        Assert.Equal(5, refusal.Detail["observed"]);
        Assert.Equal(0, store.StagedCount);

        // A field limit above the store's never widens it: detail.limit is the store's (ck-7, board P4).
        var lower = Store(itemLimit: 4);
        var storeRefusal = Assert.Throws<ContentRefusedException>(() => lower.Put(TenantA, new MemoryStream(Hello), "text/plain", null, new FieldNarrowing("field:wide", 1000, null)));
        Assert.Equal(4, storeRefusal.Detail["limit"]);
        Assert.Equal(InMemoryContentStore.StoreDeclaration, storeRefusal.Definition);

        // Exactly at the limit is admitted.
        lower.Put(TenantA, new MemoryStream("hell"u8.ToArray()), "text/plain", null);
    }

    [Fact, Trait("DES-0054", "content-store-ck-8")]
    public void Clause_ck8_eng5_two_concurrent_commits_that_fit_alone_but_not_together_exactly_one_commits()
    {
        foreach (var (options, expected) in new[]
        {
            (new ContentStoreOptions(100, 5, 1000, TimeSpan.FromMinutes(1), 3), ContentRefusals.RecordQuota),
            (new ContentStoreOptions(100, 1000, 5, TimeSpan.FromMinutes(1), 3), ContentRefusals.TenantQuota),
        })
        {
            var store = new InMemoryContentStore(options, new TestKeys(), new FakeTimeProvider());
            var first = store.Put(TenantA, new MemoryStream(Hello), "text/plain", null);
            var second = store.Put(TenantA, new MemoryStream("world"u8.ToArray()), "text/plain", null);
            using var barrier = new Barrier(2);
            var outcomes = new[] { first, second }.AsParallel().WithDegreeOfParallelism(2).Select(stage =>
            {
                barrier.SignalAndWait();
                try
                {
                    store.Commit(TenantA, stage, new ContentOwner("record-1", stage.ToString()), "x.txt");
                    return null;
                }
                catch (ContentRefusedException refusal)
                {
                    return refusal;
                }
            }).ToArray();

            var refusal = Assert.Single(outcomes, outcome => outcome is not null)!;
            Assert.Equal(expected, refusal.Code);
            Assert.Equal(5, refusal.Detail["limit"]);
            Assert.Equal(10, refusal.Detail["observed"]);
            Assert.Equal(1, store.CommittedCount);
        }
    }

    [Fact, Trait("DES-0054", "content-store-ck-5")]
    public void Clause_ck5_ck6_eng3_html_renamed_pdf_refuses_mismatch_and_an_allowed_type_outside_the_field_list_refuses_not_allowed()
    {
        var store = Store();
        var html = "<!DOCTYPE html><html><script>alert(1)</script></html>"u8.ToArray();
        Assert.Equal(ContentRefusals.MediaTypeMismatch,
            Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream(html), "application/pdf", null)).Code);

        var narrow = new FieldNarrowing("field:pdf-only", null, new HashSet<string> { "application/pdf" });
        var refusal = Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream(Hello), "text/plain", null, narrow));
        Assert.Equal(ContentRefusals.MediaTypeNotAllowed, refusal.Code);
        Assert.Equal("field:pdf-only", refusal.Definition);

        // Bytes with no admitted signature and no text shape are outside the store allowlist.
        var zip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01 };
        Assert.Equal(ContentRefusals.MediaTypeNotAllowed,
            Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream(zip), null, null)).Code);
        Assert.Equal(0, store.StagedCount);
    }

    [Fact, Trait("DES-0054", "content-store-eng-2")]
    public void Clause_eng2_a_declared_digest_that_differs_from_the_bytes_refuses_digest_mismatch()
    {
        var store = Store();
        var wrong = Convert.ToHexStringLower(SHA256.HashData("different"u8));
        Assert.Equal(ContentRefusals.DigestMismatch,
            Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream(Hello), "text/plain", wrong)).Code);
        Assert.Equal(ContentRefusals.DigestMismatch,
            Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream(Hello), "text/plain", "not-hex")).Code);
        Assert.Equal(0, store.StagedCount);

        store.Put(TenantA, new MemoryStream(Hello), "text/plain", Convert.ToHexStringLower(SHA256.HashData(Hello)));
        Assert.Equal(1, store.StagedCount);
    }

    [Fact, Trait("DES-0054", "content-store-ck-4")]
    public void Clause_ck4_eng4_same_bytes_twice_in_one_tenant_store_one_item_and_in_two_tenants_two_items()
    {
        var store = Store();
        var first = Commit(store, TenantA, "record-1");
        var againStage = store.Put(TenantA, new MemoryStream(Hello), "text/plain", null);
        Assert.Equal(1, store.StagedCount); // the put stages its own copy, exactly as a put of new bytes (board P2)
        var again = store.Commit(TenantA, againStage, new ContentOwner("record-2", "photos/0"), "y.txt");
        Assert.Equal(first.ContentId, again.ContentId);
        Assert.Equal(1, store.CommittedCount);
        Assert.Equal(0, store.StagedCount);

        var fresh = Commit(store, TenantA, "record-3", "other"u8.ToArray());
        Assert.Equal(typeof(ContentReference), fresh.GetType()); // same answer shape as the collapsed commit
        var other = Commit(store, TenantB, "record-1");
        Assert.NotEqual(first.ContentId, other.ContentId);
        Assert.Equal(3, store.CommittedCount);
    }

    [Fact, Trait("DES-0054", "content-store-eng-6")]
    public void Clause_eng6_eng11_an_unauthorized_read_and_another_tenants_read_refuse_exactly_as_a_nonexistent_id()
    {
        var store = Store();
        var reference = Commit(store, TenantA, "record-1");
        var missing = Assert.Throws<ContentNotFoundException>(() => store.Read(TenantA, reference with { ContentId = new("hmac-sha256:" + new string('a', 64)) }, true));
        var denied = Assert.Throws<ContentNotFoundException>(() => store.Read(TenantA, reference, false));
        var cross = Assert.Throws<ContentNotFoundException>(() => store.Read(TenantB, reference, true));
        var wrongOwner = Assert.Throws<ContentNotFoundException>(() => store.Read(TenantA, reference with { Owner = new("record-9", "x") }, true));
        Assert.All(new[] { denied, cross, wrongOwner }, refusal => Assert.Equal(missing.Message, refusal.Message));
    }

    [Fact, Trait("DES-0054", "content-store-eng-7")]
    public void Clause_eng7_a_served_item_carries_nosniff_attachment_a_strong_etag_and_only_the_detected_type()
    {
        var store = Store();
        var stage = store.Put(TenantA, new MemoryStream("a,b\r\n1,2\r\n"u8.ToArray()), "text/csv; charset=utf-8", null);
        var reference = store.Commit(TenantA, stage, new ContentOwner("record-1", "files/0"), "evil\"\r\nSet-Cookie: x.csv");
        var delivery = store.Read(TenantA, reference, true);
        Assert.Equal("nosniff", ContentDelivery.XContentTypeOptions);
        Assert.Equal("text/csv", delivery.ContentType);
        Assert.Equal("attachment; filename=\"evilSet-Cookie: x.csv\"", delivery.ContentDisposition);
        Assert.Equal("\"" + reference.ContentId.Value + "\"", delivery.ETag);
        Assert.DoesNotContain("W/", delivery.ETag, StringComparison.Ordinal);
        Assert.Equal(200, delivery.StatusCode);
    }

    [Fact, Trait("DES-0054", "content-store-ck-10")]
    public void Clause_ck10_eng9_erasing_one_of_two_references_keeps_the_bytes_and_erasing_the_last_reclaims_them()
    {
        var store = Store();
        var first = Commit(store, TenantA, "record-1");
        var second = Commit(store, TenantA, "record-2");
        Assert.False(store.RemoveReference(TenantA, first, HoldStatus.None, "disposal-1"));
        Assert.Equal(Hello, store.Read(TenantA, second, true).Bytes);

        Assert.True(store.RemoveReference(TenantA, second, HoldStatus.None, "disposal-2"));
        Assert.Throws<ContentNotFoundException>(() => store.Read(TenantA, second, true));
        Assert.Equal(0, store.CommittedCount);
        var entry = Assert.Single(store.Reclamations);
        Assert.Equal(new ReclamationEntry(second.ContentId, TenantA, "last-reference-removed", "disposal-2"), entry);
    }

    [Fact, Trait("DES-0054", "content-store-cc-4")]
    public void Clause_ck10_cc4_a_hold_keeps_the_bytes_until_released_and_an_unreadable_hold_fails_closed()
    {
        var store = Store();
        var held = Commit(store, TenantA, "record-held");
        var other = Commit(store, TenantA, "record-other");
        Assert.False(store.RemoveReference(TenantA, other, HoldStatus.None, "disposal-1"));
        Assert.False(store.RemoveReference(TenantA, held, HoldStatus.Held, "disposal-2"));
        Assert.False(store.RemoveReference(TenantA, held, HoldStatus.Unreadable, "disposal-3"));
        Assert.Equal(Hello, store.Read(TenantA, held, true).Bytes);
        Assert.Empty(store.Reclamations);

        Assert.True(store.RemoveReference(TenantA, held, HoldStatus.None, "disposal-4"));
        Assert.Single(store.Reclamations);
    }

    [Fact, Trait("DES-0054", "content-store-ck-13")]
    public void Clause_ck13_a_staged_item_nobody_commits_expires_under_the_registered_sweep_with_no_committed_orphan()
    {
        var clock = new FakeTimeProvider();
        var store = Store(clock: clock);
        var registry = new RunKindRegistry();
        ContentStoreRunRegistration.Register(registry);
        Assert.Equal("content-store", registry.Require(ContentStoreRunRegistration.StagingSweepKind).OwningEngine);

        var abandoned = store.Put(TenantA, new MemoryStream(Hello), "text/plain", null);
        var late = store.Put(TenantA, new MemoryStream("late"u8.ToArray()), "text/plain", null);
        clock.Advance(TimeSpan.FromMinutes(1));

        // Commit after expiry refuses even before any sweep has run.
        Assert.Equal(ContentRefusals.StagingExpired,
            Assert.Throws<ContentRefusedException>(() => store.Commit(TenantA, late, new ContentOwner("record-1", "0"), "x.txt")).Code);

        // No request is in flight: the put returned long ago; the sweep alone reclaims.
        Assert.Equal(1, store.SweepExpired("sweep-1"));
        Assert.Equal(0, store.StagedCount);
        Assert.Equal(0, store.CommittedCount);
        Assert.All(store.Reclamations, entry => Assert.Equal("staging-expired", entry.Cause));
        Assert.Contains(store.Reclamations, entry => entry.Run == "sweep-1");
        Assert.Equal(ContentRefusals.StagingExpired,
            Assert.Throws<ContentRefusedException>(() => store.Commit(TenantA, abandoned, new ContentOwner("record-1", "0"), "x.txt")).Code);
    }

    [Fact, Trait("DES-0054", "content-store-ck-12")]
    public void Clause_ck12_cc5_two_references_to_one_item_are_each_governed_by_their_own_marking()
    {
        var store = Store();
        var open = Commit(store, TenantA, "record-open");
        var restricted = Commit(store, TenantA, "record-restricted");
        Assert.Equal(open.ContentId, restricted.ContentId);

        // The gate evaluates each reference's field marking upstream; the store keeps no marking of its own.
        Assert.Equal(Hello, store.Read(TenantA, open, authorized: true).Bytes);
        Assert.Throws<ContentNotFoundException>(() => store.Read(TenantA, restricted, authorized: false));
    }

    [Fact, Trait("DES-0054", "content-store-cc-7")]
    public void Clause_cc7_export_fixity_verifies_every_keyed_digest_a_tampered_byte_refuses_and_no_plain_sha256_is_carried()
    {
        var store = Store();
        var references = new[] { Commit(store, TenantA, "record-1"), Commit(store, TenantA, "record-2", "second"u8.ToArray()) };
        var export = store.Export(TenantA, references, authorized: true);
        InMemoryContentStore.VerifyExport(export.Package, export.FixityKey);

        foreach (var item in export.Package.Items)
        {
            Assert.NotEqual(SHA256.HashData(item.Bytes), item.Digest);
            Assert.DoesNotContain(Convert.ToHexStringLower(SHA256.HashData(item.Bytes)), System.Text.Json.JsonSerializer.Serialize(export.Package), StringComparison.Ordinal);
        }

        Assert.NotEqual(export.FixityKey, store.Export(TenantA, references, authorized: true).FixityKey); // fresh per export
        export.Package.Items[1].Bytes[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => InMemoryContentStore.VerifyExport(export.Package, export.FixityKey));
        Assert.Throws<ContentNotFoundException>(() => store.Export(TenantA, references, authorized: false));
    }

    [Fact]
    public void Identical_bytes_in_two_tenants_get_different_keyed_ids_and_no_plain_digest_is_persisted()
    {
        var store = Store();
        var plain = SHA256.HashData(Hello);
        var stage = store.Put(TenantA, new MemoryStream(Hello), "text/plain", Convert.ToHexStringLower(plain));
        var a = store.Commit(TenantA, stage, new ContentOwner("record-1", "0"), "x.txt");
        var b = Commit(store, TenantB, "record-1");
        Assert.NotEqual(a.ContentId, b.ContentId);
        Assert.Equal("hmac-sha256:" + Convert.ToHexStringLower(HMACSHA256.HashData(new TestKeys().GetKey(TenantA), Hello)), a.ContentId.Value);

        var retained = Reachable(store);
        Assert.DoesNotContain(retained.OfType<byte[]>(), bytes => bytes.AsSpan().IndexOf(plain) >= 0);
        Assert.DoesNotContain(retained.OfType<string>(), text => text.Contains(Convert.ToHexStringLower(plain), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_tampered_or_truncated_ciphertext_refuses_on_read()
    {
        var store = Store();
        var tampered = Commit(store, TenantA, "record-1", "0123456789"u8.ToArray());
        store.CorruptCiphertext(TenantA, tampered.ContentId, 1);
        Assert.ThrowsAny<CryptographicException>(() => store.Read(TenantA, tampered, true));

        var truncated = Commit(store, TenantA, "record-2", "abcdefghij"u8.ToArray());
        store.TruncateLastSegment(TenantA, truncated.ContentId);
        Assert.ThrowsAny<CryptographicException>(() => store.Read(TenantA, truncated, true));
    }

    [Fact]
    public void A_single_range_answers_206_across_segments_416_when_unsatisfiable_and_the_whole_item_on_a_stale_if_range()
    {
        var store = Store();
        var reference = Commit(store, TenantA, "record-1", "0123456789"u8.ToArray());
        var etag = "\"" + reference.ContentId.Value + "\"";

        var partial = store.Read(TenantA, reference, true, new ContentByteRange(2, 7));
        Assert.Equal(206, partial.StatusCode);
        Assert.Equal("234567"u8.ToArray(), partial.Bytes);
        Assert.Equal("bytes 2-7/10", partial.ContentRange);

        var tail = store.Read(TenantA, reference, true, new ContentByteRange(8, 500), etag);
        Assert.Equal("89"u8.ToArray(), tail.Bytes);
        Assert.Equal("bytes 8-9/10", tail.ContentRange);

        var unsatisfiable = store.Read(TenantA, reference, true, new ContentByteRange(10, 12));
        Assert.Equal(416, unsatisfiable.StatusCode);
        Assert.Equal("bytes */10", unsatisfiable.ContentRange);

        var stale = store.Read(TenantA, reference, true, new ContentByteRange(2, 7), "\"hmac-sha256:" + new string('0', 64) + "\"");
        Assert.Equal(200, stale.StatusCode);
        Assert.Equal("0123456789"u8.ToArray(), stale.Bytes);
    }

    [Fact]
    public void The_text_check_is_exactly_whatwg_7_1_over_the_1445_byte_resource_header()
    {
        var store = Store(itemLimit: 4000);
        store.Put(TenantA, new MemoryStream("esc\u001B[0m"u8.ToArray()), "text/plain", null); // 0x1B is not a binary data byte
        store.Put(TenantA, new MemoryStream([0xFF, 0xFE, 0x68, 0x00]), "text/plain", null); // UTF-16LE with a BOM
        Assert.Equal(ContentRefusals.MediaTypeMismatch,
            Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream([0x68, 0x00, 0x69, 0x00]), "text/plain", null)).Code); // UTF-16 without a BOM

        var late = Enumerable.Repeat((byte)'a', 1445).Concat(new byte[] { 0x00 }).ToArray();
        store.Put(TenantA, new MemoryStream(late), "text/plain", null); // known limit: binary after the header (ck-6)
        var early = Enumerable.Repeat((byte)'a', 1444).Concat(new byte[] { 0x00 }).ToArray();
        Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream(early), "text/plain", null));
    }

    [Fact]
    public void A_commit_naming_another_tenants_stage_refuses_and_leaves_that_stage_intact()
    {
        var store = Store();
        var stage = store.Put(TenantA, new MemoryStream(Hello), "text/plain", null);
        Assert.Equal(ContentRefusals.StagingExpired,
            Assert.Throws<ContentRefusedException>(() => store.Commit(TenantB, stage, new ContentOwner("record-1", "0"), "x.txt")).Code);
        Assert.Equal(1, store.StagedCount);
        store.Commit(TenantA, stage, new ContentOwner("record-1", "0"), "x.txt");
    }

    [Fact]
    public void The_store_refuses_to_start_without_every_unstated_measurement()
    {
        Assert.Throws<ArgumentException>(() => new InMemoryContentStore(new(0, 1, 1, TimeSpan.FromMinutes(1), 1), new TestKeys(), new FakeTimeProvider()));
        Assert.Throws<ArgumentException>(() => new InMemoryContentStore(new(1, 0, 1, TimeSpan.FromMinutes(1), 1), new TestKeys(), new FakeTimeProvider()));
        Assert.Throws<ArgumentException>(() => new InMemoryContentStore(new(1, 1, 0, TimeSpan.FromMinutes(1), 1), new TestKeys(), new FakeTimeProvider()));
        Assert.Throws<ArgumentException>(() => new InMemoryContentStore(new(1, 1, 1, TimeSpan.Zero, 1), new TestKeys(), new FakeTimeProvider()));
        Assert.Throws<ArgumentException>(() => new InMemoryContentStore(new(1, 1, 1, TimeSpan.FromMinutes(1), 0), new TestKeys(), new FakeTimeProvider()));
        Assert.Equal(4, Enum.GetValues<ContentLifecycleState>().Length); // quarantined reserved, closed at four (§6)
    }

    [Fact]
    public void Erasure_collapse_and_the_sweep_destroy_the_data_key_and_the_ciphertext()
    {
        var clock = new FakeTimeProvider();
        var store = Store(clock: clock);
        var reference = Commit(store, TenantA, "record-1");
        var committed = store.KeyMaterial(TenantA, reference.ContentId);
        Assert.False(store.RemoveReference(TenantA, reference with { Owner = new("record-9", "x") }, HoldStatus.None, "disposal-0"));
        Assert.True(store.RemoveReference(TenantA, reference, HoldStatus.None, "disposal-1"));
        Assert.All(committed.WrappedDataKey, value => Assert.Equal(0, value));
        Assert.Empty(committed.Segments);

        var kept = Commit(store, TenantA, "record-2");
        var duplicate = store.Put(TenantA, new MemoryStream(Hello), "text/plain", null);
        var collapsed = store.KeyMaterial(duplicate);
        store.Commit(TenantA, duplicate, new ContentOwner("record-3", "0"), "x.txt");
        Assert.All(collapsed.WrappedDataKey, value => Assert.Equal(0, value));
        Assert.Empty(collapsed.Segments);
        Assert.Equal(Hello, store.Read(TenantA, kept, true).Bytes);

        var abandoned = store.Put(TenantA, new MemoryStream(Hello), "text/plain", null);
        var swept = store.KeyMaterial(abandoned);
        clock.Advance(TimeSpan.FromMinutes(1));
        store.SweepExpired("sweep-1");
        Assert.All(swept.WrappedDataKey, value => Assert.Equal(0, value));
        Assert.Empty(swept.Segments);
    }

    [Fact]
    public void Every_byte_below_0x20_is_classified_exactly_as_whatwg_7_1_binary_data_bytes()
    {
        var binary = new HashSet<int>(Enumerable.Range(0x00, 9).Concat([0x0B]).Concat(Enumerable.Range(0x0E, 13)).Concat(Enumerable.Range(0x1C, 4)));
        var store = Store();
        for (var value = 0; value <= 0x20; value++)
        {
            var bytes = new byte[] { (byte)'a', (byte)value, (byte)'b' };
            if (binary.Contains(value))
            {
                Assert.Equal(ContentRefusals.MediaTypeMismatch, Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream(bytes), "text/plain", null)).Code);
            }
            else
            {
                store.Put(TenantA, new MemoryStream(bytes), "text/plain", null);
            }
        }

        store.Put(TenantA, new MemoryStream("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()), "image/webp", null);
        Assert.Throws<ContentRefusedException>(() => store.Put(TenantA, new MemoryStream("RIFF\0\0\0\0WEBP"u8.ToArray()), "image/webp", null));
    }

    [Fact]
    public void A_short_tenant_key_a_malformed_content_id_and_a_missing_segment_are_refused()
    {
        var store = new InMemoryContentStore(new(100, 100, 100, TimeSpan.FromMinutes(1), 3), new ShortKeys(), new FakeTimeProvider());
        Assert.Throws<InvalidOperationException>(() => store.Put(TenantA, new MemoryStream(Hello), "text/plain", null));

        foreach (var malformed in new[] { "sha256:" + new string('a', 64), "hmac-sha256:" + new string('a', 63), "hmac-sha256:" + new string('A', 64), "hmac-sha256:" + new string('g', 64), "hmac-sha256:" + new string('/', 64) })
        {
            Assert.Throws<ArgumentException>(() => new ContentId(malformed));
        }

        var valid = Store();
        var reference = Commit(valid, TenantA, "record-1", "0123456789"u8.ToArray());
        Assert.Equal("5"u8.ToArray(), valid.Read(TenantA, reference, true, new ContentByteRange(5, 5)).Bytes);
        valid.DropLastSegmentKeepingLength(TenantA, reference.ContentId);
        Assert.ThrowsAny<CryptographicException>(() => valid.Read(TenantA, reference, true));
    }

    private static ContentReference Commit(InMemoryContentStore store, TenantId tenant, string record, byte[]? bytes = null) =>
        store.Commit(tenant, store.Put(tenant, new MemoryStream(bytes ?? Hello), "text/plain", null), new ContentOwner(record, "files/0"), "x.txt");

    // Segment size 3 so that every multi-byte test item spans several segments.
    private static InMemoryContentStore Store(long itemLimit = 100, FakeTimeProvider? clock = null) =>
        new(new(itemLimit, 1000, 1000, TimeSpan.FromMinutes(1), 3), new TestKeys(), clock ?? new FakeTimeProvider());

    // Every object reachable from the store's fields, for the "no plain digest persisted" check.
    private static List<object> Reachable(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>([root]);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current) || current is string or byte[] || current.GetType().IsPrimitive) continue;
            if (current is IEnumerable sequence)
            {
                foreach (var element in sequence) if (element is not null) pending.Push(element);
            }

            for (var type = current.GetType(); type is not null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.GetValue(current) is { } value) pending.Push(value);
                }
            }
        }

        return [.. seen];
    }

    private sealed class TestKeys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => SHA256.HashData(Encoding.UTF8.GetBytes("test-key:" + tenantId.Value));
    }

    private sealed class ShortKeys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => new byte[16];
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
