using System.Text.Json.Nodes;

namespace Harborline.Foundation.Authorization;

/// <summary>Record facts supplied by the tenant-bound record reader, never ambient state.</summary>
public sealed record AccessRecord(string Tenant, string Kind, string Id, IReadOnlyDictionary<string, JsonNode?> Fields);

/// <summary>The complete point-of-use request forwarded to the host's existing authorization gate.</summary>
public sealed record AccessRequest(string Operation, string Principal, string Tenant, AccessRecord Record, DateTimeOffset At);

/// <summary>A scope or row-check result with a stable public reason.</summary>
public sealed record AccessCheck(bool Allowed, string Reason);
