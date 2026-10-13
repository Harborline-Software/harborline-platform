using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Harborline.Foundation.Authorization;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The ambient scope a host supplies for one write (DES-0015 records-eng-28): never read from the body.</summary>
/// <param name="Tenant">The host's tenant binding.</param>
/// <param name="ActorId">The authenticated caller; the gate decides for it, the audit carries it, and it scopes the operation id.</param>
/// <remarks>A scope is a principal's direct create. It names no form or engine origin, so it never creates a record of a
/// <c>form_only</c> or <c>engine</c> Record Type (records-ck-22, K9).</remarks>
public sealed record RecordWriteScope(string Tenant, string ActorId);

/// <summary>One record create: the Record Type it instantiates, the caller's stable operation id, the body and an optional pin.</summary>
/// <param name="RecordTypeId">The Record Type to instantiate.</param>
/// <param name="OperationId">The stable <c>operation_id</c> a retry repeats (records-run-3); the wire member is
/// <c>operation_id</c>. It is not a record id. It is taken exactly as sent: nonempty, at most 255 UTF-8 bytes, compared
/// ordinally and never trimmed or normalized; anything else refuses <c>records.write.operation_id_invalid</c> at
/// <c>/operation_id</c>.</param>
/// <param name="Body">The record fields as submitted; the operation mints the record id.</param>
/// <param name="Version">An explicit published version to bind, or null to bind the published head at first commit.</param>
public sealed record RecordCreateRequest(string RecordTypeId, string OperationId, JsonObject Body, string? Version = null);

/// <summary>
/// The lookup scope of a committed operation: ambient tenant, authenticated caller and stable operation id. Another
/// caller or tenant is another scope and never sees this scope's receipt.
/// </summary>
/// <param name="Tenant">The ambient tenant.</param>
/// <param name="CallerId">The authenticated caller.</param>
/// <param name="OperationId">The caller's stable operation id.</param>
public sealed record RecordWriteOperationKey(string Tenant, string CallerId, string OperationId)
{
    /// <summary>The kernel idempotency key: each member length-framed in UTF-16 code units, so no two scopes share one.</summary>
    public string IdempotencyKey => Framed(Tenant) + Framed(CallerId) + Framed(OperationId);

    private static string Framed(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
}

/// <summary>The record row the operation stages into the host's transaction, exactly as it was validated.</summary>
/// <param name="Tenant">The ambient tenant.</param>
/// <param name="RecordId">The opaque id the operation minted.</param>
/// <param name="RecordTypeId">The bound Record Type.</param>
/// <param name="Version">The published version the write was bound to.</param>
/// <param name="DefinitionDigest">The catalogue digest of that published body.</param>
/// <param name="Schema">The registered schema the body was validated against.</param>
/// <param name="ActorId">The caller the gate decided for.</param>
/// <param name="OperationId">The caller's stable operation id.</param>
/// <param name="RequestedVersion">The explicit pin the caller sent, or null for an unpinned request.</param>
/// <param name="IntentEncodingVersion">The <see cref="RecordWriteIntent.EncodingVersion"/> the intent digest was computed under.</param>
/// <param name="IntentDigest">The digest of the request's semantic intent (<see cref="RecordWriteIntent"/>).</param>
/// <param name="Body">The submitted body with members in ordinal key order, every number in its submitted text and every
/// other value equal to the submitted one (the JSON writer may re-escape strings). The validator judged exactly these bytes
/// and the host stores exactly these bytes.</param>
/// <param name="RecordedAt">The act instant the gate decided at.</param>
public sealed record RecordWriteRow(
    string Tenant,
    string RecordId,
    string RecordTypeId,
    string Version,
    string DefinitionDigest,
    SchemaId Schema,
    string ActorId,
    string OperationId,
    string? RequestedVersion,
    string IntentEncodingVersion,
    string IntentDigest,
    ReadOnlyMemory<byte> Body,
    DateTimeOffset RecordedAt);

/// <summary>What a committed create reports, with its original binding. A replay returns it unchanged.</summary>
public sealed record RecordWriteReceipt(
    string RecordId,
    string OperationId,
    string RecordTypeId,
    string Version,
    string DefinitionDigest,
    SchemaId Schema,
    DateTimeOffset RecordedAt);

/// <summary>A committed operation as the host stored it atomically with its record and audit.</summary>
/// <param name="IntentEncodingVersion">The encoding version the stored digest was computed under.</param>
/// <param name="IntentDigest">The stored intent digest.</param>
/// <param name="Receipt">The original receipt.</param>
public sealed record RecordWriteCommittedOperation(string IntentEncodingVersion, string IntentDigest, RecordWriteReceipt? Receipt);

/// <summary>
/// The host's read of committed operations, in the same store its transaction port commits to. It answers only the
/// exact scope it is asked for. The atomic stage of the operation identity remains the arbiter between concurrent writers.
/// </summary>
/// <remarks>
/// Null means only that no operation is accepted under the key: a lookup miss, after which the request proceeds as a
/// first-seen operation. An accepted operation whose receipt is missing or unreadable is still returned, with what was
/// stored (a null <see cref="RecordWriteCommittedOperation.Receipt"/>, or the stored encoding version and digest), so it
/// refuses <c>records.write.receipt_unsupported</c> and is never written again.
/// </remarks>
public interface IRecordWriteReceiptReader
{
    /// <summary>Returns the operation accepted under <paramref name="key"/>, readable or not, or null when none is.</summary>
    ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default);
}

