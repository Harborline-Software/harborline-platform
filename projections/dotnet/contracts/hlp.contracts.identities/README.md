# Harborline.Contracts local shadow

This package contains the revision-2 `hlp.contracts.identities` implementation and generated
revision-5 Forms, revision-2 Workflow, and revision-1 Authorization projections. Authorization
preserves the API's qualified role and owner shapes, validates the two admitted vocabularies, and
exposes pure fail-closed role-gate resolution without I/O, grants, or standing derivation.

The package is a local, distribution-blocked shadow. At capture time the earlier source held aggregate contract
identity authority; the aggregate cutover was approved separately and this repository now carries that authority.
