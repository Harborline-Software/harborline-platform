# Harborline.Foundation.Authorization local shadow

This package contains revision 1 of `hlp.foundation.actor`: current-user identity, a narrow
same-authentication-scope actor/tenant context, and fail-closed server-derived Party resolution.

Authorization policy, OIDC/Okta validation, session storage, People persistence, principal kind,
dependency-injection registration, and production mapping implementations remain outside this
bounded module. The package is a local, distribution-blocked shadow; the original capture retained aggregate
contract identity authority under the earlier repository name; see ticket 063.