/// <summary>
/// How the host's transaction settled the staged operation identity. A host keys committed operations by
/// <see cref="RecordWriteOperationKey.IdempotencyKey"/> under a uniqueness constraint enforced inside the commit, and
/// compares intent digests: the same digest is <see cref="RecordWriteCommitDisposition.Replayed"/>, any other (or one it
/// cannot compare) is <see cref="RecordWriteCommitDisposition.Conflict"/>. Either way the commit publishes no record, audit or authority snapshot.
/// </summary>
public enum RecordWriteCommitDisposition
{
    /// <summary>The record, its operation identity and its audit committed together. <see cref="RecordWriteCommit.Receipt"/>
    /// is the new receipt; one that is missing or names another operation id refuses
    /// <c>records.write.receipt_unsupported</c>.</summary>
    Created,
    /// <summary>The operation was already accepted under the key; nothing new was written. The host returns what it stored
    /// as <see cref="RecordWriteCommit.Stored"/>, which settles exactly as a lookup hit does.</summary>
    Replayed,
    /// <summary>The operation was already accepted under the key with another intent digest; nothing was written. The host
    /// returns what it stored as <see cref="RecordWriteCommit.Stored"/>, which settles exactly as a lookup hit does.</summary>
    Conflict,
}

/// <summary>The host transaction's result.</summary>
/// <param name="Disposition">How the staged operation identity settled.</param>
/// <param name="Receipt">The new receipt for <see cref="RecordWriteCommitDisposition.Created"/>. For a replay or a conflict
/// the operation reads <see cref="RecordWriteCommit.Stored"/> instead.</param>
public sealed record RecordWriteCommit(RecordWriteCommitDisposition Disposition, RecordWriteReceipt? Receipt)
{
    /// <summary>
    /// For <see cref="RecordWriteCommitDisposition.Replayed"/> and <see cref="RecordWriteCommitDisposition.Conflict"/>, the
    /// operation already accepted under the key exactly as stored, readable or not (the value
    /// <see cref="IRecordWriteReceiptReader.FindAsync"/> would return). It settles through the same rule as a lookup hit,
    /// so both paths answer one stored state alike; when it is absent the write refuses
    /// <c>records.write.receipt_unsupported</c> and discloses nothing.
    /// </summary>
    public RecordWriteCommittedOperation? Stored { get; init; }
}

/// <summary>A refused write: the ADR 0038 stage that refused, a stable code and RFC 6901 pointers. Never the body.</summary>
public sealed record RecordWriteRefusal(string Stage, string Code, IReadOnlyList<string> Pointers);

/// <summary>
/// The outcome of one write: a receipt, or a refusal. A refusal persisted nothing, with one exception:
/// <c>records.write.receipt_unsupported</c> after the host's <see cref="RecordWriteCommitDisposition.Created"/> commit
/// returned a missing or foreign receipt. There the record, its operation identity, its audit and its authority snapshot
/// did commit, <see cref="RecordWriteOutcome.Decision"/> is null, and a retry with the same operation id settles from the stored operation.
/// </summary>
/// <param name="Receipt">The committed or original receipt; null when refused.</param>
/// <param name="Replayed">True when the receipt is an earlier commit's.</param>
/// <param name="Refusal">Why the write was refused; null when it returned a receipt. Nothing was persisted, except for the
/// receipt_unsupported refusal after a Created commit described above.</param>
/// <param name="Decision">The enforcing gate's own evidence for this call's authorization, allowed or denied, when it is not
/// already persisted, for the host to persist through its decision-trace plumbing (RW-1): a denial, a refusal after the gate
/// (except the one after a Created commit), a replay or a conflict. Null when the host's commit created the record, even if
/// it returned an unreadable receipt, because the same evidence was then committed in the same transaction as the record,
/// as its audit's <see cref="KernelAuditEvidence.AuthoritySnapshot"/>, and must not be appended again; also null when the
/// request never reached the gate or the gate's evidence named another context. Its target is the record id minted for
/// this attempt, which exists only if this call created it.</param>
public sealed record RecordWriteOutcome(RecordWriteReceipt? Receipt, bool Replayed, RecordWriteRefusal? Refusal,
    AuthorizationDecisionEvidence? Decision = null)
{
    /// <summary>
    /// True when the write returned a usable receipt: it committed now or replayed an earlier commit. False for every
    /// refusal, including <c>records.write.receipt_unsupported</c> after a Created commit, where the record did commit but
    /// no receipt naming the operation came back; a retry settles that operation from what was stored.
    /// </summary>
    public bool Committed => Refusal is null;
}

