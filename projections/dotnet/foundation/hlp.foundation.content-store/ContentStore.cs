using System.Buffers.Binary;
using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    /// <summary>Returns the 32-byte tenant root key from which purpose-bound content-store subkeys are derived.</summary>
    byte[] GetKey(TenantId tenantId);
}

/// <summary>Reports the runtime quota base that only the host and its physical backing can measure (DES-0054 Q22, Q32).</summary>
public interface IContentStoreQuotaBase
{
    /// <summary>Returns free space available to the store's caller plus this store's physical bytes on that same volume.</summary>
    long GetQuotaBaseBytes();
}

/// <summary>
/// The installation values DES-0054 does not state. Each is required and positive; none has a default, because
/// DES-0054 ck-7, ck-8 and ck-13 assign them to the first DES-0037 measurement (§10 ruling 1, board P3, Q19).
/// </summary>
/// <param name="ItemLimitBytes">The store's per-item plaintext limit (ck-7), set from the streamed-upload evidence.</param>
/// <param name="RecordQuotaBytes">The logical-byte quota per record (ck-8): the stated share of the tenant quota.</param>
/// <param name="TenantQuotaBytes">The logical-byte quota per tenant (ck-8): the stated fraction of the runtime base (Q22).</param>
/// <param name="StagingExpiry">How long an uncommitted staged item lives (ck-13): the stated multiple of the measured put time.</param>
public sealed record ContentStoreOptions(
    long ItemLimitBytes,
    long RecordQuotaBytes,
    long TenantQuotaBytes,
    TimeSpan StagingExpiry)
{
    /// <summary>Refuses a missing or non-positive value; the store never substitutes one.</summary>
    public void Validate()
    {
        if (ItemLimitBytes <= 0 || RecordQuotaBytes <= 0 || TenantQuotaBytes <= 0 || StagingExpiry <= TimeSpan.Zero)
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
    // DES-0054 design.md:44 (ck-9) fixes this closed refusal vocabulary.
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
    /// <summary>The algorithm tag (DES-0054 design.md:37, ck-2; §10 ruling 3 as superseded by board P1).</summary>
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

/// <summary>Identifies one content item in a backing without exposing its tenant-keyed content id to telemetry.</summary>
/// <param name="TenantId">The ambient tenant that owns the item.</param>
/// <param name="ContentId">The item identity within that tenant.</param>
public readonly record struct ContentStorageItemKey(TenantId TenantId, ContentId ContentId);

/// <summary>One encrypted segment held by a content-store backing (DES-0054 eng-10).</summary>
/// <param name="Ciphertext">The ciphertext for this segment.</param>
/// <param name="Tag">The AES-GCM authentication tag for this segment.</param>
public sealed record ContentCiphertextSegment(byte[] Ciphertext, byte[] Tag);

/// <summary>All durable item metadata, wrapped-key material and ciphertext that a storage backing must preserve (DES-0054 Q31).</summary>
public sealed class ContentStoreItem
{
    /// <summary>Creates an item that is initially staged and whose fixed segment size is stored with the item.</summary>
    public ContentStoreItem(Guid stageId, TenantId tenant, ContentId contentId, long length, string mediaType, DateTimeOffset createdUtc,
        byte[] wrappedDataKey, byte[] wrapNonce, byte[] wrapTag, byte[] noncePrefix, int segmentSize, List<ContentCiphertextSegment> segments)
    {
        StageId = stageId;
        Tenant = tenant;
        ContentId = contentId;
        Length = length;
        MediaType = mediaType;
        CreatedUtc = createdUtc;
        WrappedDataKey = wrappedDataKey;
        WrapNonce = wrapNonce;
        WrapTag = wrapTag;
        NoncePrefix = noncePrefix;
        SegmentSize = segmentSize;
        Segments = segments;
    }

    /// <summary>The upload-stage key until commit.</summary>
    public Guid StageId { get; }
    /// <summary>The tenant that owns this item.</summary>
    public TenantId Tenant { get; }
    /// <summary>The tenant-keyed identity.</summary>
    public ContentId ContentId { get; }
    /// <summary>The plaintext length used for delivery and range validation.</summary>
    public long Length { get; set; }
    /// <summary>The detected, stored media type.</summary>
    public string MediaType { get; }
    /// <summary>The server time at which the upload was staged.</summary>
    public DateTimeOffset CreatedUtc { get; }
    /// <summary>The data key wrapped under the tenant's data-key-wrap subkey.</summary>
    public byte[] WrappedDataKey { get; }
    /// <summary>The nonce used to wrap the per-item data key.</summary>
    public byte[] WrapNonce { get; }
    /// <summary>The authentication tag for the wrapped data key.</summary>
    public byte[] WrapTag { get; }
    /// <summary>The random prefix from which segment nonces are built.</summary>
    public byte[] NoncePrefix { get; }
    /// <summary>The immutable plaintext segment size selected when the item was written.</summary>
    public int SegmentSize { get; }
    /// <summary>The ordered ciphertext segments.</summary>
    public List<ContentCiphertextSegment> Segments { get; }
    /// <summary>The committed owners that currently reference the item.</summary>
    public List<ContentOwner> Owners { get; } = [];
    /// <summary>The item's lifecycle state.</summary>
    public ContentLifecycleState State { get; set; } = ContentLifecycleState.Staged;
    /// <summary>The index that a complete stored item must end at, calculated from durable metadata.</summary>
    public int LastSegment => (int)Math.Max(0, (Length - 1) / SegmentSize);

    /// <summary>Destroys wrapped-key and ciphertext material when a staged copy or last reference is reclaimed.</summary>
    public void Destroy()
    {
        CryptographicOperations.ZeroMemory(WrappedDataKey);
        foreach (var segment in Segments) CryptographicOperations.ZeroMemory(segment.Ciphertext);
        Segments.Clear();
    }
}

/// <summary>A durable integrity episode for one item; generic health reads only whether any episode remains active (DES-0054 Q34-Q36).</summary>
public sealed record ContentIntegrityEntry(Guid ItemKey, TenantId TenantId, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc,
    long Count, int SegmentIndex, bool Active, string TraceId, string OperationId, string PrincipalId, ContentOwner Owner);

/// <summary>Structured information emitted only for the first failure of an integrity episode (DES-0054 §10 Q34-Q36).</summary>
public sealed record ContentIntegrityLogEvent(string TraceId, string OperationId, string OpaqueTenantId, string PrincipalId,
    Guid ItemKey, ContentOwner Owner, int SegmentIndex);

/// <summary>Receives the one structured event emitted when an integrity episode begins, without imposing a logging package.</summary>
public interface IContentIntegrityLogger
{
    /// <summary>Records the first failure of an item integrity episode.</summary>
    void LogIntegrityFailure(ContentIntegrityLogEvent integrityEvent);
}

/// <summary>Optional read correlation supplied by the host after it has minted trace and operation identifiers.</summary>
public sealed record ContentReadContext(string TraceId, string OperationId, string OpaqueTenantId, string PrincipalId);

/// <summary>Typed encrypted-content failure that preserves host-only item, owner and segment context and never becomes a refusal code.</summary>
public sealed class ContentIntegrityException : CryptographicException
{
    /// <summary>Creates the typed failure from the failed backing operation.</summary>
    public ContentIntegrityException(Guid itemKey, ContentOwner owner, int segmentIndex, Exception innerException)
        : base("Content integrity verification failed.", innerException)
    {
        ItemKey = itemKey;
        Owner = owner;
        SegmentIndex = segmentIndex;
    }

    /// <summary>The internal item key, not the content id.</summary>
    public Guid ItemKey { get; }
    /// <summary>The authorized reference owner through which the item was read.</summary>
    public ContentOwner Owner { get; }
    /// <summary>The failed segment index; zero denotes data-key unwrapping before the first segment.</summary>
    public int SegmentIndex { get; }
}

/// <summary>Storage port implemented by the host's durable backing or the supplied in-memory backing (DES-0054 Q31).</summary>
public interface IContentStoreStorage
{
    /// <summary>Supplies one synchronization boundary for conditional duplicate collapse and quota accounting.</summary>
    object SyncRoot { get; }
    /// <summary>Stores committed item metadata, ciphertext segments and wrapped data keys by tenant and content id.</summary>
    IDictionary<ContentStorageItemKey, ContentStoreItem> Items { get; }
    /// <summary>Stores uploads that are verified but not committed.</summary>
    IDictionary<Guid, ContentStoreItem> StagedItems { get; }
    /// <summary>Stores the append-only reclamation audit log.</summary>
    IList<ReclamationEntry> Reclamations { get; }
    /// <summary>Stores durable integrity episode entries keyed by tenant and internal item key.</summary>
    IDictionary<(TenantId TenantId, Guid ItemKey), ContentIntegrityEntry> IntegrityEntries { get; }
}

/// <summary>One non-durable storage-port implementation for tests, prototypes and a host that explicitly chooses process-local state.</summary>
public sealed class InMemoryContentStoreStorage : IContentStoreStorage
{
    /// <inheritdoc />
    public object SyncRoot { get; } = new object();
    /// <inheritdoc />
    public IDictionary<ContentStorageItemKey, ContentStoreItem> Items { get; } = new Dictionary<ContentStorageItemKey, ContentStoreItem>();
    /// <inheritdoc />
    public IDictionary<Guid, ContentStoreItem> StagedItems { get; } = new Dictionary<Guid, ContentStoreItem>();
    /// <inheritdoc />
    public IList<ReclamationEntry> Reclamations { get; } = new List<ReclamationEntry>();
    /// <inheritdoc />
    public IDictionary<(TenantId TenantId, Guid ItemKey), ContentIntegrityEntry> IntegrityEntries { get; } = new Dictionary<(TenantId TenantId, Guid ItemKey), ContentIntegrityEntry>();
}

/// <summary>A served item and its headers (DES-0054 eng-7, eng-8).</summary>
/// <param name="Body">The selected bytes as a streaming body; it throws <see cref="ContentIntegrityException"/> rather than completing after tampering.</param>
/// <param name="StatusCode">200, 206 or 416 (RFC 9110 §14, §15.3.7, §15.5.17).</param>
/// <param name="ContentType">The detected type, never another.</param>
/// <param name="ContentDisposition"><c>attachment</c> with the reference's file name (RFC 6266).</param>
/// <param name="ETag">The strong ETag: the quoted content id (RFC 9110 §8.8.3).</param>
/// <param name="ContentRange">The <c>Content-Range</c> value on 206 and 416; null on 200.</param>
public sealed record ContentDelivery(Stream Body, int StatusCode, string ContentType, string ContentDisposition, string ETag, string? ContentRange)
{
    /// <summary>Always <c>nosniff</c> (DES-0054 design.md:74, eng-7).</summary>
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
    // DES-0054 design.md:114 and :207 (Q29) fix this lower-case kebab run kind.
    /// <summary>The run kind of the store's one run of its own, the staging sweep.</summary>
    public static RunKind StagingSweepKind { get; } = new("content-staging-sweep");

    /// <summary>The trigger kind registered for the platform schedule (DES-0054 design.md:114, Q29).</summary>
    public const string StagingSweepTriggerKind = "schedule";

    /// <summary>The platform-owned schedule that fires the staging sweep (DES-0054 design.md:114, Q29).</summary>
    public const string StagingSweepSchedule = "sys.sched.content-staging";

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
    /// <summary>The name a refusal gives when the store's own declaration set the limit (DES-0054 design.md:207, Q29).</summary>
    public const string StoreDeclaration = "content-store";

    // The R1 allowlist, exactly: DES-0054 §10 ruling 4 (owner, 2026-09-30); ck-5.
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        "application/pdf", "image/png", "image/jpeg", "image/webp", "text/plain", "text/csv",
    };

    // DES-0054 design.md:41 (ck-6, eng-3; Q21): WHATWG MIME Sniffing's resource header is at most 1,445 bytes.
    private const int ResourceHeaderBytes = 1445;

    // DES-0054 design.md:77 (eng-10): AES-256-GCM, 12-byte nonce and 16-byte tag, as Tink's streaming AEAD uses them.
    // R-0136 note.md:19: the segment nonce is Tink's 7-byte random prefix || 4-byte big-endian segment
    // number || 1-byte last-segment flag, so reordering, dropping or truncating a segment fails authentication.
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int NoncePrefixBytes = 7;

    // ponytail: one lock over all state; per-tenant locks if contention shows. It makes commit's quota check and
    // insert atomic, which eng-5 requires.
    private readonly ContentStoreOptions _options;
    private readonly ITenantContentKeyProvider _keys;
    private readonly TimeProvider _clock;
    private readonly IContentStoreStorage _storage;
    private readonly IContentIntegrityLogger? _integrityLogger;

    // DES-0054 design.md:205 (Q27): 65,536 plaintext bytes is a store constant recorded on every item, not an installation option.
    private const int SegmentSizeBytes = 65_536;
    // DES-0054 design.md:206 (Q28): these RFC 5869 HKDF info labels separate the HMAC and data-key-wrap purposes.
    private const string ContentIdKeyInfo = "harborline-content-store:content-id:v1";
    private const string DataKeyWrapKeyInfo = "harborline-content-store:data-key-wrap:v1";
    private static readonly ActivitySource IntegrityActivitySource = new("Harborline.Foundation.ContentStore");
    private static readonly Meter IntegrityMeter = new("Harborline.Foundation.ContentStore");
    private static readonly Counter<long> IntegrityFailures = IntegrityMeter.CreateCounter<long>("content.integrity.failures");

    /// <summary>Creates a store; refuses unless every unstated measurement is supplied. The clock is the host's server clock (kernel-core-ck-9).</summary>
    public InMemoryContentStore(ContentStoreOptions options, ITenantContentKeyProvider keys, TimeProvider clock)
        : this(options, keys, clock, new InMemoryContentStoreStorage())
    {
    }

    /// <summary>Creates a store over the supplied backing so a host can preserve committed, staged and audit state across store reconstruction.</summary>
    public InMemoryContentStore(ContentStoreOptions options, ITenantContentKeyProvider keys, TimeProvider clock, IContentStoreStorage storage,
        IContentIntegrityLogger? integrityLogger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(storage);
        options.Validate();
        _options = options;
        _keys = keys;
        _clock = clock;
        _storage = storage;
        _integrityLogger = integrityLogger;
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
        var contentIdKey = DeriveSubkey(tenantKey, tenant, ContentIdKeyInfo);
        var dataKeyWrapKey = DeriveSubkey(tenantKey, tenant, DataKeyWrapKeyInfo);
        var dataKey = RandomNumberGenerator.GetBytes(KeyBytes); // a fresh data key per item (ck-11, eng-10, §10 ruling 8)
        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixBytes);
        try
        {
            using var id = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, contentIdKey);
            using var wire = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var header = new List<byte>(ResourceHeaderBytes);
            var segments = new List<ContentCiphertextSegment>();
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
            var (wrapped, wrapNonce, wrapTag) = Wrap(dataKeyWrapKey, dataKey, contentId);
            var item = new ContentStoreItem(Guid.NewGuid(), tenant, contentId, length, mediaType, _clock.GetUtcNow(), wrapped, wrapNonce, wrapTag, noncePrefix, SegmentSizeBytes, segments);
            lock (_storage.SyncRoot) _storage.StagedItems.Add(item.StageId, item);
            return item.StageId;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(contentIdKey);
            CryptographicOperations.ZeroMemory(dataKeyWrapKey);
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
        lock (_storage.SyncRoot)
        {
            if (!_storage.StagedItems.TryGetValue(stageId, out var staged) || staged.Tenant != tenant)
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

            _storage.StagedItems.Remove(stageId);
            if (_storage.Items.TryGetValue(new(tenant, staged.ContentId), out var existing))
            {
                staged.Destroy();
                existing.Owners.Add(owner);
                return new(existing.ContentId, existing.Length, existing.MediaType, displayName, owner);
            }

            staged.State = ContentLifecycleState.Committed;
            staged.Owners.Add(owner);
            _storage.Items.Add(new(tenant, staged.ContentId), staged);
            return new(staged.ContentId, staged.Length, staged.MediaType, displayName, owner);
        }
    }

    /// <summary>
    /// Opens a streaming body through a reference the upstream gate has authorized (<paramref name="authorized"/>, eng-6). A
    /// missing id, an unauthorized owner and another tenant's id all throw the same <see cref="ContentNotFoundException"/>.
    /// A single range is served as 206, or 416 when unsatisfiable; an <paramref name="ifRange"/> that is not the current
    /// ETag, or an invalid range, is answered with the whole item (RFC 9110 §13.1.5, §14.2). The caller answers a
    /// multi-range request with the whole item by passing no range (§10 ruling 5).
    /// </summary>
    public ContentDelivery Read(TenantId tenant, ContentReference reference, bool authorized, ContentByteRange? range = null, string? ifRange = null,
        ContentReadContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var item = Readable(tenant, reference, authorized);
        var etag = "\"" + item.ContentId.Value + "\"";
        var disposition = Attachment(reference.DisplayName);
        if (range is not { } requested || (ifRange is not null && ifRange != etag) || requested.Start < 0 || requested.End < requested.Start)
        {
            return new(OpenRead(item, reference.Owner, RequireKey(tenant), 0, item.LastSegment, 0, item.Length, true, context), 200, item.MediaType, disposition, etag, null);
        }

        if (requested.Start >= item.Length)
        {
            return new(Stream.Null, 416, item.MediaType, disposition, etag, $"bytes */{item.Length}");
        }

        var end = Math.Min(requested.End, item.Length - 1);
        var first = (int)(requested.Start / item.SegmentSize);
        var last = (int)(end / item.SegmentSize);
        var offset = requested.Start - ((long)first * item.SegmentSize);
        return new(OpenRead(item, reference.Owner, RequireKey(tenant), first, last, offset, end - requested.Start + 1, false, context), 206,
            item.MediaType, disposition, etag, $"bytes {requested.Start}-{end}/{item.Length}");
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
        lock (_storage.SyncRoot)
        {
            if (!_storage.Items.TryGetValue(new(tenant, reference.ContentId), out var item) || !item.Owners.Remove(reference.Owner)) return false;
            if (item.Owners.Count != 0) return false;
            _storage.Items.Remove(new(tenant, item.ContentId));
            item.Destroy();
            item.State = ContentLifecycleState.Reclaimed;
            ResolveIntegrity(item); // completed erasure ends the episode (DES-0054 Q34, design.md:212)
            _storage.Reclamations.Add(new(item.ContentId, tenant, "last-reference-removed", run));
            return true;
        }
    }

    /// <summary>The staging sweep (§5): reclaims every staged item past the staging expiry, with no request in flight.</summary>
    public int SweepExpired(string run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        lock (_storage.SyncRoot)
        {
            var expired = _storage.StagedItems.Values.Where(Expired).ToArray();
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
            using var body = Read(tenant, reference, authorized).Body;
            using var plaintext = new MemoryStream();
            body.CopyTo(plaintext);
            var bytes = plaintext.ToArray();
            return new ContentExportItem(item.ContentId, bytes, FixityDigest(exportKey, item.ContentId, bytes));
        }).ToArray();
        return new(new ContentExport(items), exportKey);
    }

    // DES-0054 cc-7, Q20, Q25: a per-export keyed digest over the length-prefixed content id then the bytes, so swapping ids fails; never a plain SHA-256.
    private static byte[] FixityDigest(byte[] key, ContentId contentId, byte[] bytes)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        var id = Encoding.ASCII.GetBytes(contentId.Value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)id.Length);
        hmac.AppendData(length);
        hmac.AppendData(id);
        hmac.AppendData(bytes);
        return hmac.GetHashAndReset();
    }

    /// <summary>Re-reads a package: every item's keyed digest must verify, or it throws <see cref="InvalidDataException"/> (cc-7).</summary>
    public static void VerifyExport(ContentExport package, byte[] fixityKey)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(fixityKey);
        foreach (var item in package.Items)
        {
            if (!CryptographicOperations.FixedTimeEquals(item.Digest, FixityDigest(fixityKey, item.ContentId, item.Bytes)))
            {
                throw new InvalidDataException("An exported item failed its per-export fixity check.");
            }
        }
    }

    /// <summary>The reclamation entries, append-only (ck-18).</summary>
    public IReadOnlyList<ReclamationEntry> Reclamations
    {
        get { lock (_storage.SyncRoot) return [.. _storage.Reclamations]; }
    }

    /// <summary>How many staged items exist, for the sweep's operators.</summary>
    public int StagedCount
    {
        get { lock (_storage.SyncRoot) return _storage.StagedItems.Count; }
    }

    /// <summary>How many committed items exist, across tenants.</summary>
    public int CommittedCount
    {
        get { lock (_storage.SyncRoot) return _storage.Items.Count; }
    }

    /// <summary>Returns durable integrity episodes for an authorized host audit reader; an unauthenticated health surface must use only <see cref="IsIntegrityHealthy"/>.</summary>
    public IReadOnlyList<ContentIntegrityEntry> IntegrityEntries
    {
        get { lock (_storage.SyncRoot) return [.. _storage.IntegrityEntries.Values]; }
    }

    /// <summary>Reports the generic count-free health signal: true when no unresolved item integrity episode exists.</summary>
    public bool IsIntegrityHealthy
    {
        get { lock (_storage.SyncRoot) return !_storage.IntegrityEntries.Values.Any(entry => entry.Active); }
    }

    internal void CorruptCiphertext(TenantId tenant, ContentId contentId, int segment)
    {
        lock (_storage.SyncRoot) _storage.Items[new(tenant, contentId)].Segments[segment].Ciphertext[0] ^= 1;
    }

    internal void CorruptWrapTag(TenantId tenant, ContentId contentId)
    {
        lock (_storage.SyncRoot) _storage.Items[new(tenant, contentId)].WrapTag[0] ^= 1;
    }

    internal void RewrapWithContentIdSubkey(TenantId tenant, ContentId contentId)
    {
        lock (_storage.SyncRoot)
        {
            var item = _storage.Items[new(tenant, contentId)];
            var root = RequireKey(tenant);
            var (_, correctWrapKey) = DeriveSubkeys(root, tenant);
            var (contentIdKey, _) = DeriveSubkeys(root, tenant);
            var dataKey = new byte[KeyBytes];
            try
            {
                using (var wrapper = new AesGcm(correctWrapKey, TagBytes))
                {
                    wrapper.Decrypt(item.WrapNonce, item.WrappedDataKey, item.WrapTag, dataKey, Encoding.ASCII.GetBytes(item.ContentId.Value));
                }

                using var wrongWrapper = new AesGcm(contentIdKey, TagBytes);
                wrongWrapper.Encrypt(item.WrapNonce, dataKey, item.WrappedDataKey, item.WrapTag, Encoding.ASCII.GetBytes(item.ContentId.Value));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(correctWrapKey);
                CryptographicOperations.ZeroMemory(contentIdKey);
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
    }

    internal ContentStoreItem StorageItem(TenantId tenant, ContentId contentId)
    {
        lock (_storage.SyncRoot) return _storage.Items[new(tenant, contentId)];
    }

    internal void DropLastSegmentKeepingLength(TenantId tenant, ContentId contentId)
    {
        lock (_storage.SyncRoot)
        {
            var segments = _storage.Items[new(tenant, contentId)].Segments;
            segments.RemoveAt(segments.Count - 1);
        }
    }

    // Live references to an item's key material, captured before an erase so a test can see it destroyed.
    internal (byte[] WrappedDataKey, ICollection Segments) KeyMaterial(TenantId tenant, ContentId contentId)
    {
        lock (_storage.SyncRoot) return (_storage.Items[new(tenant, contentId)].WrappedDataKey, _storage.Items[new(tenant, contentId)].Segments);
    }

    internal (byte[] WrappedDataKey, ICollection Segments) KeyMaterial(Guid stageId)
    {
        lock (_storage.SyncRoot) return (_storage.StagedItems[stageId].WrappedDataKey, _storage.StagedItems[stageId].Segments);
    }

    // Simulates an attacker who drops the tail segment and shortens the length to match, so only the last-segment
    // flag in the nonce can detect it.
    internal void TruncateLastSegment(TenantId tenant, ContentId contentId)
    {
        lock (_storage.SyncRoot)
        {
            var item = _storage.Items[new(tenant, contentId)];
            item.Segments.RemoveAt(item.Segments.Count - 1);
            item.Length = (long)item.Segments.Count * item.SegmentSize;
        }
    }

    private static ReadOnlyDictionary<string, long> Detail(long limit, long observed) =>
        new ReadOnlyDictionary<string, long>(new Dictionary<string, long> { ["limit"] = limit, ["observed"] = observed });

    private bool Expired(ContentStoreItem item) => _clock.GetUtcNow() - item.CreatedUtc >= _options.StagingExpiry;

    private void ReclaimStaged(ContentStoreItem item, string cause, string run)
    {
        _storage.StagedItems.Remove(item.StageId);
        item.Destroy();
        item.State = ContentLifecycleState.Reclaimed;
        ResolveIntegrity(item); // completed erasure ends the episode (DES-0054 Q34)
        _storage.Reclamations.Add(new(item.ContentId, item.Tenant, cause, run));
    }

    private ContentStoreItem Readable(TenantId tenant, ContentReference reference, bool authorized)
    {
        lock (_storage.SyncRoot)
        {
            if (!authorized || !_storage.Items.TryGetValue(new(tenant, reference.ContentId), out var item) || !item.Owners.Contains(reference.Owner))
            {
                throw new ContentNotFoundException();
            }

            return item;
        }
    }

    private static byte[] ReadSegment(Stream stream)
    {
        var buffer = new byte[SegmentSizeBytes];
        var filled = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return filled == buffer.Length ? buffer : buffer[..filled];
    }

    private static ContentCiphertextSegment Seal(AesGcm aead, byte[] noncePrefix, int index, byte[] plaintext, bool last)
    {
        var nonce = SegmentNonce(noncePrefix, index, last);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagBytes];
        aead.Encrypt(nonce, plaintext, cipher, tag);
        return new ContentCiphertextSegment(cipher, tag);
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

    private ContentReadStream OpenRead(ContentStoreItem item, ContentOwner owner, byte[] tenantKey, int first, int last, long offset, long length,
        bool verifiesWholeItem, ContentReadContext? context)
    {
        var wrapKey = DeriveSubkey(tenantKey, item.Tenant, DataKeyWrapKeyInfo);
        try
        {
            return new ContentReadStream(item, owner, wrapKey, first, last, offset, length, verifiesWholeItem, context, ReportIntegrity, ResolveIntegrity);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
        }
    }

    private void ReportIntegrity(ContentStoreItem item, ContentOwner owner, int segmentIndex, ContentReadContext? context)
    {
        var effective = context ?? new ContentReadContext(Activity.Current?.TraceId.ToString() ?? string.Empty, string.Empty, item.Tenant.Value, string.Empty);
        using var activity = IntegrityActivitySource.StartActivity("content.integrity");
        (activity ?? Activity.Current)?.SetStatus(ActivityStatusCode.Error);
        (activity ?? Activity.Current)?.SetTag("error.type", "content.integrity");
        IntegrityFailures.Add(1);
        var beginsEpisode = false;
        lock (_storage.SyncRoot)
        {
            if (item.State == ContentLifecycleState.Reclaimed) return; // a read racing erasure cannot reopen an episode (Q34)
            var key = (item.Tenant, item.StageId);
            if (!_storage.IntegrityEntries.TryGetValue(key, out var prior) || !prior.Active)
            {
                beginsEpisode = true;
                _storage.IntegrityEntries[key] = new(item.StageId, item.Tenant, _clock.GetUtcNow(), _clock.GetUtcNow(), 1, segmentIndex, true,
                    effective.TraceId, effective.OperationId, effective.PrincipalId, owner);
            }
            else
            {
                _storage.IntegrityEntries[key] = prior with { LastSeenUtc = _clock.GetUtcNow(), Count = prior.Count + 1, SegmentIndex = segmentIndex,
                    TraceId = effective.TraceId, OperationId = effective.OperationId, PrincipalId = effective.PrincipalId, Owner = owner };
            }
        }

        if (beginsEpisode)
        {
            _integrityLogger?.LogIntegrityFailure(new(effective.TraceId, effective.OperationId, effective.OpaqueTenantId, effective.PrincipalId,
                item.StageId, owner, segmentIndex));
        }
    }

    private void ResolveIntegrity(ContentStoreItem item)
    {
        lock (_storage.SyncRoot)
        {
            var key = (item.Tenant, item.StageId);
            if (_storage.IntegrityEntries.TryGetValue(key, out var entry) && entry.Active)
            {
                _storage.IntegrityEntries[key] = entry with { Active = false };
            }
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

    // DES-0054 design.md:74 and :211 (Q33): filename is ASCII-safe and filename* is RFC 8187 UTF-8 percent-encoded.
    private static string Attachment(string name)
    {
        var fallback = new string([.. name.Select(character => character <= 0x7f && character is not ('"' or '\\') && !char.IsControl(character) ? character : '_')]);
        var faithful = fallback == name && !HasPercentHexPair(fallback);
        var filename = faithful ? fallback : fallback.Replace("%", "_", StringComparison.Ordinal);
        return faithful
            ? "attachment; filename=\"" + filename + "\""
            : "attachment; filename=\"" + filename + "\"; filename*=UTF-8''" + EncodeFilenameStar(name);
    }

    private static bool HasPercentHexPair(string value) => value.Select((character, index) => (character, index))
        .Any(pair => pair.character == '%' && pair.index + 2 < value.Length && Uri.IsHexDigit(value[pair.index + 1]) && Uri.IsHexDigit(value[pair.index + 2]));

    private static string EncodeFilenameStar(string value)
    {
        var output = new StringBuilder();
        foreach (var octet in Encoding.UTF8.GetBytes(value))
        {
            if (octet is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'!' or (byte)'#' or (byte)'$' or (byte)'&' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'^' or (byte)'_' or (byte)'`' or (byte)'|' or (byte)'~')
            {
                output.Append((char)octet);
            }
            else
            {
                output.Append('%').Append(octet.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return output.ToString();
    }

    private byte[] RequireKey(TenantId tenant)
    {
        var key = _keys.GetKey(tenant);
        if (key is null || key.Length != KeyBytes) throw new InvalidOperationException("A tenant content key must be 32 bytes.");
        return key;
    }

    internal static (byte[] ContentIdKey, byte[] DataKeyWrapKey) DeriveSubkeys(byte[] tenantKey, TenantId tenant)
    {
        return (DeriveSubkey(tenantKey, tenant, ContentIdKeyInfo), DeriveSubkey(tenantKey, tenant, DataKeyWrapKeyInfo));
    }

    private static byte[] DeriveSubkey(byte[] tenantKey, TenantId tenant, string info)
    {
        var derived = new byte[KeyBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, tenantKey, derived, Encoding.UTF8.GetBytes(tenant.Value), Encoding.ASCII.GetBytes(info));
        return derived;
    }

    private long LogicalUsage(TenantId tenant, string? record) =>
        _storage.Items.Values.Where(item => item.Tenant == tenant)
            .Sum(item => item.Owners.Count(owner => record is null || owner.Record == record) * item.Length);

    private sealed class ContentReadStream : Stream
    {
        private readonly ContentStoreItem _item;
        private readonly ContentOwner _owner;
        private readonly int _last;
        private readonly bool _verifiesWholeItem;
        private readonly ContentReadContext? _context;
        private readonly Action<ContentStoreItem, ContentOwner, int, ContentReadContext?> _report;
        private readonly Action<ContentStoreItem> _resolve;
        private readonly byte[] _dataKey = new byte[KeyBytes];
        private long _remaining;
        private int _index;
        private int _offset;
        private byte[] _clear = [];
        private bool _completed;

        public ContentReadStream(ContentStoreItem item, ContentOwner owner, byte[] wrapKey, int first, int last, long offset, long length,
            bool verifiesWholeItem, ContentReadContext? context, Action<ContentStoreItem, ContentOwner, int, ContentReadContext?> report,
            Action<ContentStoreItem> resolve)
        {
            _item = item;
            _owner = owner;
            _last = last;
            _remaining = length;
            _index = first;
            _offset = checked((int)offset);
            _verifiesWholeItem = verifiesWholeItem;
            _context = context;
            _report = report;
            _resolve = resolve;
            try
            {
                using var wrapper = new AesGcm(wrapKey, TagBytes);
                wrapper.Decrypt(item.WrapNonce, item.WrappedDataKey, item.WrapTag, _dataKey, Encoding.ASCII.GetBytes(item.ContentId.Value));
                LoadSegment(); // Verify the first covering segment before the host writes headers.
            }
            catch (Exception exception) when (exception is CryptographicException or IndexOutOfRangeException)
            {
                Fail(exception);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _remaining;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            try
            {
                var copied = 0;
                while (!buffer.IsEmpty && _remaining > 0)
                {
                    if (_offset == _clear.Length)
                    {
                        _index++;
                        LoadSegment();
                        _offset = 0;
                    }

                    var take = (int)Math.Min(Math.Min(_clear.Length - _offset, buffer.Length), _remaining);
                    _clear.AsSpan(_offset, take).CopyTo(buffer);
                    _offset += take;
                    _remaining -= take;
                    copied += take;
                    buffer = buffer[take..];
                }

                if (_remaining == 0 && !_completed)
                {
                    _completed = true;
                    if (_verifiesWholeItem) _resolve(_item);
                }

                return copied;
            }
            catch (ContentIntegrityException)
            {
                throw;
            }
            catch (Exception exception) when (exception is CryptographicException or IndexOutOfRangeException)
            {
                return Fail(exception);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CryptographicOperations.ZeroMemory(_dataKey);
            base.Dispose(disposing);
        }

        private void LoadSegment()
        {
            if (_index > _last || _index >= _item.Segments.Count)
            {
                throw new AuthenticationTagMismatchException("A ciphertext segment is missing.");
            }

            var segment = _item.Segments[_index];
            _clear = new byte[segment.Ciphertext.Length];
            using var aead = new AesGcm(_dataKey, TagBytes);
            aead.Decrypt(SegmentNonce(_item.NoncePrefix, _index, _index == _item.LastSegment), segment.Ciphertext, segment.Tag, _clear);
        }

        private int Fail(Exception exception)
        {
            _report(_item, _owner, _index, _context);
            throw new ContentIntegrityException(_item.StageId, _owner, _index, exception);
        }
    }
}
