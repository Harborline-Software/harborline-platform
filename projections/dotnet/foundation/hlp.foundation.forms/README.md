# Harborline.Foundation.Forms

Complete caller-backed Dynamic Forms state for atomic creation and idempotent register-and-publish, immutable tenant-scoped definitions,
lossless Harborline App authoring metadata, lifecycle versions (a save mints the next version and a restore registers an old version as a new Draft, publishing nothing), bounded reuse-by-reference, atomically
authenticated form-bound save/resume drafts, and short-lived pre-auth capture promotion or purge.
Publication resolves a submit gate against the host's registers: the capability arm through Access's capability register, the role arm
through Access's role vocabulary and the standing arm through the standings installed standing rules declare; publication refuses an arm whose
register the host did not supply.
Executable render/validate/submit orchestration remains in the separate Forms Engine module.
