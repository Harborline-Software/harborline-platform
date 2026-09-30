using System.Buffers.Binary;
using System.Collections;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ExecutionRuntime;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Harborline.Foundation.ContentStore.Tests")]

namespace Harborline.Foundation.ContentStore;

// DES-0054 (harborline-control designs/DES-0054-content-store/design.md) is the contract implemented here; each
// constant below cites its row. Every number DES-0054 leaves to the first DES-0037 measurement (ck-7, ck-8, ck-13;
// §10 ruling 1, board P3, Q19) is a required ContentStoreOptions input with no default.

/// <summary>Supplies a tenant's 256-bit key without prescribing where keys are stored (DES-0054 ck-2, eng-10).</summary>
public interface ITenantContentKeyProvider
{
    /// <summary>Returns the tenant key that keys content ids and wraps each item's data key. Must be 32 bytes.</summary>
    byte[] GetKey(TenantId tenantId);
}

/// <summary>
/// The installation values DES-0054 does not state. Each is required and positive; none has a default, because
/// DES-0054 ck-7, ck-8 and ck-13 assign them to the first DES-0037 measurement (§10 ruling 1, board P3, Q19), and no
/// segment size is stated for eng-10.
/// </summary>
/// <param name="ItemLimitBytes">The store's per-item plaintext limit (ck-7), set from the streamed-upload evidence.</param>
/// <param name="RecordQuotaBytes">The logical-byte quota per record (ck-8): the stated share of the tenant quota.</param>
/// <param name="TenantQuotaBytes">The logical-byte quota per tenant (ck-8): the stated fraction of the runtime base (Q22).</param>
/// <param name="StagingExpiry">How long an uncommitted staged item lives (ck-13): the stated multiple of the measured put time.</param>
/// <param name="SegmentSizeBytes">The plaintext bytes per AEAD segment (eng-10).</param>
public sealed record ContentStoreOptions(
    long ItemLimitBytes,
    long RecordQuotaBytes,
    long TenantQuotaBytes,
    TimeSpan StagingExpiry,
    int SegmentSizeBytes)
{
    /// <summary>Refuses a missing or non-positive value; the store never substitutes one.</summary>
    public void Validate()
    {
        if (ItemLimitBytes <= 0 || RecordQuotaBytes <= 0 || TenantQuotaBytes <= 0 ||
            StagingExpiry <= TimeSpan.Zero || SegmentSizeBytes <= 0)
        {
            throw new ArgumentException("Every content-store measurement must be supplied and positive; none has a default.");
        }
    }
}

/// <summary>The closed lifecycle vocabulary, DES-0054 §6 <c>staged | committed | quarantined | reclaimed</c>.</summary>
public enum ContentLifecycleState
{
    /// <summary>Uploaded and verified, not yet referenced (ck-16).</summary>
    Staged,
    /// <summary>At least one committed reference reaches the item (ck-17).</summary>
    Committed,
    /// <summary>Reserved for a future malware-scan slice; R1 never enters it (ck-14, §10 ruling 6).</summary>
    Quarantined,
    /// <summary>Data key destroyed and ciphertext deleted; a reclamation entry remains (ck-18).</summary>
    Reclaimed,
}

/// <summary>The closed refusal vocabulary, DES-0054 ck-9.</summary>
public static class ContentRefusals
{
    /// <summary>The item exceeds the effective limit, the lower of the store's and the field's.</summary>
    public const string TooLarge = "content.too-large";
    /// <summary>A record would exceed its logical-byte quota.</summary>
    public const string RecordQuota = "content.record-quota";
    /// <summary>A tenant would exceed its logical-byte quota.</summary>
    public const string TenantQuota = "content.tenant-quota";
    /// <summary>The detected type is outside the store allowlist or the field's narrower list.</summary>
    public const string MediaTypeNotAllowed = "content.media-type-not-allowed";
    /// <summary>The declared type disagrees with detection, or a declared text type is on bytes that fail the text check.</summary>
    public const string MediaTypeMismatch = "content.media-type-mismatch";
    /// <summary>The client's wire-only SHA-256 disagrees with the stream (eng-2).</summary>
    public const string DigestMismatch = "content.digest-mismatch";
    /// <summary>The staged item expired, or no staged item of this tenant has that id.</summary>
    public const string StagingExpired = "content.staging-expired";
    /// <summary>At install: a field's accepted type is outside the store allowlist (§7).</summary>
    public const string FieldMediaTypeNotAllowed = "content.field-media-type-not-allowed";
}

