using System.Buffers.Binary;
using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ExecutionRuntime;
using Harborline.Kernel.Core;

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

/// <summary>
/// One item's bookkeeping entry: its metadata and wrapped-key material, with no ciphertext (ADR 0113 decision 1, ADR 0114
/// decision 1). An entry is immutable; a change to its owners or state replaces the entry in the backing.
/// </summary>
/// <param name="StageId">The opaque internal item key: the upload-stage key until commit, and the name of the item's bytes in the byte port.</param>
/// <param name="Tenant">The tenant that owns this item.</param>
/// <param name="ContentId">The tenant-keyed identity.</param>
/// <param name="Length">The plaintext length used for delivery and range validation.</param>
/// <param name="MediaType">The detected, stored media type.</param>
/// <param name="CreatedUtc">The server time at which the upload was staged.</param>
/// <param name="WrappedDataKey">The data key wrapped under the tenant's data-key-wrap subkey.</param>
/// <param name="WrapNonce">The nonce used to wrap the per-item data key.</param>
/// <param name="WrapTag">The authentication tag for the wrapped data key.</param>
/// <param name="NoncePrefix">The random prefix from which segment nonces are built.</param>
/// <param name="SegmentSize">The immutable plaintext segment size selected when the item was written.</param>
public sealed record ContentStoreItem(Guid StageId, TenantId Tenant, ContentId ContentId, long Length, string MediaType, DateTimeOffset CreatedUtc,
    byte[] WrappedDataKey, byte[] WrapNonce, byte[] WrapTag, byte[] NoncePrefix, int SegmentSize)
{
    /// <summary>The committed owners that currently reference the item.</summary>
    public IReadOnlyList<ContentOwner> Owners { get; init; } = [];
    /// <summary>The item's lifecycle state.</summary>
    public ContentLifecycleState State { get; init; } = ContentLifecycleState.Staged;
    /// <summary>The index that a complete stored item must end at, calculated from durable metadata.</summary>
    public int LastSegment => (int)Math.Max(0, (Length - 1) / SegmentSize);
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

/// <summary>
/// The reader of one read, required on every read. <see cref="PrincipalId"/> is the actor of any integrity episode the read
/// opens or closes (owner's ruling, 2026-11-05, 22:11 UTC; DES-0041 ck-1), so a blank one is refused before any byte is read.
/// </summary>
/// <param name="TraceId">The host's trace identifier for the read.</param>
/// <param name="OperationId">The host's operation identifier for the read.</param>
/// <param name="OpaqueTenantId">The opaque tenant identifier for operational events.</param>
/// <param name="PrincipalId">The reader; never blank.</param>
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

/// <summary>The state-changing store operation a kernel command carries.</summary>
public enum ContentStoreOperation
{
    /// <summary><see cref="InMemoryContentStore.PutAsync"/> staged an upload.</summary>
    Put,
    /// <summary><see cref="InMemoryContentStore.CommitReferenceAsync"/> promoted a staged item, added an owner to a duplicate's held item, or reclaimed an expired stage before refusing.</summary>
    Commit,
    /// <summary><see cref="InMemoryContentStore.RemoveReferenceAsync"/> removed an owner, and reclaimed the item when it was the last.</summary>
    RemoveReference,
    /// <summary><see cref="InMemoryContentStore.SweepExpiredAsync"/> reclaimed expired staged items.</summary>
    SweepExpired,
    /// <summary>A failed read opened an integrity episode, or counted another failure in the active one.</summary>
    IntegrityFailure,
    /// <summary>A full verified read closed the item's active integrity episode.</summary>
    IntegrityRecovery,
}

/// <summary>Which quota refused a reservation (DES-0054 ck-8).</summary>
public enum ContentQuotaGrain
{
    /// <summary>The per-record logical-byte quota.</summary>
    Record,
    /// <summary>The per-tenant logical-byte quota.</summary>
    Tenant,
}

/// <summary>A conditional quota update's answer: null <paramref name="Refused"/> means it was applied.</summary>
/// <param name="Refused">The grain that refused, checked record first; null when the reservation was applied.</param>
/// <param name="Observed">On a refusal, the total the reservation would have reached at that grain.</param>
public readonly record struct ContentQuotaReservation(ContentQuotaGrain? Refused, long Observed);

/// <summary>
/// One store write's kernel evidence, from the caller (owner's ruling, 2026-11-05, 17:47 UTC). The store never invents an
/// actor (DES-0041 ck-1): <paramref name="Audit"/> names the actor and is written as this write's own audit row, joined or
/// not (DES-0029 Q37, Q38).
/// </summary>
/// <param name="Operation">This write's own identity, which the backing enforces idempotency on.</param>
/// <param name="Audit">This write's own audit evidence.</param>
/// <param name="Transaction">
/// The caller's open transaction, when the write runs inside the caller's boundary execution (ck-6). Null means the store
/// executes its own; inside an open execution that is refused as <see cref="KernelTransactionErrors.NestedExecution"/>.
/// </param>
public sealed record ContentStoreWrite(KernelOperationIdentity Operation, KernelAuditEvidence Audit, IContentBookkeepingSession? Transaction = null);

/// <summary>
/// The bookkeeping reads and writes inside one open transaction, with no commit or rollback, so a caller can hand it to the
/// store without giving up its one commit point. Each write is a conditional statement the backing decides on its own
/// (CWE-362, CWE-367), so the store keeps no lock and no copy of the state.
/// </summary>
public interface IContentBookkeepingSession : IKernelTransactionParticipant<ContentStoreOperation>
{
    /// <summary>Reads a committed item's entry, or null.</summary>
    ValueTask<ContentStoreItem?> FindItemAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default);
    /// <summary>
    /// Reads a committed item's entry and holds its row until the transaction ends, as <c>SELECT ... FOR UPDATE</c> does, or
    /// answers null. An episode write and an erasure of the same item take this row, so they run one after the other. An
    /// episode write is not cancelled with its read, so a durable backing must bound this wait with its own lock timeout;
    /// the timeout surfaces as a failed episode write.
    /// </summary>
    ValueTask<ContentStoreItem?> FindItemForUpdateAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default);
    /// <summary>Reads a staged item's entry, or null.</summary>
    ValueTask<ContentStoreItem?> FindStagedAsync(Guid stageId, CancellationToken cancellationToken = default);
    /// <summary>Reads an item's integrity episode, or null.</summary>
    ValueTask<ContentIntegrityEntry?> FindIntegrityAsync(TenantId tenant, Guid itemKey, CancellationToken cancellationToken = default);
    /// <summary>Writes a staged item's entry. The store calls it only after the item's bytes are durable.</summary>
    ValueTask AddStagedAsync(ContentStoreItem item, CancellationToken cancellationToken = default);
    /// <summary>Removes a staged entry if it is still there; false means another operation already took it.</summary>
    ValueTask<bool> RemoveStagedAsync(Guid stageId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Adds <paramref name="bytes"/> to the record's and the tenant's counts in one conditional update. It refuses, changing
    /// neither, when a new total would pass its limit; a total equal to its limit fits (ck-8, eng-5).
    /// </summary>
    ValueTask<ContentQuotaReservation> TryReserveQuotaAsync(TenantId tenant, string record, long bytes, long recordLimit, long tenantLimit, CancellationToken cancellationToken = default);
    /// <summary>Subtracts a removed reference's bytes from the record's and the tenant's counts.</summary>
    ValueTask ReleaseQuotaAsync(TenantId tenant, string record, long bytes, CancellationToken cancellationToken = default);
    /// <summary>
    /// Creates the item if the tenant does not hold that content id, and answers null. If it does, changes nothing and answers
    /// the held item (eng-4). A concurrent create of the same key decides first: the answer waits until that transaction ends.
    /// </summary>
    ValueTask<ContentStoreItem?> TryCreateItemAsync(ContentStoreItem item, CancellationToken cancellationToken = default);
    /// <summary>Adds an owner to a held item; false means the item no longer exists.</summary>
    ValueTask<bool> AddOwnerAsync(ContentStorageItemKey key, ContentOwner owner, CancellationToken cancellationToken = default);
    /// <summary>Removes one occurrence of an owner; false means the item or that owner no longer exists.</summary>
    ValueTask<bool> RemoveOwnerAsync(ContentStorageItemKey key, ContentOwner owner, CancellationToken cancellationToken = default);
    /// <summary>Deletes the item's entry only if it has no owners; false means it is still referenced or already gone.</summary>
    ValueTask<bool> DeleteItemIfUnownedAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default);
    /// <summary>Appends a reclamation entry.</summary>
    ValueTask AppendReclamationAsync(ReclamationEntry entry, CancellationToken cancellationToken = default);
    /// <summary>Writes an item's integrity episode.</summary>
    ValueTask SetIntegrityAsync(ContentIntegrityEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>A bookkeeping transaction the store opens for itself: the kernel boundary owns its commit and rollback (ck-6; CWE-460).</summary>
public interface IContentBookkeepingTransaction : IContentBookkeepingSession, IKernelPreparedTransaction<ContentStoreOperation, bool>;

/// <summary>
/// The bookkeeping port: items, owners, staged items, quota counts, reclamation entries and integrity entries, and no
/// ciphertext (ADR 0113 decision 1). A host keeps these in its own storage model (DES-0054 Q31). A backing isolates its
/// transactions at read committed or stronger: the sweep decides what bytes to delete from what this port reads, so an
/// uncommitted removal must still name its bytes.
/// </summary>
public interface IContentBookkeeping
{
    /// <summary>Opens the transaction for a store write that has no caller transaction.</summary>
    ValueTask<IContentBookkeepingTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    /// <summary>Lists the staged entries created at or before the cutoff.</summary>
    ValueTask<IReadOnlyList<ContentStoreItem>> StagedCreatedAtOrBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    /// <summary>Whether any staged or committed entry names these bytes.</summary>
    ValueTask<bool> NamesBytesAsync(Guid itemKey, CancellationToken cancellationToken = default);
    /// <summary>Reads a committed item's entry outside any transaction, or null.</summary>
    ValueTask<ContentStoreItem?> FindItemAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default);
    /// <summary>Reads an item's integrity episode outside any transaction, or null.</summary>
    ValueTask<ContentIntegrityEntry?> FindIntegrityAsync(TenantId tenant, Guid itemKey, CancellationToken cancellationToken = default);
    /// <summary>The append-only reclamation entries, in order (ck-18).</summary>
    IReadOnlyList<ReclamationEntry> Reclamations { get; }
    /// <summary>Every integrity episode, active or closed.</summary>
    IReadOnlyList<ContentIntegrityEntry> IntegrityEntries { get; }
    /// <summary>How many staged entries exist.</summary>
    int StagedCount { get; }
    /// <summary>How many committed items exist, across tenants.</summary>
    int CommittedCount { get; }
}

