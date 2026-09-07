using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The durable file-journal archive store: atomic temp-then-move persistence, ordinal
/// key encoding, fail-closed corrupt-journal refusal, and restart recovery from disk.
/// </summary>
public sealed class FileJournalDefinitionLifecycleStore : IDefinitionLifecycleStore, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private HashSet<string> _archived;

    /// <summary>Opens (or recovers) the journal at <paramref name="path"/>; a corrupt journal is refused, never silently reset.</summary>
    /// <param name="path">The durable journal file path.</param>
    public FileJournalDefinitionLifecycleStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A durable path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        _archived = Load(_path);
    }

    /// <inheritdoc />
    public ValueTask<bool> IsArchivedAsync(DefinitionLifecycleKey key, CancellationToken cancellationToken = default)
    {
        Validate(key);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_archived.Contains(Encode(key)));
    }

    /// <inheritdoc />
    public async ValueTask ArchiveAsync(DefinitionLifecycleKey key, CancellationToken cancellationToken = default)
        => await MutateAsync(key, archive: true, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask UnarchiveAsync(DefinitionLifecycleKey key, CancellationToken cancellationToken = default)
        => await MutateAsync(key, archive: false, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListArchivedAsync(string tenant, DefinitionKind kind, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("Tenant is required.", nameof(tenant));
        var prefix = $"{tenant.Length}:{tenant}|{kind}|";
        foreach (var encoded in _archived.Where(value => value.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return encoded[prefix.Length..];
            await Task.Yield();
        }
    }

    private async ValueTask MutateAsync(DefinitionLifecycleKey key, bool archive, CancellationToken ct)
    {
        Validate(key);
        await _mutation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = new HashSet<string>(_archived, StringComparer.Ordinal);
            if (archive) next.Add(Encode(key)); else next.Remove(Encode(key));
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next.Order(StringComparer.Ordinal)), ct).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
            _archived = next;
        }
        finally { _mutation.Release(); }
    }

    private static HashSet<string> Load(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try { return new(JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [], StringComparer.Ordinal); }
        catch (JsonException error) { throw new InvalidDataException("definition-lifecycle-corrupt", error); }
    }

    private static string Encode(DefinitionLifecycleKey key) => $"{key.Tenant.Length}:{key.Tenant}|{key.Kind}|{key.DefinitionKey}";
    private static void Validate(DefinitionLifecycleKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key.Tenant)) throw new ArgumentException("definition-tenant-required", nameof(key));
        if (string.IsNullOrWhiteSpace(key.DefinitionKey)) throw new ArgumentException("definition-key-required", nameof(key));
    }
    /// <summary>Releases the mutation gate.</summary>
    public void Dispose() => _mutation.Dispose();
}
