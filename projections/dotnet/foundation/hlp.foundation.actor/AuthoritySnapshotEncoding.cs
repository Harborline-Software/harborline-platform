using System.Buffers;
using System.Text.Json;

namespace Harborline.Foundation.Authorization;

/// <summary>
/// The one byte grammar of a stored authority snapshot (DES-0004 <c>authority_snapshot</c>, ADR 0068, decision D6): the
/// enforcing gate's evidence exactly as it froze it, written at the act and persisted atomically with the act's
/// authoritative outcome. Every Platform writer that persists a decision's snapshot uses this grammar, so a stored snapshot
/// written by one module (a form submit gate at acceptance) can be read back by another (a Records completion), and nothing
/// is re-derived (revision 6, plan W6/W8).
/// </summary>
/// <remarks>
/// <para>The bytes are one JSON object with exactly four members, in this order: <c>tenant</c> (string), <c>principal</c>
/// (string), <c>version</c> (the evidence schema version, an integer) and <c>steps</c>, the four projected
/// <see cref="AuthorizationTraceStep"/> values as objects of <c>ordinal</c>, <c>stage</c> and <c>facts</c> (an array of
/// strings). This is the grammar <c>RecordWriteOperation</c> has committed since T-616 003c, unchanged.</para>
/// <para>Reading a snapshot calculates no verdict: <see cref="Fact"/> returns a fact the gate recorded, such as
/// <c>verdict:allowed</c>, and never evaluates grants, roles or scopes. A snapshot is evidence for a reader, not an
/// authority to act; it carries role-binding facts, so a host must not disclose it to clients (D6).</para>
/// </remarks>
public static class AuthoritySnapshotEncoding
{
    /// <summary>Encodes <paramref name="evidence"/>'s tenant, principal, version and four projected steps, adding nothing.</summary>
    public static byte[] Encode(AuthorizationDecisionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
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

    /// <summary>
    /// Reads bytes written by <see cref="Encode"/> back as the stored snapshot, or returns null when they are not exactly
    /// that grammar: not JSON, another root, a missing, repeated or unexpected member, a blank tenant or principal, a
    /// version that is not an integer, or steps that are not the four stages <c>act</c>, <c>effective-roles</c>,
    /// <c>standings</c> and <c>verdict</c> at ordinals 1 to 4 with string facts. A malformed snapshot is never repaired.
    /// </summary>
    public static AuthorizationTraceSnapshot? TryDecode(ReadOnlySpan<byte> snapshot)
    {
        if (snapshot.IsEmpty) return null;
        try
        {
            // The whole span must be the one value: comments, trailing commas and trailing content are refused.
            using var document = JsonDocument.Parse(snapshot.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string[] expected = ["tenant", "principal", "version", "steps"];
            var members = root.EnumerateObject().Select(member => member.Name).ToArray();
            if (!members.SequenceEqual(expected, StringComparer.Ordinal)) return null;
            var tenant = root.GetProperty("tenant");
            var principal = root.GetProperty("principal");
            var version = root.GetProperty("version");
            var steps = root.GetProperty("steps");
            if (tenant.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tenant.GetString())
                || principal.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(principal.GetString())
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber)
                || steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() != AuthorizationDecisionEvidence.StepCount)
                return null;
            string[] stages =
            [
                AuthorizationDecisionEvidence.ActStage, AuthorizationDecisionEvidence.EffectiveRolesStage,
                AuthorizationDecisionEvidence.StandingsStage, AuthorizationDecisionEvidence.VerdictStage,
            ];
            var read = new List<AuthorizationTraceStep>(stages.Length);
            var index = 0;
            foreach (var step in steps.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object) return null;
                string[] stepMembers = ["ordinal", "stage", "facts"];
                if (!step.EnumerateObject().Select(member => member.Name).SequenceEqual(stepMembers, StringComparer.Ordinal)) return null;
                var ordinal = step.GetProperty("ordinal");
                var stage = step.GetProperty("stage");
                var facts = step.GetProperty("facts");
                if (ordinal.ValueKind != JsonValueKind.Number || !ordinal.TryGetInt32(out var ordinalNumber) || ordinalNumber != index + 1
                    || stage.ValueKind != JsonValueKind.String || !StringComparer.Ordinal.Equals(stage.GetString(), stages[index])
                    || facts.ValueKind != JsonValueKind.Array
                    || facts.EnumerateArray().Any(fact => fact.ValueKind != JsonValueKind.String))
                    return null;
                read.Add(new AuthorizationTraceStep(ordinalNumber, stages[index], facts.EnumerateArray().Select(fact => fact.GetString()!)));
                index++;
            }
            return new AuthorizationTraceSnapshot(tenant.GetString()!, principal.GetString(), versionNumber, read);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The value of the one fact of <paramref name="stage"/> that begins with <paramref name="prefix"/> (for example
    /// stage <c>verdict</c>, prefix <c>verdict:</c>), without the prefix; null when the stage has no such fact or more than
    /// one. It reads what the gate recorded and decides nothing.
    /// </summary>
    public static string? Fact(AuthorizationTraceSnapshot snapshot, string stage, string prefix)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrEmpty(stage);
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        var matches = snapshot.Steps
            .Where(step => StringComparer.Ordinal.Equals(step.Stage, stage))
            .SelectMany(step => step.Facts)
            .Where(fact => fact.StartsWith(prefix, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0][prefix.Length..] : null;
    }
}