/// <summary>
/// A content-store refusal in the DES-0014 C3 shape: <see cref="Code"/>, <see cref="Exception.Message"/>, the
/// <see cref="Definition"/> that set the limit, and the optional machine-readable <see cref="Detail"/>. A size or quota
/// refusal carries <c>limit</c> and <c>observed</c> there (DES-0054 ck-9; DES-0014 C3).
/// </summary>
public sealed class ContentRefusedException : InvalidOperationException
{
    /// <summary>Creates a refusal with its code, the declaration that set the limit, and optional detail values.</summary>
    public ContentRefusedException(string code, string message, string definition, IReadOnlyDictionary<string, long>? detail = null)
        : base(message)
    {
        Code = code;
        Definition = definition;
        Detail = detail ?? ReadOnlyDictionary<string, long>.Empty;
    }

    /// <summary>The stable ck-9 refusal code.</summary>
    public string Code { get; }

    /// <summary>The declaration that set the limit: <see cref="InMemoryContentStore.StoreDeclaration"/> or the field's declaration.</summary>
    public string Definition { get; }

    /// <summary>C3's optional detail; <c>limit</c> and <c>observed</c> on every size and quota refusal.</summary>
    public IReadOnlyDictionary<string, long> Detail { get; }
}

/// <summary>
/// A read that finds nothing readable. The same exception, with the same message, answers a missing id, an
/// unauthorized owner and another tenant's id, so existence never leaks (DES-0054 §6, eng-6, eng-11).
/// </summary>
public sealed class ContentNotFoundException : Exception
{
    /// <summary>Creates the single not-found outcome.</summary>
    public ContentNotFoundException() : base("No readable content exists for that reference.") { }
}

/// <summary>A content id: <c>hmac-sha256:&lt;64 lower-case hex&gt;</c> under the tenant key (DES-0054 ck-2, board P1).</summary>
public readonly record struct ContentId
{
    /// <summary>The algorithm tag (ck-2; §10 ruling 3 as superseded by board P1).</summary>
    public const string Algorithm = "hmac-sha256:";

    /// <summary>Validates the algorithm-tagged, lower-case hexadecimal form.</summary>
    public ContentId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!value.StartsWith(Algorithm, StringComparison.Ordinal) || value.Length != Algorithm.Length + 64 ||
            !value[Algorithm.Length..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new ArgumentException("A content id is hmac-sha256: followed by 64 lower-case hex digits.", nameof(value));
        }

        Value = value;
    }

    /// <summary>The identity; also the ETag and the export id. Never an authorization (ck-2).</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>The referencing owner: a record field element, or an issued document (DES-0054 ck-3).</summary>
/// <param name="Record">The record (or issued document) the record quota is counted against (ck-8).</param>
/// <param name="Element">The field element or document slot holding the reference.</param>
public sealed record ContentOwner(string Record, string Element);

/// <summary>An immutable consumer-held reference (DES-0054 ck-3). The display name lives here, never on the item.</summary>
/// <param name="ContentId">The item identity within the tenant.</param>
/// <param name="Length">The plaintext byte length.</param>
/// <param name="MediaType">The detected media type.</param>
/// <param name="DisplayName">The display file name.</param>
/// <param name="Owner">The referencing owner.</param>
public sealed record ContentReference(ContentId ContentId, long Length, string MediaType, string DisplayName, ContentOwner Owner);

/// <summary>A field's narrowing of the store: it may lower the limit and narrow the allowlist, never widen (ck-5, ck-7).</summary>
/// <param name="Declaration">The field declaration, named by a refusal it causes.</param>
/// <param name="LimitBytes">The field's size limit, if any; the effective limit is the lower of this and the store's.</param>
/// <param name="AcceptedMediaTypes">The field's accepted types, if any; a subset of the store allowlist.</param>
public sealed record FieldNarrowing(string Declaration, long? LimitBytes, IReadOnlySet<string>? AcceptedMediaTypes);

/// <summary>Whether a hold binds the owner whose reference is being removed (DES-0054 ck-10, eng-9).</summary>
public enum HoldStatus
{
    /// <summary>No hold binds the owner.</summary>
    None,
    /// <summary>A hold binds the owner; the reference and bytes stay.</summary>
    Held,
    /// <summary>The hold authority could not be read; fail closed and keep the bytes (eng-9, board D7; DES-0046 ck-6).</summary>
    Unreadable,
}

