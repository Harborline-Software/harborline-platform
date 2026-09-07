using System.Collections;

namespace Harborline.UIAdapters.Blazor.Components.Forms;

internal sealed class SchemaFormValueStore
{
    private IReadOnlyDictionary<string, object?> values;
    private readonly Dictionary<string, Subscription> subscriptions = new(StringComparer.Ordinal);

    public SchemaFormValueStore(IReadOnlyDictionary<string, object?> values) => this.values = values;

    public IReadOnlyDictionary<string, object?> Values => values;

    public object? Get(IReadOnlyList<object> path) => GetIn(values, path);

    public IReadOnlyDictionary<string, object?> Set(IReadOnlyList<object> path, object? value)
    {
        var previous = values;
        values = (IReadOnlyDictionary<string, object?>)SetIn(values, path, value)!;
        NotifyChanged(previous, values, path);
        return values;
    }

    public IReadOnlyDictionary<string, object?> With(IReadOnlyList<object> path, object? value) =>
        (IReadOnlyDictionary<string, object?>)SetIn(values, path, value)!;

    public void Replace(IReadOnlyDictionary<string, object?> next)
    {
        if (ReferenceEquals(values, next)) return;
        var previous = values;
        values = next;
        foreach (var subscription in subscriptions.Values.ToArray())
        {
            if (!ValueEquals(GetIn(previous, subscription.Path), GetIn(next, subscription.Path)))
            {
                foreach (var listener in subscription.Listeners.ToArray()) listener();
            }
        }
    }

    public IDisposable Subscribe(IReadOnlyList<object> path, Action listener)
    {
        var key = PathKey(path);
        if (!subscriptions.TryGetValue(key, out var subscription))
        {
            subscription = new(path.ToArray());
            subscriptions.Add(key, subscription);
        }

        subscription.Listeners.Add(listener);
        return new CallbackDisposable(() =>
        {
            subscription.Listeners.Remove(listener);
            if (subscription.Listeners.Count == 0) subscriptions.Remove(key);
        });
    }

    private void NotifyChanged(
        IReadOnlyDictionary<string, object?> previous,
        IReadOnlyDictionary<string, object?> next,
        IReadOnlyList<object> changedPath)
    {
        for (var length = 1; length <= changedPath.Count; length++)
        {
            var prefix = changedPath.Take(length).ToArray();
            if (!subscriptions.TryGetValue(PathKey(prefix), out var subscription)) continue;
            if (ValueEquals(GetIn(previous, prefix), GetIn(next, prefix))) continue;
            foreach (var listener in subscription.Listeners.ToArray()) listener();
        }
    }

    private static object? GetIn(object? current, IReadOnlyList<object> path)
    {
        foreach (var segment in path)
        {
            current = segment switch
            {
                string key when current is IReadOnlyDictionary<string, object?> readOnly && readOnly.TryGetValue(key, out var value) => value,
                string key when current is IDictionary<string, object?> dictionary && dictionary.TryGetValue(key, out var value) => value,
                int index when current is IList list && index >= 0 && index < list.Count => list[index],
                _ => null,
            };
        }

        return current;
    }

    private static object? SetIn(object? node, IReadOnlyList<object> path, object? value)
    {
        if (path.Count == 0) return value;
        var head = path[0];
        var rest = path.Skip(1).ToArray();
        if (head is int index)
        {
            var array = node is IList existing ? existing.Cast<object?>().ToList() : [];
            while (array.Count <= index) array.Add(null);
            array[index] = SetIn(array[index], rest, value);
            return array;
        }

        var key = (string)head;
        var dictionary = node switch
        {
            IReadOnlyDictionary<string, object?> readOnly => new Dictionary<string, object?>(readOnly, StringComparer.Ordinal),
            IDictionary<string, object?> mutable => new Dictionary<string, object?>(mutable, StringComparer.Ordinal),
            _ => new Dictionary<string, object?>(StringComparer.Ordinal),
        };
        dictionary.TryGetValue(key, out var child);
        dictionary[key] = SetIn(child, rest, value);
        return dictionary;
    }

    private static bool ValueEquals(object? left, object? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    private static string PathKey(IEnumerable<object> path) => string.Join("\u001f", path.Select(value => value switch
    {
        int index => $"#{index}",
        string text => $"${text.Replace("\u001f", "\u001f\u001f", StringComparison.Ordinal)}",
        _ => throw new InvalidOperationException("schema-form-invalid-value-path"),
    }));

    private sealed class Subscription(IReadOnlyList<object> path)
    {
        public IReadOnlyList<object> Path { get; } = path;
        public HashSet<Action> Listeners { get; } = [];
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private Action? callback = callback;
        public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
    }
}
