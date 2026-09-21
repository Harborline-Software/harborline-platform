using System.Security.Cryptography;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The catalogue a replay installed, seen as an installed node. It keeps the items the replay
/// committed and nothing derived from the request, so every value an install receipt reports is
/// recomputed here from what is stored: the digest is what resolved, never what was asked for.
/// </summary>
public sealed class InstalledPlatformCatalogue : IPlatformPackageReplayTarget, IInstalledNode
{
    private readonly Func<string, bool> gate;
    private readonly List<PlatformPackageItem> stored = [];
    private string witness = string.Empty;

    /// <summary>
    /// Creates a catalogue whose authorization answers come from this node's gate. A host with a real
    /// gate supplies it; the parameterless form answers from the installed grants, which is the only
    /// authority a clean node has.
    /// </summary>
    public InstalledPlatformCatalogue(Func<string, bool> nodeGate)
    {
        ArgumentNullException.ThrowIfNull(nodeGate);
        gate = nodeGate;
    }

    /// <summary>Creates a catalogue that answers authorization from its own installed grants.</summary>
    public InstalledPlatformCatalogue() => gate = capability => GrantedRoles().Overlaps(OfferedRoles(capability));

    /// <summary>The node's witness for the install that wrote this catalogue; empty until one has.</summary>
    public string Witness => witness;

    /// <inheritdoc />
    public ValueTask<bool> TryApplyToEmptyAsync(IReadOnlyList<PlatformPackageItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (stored.Count != 0) return ValueTask.FromResult(false);
        stored.AddRange(items);
        // Minted by the install, inside the apply that committed the items. Nothing else mints one,
        // which is what a receipt's witness proves about the document that carries it.
        witness = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    public ValueTask<InstalledState> ReadInstalledStateAsync(CancellationToken cancellationToken = default)
    {
        if (stored.Count == 0) throw new InvalidOperationException("install-receipt-node-not-installed");
        using var record = Read("platform-package-ck-1");
        var key = record.RootElement.GetProperty("id").GetString()!;
        var version = record.RootElement.GetProperty("version").GetString()!;
        // Re-exported from the stored items. The published digest is never remembered, so a stored
        // item that differs from the published one moves this value and the checker refuses.
        var digest = PublishedArtifact
            .FromExport(PlatformPackageExporter.Export(new PlatformPackageManifest(1, key, version, stored)))
            .Digest;

        return ValueTask.FromResult(new InstalledState(
            [new InstalledPackage(key, version, digest, witness)],
            [.. Capabilities().Select(name => new DeclaredCapability(name, GrantedRoles().Overlaps(OfferedRoles(name))))],
            [.. Members("platform-package-ck-3")],
            [.. Members("platform-package-ck-4")],
            [.. stored.Select(item => new InstalledRecord(
                item.Id, Convert.ToHexStringLower(SHA256.HashData(item.Content.Payload.Span))))]));
    }

    /// <inheritdoc />
    public ValueTask<bool> AuthorizeAsync(string capability, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        return ValueTask.FromResult(gate(capability));
    }

    private JsonDocument Read(string itemId)
    {
        var item = stored.FirstOrDefault(candidate => candidate.Id == itemId)
            ?? throw new InvalidOperationException($"install-receipt-item-not-installed:{itemId}");
        return JsonDocument.Parse(item.Content.Payload);
    }

    private string[] Capabilities()
    {
        using var access = Read("platform-package-ck-10");
        return [.. access.RootElement.GetProperty("capabilities").EnumerateArray().Select(name => name.GetString()!)];
    }

    private HashSet<string> OfferedRoles(string capability)
    {
        using var access = Read("platform-package-ck-10");
        return [.. access.RootElement.GetProperty("bindings").EnumerateArray()
            .Where(binding => binding.GetProperty("operation").GetString() == capability)
            .SelectMany(binding => binding.GetProperty("offeredRoles").EnumerateArray().Select(role => role.GetString()!))];
    }

    private HashSet<string> GrantedRoles()
    {
        using var access = Read("platform-package-ck-9");
        return [.. access.RootElement.GetProperty("grants").EnumerateArray()
            .Select(grant => grant.TryGetProperty("role", out var role) ? role.GetString() : null)
            .OfType<string>()];
    }

    private string[] Members(string itemId)
    {
        using var item = Read(itemId);
        return [.. item.RootElement.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("id").GetString()!)];
    }
}
