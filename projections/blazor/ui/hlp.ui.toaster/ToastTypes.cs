namespace Harborline.UIAdapters.Blazor.Components.Feedback;

/// <summary>A toast identifier, wrapping a string value.</summary>
public readonly record struct ToastId(string Value)
{
    /// <summary>Provides the formatted identifier value.</summary>
    public override string ToString() => Value;
}
/// <summary>The severity styling and icon of a toast.</summary>
public enum ToastVariant
{
    /// <summary>Shows a plain toast with no status styling.</summary>
    Default,
    /// <summary>Shows the toast as a success confirmation.</summary>
    Success,
    /// <summary>Shows the toast as an error.</summary>
    Error,
    /// <summary>Shows the toast as a warning.</summary>
    Warning,
    /// <summary>Shows the toast as informational.</summary>
    Information,
    /// <summary>Shows the toast with a progress indicator for work still running.</summary>
    Loading
}
/// <summary>Which screen corner or edge toasts stack in.</summary>
public enum ToastPosition
{
    /// <summary>Stacks toasts in the top-left corner.</summary>
    TopLeft,
    /// <summary>Stacks toasts along the top center.</summary>
    TopCenter,
    /// <summary>Stacks toasts in the top-right corner.</summary>
    TopRight,
    /// <summary>Stacks toasts in the bottom-left corner.</summary>
    BottomLeft,
    /// <summary>Stacks toasts along the bottom center.</summary>
    BottomCenter,
    /// <summary>Stacks toasts in the bottom-right corner.</summary>
    BottomRight
}
/// <summary>A button on a toast: its label and the action it runs.</summary>
public sealed record ToastAction(string Label, Func<Task> ActivateAsync);
/// <summary>Per-toast settings: how long it stays, an optional description and an optional action.</summary>
public sealed record ToastOptions(int? DurationMilliseconds = null, string? Description = null, ToastAction? Action = null);
/// <summary>Host settings for toasts: default duration, how many show at once, close button, colours and whether errors stay.</summary>
public sealed record ToastHostConfiguration(int? DurationMilliseconds = 4000, int MaximumVisible = 3, bool ShowCloseButton = false, bool UseSemanticColors = false, bool KeepErrorsPersistent = true, ToastOptions? Defaults = null);
/// <summary>A toast as shown: id, message, variant, description, action, duration, whether it persists and when it was created.</summary>
public sealed record ToastEntry(ToastId Id, string Message, ToastVariant Variant, string? Description, ToastAction? Action, int? DurationMilliseconds, bool Persistent, DateTimeOffset CreatedAt);

/// <summary>Queues toast notifications for the current circuit and dismisses them on timeout or user action.</summary>
public interface IToastService
{
/// <summary>Raised whenever the queue changes so the host can re-render.</summary>
    event Action? Changed;
/// <summary>The toasts currently queued.</summary>
    IReadOnlyList<ToastEntry> Entries { get; }
/// <summary>Applies the host toast settings, such as duration and how many show at once.</summary>
    void ConfigureHost(ToastHostConfiguration configuration);
/// <summary>Shows a plain toast and returns its id.</summary>
    ToastId Show(string message, ToastOptions? options = null);
/// <summary>Shows a success toast and returns its id.</summary>
    ToastId Success(string message, ToastOptions? options = null);
/// <summary>Shows an error toast and returns its id.</summary>
    ToastId Error(string message, ToastOptions? options = null);
/// <summary>Shows a warning toast and returns its id.</summary>
    ToastId Warning(string message, ToastOptions? options = null);
/// <summary>Shows an information toast and returns its id.</summary>
    ToastId Information(string message, ToastOptions? options = null);
/// <summary>Shows a loading toast and returns its id.</summary>
    ToastId Loading(string message, ToastOptions? options = null);
/// <summary>Shows a loading toast while a task runs, then replaces it with a success or error toast.</summary>
    Task<T> TrackAsync<T>(Task<T> operation, string loadingMessage, Func<T, string> successMessage, Func<Exception, string> errorMessage, ToastOptions? options = null);
/// <summary>Dismisses the toast with the given id, or the newest toast when none is given.</summary>
    void Dismiss(ToastId? id = null);
/// <summary>Dismisses every toast.</summary>
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
/// <summary>Raised whenever the queue changes so the host can re-render.</summary>
    public event Action? Changed;
/// <summary>A snapshot of the toasts currently queued.</summary>
    public IReadOnlyList<ToastEntry> Entries { get { lock (gate) return entries.ToArray(); } }

/// <summary>Stores the host toast settings and applies them to toasts already queued.</summary>
    public void ConfigureHost(ToastHostConfiguration value)
    {
        if (value.MaximumVisible <= 0) throw new InvalidOperationException("invalid-toast-limit");
        if (value.DurationMilliseconds < 0) throw new InvalidOperationException("invalid-toast-duration");
        configuration = value;
    }

/// <summary>Queues a plain toast and starts its timeout.</summary>
    public ToastId Show(string message, ToastOptions? options = null) => Add(message, ToastVariant.Default, options);
/// <summary>Queues a success toast and starts its timeout.</summary>
    public ToastId Success(string message, ToastOptions? options = null) => Add(message, ToastVariant.Success, options);
/// <summary>Queues an error toast; errors stay until dismissed unless the host says otherwise.</summary>
    public ToastId Error(string message, ToastOptions? options = null) => Add(message, ToastVariant.Error, options);
/// <summary>Queues a warning toast and starts its timeout.</summary>
    public ToastId Warning(string message, ToastOptions? options = null) => Add(message, ToastVariant.Warning, options);
/// <summary>Queues an information toast and starts its timeout.</summary>
    public ToastId Information(string message, ToastOptions? options = null) => Add(message, ToastVariant.Information, options);
/// <summary>Queues a loading toast, which stays until it is replaced or dismissed.</summary>
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

/// <summary>Shows a loading toast while the task runs, then replaces it with a success or error toast and returns the task result.</summary>
    public async Task<T> TrackAsync<T>(Task<T> operation, string loadingMessage, Func<T, string> successMessage, Func<Exception, string> errorMessage, ToastOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var id = Loading(loadingMessage, options);
        try { var result = await operation; Upsert(id, successMessage(result), ToastVariant.Success, options); return result; }
        catch (Exception error) { Upsert(id, errorMessage(error), ToastVariant.Error, options); throw; }
    }

/// <summary>Removes the toast with the given id, or the newest one when none is given, and cancels its timer.</summary>
    public void Dismiss(ToastId? id = null)
    {
        lock (gate)
        {
            if (id is null) { foreach (var key in timers.Keys.ToArray()) CancelTimer(key); entries.Clear(); }
            else { CancelTimer(id.Value); entries.RemoveAll(entry => entry.Id == id.Value); }
        }
        Changed?.Invoke();
    }
/// <summary>Removes every toast.</summary>
    public void Clear() => Dismiss();
    private void CancelTimer(ToastId id){if(!timers.Remove(id,out var timer))return;timer.Cancel();timer.Dispose();}
/// <summary>Cancels all timers and clears the queue when the service is disposed.</summary>
    public void Dispose(){lock(gate){foreach(var key in timers.Keys.ToArray())CancelTimer(key);entries.Clear();}}
}
