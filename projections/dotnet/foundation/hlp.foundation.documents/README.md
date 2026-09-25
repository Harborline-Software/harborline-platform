# Harborline Foundation Documents

Owns the Documents composition's `TemplateDefinition`, content kind 6 (DES-0021): its envelope,
canonical JSON, pack content and structural admission.

A template pins one published Layout page surface with intent `issue` by exact identity and version
(ADR 0094: the legacy flat block list is replaced by the Layout tree, not migrated). This package never
references Layout's types: the host supplies `TemplateSurfaces`, which resolves the pin from the shared
builder-definitions catalogue and runs Layout's own validator, whose schema owns the numeric ranges.

Templates are stored in the shared catalogue; this package owns no store. Tenant and provenance are
server-derived: exported pack content never carries them, and installation stamps them.
