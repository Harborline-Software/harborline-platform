# Harborline Kernel Work Items

Local-shadow NuGet package for the common one-command request fence and tenant-scoped durable
work-item lifecycle. `CommandRequestBoundary` rejects multi-command bodies before dispatch and names
the observed count. The package also owns atomic snapshot,
event, idempotency receipt, audit, and outbox persistence behind `IWorkItemKernel`.

It intentionally excludes workflow-definition interpretation, application UI, and host-specific
domain effects. Public publication is not authorized.
