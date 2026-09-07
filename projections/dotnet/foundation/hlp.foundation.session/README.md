# Harborline.Foundation.Session local shadow

This package contains revision 1 of `hlp.foundation.session`: immutable authoritative session
records, an async session store, a single-process in-memory reference adapter, and fail-closed
session-to-actor resolution.

Cookie transport, OIDC/Okta validation, role lookup, session establishment, and database-backed
multi-instance storage remain host adapters. The package is local and distribution-blocked;
The original capture retained aggregate contract identity authority under the earlier repository name; see ticket 063.
