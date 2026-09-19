using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// A test <see cref="IPartyContext"/>: the kernel's authenticated context resolved to one fixed Party,
/// or failing closed the way the real <c>PartyContext</c> does when nobody is authenticated.
/// </summary>
internal sealed class FixedRequester(Func<Guid> party) : IPartyContext
{
    public FixedRequester(Guid party) : this(() => party) { }

    public static FixedRequester Unauthenticated { get; } =
        new(() => throw PrincipalPartyResolutionException.NoAuthenticatedPrincipal());

    public ValueTask<Guid> GetCurrentPartyIdAsync(CancellationToken cancellationToken = default) => new(party());
}
