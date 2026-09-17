# Harborline Foundation Data Exchange

Owns the platform-neutral, inbound-only Data Exchange contracts: the constrained CSVW mapping
profile, canonical Records target admission, immutable run evidence, stable replay identities, and
bounded per-command commit orchestration.

Hosts supply trusted proposal evaluation, source outcome policy, lifecycle policy, canonical target
registry, Access, command, evidence and checkpoint adapters. Commit reevaluates semantic dependencies;
a changed batch preimage persists a linked superseding dry run and refuses the old approval.
Batch v2 includes mapping profile/document, connector resolution, transform/lookup versions, matching
inputs and selected boundary. AttemptId is never semantic. Existing v1 ledger keys are not v2 keys;
hosts must re-review old evidence when adopting this public contract revision.

DryRunRequest cites a string retention class and accepts no RetainUntil override. Lifecycle policy
derives retention for each run. Disposition eligibility checks both recorded and current legal hold;
a host that deletes evidence must recheck policy atomically with deletion.

ICanonicalTargetCommandPort is the sole target execution boundary. Every execute/correct-and-record
call receives tenant, actor and authorization context. The target independently authorizes, validates,
audits and atomically commits its effect and outcome in its own transaction. It also owns idempotency
and durable outcome lookup, including after a response is lost. Data Exchange has no separate outcome
write. Domain-owned forward correction is policy-selected; rollback and multi-command atomic requests
are explicitly refused. Production adapters are intentionally outside this foundation package.
