# Harborline Kernel Work Items

Local-shadow NuGet package for tenant-scoped durable work-item lifecycle. It owns atomic snapshot,
event, idempotency receipt, audit, and outbox persistence behind `IWorkItemKernel`.

It intentionally excludes workflow-definition interpretation, application UI, and host-specific
domain effects. Public publication is not authorized.