/// <summary>Writes one item's bytes. Disposing a writer that was not completed discards what it wrote.</summary>
public interface IContentByteWriter : IAsyncDisposable
{
    /// <summary>Appends the next ciphertext segment.</summary>
    ValueTask AppendAsync(ContentCiphertextSegment segment, CancellationToken cancellationToken = default);
    /// <summary>Returns only once every appended segment is durable and readable under the item key (ADR 0113 decision 3).</summary>
    /// <param name="durableUtc">The server time the sweep's grace period counts from.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    ValueTask CompleteAsync(DateTimeOffset durableUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// The byte port: ciphertext segments under an opaque item key, written outside any bookkeeping transaction and write-once
/// after <see cref="IContentByteWriter.CompleteAsync"/> (ADR 0113 decision 3, ADR 0114 decision 2).
/// </summary>
public interface IContentByteStore
{
    /// <summary>Opens a writer. The bytes are listed from now on, so a crash before completion still leaves them for the sweep.</summary>
    /// <param name="itemKey">The opaque key the bytes are named by.</param>
    /// <param name="startedUtc">The server time the grace period counts from until the writer completes.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    ValueTask<IContentByteWriter> OpenWriteAsync(Guid itemKey, DateTimeOffset startedUtc, CancellationToken cancellationToken = default);
    /// <summary>Reads one segment, or null when the item has no such segment.</summary>
    ValueTask<ContentCiphertextSegment?> ReadSegmentAsync(Guid itemKey, int index, CancellationToken cancellationToken = default);
    /// <summary>Deletes an item's bytes; deleting bytes that are not there does nothing.</summary>
    ValueTask DeleteAsync(Guid itemKey, CancellationToken cancellationToken = default);
    /// <summary>Lists the keys whose bytes were last written at or before the cutoff, completed or not.</summary>
    ValueTask<IReadOnlyList<Guid>> WrittenAtOrBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

/// <summary>A durable host's two storage ports, passed to the store together (DES-0054 Q31; ADR 0113).</summary>
public interface IContentStoreStorage
{
    /// <summary>The port that holds everything except ciphertext.</summary>
    IContentBookkeeping Bookkeeping { get; }
    /// <summary>The port that holds the ciphertext.</summary>
    IContentByteStore Bytes { get; }
}

/// <summary>One non-durable backing of both ports for tests, prototypes and a host that explicitly chooses process-local state.</summary>
public sealed class InMemoryContentStoreStorage : IContentStoreStorage
{
    /// <inheritdoc />
    public IContentBookkeeping Bookkeeping { get; } = new InMemoryContentBookkeeping();
    /// <inheritdoc />
    public IContentByteStore Bytes { get; } = new InMemoryContentByteStore();
}

/// <summary>
/// The in-memory bookkeeping backing, isolated as a database at read committed is. A transaction's writes stay in the
/// transaction until it commits, and no other reader sees them. Writing a staged or item entry takes that entry's row lock
/// until the transaction ends, and a second writer waits for it, as a row lock and a unique index make it wait. A quota
/// reservation counts against concurrent ones while it is pending, which errs toward refusing and never toward exceeding a
/// limit; a release frees nothing for anyone else until it commits. A repeated idempotency key is refused; replaying its
/// first answer is a durable host's work.
/// </summary>
public sealed class InMemoryContentBookkeeping : IContentBookkeeping
{
    private readonly object _gate = new();
    private readonly Dictionary<ContentStorageItemKey, ContentStoreItem> _items = [];
    private readonly Dictionary<Guid, ContentStoreItem> _staged = [];
    private readonly List<ReclamationEntry> _reclamations = [];
    private readonly Dictionary<(TenantId Tenant, Guid ItemKey), ContentIntegrityEntry> _integrity = [];
    private readonly Dictionary<(TenantId Tenant, string? Record), long> _quota = [];
    private readonly Dictionary<(TenantId Tenant, string? Record), long> _pendingReservations = [];
    private readonly Dictionary<object, Transaction> _rowLocks = [];
    private readonly HashSet<string> _idempotencyKeys = new(StringComparer.Ordinal);
    private readonly List<KernelAuditEvidence> _audits = [];

    /// <inheritdoc />
    public ValueTask<IContentBookkeepingTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IContentBookkeepingTransaction>(new Transaction(this));

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContentStoreItem>> StagedCreatedAtOrBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        lock (_gate) return ValueTask.FromResult<IReadOnlyList<ContentStoreItem>>([.. _staged.Values.Where(item => item.CreatedUtc <= cutoff)]);
    }

    /// <inheritdoc />
    public ValueTask<bool> NamesBytesAsync(Guid itemKey, CancellationToken cancellationToken = default)
    {
        lock (_gate) return ValueTask.FromResult(_staged.ContainsKey(itemKey) || _items.Values.Any(item => item.StageId == itemKey));
    }

    /// <inheritdoc />
    public ValueTask<ContentStoreItem?> FindItemAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default)
    {
        lock (_gate) return ValueTask.FromResult(_items.GetValueOrDefault(key));
    }

    /// <inheritdoc />
    public ValueTask<ContentIntegrityEntry?> FindIntegrityAsync(TenantId tenant, Guid itemKey, CancellationToken cancellationToken = default)
    {
        lock (_gate) return ValueTask.FromResult(_integrity.GetValueOrDefault((tenant, itemKey)));
    }

    /// <inheritdoc />
    public IReadOnlyList<ReclamationEntry> Reclamations
    {
        get { lock (_gate) return [.. _reclamations]; }
    }

    /// <inheritdoc />
    public IReadOnlyList<ContentIntegrityEntry> IntegrityEntries
    {
        get { lock (_gate) return [.. _integrity.Values]; }
    }

    /// <inheritdoc />
    public int StagedCount
    {
        get { lock (_gate) return _staged.Count; }
    }

    /// <inheritdoc />
    public int CommittedCount
    {
        get { lock (_gate) return _items.Count; }
    }

    // The current state and counts, for a test's before and after images.
    internal (ContentStoreItem[] Items, ContentStoreItem[] Staged, KeyValuePair<(TenantId Tenant, string? Record), long>[] Quota, KernelAuditEvidence[] Audits) Snapshot()
    {
        lock (_gate) return ([.. _items.Values], [.. _staged.Values], [.. _quota], [.. _audits]);
    }