/// <summary>
/// The versioned semantic intent of a record create (encoding <c>records-write-intent/1</c>, proposed for review). It is
/// the identity a retry is compared by, never the bytes that are validated or stored.
/// </summary>
/// <remarks>
/// <para>The intent is <c>S(version) S(record_type_id) (N | S(pin)) V(body)</c>. Values encode structurally:
/// <c>N</c> null; <c>T</c>/<c>F</c> booleans; <c>S</c>&lt;UTF-8 byte length&gt;<c>:</c>&lt;UTF-8 bytes&gt; strings;
/// <c>A</c>&lt;count&gt;<c>:</c> then elements in order; <c>O</c>&lt;count&gt;<c>:</c> then each member as a string key and
/// its value, keys ordered by their UTF-8 bytes. Numbers are exact: <c>Z</c> for any zero (so <c>-0</c> is <c>0</c>),
/// otherwise <c>D</c>, a sign, <c>&lt;length&gt;:</c>, the coefficient digits without leading or trailing zeros, then a
/// signed base-10 exponent and <c>;</c>. So <c>3</c>, <c>3.0</c> and <c>3e0</c> are one value and the string
/// <c>"3"</c> is another. Counts and lengths are decimal without leading zeros.</para>
/// <para>No number is converted to binary64, and an exponent is never expanded or parsed at arbitrary precision: the
/// written exponent's leading zeros are skipped, and one with more than 18 significant digits (a magnitude of 10^18 or
/// more) has no intent and throws <see cref="FormatException"/>, which a create refuses as
/// <c>entity.validation.body_invalid</c> before any lookup. The bound is lexical, on the exponent as written, whatever
/// the significand (so <c>0e1000000000000000000</c> refuses too); every number within it encodes exactly, and its
/// normalized exponent may pass 10^18 by the significand's own digit counts. Encoding costs time and memory linear in
/// the lexeme.</para>
/// </remarks>
public static partial class RecordWriteIntent
{
    /// <summary>The encoding version this producer writes and the only one it compares.</summary>
    public const string EncodingVersion = "records-write-intent/1";

    // UTF-8 that throws on a lone surrogate instead of substituting U+FFFD; the operation id bound measures with it too.
    internal static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // The most significant digits a written exponent may have: its magnitude is below 10^18, so it and the normalized
    // exponent fit a long exactly. A longer one has no intent (FormatException; a create refuses it as body_invalid).
    private const int MaxExponentDigits = 18;

