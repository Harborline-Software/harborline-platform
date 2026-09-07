# Harborline.Foundation.MultiTenancy local shadow

This package contains the revision-1 `hlp.foundation.tenancy` implementation: tenant metadata,
ambient tenant context, required tenant-scoped markers, and fail-closed `IQueryable` filtering.

Tenant catalogs, lifecycle mutation, host resolution, authorization, provider-specific global
filters, and cross-tenant administration remain outside this bounded module. The package is a local,
distribution-blocked shadow; the original capture retained aggregate contract identity authority under the earlier repository name; see ticket 063.