    // Stands in for an attacker who edits the entry directly; not a store operation.
    internal void ReplaceItem(ContentStoreItem item)
    {
        lock (_gate) _items[new(item.Tenant, item.ContentId)] = item;
    }

    internal ContentStoreItem StagedEntry(Guid stageId)
    {
        lock (_gate) return _staged[stageId];
    }

    private sealed class Transaction(InMemoryContentBookkeeping owner) : IContentBookkeepingTransaction
    {
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<ContentStorageItemKey, ContentStoreItem?> _items = [];
        private readonly Dictionary<Guid, ContentStoreItem?> _staged = [];
        private readonly Dictionary<(TenantId Tenant, Guid ItemKey), ContentIntegrityEntry> _integrity = [];
        private readonly Dictionary<(TenantId Tenant, string? Record), long> _reservations = [];
        private readonly Dictionary<(TenantId Tenant, string? Record), long> _releases = [];
        private readonly List<ReclamationEntry> _reclamations = [];
        private readonly List<KernelAuditEvidence> _audits = [];
        private readonly List<string> _keys = [];
        private bool _finished;

        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            lock (owner._gate)
            {
                if (!owner._idempotencyKeys.Add(operation.IdempotencyKey)) throw new InvalidOperationException("That idempotency key was already used.");
                _keys.Add(operation.IdempotencyKey);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask StageRecordAsync(ContentStoreOperation record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audit);
            lock (owner._gate) _audits.Add(audit);
            return ValueTask.CompletedTask;
        }

        public ValueTask<ContentStoreItem?> FindItemAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default)
        {
            lock (owner._gate) return ValueTask.FromResult(CurrentItem(key));
        }

        public async ValueTask<ContentStoreItem?> FindItemForUpdateAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default)
        {
            await LockAsync(key, cancellationToken).ConfigureAwait(false);
            lock (owner._gate) return CurrentItem(key);
        }

        public ValueTask<ContentStoreItem?> FindStagedAsync(Guid stageId, CancellationToken cancellationToken = default)
        {
            lock (owner._gate) return ValueTask.FromResult(CurrentStaged(stageId));
        }

        public ValueTask<ContentIntegrityEntry?> FindIntegrityAsync(TenantId tenant, Guid itemKey, CancellationToken cancellationToken = default)
        {
            lock (owner._gate) return ValueTask.FromResult(_integrity.GetValueOrDefault((tenant, itemKey)) ?? owner._integrity.GetValueOrDefault((tenant, itemKey)));
        }

        public ValueTask AddStagedAsync(ContentStoreItem item, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            lock (owner._gate) _staged[item.StageId] = item; // a stage id is minted for this put, so no other writer can name it yet
            return ValueTask.CompletedTask;
        }

        public async ValueTask<bool> RemoveStagedAsync(Guid stageId, CancellationToken cancellationToken = default)
        {
            await LockAsync(stageId, cancellationToken).ConfigureAwait(false);
            lock (owner._gate)
            {
                if (CurrentStaged(stageId) is null) return false;
                _staged[stageId] = null;
                return true;
            }
        }

        public ValueTask<ContentQuotaReservation> TryReserveQuotaAsync(TenantId tenant, string record, long bytes, long recordLimit, long tenantLimit, CancellationToken cancellationToken = default)
        {
            lock (owner._gate)
            {
                var recordTotal = Held((tenant, record)) + bytes;
                if (recordTotal > recordLimit) return ValueTask.FromResult(new ContentQuotaReservation(ContentQuotaGrain.Record, recordTotal));
                var tenantTotal = Held((tenant, null)) + bytes;
                if (tenantTotal > tenantLimit) return ValueTask.FromResult(new ContentQuotaReservation(ContentQuotaGrain.Tenant, tenantTotal));
                foreach (var key in new (TenantId, string?)[] { (tenant, record), (tenant, null) })
                {
                    _reservations[key] = _reservations.GetValueOrDefault(key) + bytes;
                    owner._pendingReservations[key] = owner._pendingReservations.GetValueOrDefault(key) + bytes;
                }

                return ValueTask.FromResult(new ContentQuotaReservation(null, 0));
            }
        }

        public ValueTask ReleaseQuotaAsync(TenantId tenant, string record, long bytes, CancellationToken cancellationToken = default)
        {
            lock (owner._gate)
            {
                foreach (var key in new (TenantId, string?)[] { (tenant, record), (tenant, null) }) _releases[key] = _releases.GetValueOrDefault(key) + bytes;
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask<ContentStoreItem?> TryCreateItemAsync(ContentStoreItem item, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            var key = new ContentStorageItemKey(item.Tenant, item.ContentId);
            await LockAsync(key, cancellationToken).ConfigureAwait(false);
            lock (owner._gate)
            {
                if (CurrentItem(key) is { } held) return held;
                _items[key] = item;
                return null;
            }
        }

        public async ValueTask<bool> AddOwnerAsync(ContentStorageItemKey key, ContentOwner added, CancellationToken cancellationToken = default)
        {
            await LockAsync(key, cancellationToken).ConfigureAwait(false);
            lock (owner._gate)
            {
                if (CurrentItem(key) is not { } item) return false;
                _items[key] = item with { Owners = [.. item.Owners, added] };
                return true;
            }
        }

        public async ValueTask<bool> RemoveOwnerAsync(ContentStorageItemKey key, ContentOwner removed, CancellationToken cancellationToken = default)
        {
            await LockAsync(key, cancellationToken).ConfigureAwait(false);
            lock (owner._gate)
            {
                if (CurrentItem(key) is not { } item) return false;
                var owners = item.Owners.ToList();
                if (!owners.Remove(removed)) return false;
                _items[key] = item with { Owners = owners };
                return true;
            }
        }

        public async ValueTask<bool> DeleteItemIfUnownedAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default)
        {
            await LockAsync(key, cancellationToken).ConfigureAwait(false);
            lock (owner._gate)
            {
                if (CurrentItem(key) is not { Owners.Count: 0 }) return false;
                _items[key] = null;
                return true;
            }
        }

        public ValueTask AppendReclamationAsync(ReclamationEntry entry, CancellationToken cancellationToken = default)
        {
            lock (owner._gate) _reclamations.Add(entry);
            return ValueTask.CompletedTask;
        }

        public ValueTask SetIntegrityAsync(ContentIntegrityEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            lock (owner._gate) _integrity[(entry.TenantId, entry.ItemKey)] = entry;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default)
        {
            End(publish: true);
            return ValueTask.FromResult(true);
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            End(publish: false);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            End(publish: false);
            return ValueTask.CompletedTask;
        }

        private ContentStoreItem? CurrentItem(ContentStorageItemKey key) => _items.TryGetValue(key, out var own) ? own : owner._items.GetValueOrDefault(key);

        private ContentStoreItem? CurrentStaged(Guid stageId) => _staged.TryGetValue(stageId, out var own) ? own : owner._staged.GetValueOrDefault(stageId);

        // Committed bytes, plus every pending reservation, minus only this transaction's own releases.
        private long Held((TenantId Tenant, string? Record) key) =>
            owner._quota.GetValueOrDefault(key) + owner._pendingReservations.GetValueOrDefault(key) - _releases.GetValueOrDefault(key);

        private async ValueTask LockAsync(object row, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task other;
                lock (owner._gate)
                {
                    if (!owner._rowLocks.TryGetValue(row, out var holder) || holder == this)
                    {
                        owner._rowLocks[row] = this;
                        return;
                    }

                    other = holder._ended.Task;
                }

                await other.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private void End(bool publish)
        {
            lock (owner._gate)
            {
                if (_finished) return;
                _finished = true;
                foreach (var (key, bytes) in _reservations) Move(owner._pendingReservations, key, -bytes);
                if (publish)
                {
                    foreach (var (key, bytes) in _reservations) Move(owner._quota, key, bytes);
                    foreach (var (key, bytes) in _releases) Move(owner._quota, key, -bytes);
                    foreach (var (key, item) in _items) Publish(owner._items, key, item);
                    foreach (var (key, item) in _staged) Publish(owner._staged, key, item);
                    foreach (var (key, entry) in _integrity) owner._integrity[key] = entry;
                    owner._reclamations.AddRange(_reclamations);
                    owner._audits.AddRange(_audits);
                }
                else
                {
                    foreach (var key in _keys) owner._idempotencyKeys.Remove(key);
                }

                foreach (var row in owner._rowLocks.Where(pair => pair.Value == this).Select(pair => pair.Key).ToArray()) owner._rowLocks.Remove(row);
            }

            _ended.SetResult();
        }

        private static void Publish<TKey>(Dictionary<TKey, ContentStoreItem> rows, TKey key, ContentStoreItem? item) where TKey : notnull
        {
            if (item is null) rows.Remove(key);
            else rows[key] = item;
        }
    }

    private static void Move(Dictionary<(TenantId Tenant, string? Record), long> rows, (TenantId Tenant, string? Record) key, long bytes)
    {
        var total = rows.GetValueOrDefault(key) + bytes;
        if (total == 0) rows.Remove(key);
        else rows[key] = total;
    }
}

