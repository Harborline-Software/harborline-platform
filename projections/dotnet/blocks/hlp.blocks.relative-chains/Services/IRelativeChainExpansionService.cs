namespace Harborline.Blocks.RelativeChains;

/// <summary>The pure deterministic relative-chain expansion port.</summary>
public interface IRelativeChainExpansionService
{
    /// <summary>Reconciles the requested immutable projection against a supplied ledger snapshot.</summary>
    /// <param name="request">Every definition, state, clock, and timezone input.</param>
    /// <returns>The rich append-only reconciliation result.</returns>
    RelativeChainExpansionResult Expand(RelativeChainExpansionRequest request);
}