/// <summary>The audit fact of a reclamation: no bytes and no display name (DES-0054 ck-18).</summary>
/// <param name="ContentId">The reclaimed id.</param>
/// <param name="TenantId">The tenant.</param>
/// <param name="Cause">Why it was reclaimed.</param>
/// <param name="Run">The disposal or sweep run that reclaimed it.</param>
public sealed record ReclamationEntry(ContentId ContentId, TenantId TenantId, string Cause, string Run);

/// <summary>A served item and its headers (DES-0054 eng-7, eng-8).</summary>
/// <param name="Bytes">The selected bytes: the whole item, one range, or none on 416.</param>
/// <param name="StatusCode">200, 206 or 416 (RFC 9110 §14, §15.3.7, §15.5.17).</param>
/// <param name="ContentType">The detected type, never another.</param>
/// <param name="ContentDisposition"><c>attachment</c> with the reference's file name (RFC 6266).</param>
/// <param name="ETag">The strong ETag: the quoted content id (RFC 9110 §8.8.3).</param>
/// <param name="ContentRange">The <c>Content-Range</c> value on 206 and 416; null on 200.</param>
public sealed record ContentDelivery(byte[] Bytes, int StatusCode, string ContentType, string ContentDisposition, string ETag, string? ContentRange)
{
    /// <summary>Always <c>nosniff</c> (eng-7).</summary>
    public static string XContentTypeOptions => "nosniff";
}

/// <summary>One byte range, inclusive, as RFC 9110 §14.1.2 writes it. R1 serves a single range (eng-8, §10 ruling 5).</summary>
/// <param name="Start">The first byte offset.</param>
/// <param name="End">The last byte offset; clamped to the item's last byte.</param>
public readonly record struct ContentByteRange(long Start, long End);

/// <summary>One exported item: its content id, bytes and a per-export keyed digest, never a plain SHA-256 (DES-0054 cc-7).</summary>
/// <param name="ContentId">The item's tenant-keyed id.</param>
/// <param name="Bytes">The plaintext bytes.</param>
/// <param name="Digest">HMAC-SHA-256 of the bytes under the per-export key.</param>
public sealed record ContentExportItem(ContentId ContentId, byte[] Bytes, byte[] Digest);

/// <summary>An export package. The per-export key is not in it; the exporting contract keeps that (cc-7, Q20).</summary>
/// <param name="Items">The exported items.</param>
public sealed record ContentExport(IReadOnlyList<ContentExportItem> Items);

/// <summary>The export package and the fresh per-export key that verifies it.</summary>
/// <param name="Package">The package.</param>
/// <param name="FixityKey">The per-export key, held apart from the package.</param>
public sealed record ContentExportResult(ContentExport Package, byte[] FixityKey);

/// <summary>The staging sweep's registration with the execution runtime (DES-0054 §5, eng-9; ADR 0099 decision 2).</summary>
public static class ContentStoreRunRegistration
{
    /// <summary>The run kind of the store's one run of its own, the staging sweep.</summary>
    public static RunKind StagingSweepKind { get; } = new("content-staging-sweep");

    /// <summary>The trigger the platform schedules the sweep on: staged items past the staging expiry (ck-13).</summary>
    public const string StagingSweepTrigger = "content-staging-expiry";

    /// <summary>Registers the sweep kind once, owned by the content store.</summary>
    public static RunKindRegistration Register(RunKindRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.Register(StagingSweepKind, InMemoryContentStore.StoreDeclaration);
    }
}

/// <summary>
/// The content store over an in-memory backing: every put stages its own bytes; commit deduplicates inside the
/// tenant and counts logical-byte quotas in the same critical section; reads require the upstream gate's verdict on
/// the referencing owner; erasure destroys the item's data key (DES-0054 §3).
/// </summary>
public sealed class InMemoryContentStore
{
    /// <summary>The name a refusal gives when the store's own declaration set the limit (C3 <c>definition</c>).</summary>
    public const string StoreDeclaration = "content-store";