    /// <summary>Encodes one create's intent: version, Record Type, pin (or none) and body.</summary>
    public static byte[] Encode(string recordTypeId, string? version, JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(recordTypeId);
        ArgumentNullException.ThrowIfNull(body);
        var output = new ArrayBufferWriter<byte>();
        WriteString(output, EncodingVersion);
        WriteString(output, recordTypeId);
        if (version is null) Write(output, "N");
        else WriteString(output, version);
        WriteValue(output, body);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>Encodes one JSON value under this grammar.</summary>
    public static byte[] EncodeValue(JsonNode? value)
    {
        var output = new ArrayBufferWriter<byte>();
        WriteValue(output, value);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>The intent digest: lowercase hexadecimal SHA-256 of the encoding.</summary>
    public static string Digest(ReadOnlySpan<byte> encoding) => Convert.ToHexStringLower(SHA256.HashData(encoding));

    private static void WriteValue(ArrayBufferWriter<byte> output, JsonNode? node)
    {
        switch (node)
        {
            case null:
                Write(output, "N");
                break;
            case JsonObject members:
                var keyed = members.Select(member => (Key: Strict.GetBytes(member.Key), member.Value)).ToArray();
                Array.Sort(keyed, (left, right) => left.Key.AsSpan().SequenceCompareTo(right.Key));
                Write(output, "O" + keyed.Length.ToString(CultureInfo.InvariantCulture) + ":");
                foreach (var (key, value) in keyed)
                {
                    WriteFramed(output, key);
                    WriteValue(output, value);
                }
                break;
            case JsonArray items:
                Write(output, "A" + items.Count.ToString(CultureInfo.InvariantCulture) + ":");
                foreach (var item in items) WriteValue(output, item);
                break;
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        WriteString(output, value.TryGetValue<string>(out var text) ? text : JsonNode.Parse(value.ToJsonString())!.GetValue<string>());
                        break;
                    case JsonValueKind.Number:
                        WriteNumber(output, value.TryGetValue<JsonElement>(out var element) ? element.GetRawText() : value.ToJsonString());
                        break;
                    case JsonValueKind.True:
                        Write(output, "T");
                        break;
                    case JsonValueKind.False:
                        Write(output, "F");
                        break;
                    case JsonValueKind.Null:
                        Write(output, "N");
                        break;
                    default:
                        WriteValue(output, JsonNode.Parse(value.ToJsonString()));
                        break;
                }
                break;
        }
    }

    private static void WriteNumber(ArrayBufferWriter<byte> output, string lexeme)
    {
        if (!JsonNumber().IsMatch(lexeme)) throw new FormatException("The value is not a JSON number.");
        var number = lexeme.AsSpan();
        var negative = number[0] == '-';
        if (negative) number = number[1..];
        var exponentAt = number.IndexOfAny('e', 'E');
        var exponent = exponentAt < 0 ? 0L : WrittenExponent(number[(exponentAt + 1)..]);
        var significand = exponentAt < 0 ? number : number[..exponentAt];
        var point = significand.IndexOf('.');
        var fraction = point < 0 ? ReadOnlySpan<char>.Empty : significand[(point + 1)..];
        var digits = string.Concat(point < 0 ? significand : significand[..point], fraction).TrimStart('0');
        if (digits.Length == 0)
        {
            Write(output, "Z");
            return;
        }
        var coefficient = digits.TrimEnd('0');
        // |exponent| < 10^18 and both lengths are below 2^31, so this is exact in a long.
        var power = checked(exponent - fraction.Length + (digits.Length - coefficient.Length));
        Write(output, "D" + (negative ? "-" : "+") + coefficient.Length.ToString(CultureInfo.InvariantCulture) + ":" + coefficient
            + (power < 0 ? "-" : "+") + (power < 0 ? -power : power).ToString(CultureInfo.InvariantCulture) + ";");
    }

    // The written exponent: an optional sign, then ASCII digits (the lexeme already matched JsonNumber). Leading zeros
    // are skipped at the cost of their own digits; more than MaxExponentDigits significant digits refuses before any
    // arithmetic, so no arbitrary-precision parse ever runs and the accumulator cannot overflow.
    private static long WrittenExponent(ReadOnlySpan<char> written)
    {
        var negative = written[0] == '-';
        if (written[0] is '-' or '+') written = written[1..];
        var significant = written.TrimStart('0');
        if (significant.Length > MaxExponentDigits) throw new FormatException("The exponent is outside the encodable range.");
        var magnitude = 0L;
        foreach (var digit in significant) magnitude = magnitude * 10 + (digit - '0');
        return negative ? -magnitude : magnitude;
    }

    private static void WriteString(ArrayBufferWriter<byte> output, string value) => WriteFramed(output, Strict.GetBytes(value));

    private static void WriteFramed(ArrayBufferWriter<byte> output, byte[] utf8)
    {
        Write(output, "S" + utf8.Length.ToString(CultureInfo.InvariantCulture) + ":");
        output.Write(utf8);
    }

    private static void Write(ArrayBufferWriter<byte> output, string ascii) => output.Write(Encoding.ASCII.GetBytes(ascii));

    [GeneratedRegex(@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex JsonNumber();
}

/// <summary>
/// The host decider contract for a Records create (decision D1, plan W9 and W10): the one request
/// <see cref="RecordWriteOperation.CreateAsync"/> sends to <see cref="IAuthorizationDecider"/> for a create, and what a
/// conforming host adapter must decide on it. Platform calculates no verdict (the AccessDecider fence); it states the
/// question, and it enforces only the type-level rule that needs no verdict (a <c>form_only</c> or <c>engine</c> bound
/// revision admits no direct create).
/// </summary>
/// <remarks>
/// <para><b>What Platform sends, exactly once per call, before any other stage:</b> operation <see cref="Act"/>,
/// the authenticated principal and ambient tenant, an <see cref="AccessRecord"/> of kind <see cref="RecordKind"/> whose id
/// is the record id minted for this attempt, whose <see cref="AccessRecord.RecordTypeId"/> is the requested Record Type,
/// and whose <see cref="AccessRecord.Fields"/> are empty, and the act instant. The request carries no version pin, no
/// creation role and no form: a pinned create is decided on the type, not on the pinned version.</para>
/// <para><b>What a conforming adapter decides:</b> it reads the type's published head policy through
/// <see cref="RecordTypeDefinitionStore.GetCreationPolicyAsync"/> (version null) and allows only when that policy is
/// <see cref="RecordCreationGate.Role"/> and the principal holds its <c>creation_role_id</c> in the tenant; it denies a
/// <c>form_only</c> or <c>engine</c> head, an unpublished type and a principal in an engine or reserved namespace
/// (W10). Its evidence copies the request, so it names the same typed target.</para>
/// <para><b>Consequences, accepted as the lead convention (E4):</b> the newest publication's creation policy governs
/// every create of the type. A pin to an older version whose role differs is decided under the head's role, so a holder
/// of the head's role creates a record bound to the older version, and a holder of only the older version's role is
/// denied. A pin to an older <c>role</c> version under a <c>form_only</c> head is denied by the adapter; a pin to a
/// <c>form_only</c> version under a <c>role</c> head is refused by Platform at bind.</para>
/// <para><b>L152 (role-gated creation) is therefore met only when a W10 adapter honours this contract.</b> Platform tests
/// that use a role-aware fixture decider prove the contract's specification, not production enforcement.</para>
/// </remarks>
public static class RecordCreationAuthorization
{
    /// <summary>The act a Records create is decided under, the API gate's spelling.</summary>
    public const string Act = "records:write";

    /// <summary>The access record kind of a Records create's target.</summary>
    public const string RecordKind = "record";
}

/// <summary>
/// The first public Records write (T-616, DES-0015 section 3): one record create of a published Record Type.
/// It authorizes through the host's sole gate, then settles a committed operation from its stored receipt, or binds the
/// Record Type, validates the submitted bytes against that schema and commits the record, its operation identity and its
/// audit once through <see cref="KernelTransactionBoundary"/>.
/// </summary>
/// <remarks>
/// <para>The gate decides every call first, a replay included, so a denial discloses no receipt and no conflict and
/// reaches no lookup, binder, registry or store. An empty, over-long (more than 255 UTF-8 bytes) or non-text operation
/// id then refuses <c>records.write.operation_id_invalid</c> at <c>/operation_id</c> before any lookup. An operation is
/// looked up by tenant, authenticated caller and operation id. A committed one is never re-executed or written again:
/// another intent (a changed body, Record Type or explicit pin) conflicts, the same intent returns its original receipt
/// with its original binding, and a missing or foreign receipt, or a stored encoding version this producer does not
/// write, refuses without disclosing anything.</para>
/// <para>A first-seen operation binds the explicit pin or, when unpinned, the published head, so a newer publication
/// governs the next new write (RW-6). It validates the body with its numbers exactly as submitted; the intent's numeric
/// normalization never reaches the validator or the store. An unpublished, unknown-version or no-longer-admissible type,
/// and a schema the registry does not hold, refuse <c>entity.validation.schema_unknown</c> (RW-3); a body the schema
/// refuses persists nothing (RW-2); a registry fault, timeout or cancellation propagates after rollback (RW-5). The
/// atomic stage of the operation identity arbitrates concurrent first writes. The gate is asked once. A create commits
/// the gate's own decision evidence as its audit's <see cref="KernelAuditEvidence.AuthoritySnapshot"/>, atomically with
/// the record; every other outcome returns it as <see cref="RecordWriteOutcome.Decision"/> for the host's trace plumbing.</para>
/// <para>Creation policy (decision D1, K9, plan W2/W9): the gate is asked for a typed target, the requested Record Type
/// (<see cref="AccessRecord.RecordTypeId"/>), under the contract <see cref="RecordCreationAuthorization"/> states for the
/// host's decider. The evidence must name the same type. Independently of the decider, the bound revision's own policy is
/// enforced before anything is staged: this entry is a principal's direct create, so a <c>form_only</c> type refuses
/// <c>records.write.form_only</c> and an <c>engine</c> type refuses <c>records.write.engine_only</c>, whatever the gate
/// answered and whatever form metadata a caller had. Both refuse in the bind stage (ADR 0038 order authorize, bind, ...)
/// at <c>/record_type_id</c>, because the refusing fact is a property of the bound revision, not of the principal: the
/// returned <see cref="RecordWriteOutcome.Decision"/> is the authorize stage's own verdict on the typed act, exactly as
/// for <c>entity.validation.schema_unknown</c> at bind, and no refusal is ever reported in the authorize stage beside an
/// allowed decision (DES-0032: the trace equals enforcement). A form invoking a <c>role</c> type's create uses this same operation and adds no
/// creation gate. Mutate, Rules, React and the form-origin entry for <c>form_only</c> types (plan W8) are not in this
/// slice; T-616 still owns them.</para>
/// </remarks>
public sealed class RecordWriteOperation
{
    private const string WriteAct = RecordCreationAuthorization.Act;
    private const string RecordKind = RecordCreationAuthorization.RecordKind;
    private const string AuthorizeStage = "authorize";
    private const string BindStage = "bind";
    private const string ValidateStage = "validate";
    private const string CommitStage = "commit";
    private const string SchemaUnknown = "entity.validation.schema_unknown";
    private const string BodyInvalid = "entity.validation.body_invalid";
    // D6 (approved 2026-10-10): the only codes for these conditions; a host maps them and adds no competing code.
    private const string ReplayConflict = "records.write.replay_conflict";
    private const string ReceiptUnsupported = "records.write.receipt_unsupported";
    // RFC 6901 pointer to the request's operation id member, as the Records grammar spells it on the wire.
    private const string OperationIdPointer = "/operation_id";
    // D6: an operation id is nonempty and at most 255 UTF-8 bytes, taken exactly as sent.
    private const string OperationIdInvalid = "records.write.operation_id_invalid";
    private const int MaxOperationIdBytes = 255;
    // K9 (approved 2026-10-10): a principal's direct create of a type whose creation gate admits no such create. The
    // spellings are proposed for owner review.
    private const string FormOnlyCreation = "records.write.form_only";
    private const string EngineOnlyCreation = "records.write.engine_only";
    private const string RecordTypePointer = "/record_type_id";

    private static readonly IReadOnlyDictionary<string, JsonNode?> NoFields = new Dictionary<string, JsonNode?>().AsReadOnly();

    private readonly AccessProvider _access;
    private readonly RecordTypeDefinitionStore _recordTypes;
    private readonly ISchemaRegistry _registry;
    private readonly IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit> _port;
    private readonly IRecordWriteReceiptReader _receipts;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Composes the existing producers. <paramref name="registry"/> must be the registry
    /// <paramref name="recordTypes"/> registers into; any other refuses every new write as an unknown schema.
    /// <paramref name="receipts"/> must read the store <paramref name="port"/> commits to.
    /// </summary>
    public RecordWriteOperation(AccessProvider access, RecordTypeDefinitionStore recordTypes, ISchemaRegistry registry,
        IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit> port, IRecordWriteReceiptReader receipts, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(recordTypes);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(clock);
        _access = access;
        _recordTypes = recordTypes;
        _registry = registry;
        _port = port;
        _receipts = receipts;
        _clock = clock;
    }

    /// <summary>
    /// Creates one record, returns the receipt of the same committed operation, or refuses. A refusal persisted nothing,
    /// except <c>records.write.receipt_unsupported</c> after a Created commit with an unreadable receipt (see
    /// <see cref="RecordWriteOutcome"/>).
    /// </summary>
    public async ValueTask<RecordWriteOutcome> CreateAsync(RecordWriteScope scope, RecordCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Body);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RecordTypeId);
        // Only null is a caller error; an empty or over-long operation id is a refusal after the gate, never trimmed.
        ArgumentNullException.ThrowIfNull(request.OperationId);
        if (request.Version is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.Version);

        var instant = _clock.GetUtcNow();
        var recordId = Guid.NewGuid().ToString("N");

        // W9: the target is typed by the requested Record Type, so the decider decides a create of exactly that type.
        var access = await _access.CheckWithEvidenceAsync(new AccessRequest(WriteAct, scope.ActorId, scope.Tenant,
            new AccessRecord(scope.Tenant, RecordKind, recordId, NoFields) { RecordTypeId = request.RecordTypeId }, instant),
            cancellationToken).ConfigureAwait(false);
        if (!access.Check.Allowed) return Refused(AuthorizeStage, access.Check.Reason, []) with { Decision = access.Evidence };
        // AccessProvider allows only with evidence that matched every context field.
        var evidence = access.Evidence ?? throw new InvalidOperationException("The gate allowed without its evidence.");

        var (outcome, evidenceCommitted) = await WriteAsync(scope, request, recordId, instant, evidence, cancellationToken)
            .ConfigureAwait(false);
        // A Created commit already committed the evidence with its record, even when its receipt is unreadable; anything else
        // returns it, persisted nowhere yet.
        return evidenceCommitted ? outcome : outcome with { Decision = evidence };
    }

    // Everything after the gate allowed this call. EvidenceCommitted is true only when the host's commit created the record.
    private async ValueTask<(RecordWriteOutcome Outcome, bool EvidenceCommitted)> WriteAsync(RecordWriteScope scope, RecordCreateRequest request, string recordId,
        DateTimeOffset instant, AuthorizationDecisionEvidence evidence, CancellationToken cancellationToken)
    {
        if (!IsOperationId(request.OperationId)) return (Refused(ValidateStage, OperationIdInvalid, [OperationIdPointer]), false);
        var key = new RecordWriteOperationKey(scope.Tenant, scope.ActorId, request.OperationId);
        string intentDigest;
        try
        {
            intentDigest = RecordWriteIntent.Digest(RecordWriteIntent.Encode(request.RecordTypeId, request.Version, request.Body));
        }
        catch (Exception unencodable) when (unencodable is EncoderFallbackException or InvalidOperationException or FormatException)
        {
            // A string that is not Unicode text (a lone surrogate) has no intent; it refuses before any lookup or store.
            return (Refused(ValidateStage, BodyInvalid, []), false);
        }
        var committed = await _receipts.FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (committed is not null) return (Settle(committed, key, intentDigest), false);

        RecordTypePublication? bound;
        try
        {
            bound = await _recordTypes.ResolvePublishedSchemaAsync(scope.Tenant, request.RecordTypeId, request.Version,
                cancellationToken).ConfigureAwait(false);
        }
        catch (DefinitionRefusalException)
        {
            bound = null;
        }
        if (bound is null) return (Refused(BindStage, SchemaUnknown, []), false);
        RecordTypePublication publication = bound;
        // K9: the bound revision's own creation gate decides whether a principal's direct create may exist at all; the
        // decider's allow cannot widen it. It is a fact of the bound revision, so it refuses in the bind stage and the
        // authorize stage's allowed decision is never reported beside an authorize-stage refusal (F1). Nothing is staged.
        switch (RecordCreationPolicy.Of(RecordTypeDefinitionJson.Deserialize(
            Encoding.UTF8.GetBytes(publication.Revision.Document.BodyJson))).Gate)
        {
            case RecordCreationGate.FormOnly:
                return (Refused(BindStage, FormOnlyCreation, [RecordTypePointer]), false);
            case RecordCreationGate.Engine:
                return (Refused(BindStage, EngineOnlyCreation, [RecordTypePointer]), false);
        }

        var body = Submitted(request.Body);
        KernelTransactionResult<RecordWriteCommit> transaction;
        try
        {
            transaction = await KernelTransactionBoundary.ExecutePreparedAsync(
                async token =>
                {
                    SchemaValidationResult validation;
                    try
                    {
                        validation = await _registry.ValidateAsync(publication.Schema.Id, body, token).ConfigureAwait(false);
                    }
                    catch (SchemaNotFoundException)
                    {
                        throw new RefusedWriteException(new(ValidateStage, SchemaUnknown, []));
                    }
                    if (!validation.IsValid)
                        throw new RefusedWriteException(new(ValidateStage, BodyInvalid, Pointers(validation)));

                    var row = new RecordWriteRow(scope.Tenant, recordId, request.RecordTypeId, publication.Revision.Document.Version,
                        publication.Revision.Digest, publication.Schema.Id, scope.ActorId, request.OperationId, request.Version,
                        RecordWriteIntent.EncodingVersion, intentDigest, body, instant);
                    // The gate's own evidence is the audit's authority snapshot: staged in the one audit call, it commits
                    // or rolls back with the record and the operation identity (ADR 0068), never after the commit.
                    return new KernelCommand<RecordWriteRow>(
                        new(recordId, key.IdempotencyKey, intentDigest),
                        row,
                        new(Guid.NewGuid().ToString("N"), scope.ActorId, instant, AuditPayload(row))
                        {
                            AuthoritySnapshot = AuthoritySnapshotOf(evidence),
                        });
                },
                _port,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RefusedWriteException refused)
        {
            return (new RecordWriteOutcome(null, false, refused.Refusal), false);
        }

        var commit = transaction.Value
            ?? throw new InvalidOperationException(transaction.Refusal?.Code ?? "The kernel transaction did not return a result.");
        return (SettleCommit(commit, key, intentDigest), commit.Disposition == RecordWriteCommitDisposition.Created);
    }

    // An operation that is already accepted is settled from what was stored, never re-executed, by this one rule: a lookup
    // hit and a commit the host settled Replayed or Conflict both pass the stored operation here. It is pure and holds no
    // port. On the lookup path nothing is ever staged; on the commit path the host's unique operation key and its contract
    // (a Replayed or Conflict commit publishes nothing) keep the record from being written twice. Nothing is disclosed
    // unless the stored evidence is in this producer's encoding, is complete and names the operation id that was asked
    // for; the reader's contract answers only the exact tenant and caller scope. A lookup miss is the only way to a
    // first-seen write.
    // Another intent conflicts whether or not a receipt is readable, as the atomic stage settles it; only the same intent
    // needs the stored receipt.
    private static RecordWriteOutcome Settle(RecordWriteCommittedOperation committed, RecordWriteOperationKey key, string intentDigest)
    {
        if (committed.IntentEncodingVersion != RecordWriteIntent.EncodingVersion || string.IsNullOrWhiteSpace(committed.IntentDigest))
            return Refused(CommitStage, ReceiptUnsupported, []);
        if (committed.IntentDigest != intentDigest) return Refused(CommitStage, ReplayConflict, [OperationIdPointer]);
        return Readable(committed.Receipt, key) ? new(committed.Receipt, true, null) : Refused(CommitStage, ReceiptUnsupported, []);
    }

    // Created needs the new receipt for this operation id. When the atomic stage found the operation already accepted (a
    // concurrent first write), the host's disposition only says so: what it is settles from the stored operation through
    // Settle, exactly as a lookup hit, and without a stored operation nothing can be settled or disclosed.
    private static RecordWriteOutcome SettleCommit(RecordWriteCommit commit, RecordWriteOperationKey key, string intentDigest)
        => commit.Disposition switch
        {
            RecordWriteCommitDisposition.Created => Readable(commit.Receipt, key)
                ? new(commit.Receipt, false, null)
                : Refused(CommitStage, ReceiptUnsupported, []),
            RecordWriteCommitDisposition.Replayed or RecordWriteCommitDisposition.Conflict => commit.Stored is { } stored
                ? Settle(stored, key, intentDigest)
                : Refused(CommitStage, ReceiptUnsupported, []),
            _ => throw new InvalidOperationException("The host returned an unknown commit disposition."),
        };

    private static bool Readable(RecordWriteReceipt? receipt, RecordWriteOperationKey key)
        => receipt is not null && receipt.OperationId == key.OperationId;

    // Nonempty and at most 255 UTF-8 bytes, measured on the id exactly as sent. Every UTF-16 code unit is at least one
    // UTF-8 byte, so a longer string is over the bound unmeasured; a lone surrogate is not text and has no byte length.
    private static bool IsOperationId(string operationId)
    {
        if (operationId.Length is 0 or > MaxOperationIdBytes) return false;
        try
        {
            return RecordWriteIntent.Strict.GetByteCount(operationId) <= MaxOperationIdBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static RecordWriteOutcome Refused(string stage, string code, IReadOnlyList<string> pointers)
        => new(null, false, new(stage, code, pointers));

    // Pointers only: a validator message may quote the submitted value (RW-4, RW-H6).
    private static string[] Pointers(SchemaValidationResult validation)
        => validation.Errors.Select(error => error.JsonPointer).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    // The submitted body with object members in ordinal key order. Every number keeps its submitted text, so every numeric
    // lexeme reaches total_digits, fraction_digits, max_bytes and the schema unchanged; strings may be re-escaped.
    private static byte[] Submitted(JsonObject body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteSubmitted(writer, body);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteSubmitted(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject members:
                writer.WriteStartObject();
                foreach (var member in members.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(member.Key);
                    WriteSubmitted(writer, member.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray items:
                writer.WriteStartArray();
                foreach (var item in items) WriteSubmitted(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValue value:
                value.WriteTo(writer);
                break;
        }
    }

    // D6 (approved 2026-10-10): exactly seven identifier keys, in this order. version is the published Record Type
    // definition version the write bound to. No body, no intent digest, no authority evidence; tenant, executor, submitter
    // provenance, outcome and format version belong to the audit envelope, not here.
    private static byte[] AuditPayload(RecordWriteRow row)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("act", WriteAct);
            writer.WriteString("operation_id", row.OperationId);
            writer.WriteString("record_id", row.RecordId);
            writer.WriteString("record_type_id", row.RecordTypeId);
            writer.WriteString("version", row.Version);
            writer.WriteString("definition_digest", row.DefinitionDigest);
            writer.WriteString("schema_id", row.Schema.Value);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    // The enforcing gate's evidence exactly as it froze it: tenant, principal, trace version and the four projected steps
    // (the AuthorizationTraceSnapshot read shape). Nothing is re-derived or added (proposed byte grammar, for review).
    private static byte[] AuthoritySnapshotOf(AuthorizationDecisionEvidence evidence)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("tenant", evidence.Tenant);
            writer.WriteString("principal", evidence.Principal);
            writer.WriteNumber("version", evidence.Version);
            writer.WriteStartArray("steps");
            foreach (var step in evidence.Project())
            {
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", step.Ordinal);
                writer.WriteString("stage", step.Stage);
                writer.WriteStartArray("facts");
                foreach (var fact in step.Facts) writer.WriteStringValue(fact);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private sealed class RefusedWriteException(RecordWriteRefusal refusal) : Exception(refusal.Code)
    {
        public RecordWriteRefusal Refusal { get; } = refusal;
    }
}
