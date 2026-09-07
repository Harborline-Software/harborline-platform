namespace Harborline.Foundation.Authorization;

/// <summary>Identity and roles asserted for the current authenticated principal.</summary>
public interface ICurrentUser
{
    /// <summary>Stable host-derived user identifier, such as a validated OIDC subject.</summary>
    string UserId { get; }

    /// <summary>Host-derived role names. The list may be empty.</summary>
    IReadOnlyList<string> Roles { get; }
}
