namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <summary>
/// Review M5: marks the async flow on which the engine holds a submission store's exclusion scope while it runs host code
/// inside the submit transaction (the accepted command's submit-gate decider, reuse, evaluation, the record-body digest and
/// field protection). A built-in store entered on that flow throws <see cref="FormEngineStoreReentrancyException"/>
/// instead of waiting on its own non-re-entrant scope, so a host port that calls back into the store fails closed at once
/// and can never deadlock the submit. A host store (W10) must apply the same rule.
/// </summary>
internal static class FormStoreReentrancy
{
    private static readonly AsyncLocal<bool> Held = new();

    /// <summary>Marks this flow until the returned scope is disposed; the mark also ends when the calling async method returns.</summary>
    internal static Scope Enter()
    {
        Held.Value = true;
        return new Scope();
    }

    /// <summary>Throws when this flow holds a submission store scope.</summary>
    internal static void ThrowIfHeld()
    {
        if (Held.Value) throw new FormEngineStoreReentrancyException();
    }

    internal readonly struct Scope : IDisposable
    {
        public void Dispose() => Held.Value = false;
    }
}
