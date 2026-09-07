namespace Harborline.UIAdapters.Blazor.Components.Feedback;

public readonly record struct ToastId(string Value) { public override string ToString() => Value; }
public enum ToastVariant { Default, Success, Error, Warning, Information, Loading }
public enum ToastPosition { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }
public sealed record ToastAction(string Label, Func<Task> ActivateAsync);
public sealed record ToastOptions(int? DurationMilliseconds = null, string? Description = null, ToastAction? Action = null);
public sealed record ToastHostConfiguration(int? DurationMilliseconds = 4000, int MaximumVisible = 3, bool ShowCloseButton = false, bool UseSemanticColors = false, bool KeepErrorsPersistent = true, ToastOptions? Defaults = null);
public sealed record ToastEntry(ToastId Id, string Message, ToastVariant Variant, string? Description, ToastAction? Action, int? DurationMilliseconds, bool Persistent, DateTimeOffset CreatedAt);

public interface IToastService
{
    event Action? Changed;
    IReadOnlyList<ToastEntry> Entries { get; }
    void ConfigureHost(ToastHostConfiguration configuration);
    ToastId Show(string message, ToastOptions? options = null);
    ToastId Success(string message, ToastOptions? options = null);
    ToastId Error(string message, ToastOptions? options = null);
    ToastId Warning(string message, ToastOptions? options = null);
    ToastId Information(string message, ToastOptions? options = null);
    ToastId Loading(string message, ToastOptions? options = null);
    Task<T> TrackAsync<T>(Task<T> operation, string loadingMessage, Func<T, string> successMessage, Func<Exception, string> errorMessage, ToastOptions? options = null);
    void Dismiss(ToastId? id = null);
    void Clear();
}

/// <summary>Scoped toast queue. Register once per application root or Blazor circuit.</summary>
public sealed class ToastService : IToastService, IDisposable
{
    private readonly object gate = new();
    private readonly List<ToastEntry> entries = [];
    private readonly Dictionary<ToastId, CancellationTokenSource> timers = [];
    private ToastHostConfiguration configuration = new();
    private long nextId;
    public event Action? Changed;
    public IReadOnlyList<ToastEntry> Entries { get { lock (gate) return entries.ToArray(); } }

    public void ConfigureHost(ToastHostConfiguration value)
    {
        if (value.MaximumVisible <= 0) throw new InvalidOperationException("invalid-toast-limit");
        if (value.DurationMilliseconds < 0) throw new InvalidOperationException("invalid-toast-duration");
        configuration = value;
    }

    public ToastId Show(string message, ToastOptions? options = null) => Add(message, ToastVariant.Default, options);
    public ToastId Success(string message, ToastOptions? options = null) => Add(message, ToastVariant.Success, options);
    public ToastId Error(string message, ToastOptions? options = null) => Add(message, ToastVariant.Error, options);
    public ToastId Warning(string message, ToastOptions? options = null) => Add(message, ToastVariant.Warning, options);
    public ToastId Information(string message, ToastOptions? options = null) => Add(message, ToastVariant.Information, options);
    public ToastId Loading(string message, ToastOptions? options = null) => Add(message, ToastVariant.Loading, options);

    private ToastId Add(string message, ToastVariant variant, ToastOptions? options)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("toast-message-required");
        if (options?.DurationMilliseconds < 0) throw new InvalidOperationException("invalid-toast-duration");
        var id = new ToastId($"toast-{Interlocked.Increment(ref nextId)}");
        Upsert(id, message, variant, options);
        return id;
    }

    private void Upsert(ToastId id, string message, ToastVariant variant, ToastOptions? options)
    {
        var defaults = configuration.Defaults;
        var duration = options?.DurationMilliseconds ?? defaults?.DurationMilliseconds ?? configuration.DurationMilliseconds;
        var persistent = variant == ToastVariant.Loading || (variant == ToastVariant.Error && configuration.KeepErrorsPersistent);
        var entry = new ToastEntry(id, message, variant, options?.Description ?? defaults?.Description, options?.Action ?? defaults?.Action, persistent ? null : duration, persistent, DateTimeOffset.UtcNow);
        lock (gate)
        {
            var index = entries.FindIndex(candidate => candidate.Id == id);
            if (index < 0) entries.Add(entry); else entries[index] = entry;
            CancelTimer(id);
            if (!persistent && duration > 0)
            {
                var cancellation = new CancellationTokenSource();
                timers[id] = cancellation;
                _ = DismissAfterAsync(id, duration.Value, cancellation.Token);
            }
        }
        Changed?.Invoke();
    }

    private async Task DismissAfterAsync(ToastId id, int milliseconds, CancellationToken cancellationToken)
    {
        try { await Task.Delay(milliseconds, cancellationToken); if (!cancellationToken.IsCancellationRequested) Dismiss(id); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async Task<T> TrackAsync<T>(Task<T> operation, string loadingMessage, Func<T, string> successMessage, Func<Exception, string> errorMessage, ToastOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var id = Loading(loadingMessage, options);
        try { var result = await operation; Upsert(id, successMessage(result), ToastVariant.Success, options); return result; }
        catch (Exception error) { Upsert(id, errorMessage(error), ToastVariant.Error, options); throw; }
    }

    public void Dismiss(ToastId? id = null)
    {
        lock (gate)
        {
            if (id is null) { foreach (var key in timers.Keys.ToArray()) CancelTimer(key); entries.Clear(); }
            else { CancelTimer(id.Value); entries.RemoveAll(entry => entry.Id == id.Value); }
        }
        Changed?.Invoke();
    }
    public void Clear() => Dismiss();
    private void CancelTimer(ToastId id){if(!timers.Remove(id,out var timer))return;timer.Cancel();timer.Dispose();}
    public void Dispose(){lock(gate){foreach(var key in timers.Keys.ToArray())CancelTimer(key);entries.Clear();}}
}
