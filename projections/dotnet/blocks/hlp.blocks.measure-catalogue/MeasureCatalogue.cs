using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Blocks.Aggregates;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.MeasureCatalogue;

/// <summary>
/// The substrate-owned resolution seam. It owns addressing, parameter admission, filter narrowing
/// and the authorization-filtered input set; it owns no math. Declared entries evaluate through the
/// shipped aggregates engine and bound entries run code at bound depth, behind one reference type
/// and one method.
/// </summary>
public sealed class MeasureCatalogue : IMeasureCatalogue
{
    private static readonly IReadOnlyDictionary<string, string> NoParameters =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly Dictionary<string, IMeasureEntry> _entries = new(StringComparer.Ordinal);
    private readonly IAccessSetFilter _access;

    /// <summary>Registers every entry under its own address. Two entries may not claim one address.</summary>
    /// <param name="access">The production Access set filter promoted by T-625.</param>
    /// <param name="entries">Declared and bound entries, in any order.</param>
    public MeasureCatalogue(IAccessSetFilter access, IEnumerable<IMeasureEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _access = access ?? throw new ArgumentNullException(nameof(access));
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!_entries.TryAdd(entry.Reference.Value, entry))
            {
                throw new MeasureException(MeasureCodes.ReferenceDuplicated, $"/measures/{entry.Reference.Value}",
                    "One address carries exactly one entry.");
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<MeasureDescriptor?> ResolveAsync(MeasureRef reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_entries.TryGetValue(reference.Value, out var entry)
            ? new MeasureDescriptor(entry.Reference, entry.ParameterNames)
            : null);
    }

    /// <inheritdoc />
    public async ValueTask<MeasureResult> EvaluateAsync(MeasureRef reference, MeasureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Resolution and admission refuse before a single row is read, so an unknown or malformed
        // request never reaches the caller's row set.
        if (!_entries.TryGetValue(reference.Value, out var entry))
        {
            throw new MeasureException(MeasureCodes.UnknownReference, $"/measures/{reference.Value}",
                "No measure is registered under that reference.");
        }
        var parameters = request.Parameters ?? NoParameters;
        foreach (var name in entry.ParameterNames)
        {
            if (!parameters.ContainsKey(name))
            {
                throw new MeasureException(MeasureCodes.ParameterMissing,
                    $"/measures/{reference.Value}/parameters/{name}", $"Parameter '{name}' is required.");
            }
        }
        foreach (var name in parameters.Keys)
        {
            if (!entry.ParameterNames.Contains(name, StringComparer.Ordinal))
            {
                throw new MeasureException(MeasureCodes.ParameterUnknown,
                    $"/measures/{reference.Value}/parameters/{name}", $"Parameter '{name}' is not declared.");
            }
        }
        var filtering = MeasureNarrowing.Compose(entry.AuthoredFilter, request.Narrow, reference);

        // One bind, at the caller's explicit principal, tenant and instant, to the production
        // Access provider. The catalogue owns no second access system.
        var predicate = _access.Bind(entry.Operation, request.Principal, request.Tenant, entry.RecordKind, request.At);

        IReadOnlyList<MeasureRow> rows = Array.Empty<MeasureRow>();
        IMeasureBasis? basis = null;
        var basisToken = "live";
        switch (request.Rows)
        {
            case SuppliedRows supplied:
                // The filter runs here: before the page window, before grouping, before any fold.
                var visible = await predicate
                    .FilterAsync(supplied.Rows, row => new AccessRecord(request.Tenant, entry.RecordKind, row.Id, Facts(row)), cancellationToken)
                    .ConfigureAwait(false);
                rows = Window(visible, request.Page);
                break;
            case BasisRows supplied:
                basis = supplied.Basis;
                basisToken = supplied.Basis.Token;
                break;
            default:
                throw new MeasureException(MeasureCodes.BasisUnsupported, $"/measures/{reference.Value}/rows",
                    "The caller supplied no rows and no basis.");
        }

        return await entry.EvaluateAsync(new MeasureEvaluation(entry.Reference, request.Tenant, request.Principal,
            request.At, rows, basis, basisToken, predicate, entry.RecordKind, parameters, filtering),
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<MeasureRow> Window(IReadOnlyList<MeasureRow> rows, MeasurePage? page) =>
        page is null ? rows : rows.Skip(page.Skip).Take(page.Take).ToArray();

    private static IReadOnlyDictionary<string, JsonNode?> Facts(MeasureRow row) =>
        row.Fields.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToNode(pair.Value.Value), StringComparer.Ordinal);
}
