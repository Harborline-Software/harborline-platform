namespace Harborline.Foundation.Session;

/// <summary>Provider-neutral reason that a host established a server-side session.</summary>
public enum SessionEstablishmentReason
{
    /// <summary>Credential verification completed.</summary>
    PasswordLogin,

    /// <summary>Email verification completed and established a session.</summary>
    VerifyEmailCompletion,

    /// <summary>A single-use magic link was consumed.</summary>
    MagicLinkConsume,

    /// <summary>An external identity provider, such as an OIDC provider, authenticated the user.</summary>
    ExternalIdentity,
}