    // The R1 allowlist, exactly: DES-0054 §10 ruling 4 (owner, 2026-09-30); ck-5.
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        "application/pdf", "image/png", "image/jpeg", "image/webp", "text/plain", "text/csv",
    };

    // WHATWG MIME Sniffing §5 resource header: at most 1,445 bytes (DES-0054 ck-6, eng-3; Q21).
    private const int ResourceHeaderBytes = 1445;

    // AES-256-GCM, 12-byte nonce, 16-byte tag, as Tink's AES-GCM-HKDF streaming AEAD uses them (DES-0054 eng-10,
    // §12 Tink Streaming AEAD). The segment nonce is Tink's: 7-byte random prefix || 4-byte big-endian segment
    // number || 1-byte last-segment flag, so reordering, dropping or truncating a segment fails authentication.
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int NoncePrefixBytes = 7;

    // ponytail: one lock over all state; per-tenant locks if contention shows. It makes commit's quota check and
    // insert atomic, which eng-5 requires.
    private readonly Lock _gate = new();
    private readonly ContentStoreOptions _options;
    private readonly ITenantContentKeyProvider _keys;
    private readonly TimeProvider _clock;
    private readonly Dictionary<(TenantId Tenant, ContentId Id), Item> _items = [];
    private readonly Dictionary<Guid, Item> _staged = [];
    private readonly List<ReclamationEntry> _reclamations = [];

    /// <summary>Creates a store; refuses unless every unstated measurement is supplied. The clock is the host's server clock (kernel-core-ck-9).</summary>
    public InMemoryContentStore(ContentStoreOptions options, ITenantContentKeyProvider keys, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();
        _options = options;
        _keys = keys;
        _clock = clock;
    }

    /// <summary>
    /// Streams one upload into a staged item and returns its stage id, or refuses at a named stage. The size limit is
    /// enforced on the stream (eng-1); the type is detected from the bytes (eng-3); a declared plain SHA-256, when sent,
    /// must match and is then discarded (eng-2). The put never looks at what the tenant already holds (eng-4).
    /// </summary>
    public Guid Put(TenantId tenant, Stream stream, string? declaredMediaType, string? declaredSha256Hex, FieldNarrowing? field = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var (limit, limitDeclaration) = field?.LimitBytes is { } fieldLimit && fieldLimit < _options.ItemLimitBytes
            ? (fieldLimit, field.Declaration)
            : (_options.ItemLimitBytes, StoreDeclaration);
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(field), "A field size limit must be positive.");

        var tenantKey = RequireKey(tenant);
        var dataKey = RandomNumberGenerator.GetBytes(KeyBytes); // a fresh data key per item (ck-11, eng-10, §10 ruling 8)
        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixBytes);
        try
        {
            using var id = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, tenantKey);
            using var wire = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var header = new List<byte>(ResourceHeaderBytes);
            var segments = new List<Segment>();
            long length = 0;
            byte[]? pending = null;
            using (var aead = new AesGcm(dataKey, TagBytes))
            {
                while (true)
                {
                    var chunk = ReadSegment(stream);
                    length += chunk.Length;
                    if (length > limit)
                    {
                        // Refuse at the first segment past the limit; nothing is staged and the local segments are dropped.
                        throw new ContentRefusedException(ContentRefusals.TooLarge, "The item exceeds its effective size limit.", limitDeclaration, Detail(limit, length));
                    }

                    id.AppendData(chunk);
                    wire.AppendData(chunk);
                    header.AddRange(chunk.AsSpan(0, Math.Min(chunk.Length, ResourceHeaderBytes - header.Count)));
                    if (pending is not null && chunk.Length == 0) break;
                    if (pending is not null) segments.Add(Seal(aead, noncePrefix, segments.Count, pending, last: false));
                    pending = chunk;
                    if (chunk.Length == 0) break;
                }

                segments.Add(Seal(aead, noncePrefix, segments.Count, pending!, last: true));
            }

            var mediaType = Admit([.. header], declaredMediaType, field);
            if (declaredSha256Hex is not null && !DigestMatches(declaredSha256Hex, wire.GetHashAndReset()))
            {
                throw new ContentRefusedException(ContentRefusals.DigestMismatch, "The declared SHA-256 does not match the uploaded bytes.", StoreDeclaration);
            }

            var contentId = new ContentId(ContentId.Algorithm + Convert.ToHexStringLower(id.GetHashAndReset()));
            var (wrapped, wrapNonce, wrapTag) = Wrap(tenantKey, dataKey, contentId);
            var item = new Item(Guid.NewGuid(), tenant, contentId, length, mediaType, _clock.GetUtcNow(), wrapped, wrapNonce, wrapTag, noncePrefix, _options.SegmentSizeBytes, segments);
            lock (_gate) _staged.Add(item.StageId, item);
            return item.StageId;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>
    /// Turns a staged item into a committed reference inside one critical section: record and tenant quotas are
    /// counted in logical bytes (ck-8, §10 ruling 7) and checked with the insert (eng-5); a staged copy whose id the
    /// tenant already holds becomes a reference to the existing item and its copy is destroyed (eng-4, board P2).
    /// The answer is the same shape either way.
    /// </summary>
    public ContentReference Commit(TenantId tenant, Guid stageId, ContentOwner owner, string displayName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        lock (_gate)
        {
            if (!_staged.TryGetValue(stageId, out var staged) || staged.Tenant != tenant)
            {
                throw new ContentRefusedException(ContentRefusals.StagingExpired, "The staged item is no longer available.", StoreDeclaration);
            }

            if (Expired(staged))
            {
                ReclaimStaged(staged, "staging-expired", "commit");
                throw new ContentRefusedException(ContentRefusals.StagingExpired, "The staged item is no longer available.", StoreDeclaration);
            }

            var recordObserved = LogicalUsage(tenant, owner.Record) + staged.Length;
            if (recordObserved > _options.RecordQuotaBytes)
            {
                throw new ContentRefusedException(ContentRefusals.RecordQuota, "The record's quota would be exceeded.", StoreDeclaration, Detail(_options.RecordQuotaBytes, recordObserved));
            }

            var tenantObserved = LogicalUsage(tenant, null) + staged.Length;
            if (tenantObserved > _options.TenantQuotaBytes)
            {
                throw new ContentRefusedException(ContentRefusals.TenantQuota, "The tenant's quota would be exceeded.", StoreDeclaration, Detail(_options.TenantQuotaBytes, tenantObserved));
            }

            _staged.Remove(stageId);
            if (_items.TryGetValue((tenant, staged.ContentId), out var existing))
            {
                staged.Destroy();
                existing.Owners.Add(owner);
                return new(existing.ContentId, existing.Length, existing.MediaType, displayName, owner);
            }

            staged.State = ContentLifecycleState.Committed;
            staged.Owners.Add(owner);
            _items.Add((tenant, staged.ContentId), staged);
            return new(staged.ContentId, staged.Length, staged.MediaType, displayName, owner);
        }
    }

    /// <summary>
    /// Serves bytes through a reference the upstream gate has authorized (<paramref name="authorized"/>, eng-6). A
    /// missing id, an unauthorized owner and another tenant's id all throw the same <see cref="ContentNotFoundException"/>.
    /// A single range is served as 206, or 416 when unsatisfiable; an <paramref name="ifRange"/> that is not the current
    /// ETag, or an invalid range, is answered with the whole item (RFC 9110 §13.1.5, §14.2). The caller answers a
    /// multi-range request with the whole item by passing no range (§10 ruling 5).
    /// </summary>
    public ContentDelivery Read(TenantId tenant, ContentReference reference, bool authorized, ContentByteRange? range = null, string? ifRange = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var item = Readable(tenant, reference, authorized);
        var etag = "\"" + item.ContentId.Value + "\"";
        var disposition = Attachment(reference.DisplayName);
        if (range is not { } requested || (ifRange is not null && ifRange != etag) || requested.Start < 0 || requested.End < requested.Start)
        {
            return new(Decrypt(item, RequireKey(tenant), 0, item.LastSegment + 1), 200, item.MediaType, disposition, etag, null);
        }

        if (requested.Start >= item.Length)
        {
            return new([], 416, item.MediaType, disposition, etag, $"bytes */{item.Length}");
        }

        var end = Math.Min(requested.End, item.Length - 1);
        var first = (int)(requested.Start / item.SegmentSize);
        var last = (int)(end / item.SegmentSize);
        var covered = Decrypt(item, RequireKey(tenant), first, last + 1); // only the covering segments (eng-10)
        var offset = requested.Start - ((long)first * item.SegmentSize);
        return new(covered.AsSpan((int)offset, (int)(end - requested.Start + 1)).ToArray(), 206, item.MediaType, disposition, etag, $"bytes {requested.Start}-{end}/{item.Length}");
    }

    /// <summary>
    /// Removes one reference in the records-path disposal run (eng-9). A held owner, or an unreadable hold authority,
    /// changes nothing and returns false (ck-10; board D7). When the last reference goes, the data key is destroyed,
    /// the ciphertext deleted and a reclamation entry kept (ck-11, ck-18). Returns true when the item was reclaimed.
    /// </summary>
    public bool RemoveReference(TenantId tenant, ContentReference reference, HoldStatus hold, string run)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        if (hold != HoldStatus.None) return false;
        lock (_gate)
        {
            if (!_items.TryGetValue((tenant, reference.ContentId), out var item) || !item.Owners.Remove(reference.Owner)) return false;
            if (item.Owners.Count != 0) return false;
            _items.Remove((tenant, item.ContentId));
            item.Destroy();
            item.State = ContentLifecycleState.Reclaimed;
            _reclamations.Add(new(item.ContentId, tenant, "last-reference-removed", run));
            return true;
        }
    }

    /// <summary>The staging sweep (§5): reclaims every staged item past the staging expiry, with no request in flight.</summary>
    public int SweepExpired(string run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        lock (_gate)
        {
            var expired = _staged.Values.Where(Expired).ToArray();
            foreach (var item in expired) ReclaimStaged(item, "staging-expired", run);
            return expired.Length;
        }
    }

    /// <summary>
    /// Exports the items behind authorized references with a fresh per-export key (DES-0054 cc-7, Q20). The package
    /// carries each item's content id, bytes and keyed digest, and no plain SHA-256.
    /// </summary>
    public ContentExportResult Export(TenantId tenant, IEnumerable<ContentReference> references, bool authorized)
    {
        ArgumentNullException.ThrowIfNull(references);
        var exportKey = RandomNumberGenerator.GetBytes(KeyBytes);
        var items = references.Select(reference =>
        {
            var item = Readable(tenant, reference, authorized);
            var bytes = Decrypt(item, RequireKey(tenant), 0, item.LastSegment + 1);
            return new ContentExportItem(item.ContentId, bytes, HMACSHA256.HashData(exportKey, bytes));
        }).ToArray();
        return new(new ContentExport(items), exportKey);
    }

    /// <summary>Re-reads a package: every item's keyed digest must verify, or it throws <see cref="InvalidDataException"/> (cc-7).</summary>
    public static void VerifyExport(ContentExport package, byte[] fixityKey)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(fixityKey);
        foreach (var item in package.Items)
        {
            if (!CryptographicOperations.FixedTimeEquals(item.Digest, HMACSHA256.HashData(fixityKey, item.Bytes)))
            {
                throw new InvalidDataException("An exported item failed its per-export fixity check.");
            }
        }
    }

    /// <summary>The reclamation entries, append-only (ck-18).</summary>
    public IReadOnlyList<ReclamationEntry> Reclamations
    {
        get { lock (_gate) return [.. _reclamations]; }
    }

    /// <summary>How many staged items exist, for the sweep's operators.</summary>
    public int StagedCount
    {
        get { lock (_gate) return _staged.Count; }
    }

    /// <summary>How many committed items exist, across tenants.</summary>
    public int CommittedCount
    {
        get { lock (_gate) return _items.Count; }
    }

    internal void CorruptCiphertext(TenantId tenant, ContentId contentId, int segment)
    {
        lock (_gate) _items[(tenant, contentId)].Segments[segment].Ciphertext[0] ^= 1;
    }

    internal void DropLastSegmentKeepingLength(TenantId tenant, ContentId contentId)
    {
        lock (_gate)
        {
            var segments = _items[(tenant, contentId)].Segments;
            segments.RemoveAt(segments.Count - 1);
        }
    }

    // Live references to an item's key material, captured before an erase so a test can see it destroyed.
    internal (byte[] WrappedDataKey, ICollection Segments) KeyMaterial(TenantId tenant, ContentId contentId)
    {
        lock (_gate) return (_items[(tenant, contentId)].WrappedDataKey, _items[(tenant, contentId)].Segments);
    }

    internal (byte[] WrappedDataKey, ICollection Segments) KeyMaterial(Guid stageId)
    {
        lock (_gate) return (_staged[stageId].WrappedDataKey, _staged[stageId].Segments);
    }

    // Simulates an attacker who drops the tail segment and shortens the length to match, so only the last-segment
    // flag in the nonce can detect it.
    internal void TruncateLastSegment(TenantId tenant, ContentId contentId)
    {
        lock (_gate)
        {
            var item = _items[(tenant, contentId)];
            item.Segments.RemoveAt(item.Segments.Count - 1);
            item.Length = (long)item.Segments.Count * item.SegmentSize;
        }
    }

    private static ReadOnlyDictionary<string, long> Detail(long limit, long observed) =>
        new ReadOnlyDictionary<string, long>(new Dictionary<string, long> { ["limit"] = limit, ["observed"] = observed });

    private bool Expired(Item item) => _clock.GetUtcNow() - item.CreatedUtc >= _options.StagingExpiry;

    private void ReclaimStaged(Item item, string cause, string run)
    {
        _staged.Remove(item.StageId);
        item.Destroy();
        item.State = ContentLifecycleState.Reclaimed;
        _reclamations.Add(new(item.ContentId, item.Tenant, cause, run));
    }

    private Item Readable(TenantId tenant, ContentReference reference, bool authorized)
    {
        lock (_gate)
        {
            if (!authorized || !_items.TryGetValue((tenant, reference.ContentId), out var item) || !item.Owners.Contains(reference.Owner))
            {
                throw new ContentNotFoundException();
            }

            return item;
        }
    }

    private byte[] ReadSegment(Stream stream)
    {
        var buffer = new byte[_options.SegmentSizeBytes];
        var filled = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return filled == buffer.Length ? buffer : buffer[..filled];
    }

    private static Segment Seal(AesGcm aead, byte[] noncePrefix, int index, byte[] plaintext, bool last)
    {
        var nonce = SegmentNonce(noncePrefix, index, last);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagBytes];
        aead.Encrypt(nonce, plaintext, cipher, tag);
        return new(cipher, tag);
    }

    private static byte[] SegmentNonce(byte[] prefix, int index, bool last)
    {
        var nonce = new byte[NonceBytes];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixBytes), checked((uint)index));
        nonce[NonceBytes - 1] = last ? (byte)1 : (byte)0;
        return nonce;
    }

    private static (byte[] Wrapped, byte[] Nonce, byte[] Tag) Wrap(byte[] tenantKey, byte[] dataKey, ContentId contentId)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var wrapped = new byte[dataKey.Length];
        var tag = new byte[TagBytes];
        using var wrapper = new AesGcm(tenantKey, TagBytes);
        wrapper.Encrypt(nonce, dataKey, wrapped, tag, Encoding.ASCII.GetBytes(contentId.Value)); // bound to its item
        return (wrapped, nonce, tag);
    }

    // Throws CryptographicException (AuthenticationTagMismatchException) on any tampered, reordered or truncated segment.
    private static byte[] Decrypt(Item item, byte[] tenantKey, int first, int end)
    {
        var dataKey = new byte[KeyBytes];
        using (var wrapper = new AesGcm(tenantKey, TagBytes))
        {
            wrapper.Decrypt(item.WrapNonce, item.WrappedDataKey, item.WrapTag, dataKey, Encoding.ASCII.GetBytes(item.ContentId.Value));
        }

        try
        {
            using var aead = new AesGcm(dataKey, TagBytes);
            using var plaintext = new MemoryStream();
            for (var index = first; index < end; index++)
            {
                if (index >= item.Segments.Count) throw new AuthenticationTagMismatchException("A ciphertext segment is missing.");
                var segment = item.Segments[index];
                var clear = new byte[segment.Ciphertext.Length];
                aead.Decrypt(SegmentNonce(item.NoncePrefix, index, index == item.LastSegment), segment.Ciphertext, segment.Tag, clear);
                plaintext.Write(clear);
            }

            return plaintext.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private static string Admit(byte[] header, string? declared, FieldNarrowing? field)
    {
        var declaredType = declared is null ? null : declared.Split(';', 2)[0].Trim().ToLowerInvariant();
        var detected = Detect(header, declaredType);
        if (!Allowlist.Contains(detected) || (field?.AcceptedMediaTypes is { } accepted && !accepted.Contains(detected)))
        {
            throw new ContentRefusedException(ContentRefusals.MediaTypeNotAllowed, "The detected media type is not allowed.",
                field?.AcceptedMediaTypes is not null && Allowlist.Contains(detected) ? field.Declaration : StoreDeclaration);
        }

        if (declaredType is not null && declaredType != detected)
        {
            throw new ContentRefusedException(ContentRefusals.MediaTypeMismatch, "The declared media type disagrees with the bytes.", StoreDeclaration);
        }

        return detected;
    }

    // Signatures from WHATWG MIME Sniffing §6.1 (images) and §7.1's PDF row; the text check is exactly §7.1's
    // "binary data byte" rule (DES-0054 ck-6, eng-3; Q21). A text resource takes its specific text type from the
    // declaration (board D1 as amended by Q21); a declared text type on non-text bytes is a mismatch.
    private static string Detect(ReadOnlySpan<byte> header, string? declaredType)
    {
        if (header.StartsWith("%PDF-"u8)) return "application/pdf";
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return "image/jpeg";
        if (header.Length >= 14 && header.StartsWith("RIFF"u8) && header[8..].StartsWith("WEBPVP"u8)) return "image/webp";
        if (IsText(header)) return declaredType is "text/plain" or "text/csv" ? declaredType : "text/plain";
        if (declaredType is "text/plain" or "text/csv")
        {
            throw new ContentRefusedException(ContentRefusals.MediaTypeMismatch, "The declared text type is on bytes that are not text.", StoreDeclaration);
        }

        return "application/octet-stream"; // no admitted signature; refused as not allowed by the caller
    }

    private static bool IsText(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || header.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return true;
        foreach (var value in header)
        {
            // WHATWG MIME Sniffing §7.1 binary data byte: 0x00-0x08, 0x0B, 0x0E-0x1A, 0x1C-0x1F.
            if (value is <= 0x08 or 0x0B or (>= 0x0E and <= 0x1A) or (>= 0x1C and <= 0x1F)) return false;
        }

        return true;
    }

    private static bool DigestMatches(string declaredHex, byte[] actual)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(declaredHex), actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // RFC 6266 §4.3: quotes, backslashes and control characters cannot enter the header (no header injection).
    private static string Attachment(string name) =>
        "attachment; filename=\"" + new string([.. name.Where(character => character is not ('"' or '\\') && !char.IsControl(character))]) + "\"";

    private byte[] RequireKey(TenantId tenant)
    {
        var key = _keys.GetKey(tenant);
        if (key is null || key.Length != KeyBytes) throw new InvalidOperationException("A tenant content key must be 32 bytes.");
        return key;
    }

    private long LogicalUsage(TenantId tenant, string? record) =>
        _items.Values.Where(item => item.Tenant == tenant)
            .Sum(item => item.Owners.Count(owner => record is null || owner.Record == record) * item.Length);

    private sealed class Item(Guid stageId, TenantId tenant, ContentId contentId, long length, string mediaType, DateTimeOffset createdUtc,
        byte[] wrappedDataKey, byte[] wrapNonce, byte[] wrapTag, byte[] noncePrefix, int segmentSize, List<Segment> segments)
    {
        public Guid StageId { get; } = stageId;
        public TenantId Tenant { get; } = tenant;
        public ContentId ContentId { get; } = contentId;
        public long Length { get; set; } = length;
        public string MediaType { get; } = mediaType;
        public DateTimeOffset CreatedUtc { get; } = createdUtc;
        public byte[] WrappedDataKey { get; } = wrappedDataKey;
        public byte[] WrapNonce { get; } = wrapNonce;
        public byte[] WrapTag { get; } = wrapTag;
        public byte[] NoncePrefix { get; } = noncePrefix;
        public int SegmentSize { get; } = segmentSize;
        public List<Segment> Segments { get; } = segments;
        public List<ContentOwner> Owners { get; } = [];
        public ContentLifecycleState State { get; set; } = ContentLifecycleState.Staged;

        // The last segment index comes from the length, never from the stored list, so a dropped tail is detected.
        public int LastSegment => (int)Math.Max(0, (Length - 1) / SegmentSize);

        // Key destruction is the erase (ck-11): the wrapped key and the ciphertext are zeroed and dropped.
        public void Destroy()
        {
            CryptographicOperations.ZeroMemory(WrappedDataKey);
            foreach (var segment in Segments) CryptographicOperations.ZeroMemory(segment.Ciphertext);
            Segments.Clear();
        }
    }

    private sealed record Segment(byte[] Ciphertext, byte[] Tag);
}