/// <summary>The in-memory byte backing. Completing a write is all the durability it has.</summary>
public sealed class InMemoryContentByteStore : IContentByteStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    /// <inheritdoc />
    public ValueTask<IContentByteWriter> OpenWriteAsync(Guid itemKey, DateTimeOffset startedUtc, CancellationToken cancellationToken = default)
    {
        var entry = new Entry(startedUtc);
        lock (_gate) _entries.Add(itemKey, entry);
        return ValueTask.FromResult<IContentByteWriter>(new Writer(this, itemKey, entry));
    }

    /// <inheritdoc />
    public ValueTask<ContentCiphertextSegment?> ReadSegmentAsync(Guid itemKey, int index, CancellationToken cancellationToken = default)
    {
        lock (_gate) return ValueTask.FromResult(_entries.TryGetValue(itemKey, out var entry) && index < entry.Segments.Count ? entry.Segments[index] : null);
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(Guid itemKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_entries.Remove(itemKey, out var entry))
            {
                foreach (var segment in entry.Segments) CryptographicOperations.ZeroMemory(segment.Ciphertext);
                entry.Segments.Clear();
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Guid>> WrittenAtOrBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        lock (_gate) return ValueTask.FromResult<IReadOnlyList<Guid>>([.. _entries.Where(pair => pair.Value.WrittenUtc <= cutoff).Select(pair => pair.Key)]);
    }

    // The live segment list of an item's bytes, for a test that tampers with them or watches them cleared.
    internal List<ContentCiphertextSegment> LiveSegments(Guid itemKey)
    {
        lock (_gate) return _entries[itemKey].Segments;
    }

    private sealed class Entry(DateTimeOffset writtenUtc)
    {
        public DateTimeOffset WrittenUtc { get; set; } = writtenUtc;
        public List<ContentCiphertextSegment> Segments { get; } = [];
    }

    private sealed class Writer(InMemoryContentByteStore owner, Guid itemKey, Entry entry) : IContentByteWriter
    {
        private bool _completed;

        public ValueTask AppendAsync(ContentCiphertextSegment segment, CancellationToken cancellationToken = default)
        {
            lock (owner._gate) entry.Segments.Add(segment);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(DateTimeOffset durableUtc, CancellationToken cancellationToken = default)
        {
            lock (owner._gate)
            {
                // A sweep that outran a very slow upload took its bytes: the put must fail rather than stage nothing.
                if (!owner._entries.TryGetValue(itemKey, out var current) || !ReferenceEquals(current, entry))
                {
                    throw new IOException("The item's bytes were deleted before the write completed.");
                }

                entry.WrittenUtc = durableUtc;
                _completed = true;
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed) await owner.DeleteAsync(itemKey).ConfigureAwait(false);
        }
    }
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

    /// <summary>The actor the sweep runs as, the one DES-0029 names for scheduled work; the store refuses any other.</summary>
    public const string StagingSweepActor = "sys.scheduler";

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
/// The content store over its two ports: every put stages its own bytes; commit deduplicates inside the tenant and counts
/// logical-byte quotas in the same transaction, with the backing deciding both; reads require the upstream gate's
/// verdict on the referencing owner; erasure destroys the item's data key (DES-0054 §3).
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

    private readonly ContentStoreOptions _options;
    private readonly ITenantContentKeyProvider _keys;
    private readonly TimeProvider _clock;
    private readonly IContentBookkeeping _bookkeeping;
    private readonly IContentByteStore _bytes;
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
        ArgumentNullException.ThrowIfNull(storage.Bookkeeping);
        ArgumentNullException.ThrowIfNull(storage.Bytes);
        options.Validate();
        _options = options;
        _keys = keys;
        _clock = clock;
        _bookkeeping = storage.Bookkeeping;
        _bytes = storage.Bytes;
        _integrityLogger = integrityLogger;
    }

    /// <summary>
    /// Streams one upload into a staged item and returns its stage id, or refuses at a named stage. The size limit is
    /// enforced on the stream (eng-1); the type is detected from the bytes (eng-3); a declared plain SHA-256, when sent,
    /// must match and is then discarded (eng-2). The put never looks at what the tenant already holds (eng-4). The bytes are
    /// durable before the staged entry is written (ADR 0113 decision 3); a crash between the two leaves bytes the sweep reclaims.
    /// </summary>
    public ValueTask<Guid> PutAsync(TenantId tenant, Stream stream, string? declaredMediaType, string? declaredSha256Hex, ContentStoreWrite write,
        FieldNarrowing? field = null, CancellationToken cancellationToken = default) =>
        DoomingCallerOnFailureAsync(write, () => PutCoreAsync(tenant, stream, declaredMediaType, declaredSha256Hex, write, field, cancellationToken));

    private async ValueTask<Guid> PutCoreAsync(TenantId tenant, Stream stream, string? declaredMediaType, string? declaredSha256Hex, ContentStoreWrite write,
        FieldNarrowing? field, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        RequireEvidence(write);
        var (limit, limitDeclaration) = field?.LimitBytes is { } fieldLimit && fieldLimit < _options.ItemLimitBytes
            ? (fieldLimit, field.Declaration)
            : (_options.ItemLimitBytes, StoreDeclaration);
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(field), "A field size limit must be positive.");

        var tenantKey = RequireKey(tenant);
        var contentIdKey = DeriveSubkey(tenantKey, tenant, ContentIdKeyInfo);
        var dataKeyWrapKey = DeriveSubkey(tenantKey, tenant, DataKeyWrapKeyInfo);
        var dataKey = RandomNumberGenerator.GetBytes(KeyBytes); // a fresh data key per item (ck-11, eng-10, §10 ruling 8)
        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixBytes);
        var stageId = Guid.NewGuid();
        try
        {
            var bytes = await _bytes.OpenWriteAsync(stageId, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            await using (bytes.ConfigureAwait(false))
            {
                using var id = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, contentIdKey);
                using var wire = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var header = new List<byte>(ResourceHeaderBytes);
                var segmentCount = 0;
                long length = 0;
                byte[]? pending = null;
                using (var aead = new AesGcm(dataKey, TagBytes))
                {
                    while (true)
                    {
                        var chunk = await ReadSegmentAsync(stream, cancellationToken).ConfigureAwait(false);
                        length += chunk.Length;
                        if (length > limit)
                        {
                            // Refuse at the first segment past the limit; nothing is staged and disposing the writer drops its segments.
                            throw new ContentRefusedException(ContentRefusals.TooLarge, "The item exceeds its effective size limit.", limitDeclaration, Detail(limit, length));
                        }

                        id.AppendData(chunk);
                        wire.AppendData(chunk);
                        header.AddRange(chunk.AsSpan(0, Math.Min(chunk.Length, ResourceHeaderBytes - header.Count)));
                        if (pending is not null && chunk.Length == 0) break;
                        if (pending is not null) await bytes.AppendAsync(Seal(aead, noncePrefix, segmentCount++, pending, last: false), cancellationToken).ConfigureAwait(false);
                        pending = chunk;
                        if (chunk.Length == 0) break;
                    }

                    await bytes.AppendAsync(Seal(aead, noncePrefix, segmentCount, pending!, last: true), cancellationToken).ConfigureAwait(false);
                }

                var mediaType = Admit([.. header], declaredMediaType, field);
                if (declaredSha256Hex is not null && !DigestMatches(declaredSha256Hex, wire.GetHashAndReset()))
                {
                    throw new ContentRefusedException(ContentRefusals.DigestMismatch, "The declared SHA-256 does not match the uploaded bytes.", StoreDeclaration);
                }

                var contentId = new ContentId(ContentId.Algorithm + Convert.ToHexStringLower(id.GetHashAndReset()));
                var (wrapped, wrapNonce, wrapTag) = Wrap(dataKeyWrapKey, dataKey, contentId);
                var item = new ContentStoreItem(stageId, tenant, contentId, length, mediaType, _clock.GetUtcNow(), wrapped, wrapNonce, wrapTag, noncePrefix, SegmentSizeBytes);
                await bytes.CompleteAsync(_clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                await RunAsync(ContentStoreOperation.Put, write, async (session, token) =>
                {
                    await session.AddStagedAsync(item, token).ConfigureAwait(false);
                    return true;
                }, cancellationToken).ConfigureAwait(false);
                return stageId;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(contentIdKey);
            CryptographicOperations.ZeroMemory(dataKeyWrapKey);
        }
    }

    /// <summary>
    /// Turns a staged item into a committed reference inside one transaction: the backing's conditional update reserves the
    /// record and tenant quotas in logical bytes (ck-8, §10 ruling 7; eng-5), and its conditional create decides the
    /// duplicate: a staged copy whose id the tenant already holds becomes a reference to the existing item, and its copy is
    /// destroyed once that transaction commits (eng-4, board P2). The answer is the same shape either way. A refusal inside
    /// a caller's execution dooms it, so a caught refusal cannot commit half of the write.
    /// </summary>
    public ValueTask<ContentReference> CommitReferenceAsync(TenantId tenant, Guid stageId, ContentOwner owner, string displayName, ContentStoreWrite write,
        CancellationToken cancellationToken = default) =>
        DoomingCallerOnFailureAsync(write, () => CommitReferenceCoreAsync(tenant, stageId, owner, displayName, write, cancellationToken));

    private async ValueTask<ContentReference> CommitReferenceCoreAsync(TenantId tenant, Guid stageId, ContentOwner owner, string displayName, ContentStoreWrite write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        RequireEvidence(write);
        ContentStoreItem? expired = null, collapsed = null, held = null;
        var outcome = await RunAsync(ContentStoreOperation.Commit, write, async (session, token) =>
        {
            if (await session.FindStagedAsync(stageId, token).ConfigureAwait(false) is not { } staged || staged.Tenant != tenant) throw StagingExpiredRefusal();
            if (StagedCutoff() >= staged.CreatedUtc)
            {
                // Inside a caller's execution the refusal must fail the join and doom it; the sweep reclaims the stage.
                if (write.Transaction is not null || !await ReclaimStagedAsync(session, staged, "staging-expired", "commit", token).ConfigureAwait(false)) throw StagingExpiredRefusal();
                expired = staged;
                return true;
            }

            await ReserveQuotaAsync(session, tenant, owner.Record, staged.Length, token).ConfigureAwait(false);
            if (!await session.RemoveStagedAsync(stageId, token).ConfigureAwait(false)) throw StagingExpiredRefusal(); // a concurrent commit of this stage won it
            var key = new ContentStorageItemKey(tenant, staged.ContentId);
            var committed = staged with { State = ContentLifecycleState.Committed, Owners = [owner] };
            while (true) // a held item erased between the conditional create and adding the owner is simply created
            {
                var existing = await session.TryCreateItemAsync(committed, token).ConfigureAwait(false);
                if (existing is null)
                {
                    held = committed;
                    return true;
                }

                if (await session.AddOwnerAsync(key, owner, token).ConfigureAwait(false))
                {
                    held = existing;
                    collapsed = staged;
                    return true;
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        if (expired is not null)
        {
            if (outcome == WriteOutcome.Committed) await DestroyAsync(expired, cancellationToken).ConfigureAwait(false);
            throw StagingExpiredRefusal();
        }

        if (collapsed is not null && outcome == WriteOutcome.Committed) await DestroyAsync(collapsed, cancellationToken).ConfigureAwait(false);
        return new(held!.ContentId, held.Length, held.MediaType, displayName, owner);
    }

    private async ValueTask ReserveQuotaAsync(IContentBookkeepingSession session, TenantId tenant, string record, long length, CancellationToken cancellationToken)
    {
        var reservation = await session.TryReserveQuotaAsync(tenant, record, length, _options.RecordQuotaBytes, _options.TenantQuotaBytes, cancellationToken).ConfigureAwait(false);
        switch (reservation.Refused)
        {
            case ContentQuotaGrain.Record:
                throw new ContentRefusedException(ContentRefusals.RecordQuota, "The record's quota would be exceeded.", StoreDeclaration, Detail(_options.RecordQuotaBytes, reservation.Observed));
            case ContentQuotaGrain.Tenant:
                throw new ContentRefusedException(ContentRefusals.TenantQuota, "The tenant's quota would be exceeded.", StoreDeclaration, Detail(_options.TenantQuotaBytes, reservation.Observed));
        }
    }

    private static ContentRefusedException StagingExpiredRefusal() =>
        new(ContentRefusals.StagingExpired, "The staged item is no longer available.", StoreDeclaration);

    // Any failure of a joined write dooms the caller's execution, even one raised before the write reaches the boundary, such
    // as a refused upload, so a caller that catches it cannot commit the rest of its work.
    private static async ValueTask<T> DoomingCallerOnFailureAsync<T>(ContentStoreWrite write, Func<ValueTask<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(write);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception failure) when (write.Transaction is not null)
        {
            try
            {
                await KernelTransactionBoundary.JoinAsync(DoomingCommand, new FailedParticipant(failure)).ConfigureAwait(false);
            }
            catch (Exception doomed) when (ReferenceEquals(doomed, failure) || doomed is KernelTransactionStateException)
            {
                // The join reports the same failure, or finds no open execution to doom. The caller gets the original below.
            }

            throw;
        }
    }

    // Never staged: the participant fails on its first step, which is what dooms the execution.
    private static readonly KernelCommand<ContentStoreOperation> DoomingCommand = new(
        new("content-store:failed-write", "content-store:failed-write", "content-store:failed-write"), ContentStoreOperation.Put,
        new("content-store:failed-write", "content-store:never-staged", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty));

    private sealed class FailedParticipant(Exception failure) : IKernelTransactionParticipant<ContentStoreOperation>
    {
        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => throw failure;
        public ValueTask StageRecordAsync(ContentStoreOperation record, CancellationToken cancellationToken = default) => throw failure;
        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => throw failure;
    }

    private static void RequireEvidence(ContentStoreWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Operation);
        ArgumentNullException.ThrowIfNull(write.Audit);
        write.Operation.Validate();
        write.Audit.Validate();
    }

    private enum WriteOutcome
    {
        // The store's own transaction committed, so what its removal unnamed can be destroyed now.
        Committed,
        // Nothing of the store's committed yet: nothing changed, or the write is staged in the caller's transaction. The
        // store cannot see that commit, so what it unnamed stays for the sweep.
        NotCommitted,
    }

    // The one path every store write takes through the kernel boundary (ck-6). With the caller's transaction it joins the
    // caller's open execution; without one it executes its own. Each stages its own operation, writes and audit row.
    private async ValueTask<WriteOutcome> RunAsync(ContentStoreOperation operation, ContentStoreWrite write,
        Func<IContentBookkeepingSession, CancellationToken, ValueTask<bool>> writes, CancellationToken cancellationToken)
    {
        var command = new KernelCommand<ContentStoreOperation>(write.Operation, operation, write.Audit);
        if (write.Transaction is { } session)
        {
            var joined = new JoinedWrite(session, writes);
            await KernelTransactionBoundary.JoinAsync(command, joined, cancellationToken).ConfigureAwait(false);
            return WriteOutcome.NotCommitted;
        }

        var port = new OwnWritePort(_bookkeeping);
        try
        {
            await KernelTransactionBoundary.ExecutePreparedAsync(async token =>
            {
                if (!await writes(port.Transaction!, token).ConfigureAwait(false)) throw new NothingChangedException();
                return command;
            }, port, cancellationToken).ConfigureAwait(false);
            return WriteOutcome.Committed;
        }
        catch (NothingChangedException)
        {
            return WriteOutcome.NotCommitted;
        }
    }

    // Rolls back a write that found nothing to do, so it leaves no audit row; never leaves the store.
    private sealed class NothingChangedException : Exception;

    private sealed class OwnWritePort(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<ContentStoreOperation, bool>
    {
        public IContentBookkeepingTransaction? Transaction { get; private set; }

        public async ValueTask<IKernelPreparedTransaction<ContentStoreOperation, bool>> BeginAsync(CancellationToken cancellationToken = default) =>
            Transaction = await bookkeeping.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    // The writes run when the boundary stages the record, so any failure among them dooms the caller's execution.
    private sealed class JoinedWrite(IContentBookkeepingSession session, Func<IContentBookkeepingSession, CancellationToken, ValueTask<bool>> writes)
        : IKernelTransactionParticipant<ContentStoreOperation>
    {
        public bool Changed { get; private set; }

        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) =>
            session.StageOperationAsync(operation, cancellationToken);

        public async ValueTask StageRecordAsync(ContentStoreOperation record, CancellationToken cancellationToken = default)
        {
            Changed = await writes(session, cancellationToken).ConfigureAwait(false);
            if (Changed) await session.StageRecordAsync(record, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) =>
            Changed ? session.StageAuditAsync(audit, cancellationToken) : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Opens a streaming body through a reference the upstream gate has authorized (<paramref name="authorized"/>, eng-6). A
    /// missing id, an unauthorized owner and another tenant's id all throw the same <see cref="ContentNotFoundException"/>.
    /// A single range is served as 206, or 416 when unsatisfiable; an <paramref name="ifRange"/> that is not the current
    /// ETag, or an invalid range, is answered with the whole item (RFC 9110 §13.1.5, §14.2). The caller answers a
    /// multi-range request with the whole item by passing no range (§10 ruling 5). The first covering segment is verified
    /// here, before a host writes headers. The body reads asynchronously only; its synchronous reads refuse.
    /// A read is refused inside an open boundary execution, because an episode it opens or closes commits in its own
    /// transaction, where a caller's rollback cannot erase it (Q34).
    /// </summary>
    public async ValueTask<ContentDelivery> ReadAsync(TenantId tenant, ContentReference reference, bool authorized, ContentReadContext context,
        ContentByteRange? range = null, string? ifRange = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.PrincipalId);
        await RefuseOpenExecutionAsync().ConfigureAwait(false);
        var item = await ReadableAsync(tenant, reference, authorized, cancellationToken).ConfigureAwait(false);
        var etag = "\"" + item.ContentId.Value + "\"";
        var disposition = Attachment(reference.DisplayName);
        if (range is not { } requested || (ifRange is not null && ifRange != etag) || requested.Start < 0 || requested.End < requested.Start)
        {
            return new(await OpenReadAsync(item, reference.Owner, RequireKey(tenant), 0, item.LastSegment, 0, item.Length, true, context, cancellationToken).ConfigureAwait(false),
                200, item.MediaType, disposition, etag, null);
        }

        if (requested.Start >= item.Length)
        {
            return new(Stream.Null, 416, item.MediaType, disposition, etag, $"bytes */{item.Length}");
        }

        var end = Math.Min(requested.End, item.Length - 1);
        var first = (int)(requested.Start / item.SegmentSize);
        var last = (int)(end / item.SegmentSize);
        var offset = requested.Start - ((long)first * item.SegmentSize);
        return new(await OpenReadAsync(item, reference.Owner, RequireKey(tenant), first, last, offset, end - requested.Start + 1, false, context, cancellationToken).ConfigureAwait(false),
            206, item.MediaType, disposition, etag, $"bytes {requested.Start}-{end}/{item.Length}");
    }

    // The boundary's own nesting check, asked before any byte is read: Enter refuses an open execution, and otherwise the
    // port ends the probe before anything begins. Without it a read inside a write works until the day a segment fails,
    // and the nesting refusal then takes the place of the integrity failure.
    private static async ValueTask RefuseOpenExecutionAsync()
    {
        try
        {
            await KernelTransactionBoundary.ExecutePreparedAsync<ContentStoreOperation, bool>(
                static _ => throw new InvalidOperationException("The probe never prepares a command."), ExecutionProbe.Instance).ConfigureAwait(false);
        }
        catch (ExecutionProbe.Ended)
        {
        }
    }

    private sealed class ExecutionProbe : IKernelPreparedTransactionPort<ContentStoreOperation, bool>
    {
        public static readonly ExecutionProbe Instance = new();

        public ValueTask<IKernelPreparedTransaction<ContentStoreOperation, bool>> BeginAsync(CancellationToken cancellationToken = default) => throw new Ended();

        public sealed class Ended : Exception;
    }

    /// <summary>
    /// Removes one reference in the records-path disposal run (eng-9). A held owner, or an unreadable hold authority,
    /// changes nothing and returns false (ck-10; board D7). When the last reference goes, the data key is destroyed,
    /// the ciphertext deleted and a reclamation entry kept (ck-11, ck-18), the bytes only after the transaction commits (ADR
    /// 0113 decision 3). Inside a caller's transaction the store cannot see that commit, so the sweep deletes them.
    /// Returns true when the item was reclaimed.
    /// </summary>
    public ValueTask<bool> RemoveReferenceAsync(TenantId tenant, ContentReference reference, HoldStatus hold, string run, ContentStoreWrite write,
        CancellationToken cancellationToken = default) =>
        DoomingCallerOnFailureAsync(write, () => RemoveReferenceCoreAsync(tenant, reference, hold, run, write, cancellationToken));

    private async ValueTask<bool> RemoveReferenceCoreAsync(TenantId tenant, ContentReference reference, HoldStatus hold, string run, ContentStoreWrite write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        RequireEvidence(write);
        if (hold != HoldStatus.None) return false;
        var key = new ContentStorageItemKey(tenant, reference.ContentId);
        ContentStoreItem? reclaimed = null;
        var outcome = await RunAsync(ContentStoreOperation.RemoveReference, write, async (session, token) =>
        {
            // Held first, so what this captures is the entry it removes, even if the item was erased and recreated during the wait.
            if (await session.FindItemForUpdateAsync(key, token).ConfigureAwait(false) is not { } item) return false;
            if (!await session.RemoveOwnerAsync(key, reference.Owner, token).ConfigureAwait(false)) return false;
            await session.ReleaseQuotaAsync(tenant, reference.Owner.Record, item.Length, token).ConfigureAwait(false);
            if (!await session.DeleteItemIfUnownedAsync(key, token).ConfigureAwait(false)) return true;
            await ResolveIntegrityAsync(session, item, token).ConfigureAwait(false); // completed erasure ends the episode (DES-0054 Q34, design.md:212)
            await session.AppendReclamationAsync(new(item.ContentId, tenant, "last-reference-removed", run), token).ConfigureAwait(false);
            reclaimed = item;
            return true;
        }, cancellationToken).ConfigureAwait(false);

        if (reclaimed is null) return false;
        if (outcome == WriteOutcome.Committed) await DestroyAsync(reclaimed, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The staging sweep (§5): reclaims every staged item past the staging expiry, with no request in flight, then deletes
    /// bytes no bookkeeping entry names once the grace period has passed. The grace period is the staging expiry: a put
    /// writes its staged entry within its own put time, and the expiry is a stated multiple of the longest put (ck-13), so
    /// the sweep never takes bytes whose entry is still coming (ADR 0113 decision 3). It acts as
    /// <see cref="ContentStoreRunRegistration.StagingSweepActor"/>. Returns how many staged items it reclaimed.
    /// </summary>
    public ValueTask<int> SweepExpiredAsync(string run, ContentStoreWrite write, CancellationToken cancellationToken = default) =>
        DoomingCallerOnFailureAsync(write, () => SweepExpiredCoreAsync(run, write, cancellationToken));

    private async ValueTask<int> SweepExpiredCoreAsync(string run, ContentStoreWrite write, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(run);
        RequireEvidence(write);
        if (!StringComparer.Ordinal.Equals(write.Audit.ActorId, ContentStoreRunRegistration.StagingSweepActor))
        {
            throw new ArgumentException("The staging sweep acts as " + ContentStoreRunRegistration.StagingSweepActor + ".", nameof(write));
        }

        var cutoff = StagedCutoff();
        var expired = await _bookkeeping.StagedCreatedAtOrBeforeAsync(cutoff, cancellationToken).ConfigureAwait(false);
        var reclaimed = new List<ContentStoreItem>();
        var outcome = WriteOutcome.NotCommitted;
        if (expired.Count != 0)
        {
            outcome = await RunAsync(ContentStoreOperation.SweepExpired, write, async (session, token) =>
            {
                foreach (var item in expired)
                {
                    if (await ReclaimStagedAsync(session, item, "staging-expired", run, token).ConfigureAwait(false)) reclaimed.Add(item);
                }

                return reclaimed.Count != 0;
            }, cancellationToken).ConfigureAwait(false);
        }

        if (outcome == WriteOutcome.Committed)
        {
            foreach (var item in reclaimed) await DestroyAsync(item, cancellationToken).ConfigureAwait(false);
        }

        foreach (var orphan in await _bytes.WrittenAtOrBeforeAsync(cutoff, cancellationToken).ConfigureAwait(false))
        {
            if (!await _bookkeeping.NamesBytesAsync(orphan, cancellationToken).ConfigureAwait(false)) await DeleteBytesAsync(orphan, cancellationToken).ConfigureAwait(false);
        }

        return reclaimed.Count;
    }

    /// <summary>
    /// Exports the items behind authorized references with a fresh per-export key (DES-0054 cc-7, Q20). The package
    /// carries each item's content id, bytes and keyed digest, and no plain SHA-256.
    /// </summary>
    public async ValueTask<ContentExportResult> ExportAsync(TenantId tenant, IEnumerable<ContentReference> references, bool authorized, ContentReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        var exportKey = RandomNumberGenerator.GetBytes(KeyBytes);
        var items = new List<ContentExportItem>();
        foreach (var reference in references)
        {
            var delivery = await ReadAsync(tenant, reference, authorized, context, cancellationToken: cancellationToken).ConfigureAwait(false);
            var body = delivery.Body;
            await using (body.ConfigureAwait(false))
            {
                using var plaintext = new MemoryStream();
                await body.CopyToAsync(plaintext, cancellationToken).ConfigureAwait(false);
                var bytes = plaintext.ToArray();
                var contentId = reference.ContentId;
                items.Add(new ContentExportItem(contentId, bytes, FixityDigest(exportKey, contentId, bytes)));
            }
        }

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
    public IReadOnlyList<ReclamationEntry> Reclamations => _bookkeeping.Reclamations;

    /// <summary>How many staged items exist, for the sweep's operators.</summary>
    public int StagedCount => _bookkeeping.StagedCount;

    /// <summary>How many committed items exist, across tenants.</summary>
    public int CommittedCount => _bookkeeping.CommittedCount;

    /// <summary>Returns durable integrity episodes for an authorized host audit reader; an unauthenticated health surface must use only <see cref="IsIntegrityHealthy"/>.</summary>
    public IReadOnlyList<ContentIntegrityEntry> IntegrityEntries => _bookkeeping.IntegrityEntries;

    /// <summary>Reports the generic count-free health signal: true when no unresolved item integrity episode exists.</summary>
    public bool IsIntegrityHealthy => !_bookkeeping.IntegrityEntries.Any(entry => entry.Active);

    internal async ValueTask CorruptCiphertextAsync(TenantId tenant, ContentId contentId, int segment) =>
        (await _bytes.ReadSegmentAsync((await StorageItemAsync(tenant, contentId).ConfigureAwait(false)).StageId, segment).ConfigureAwait(false))!.Ciphertext[0] ^= 1;

    internal async ValueTask CorruptWrapTagAsync(TenantId tenant, ContentId contentId) => (await StorageItemAsync(tenant, contentId).ConfigureAwait(false)).WrapTag[0] ^= 1;

    internal async ValueTask RewrapWithContentIdSubkeyAsync(TenantId tenant, ContentId contentId)
    {
        var item = await StorageItemAsync(tenant, contentId).ConfigureAwait(false);
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

    internal async ValueTask<ContentStoreItem> StorageItemAsync(TenantId tenant, ContentId contentId) =>
        await _bookkeeping.FindItemAsync(new(tenant, contentId)).ConfigureAwait(false) ?? throw new KeyNotFoundException();

    internal async ValueTask DropLastSegmentKeepingLengthAsync(TenantId tenant, ContentId contentId)
    {
        var segments = ((InMemoryContentByteStore)_bytes).LiveSegments((await StorageItemAsync(tenant, contentId).ConfigureAwait(false)).StageId);
        segments.RemoveAt(segments.Count - 1);
    }

    // Live references to an item's key material, captured before an erase so a test can see it destroyed.
    internal async ValueTask<(byte[] WrappedDataKey, ICollection Segments)> KeyMaterialAsync(TenantId tenant, ContentId contentId)
    {
        var item = await StorageItemAsync(tenant, contentId).ConfigureAwait(false);
        return (item.WrappedDataKey, ((InMemoryContentByteStore)_bytes).LiveSegments(item.StageId));
    }

    internal (byte[] WrappedDataKey, ICollection Segments) KeyMaterial(Guid stageId)
    {
        return (((InMemoryContentBookkeeping)_bookkeeping).StagedEntry(stageId).WrappedDataKey, ((InMemoryContentByteStore)_bytes).LiveSegments(stageId));
    }

    // Simulates an attacker who drops the tail segment and shortens the length to match, so only the last-segment
    // flag in the nonce can detect it.
    internal async ValueTask TruncateLastSegmentAsync(TenantId tenant, ContentId contentId)
    {
        var item = await StorageItemAsync(tenant, contentId).ConfigureAwait(false);
        var segments = ((InMemoryContentByteStore)_bytes).LiveSegments(item.StageId);
        segments.RemoveAt(segments.Count - 1);
        ((InMemoryContentBookkeeping)_bookkeeping).ReplaceItem(item with { Length = (long)segments.Count * item.SegmentSize });
    }

    private static ReadOnlyDictionary<string, long> Detail(long limit, long observed) =>
        new ReadOnlyDictionary<string, long>(new Dictionary<string, long> { ["limit"] = limit, ["observed"] = observed });

    // The latest instant whose staged items and unnamed bytes are past the staging expiry; an expiry longer than the
    // calendar reaches back to its start.
    private DateTimeOffset StagedCutoff()
    {
        var now = _clock.GetUtcNow();
        return _options.StagingExpiry >= now - DateTimeOffset.MinValue ? DateTimeOffset.MinValue : now - _options.StagingExpiry;
    }

    // False when another operation already took the stage; its transaction does the reclaiming.
    private static async ValueTask<bool> ReclaimStagedAsync(IContentBookkeepingSession session, ContentStoreItem item, string cause, string run, CancellationToken cancellationToken)
    {
        if (!await session.RemoveStagedAsync(item.StageId, cancellationToken).ConfigureAwait(false)) return false;
        await session.AppendReclamationAsync(new(item.ContentId, item.Tenant, cause, run), cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Runs only after the transaction that removed the item's last entry committed. Destroying key material and bytes cannot be undone.
    private async ValueTask DestroyAsync(ContentStoreItem item, CancellationToken cancellationToken)
    {
        CryptographicOperations.ZeroMemory(item.WrappedDataKey);
        await DeleteBytesAsync(item.StageId, cancellationToken).ConfigureAwait(false);
    }

    // The removal already committed, so a failed delete must not report the operation as failed (CWE-460). The bytes now
    // have no entry, and the sweep deletes them after the grace period.
    private async ValueTask DeleteBytesAsync(Guid itemKey, CancellationToken cancellationToken)
    {
        try
        {
            await _bytes.DeleteAsync(itemKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Deferring a content byte delete to the staging sweep: {0}", exception.GetType().Name);
        }
    }

    private async ValueTask<ContentStoreItem> ReadableAsync(TenantId tenant, ContentReference reference, bool authorized, CancellationToken cancellationToken)
    {
        if (!authorized || await _bookkeeping.FindItemAsync(new(tenant, reference.ContentId), cancellationToken).ConfigureAwait(false) is not { } item || !item.Owners.Contains(reference.Owner))
        {
            throw new ContentNotFoundException();
        }

        return item;
    }

    private static async ValueTask<byte[]> ReadSegmentAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[SegmentSizeBytes];
        var filled = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask<ContentReadStream> OpenReadAsync(ContentStoreItem item, ContentOwner owner, byte[] tenantKey, int first, int last, long offset, long length,
        bool verifiesWholeItem, ContentReadContext context, CancellationToken cancellationToken)
    {
        var wrapKey = DeriveSubkey(tenantKey, item.Tenant, DataKeyWrapKeyInfo);
        try
        {
            var body = new ContentReadStream(item, _bytes, owner, first, last, offset, length, verifiesWholeItem, context, ReportIntegrityAsync, ResolveVerifiedAsync);
            await body.VerifyFirstSegmentAsync(wrapKey, cancellationToken).ConfigureAwait(false);
            return body;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
        }
    }

    // One episode write's kernel evidence: the reader is the actor (the owner's ruling of 2026-11-05, 22:11 UTC), and each
    // write has its own identity and audit row. They name no tenant or content id, so the row reveals nothing (Q36).
    private ContentStoreWrite EpisodeWrite(string kind, ContentReadContext context)
    {
        var id = Guid.NewGuid().ToString("N");
        return new(new("content-" + kind + ":" + id, id, id), new("content-" + kind + ":" + id, context.PrincipalId, _clock.GetUtcNow(), ReadOnlyMemory<byte>.Empty));
    }

    // The episode commits in its own transaction, never a caller's, and is not cancelled with the read: the failure happened
    // whether or not the reader is still there. A failed write surfaces as itself; the body keeps its typed failure.
    private async ValueTask ReportIntegrityAsync(ContentStoreItem item, ContentOwner owner, int segmentIndex, ContentReadContext context)
    {
        using var activity = IntegrityActivitySource.StartActivity("content.integrity");
        (activity ?? Activity.Current)?.SetStatus(ActivityStatusCode.Error);
        (activity ?? Activity.Current)?.SetTag("error.type", "content.integrity");
        IntegrityFailures.Add(1);
        var beginsEpisode = false;
        await RunAsync(ContentStoreOperation.IntegrityFailure, EpisodeWrite("integrity-failure", context), async (session, token) =>
        {
            // A read racing erasure cannot reopen an episode (Q34); bytes staged again later are a different item. Holding the
            // item's row means an open erasure finishes first, and an erasure that follows sees this episode and closes it.
            var current = await session.FindItemForUpdateAsync(new(item.Tenant, item.ContentId), token).ConfigureAwait(false);
            if (current?.StageId != item.StageId) return false;
            var prior = await session.FindIntegrityAsync(item.Tenant, item.StageId, token).ConfigureAwait(false);
            var now = _clock.GetUtcNow();
            if (prior is not { Active: true })
            {
                beginsEpisode = true;
                await session.SetIntegrityAsync(new(item.StageId, item.Tenant, now, now, 1, segmentIndex, true, context.TraceId, context.OperationId, context.PrincipalId, owner), token).ConfigureAwait(false);
            }
            else
            {
                await session.SetIntegrityAsync(prior with { LastSeenUtc = now, Count = prior.Count + 1, SegmentIndex = segmentIndex,
                    TraceId = context.TraceId, OperationId = context.OperationId, PrincipalId = context.PrincipalId, Owner = owner }, token).ConfigureAwait(false);
            }

            return true;
        }, CancellationToken.None).ConfigureAwait(false);

        if (beginsEpisode)
        {
            _integrityLogger?.LogIntegrityFailure(new(context.TraceId, context.OperationId, context.OpaqueTenantId, context.PrincipalId, item.StageId, owner, segmentIndex));
        }
    }

    // A full verified read ends the item's episode as its own write; with no active episode it changes nothing.
    private async ValueTask ResolveVerifiedAsync(ContentStoreItem item, ContentReadContext context)
    {
        if (await _bookkeeping.FindIntegrityAsync(item.Tenant, item.StageId).ConfigureAwait(false) is not { Active: true } seen) return;
        await RunAsync(ContentStoreOperation.IntegrityRecovery, EpisodeWrite("integrity-recovery", context), async (session, token) =>
        {
            // Holding the item's row orders this with other episode writes; a failure counted since this read looked stays open.
            await session.FindItemForUpdateAsync(new(item.Tenant, item.ContentId), token).ConfigureAwait(false);
            if (await session.FindIntegrityAsync(item.Tenant, item.StageId, token).ConfigureAwait(false) is not { Active: true } entry || entry.Count != seen.Count) return false;
            await session.SetIntegrityAsync(entry with { Active = false }, token).ConfigureAwait(false);
            return true;
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask ResolveIntegrityAsync(IContentBookkeepingSession session, ContentStoreItem item, CancellationToken cancellationToken)
    {
        if (await session.FindIntegrityAsync(item.Tenant, item.StageId, cancellationToken).ConfigureAwait(false) is { Active: true } entry)
        {
            await session.SetIntegrityAsync(entry with { Active = false }, cancellationToken).ConfigureAwait(false);
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

    private sealed class ContentReadStream : Stream
    {
        private const string AsyncOnly = "This body reads asynchronously only; call ReadAsync or CopyToAsync.";
        private readonly ContentStoreItem _item;
        private readonly IContentByteStore _bytes;
        private readonly ContentOwner _owner;
        private readonly int _last;
        private readonly bool _verifiesWholeItem;
        private readonly ContentReadContext _context;
        private readonly Func<ContentStoreItem, ContentOwner, int, ContentReadContext, ValueTask> _report;
        private readonly Func<ContentStoreItem, ContentReadContext, ValueTask> _resolve;
        private readonly byte[] _dataKey = new byte[KeyBytes];
        private readonly long _length;
        private long _remaining;
        private int _index;
        private int _offset;
        private byte[] _clear = [];
        private bool _completed;
        private bool _disposed;
        private ContentIntegrityException? _failure;

        public ContentReadStream(ContentStoreItem item, IContentByteStore bytes, ContentOwner owner, int first, int last, long offset, long length,
            bool verifiesWholeItem, ContentReadContext context, Func<ContentStoreItem, ContentOwner, int, ContentReadContext, ValueTask> report,
            Func<ContentStoreItem, ContentReadContext, ValueTask> resolve)
        {
            _item = item;
            _bytes = bytes;
            _owner = owner;
            _last = last;
            _length = length;
            _remaining = length;
            _index = first;
            _offset = checked((int)offset);
            _verifiesWholeItem = verifiesWholeItem;
            _context = context;
            _report = report;
            _resolve = resolve;
        }

        // Verifies the first covering segment before the host writes headers.
        public async ValueTask VerifyFirstSegmentAsync(byte[] wrapKey, CancellationToken cancellationToken)
        {
            try
            {
                using (var wrapper = new AesGcm(wrapKey, TagBytes))
                {
                    wrapper.Decrypt(_item.WrapNonce, _item.WrappedDataKey, _item.WrapTag, _dataKey, Encoding.ASCII.GetBytes(_item.ContentId.Value));
                }

                await LoadSegmentAsync(_index, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is CryptographicException or IndexOutOfRangeException)
            {
                await FailAsync(exception).ConfigureAwait(false);
            }
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(AsyncOnly);
        public override int Read(Span<byte> buffer) => throw new NotSupportedException(AsyncOnly);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_failure is not null) throw _failure;
            var copied = 0;
            try
            {
                while (!buffer.IsEmpty && _remaining > 0)
                {
                    if (_offset == _clear.Length)
                    {
                        // Hand over what is already copied first: a fetch that throws now could not report those bytes.
                        if (copied != 0) break;
                        await LoadSegmentAsync(_index + 1, cancellationToken).ConfigureAwait(false);
                        _offset = 0;
                    }

                    var take = (int)Math.Min(Math.Min(_clear.Length - _offset, buffer.Length), _remaining);
                    _clear.AsMemory(_offset, take).CopyTo(buffer);
                    _offset += take;
                    _remaining -= take;
                    copied += take;
                    buffer = buffer[take..];
                }
            }
            catch (Exception exception) when (exception is CryptographicException and not ContentIntegrityException or IndexOutOfRangeException)
            {
                await FailAsync(exception).ConfigureAwait(false);
            }

            // Outside the try: a failed recovery write fails this read as itself, never as tampering, and a later read retries it.
            if (_remaining == 0 && !_completed)
            {
                if (_verifiesWholeItem) await _resolve(_item, _context).ConfigureAwait(false);
                _completed = true;
            }

            return copied;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                CryptographicOperations.ZeroMemory(_dataKey);
                CryptographicOperations.ZeroMemory(_clear);
            }
            base.Dispose(disposing);
        }

        // A cancelled fetch changes nothing, so a later read fetches the same segment and never skips it.
        private async ValueTask LoadSegmentAsync(int index, CancellationToken cancellationToken)
        {
            var segment = index > _last ? null : await _bytes.ReadSegmentAsync(_item.StageId, index, cancellationToken).ConfigureAwait(false);
            _index = index;
            CryptographicOperations.ZeroMemory(_clear);
            if (segment is null)
            {
                throw new AuthenticationTagMismatchException("A ciphertext segment is missing.");
            }

            _clear = new byte[segment.Ciphertext.Length];
            using var aead = new AesGcm(_dataKey, TagBytes);
            aead.Decrypt(SegmentNonce(_item.NoncePrefix, _index, _index == _item.LastSegment), segment.Ciphertext, segment.Tag, _clear);
        }

        private async ValueTask FailAsync(Exception exception)
        {
            _failure = new ContentIntegrityException(_item.StageId, _owner, _index, exception);
            CryptographicOperations.ZeroMemory(_dataKey);
            CryptographicOperations.ZeroMemory(_clear);
            await _report(_item, _owner, _index, _context).ConfigureAwait(false);
            throw _failure;
        }
    }
}
