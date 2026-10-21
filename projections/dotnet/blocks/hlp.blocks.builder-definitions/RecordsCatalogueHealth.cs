using System.Text;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A catalogue health finding: a question about the model, reported rather than refused (ADR-0027).</summary>
/// <param name="Code">The stable finding code.</param>
/// <param name="DefinitionId">The definition the finding is about.</param>
/// <param name="Members">The published member Record Types it counted: none, or the one.</param>
public sealed record RecordsHealthFinding(string Code, string DefinitionId, IReadOnlyList<string> Members);

/// <summary>
/// The Records catalogue health report for one tenant, over published definitions only. It reports a Class with
/// fewer than two published member types as one defect (DES-0015 records-auth-40; ADR-0054): a reference to a
/// one-member Class is a reference to that type in disguise, and a zero-member Class is an edge that looks resolved
/// and resolves to nothing. A Class is reported, never refused, because its membership is derived and changes as
/// types are published.
/// </summary>
public sealed class RecordsCatalogueHealth
{
    /// <summary>A Class whose published membership is zero or one type.</summary>
    public const string DegenerateClass = "records.class.membership_degenerate";

    private readonly IVersionedDefinitionStore _store;

    /// <summary>Creates a report over the host's shared store.</summary>
    public RecordsCatalogueHealth(IVersionedDefinitionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Returns every finding for <paramref name="tenant"/>, ordered by definition id.</summary>
    public async ValueTask<IReadOnlyList<RecordsHealthFinding>> ReportAsync(string tenant, CancellationToken cancellationToken = default)
    {
        // Membership counts each type's published head: the home a type has now, not one it had at an older version.
        var members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in await _store.ListKeysAsync(tenant, DefinitionKind.Records, cancellationToken).ConfigureAwait(false))
        {
            var head = await _store.GetPublishedHeadAsync(key, cancellationToken).ConfigureAwait(false);
            if (head is null) continue;
            // Publication requires a Class, so every published head names one.
            var home = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(head.Document.BodyJson)).ClassId!;
            if (!members.TryGetValue(home, out var list)) members[home] = list = [];
            list.Add(key.DefinitionId);
        }

        var findings = new List<RecordsHealthFinding>();
        foreach (var key in await _store.ListKeysAsync(tenant, DefinitionKind.Classes, cancellationToken).ConfigureAwait(false))
        {
            if (await _store.GetPublishedHeadAsync(key, cancellationToken).ConfigureAwait(false) is null) continue;
            var list = members.TryGetValue(key.DefinitionId, out var found) ? found : [];
            if (list.Count < 2)
                findings.Add(new(DegenerateClass, key.DefinitionId, list.ToArray()));
        }
        return findings;
    }
}
