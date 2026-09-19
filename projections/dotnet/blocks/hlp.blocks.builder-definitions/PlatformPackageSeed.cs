using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The ADR-0097 platform seed fixture, excluding the two still-open DES-0007 boundaries.</summary>
public static class PlatformPackageSeed
{
    private const string ResourceName = "Harborline.Blocks.BuilderDefinitions.platform-pack.export.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly PlatformPackageManifest CanonicalManifest = BuildManifest();

    /// <summary>The canonical dependency-free platform package manifest.</summary>
    public static PlatformPackageManifest Manifest => CanonicalManifest;

    /// <summary>Exports the canonical provider-neutral closure, manifest, and digest document.</summary>
    public static byte[] Export() => PlatformPackageExporter.Export(CanonicalManifest);

    /// <summary>Loads the checked-in canonical export embedded in the package assembly.</summary>
    public static byte[] LoadCheckedInExport()
    {
        using var stream = typeof(PlatformPackageSeed).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("platform-seed-export-resource-missing");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Reports whether the embedded checked-in document is the byte-identical canonical export.</summary>
    public static bool VerifyCheckedInExport() => PlatformPackageExporter.Verify(CanonicalManifest, LoadCheckedInExport());

    private static PlatformPackageManifest BuildManifest()
    {
        var policies = new { retention = "unset-warning", visibility = "tenant-authority", history = "enabled", amendment = "reason-required" };
        var definitionTypes = new[]
        {
            "definition-package", "record-type", "field", "form", "question", "workflow", "state", "transition",
            "automation", "rule", "calculation", "view", "report", "navigation-entry", "workspace", "taxonomy", "term",
            "document-template", "exchange", "setting", "schedule", "protocol", "constraint", "resource", "bookable",
            "allocation", "role", "role-grant", "capability", "standing-rule", "trait", "class", "definition-dependency",
            "legality-rule-set",
        }.Select(id => new { id = $"platform.definition-type.{id}", sealedType = true, provenance = new { kind = "platform" }, policies }).ToArray();
        var operationalTypes = new object[]
        {
            new { id = "platform.operational-type.work-item", sealedType = true, provenance = new { kind = "platform" }, policies = (object)policies },
            new { id = "platform.operational-type.notification", sealedType = true, provenance = new { kind = "platform" }, policies = (object)policies },
            new { id = "platform.operational-type.observation", sealedType = true, provenance = new { kind = "platform" }, policies = (object)policies },
            new
            {
                id = "platform.operational-type.audit-entry",
                sealedType = true,
                provenance = new { kind = "platform" },
                policies = (object)new
                {
                    retention = new { owner = "tenant", defaultValue = (string?)null, publication = "refuse-until-declared" },
                    visibility = "tenant-authority",
                    history = "enabled",
                    amendment = "reason-required",
                },
            },
        };
        var pillars = new[]
        {
            "asset-types", "forms", "workflows", "standards", "defaults", "terminology", "documents", "taxonomies",
            "reports", "data-exchanges", "standing-rules", "schedules", "views",
        };
        var navigation = pillars.Select((pillar, index) => new
        {
            id = pillar,
            order = index + 1,
            pillar,
        }).ToArray();
        var pillarViews = pillars.SelectMany(pillar => new[] { "list", "health", "browse" }.Select(kind => new
        {
            id = kind == "list" ? $"platform.list.{pillar}" : $"platform.{kind}.{pillar}",
            pillar,
            kind,
            shape = "table",
        })).ToArray();

        return new PlatformPackageManifest(1, "harborline.platform", "1.0.0", new[]
        {
            Item("platform-package-ck-1", PlatformSeedStage.PackageRecord, new { id = "harborline.platform", provenance = new { kind = "platform" }, version = "1.0.0" }),
            Item("platform-package-ck-2", PlatformSeedStage.SystemRecordTypes, new { members = definitionTypes }, "platform-package-ck-1"),
            Item("platform-package-ck-3", PlatformSeedStage.SystemRecordTypes, new { members = operationalTypes }, "platform-package-ck-2"),
            Item("platform-package-ck-4", PlatformSeedStage.SystemRecordTypes, new
            {
                members = new object[]
                {
                    new { id = "platform.trait.schedulable", sealedTrait = true, slots = new[] { "work_calendar", "qualification_set", "capacity_slots" } },
                    new { id = "platform.trait.bookable-resource", sealedTrait = true, slots = Array.Empty<string>() },
                },
            }, "platform-package-ck-3"),
            Item("platform-package-ck-5", PlatformSeedStage.Workspace, new { id = "platform.workspace.workshop", name = "Workshop" }, "platform-package-ck-4"),
            Item("platform-package-ck-6", PlatformSeedStage.Navigation, new { members = navigation }, "platform-package-ck-5"),
            Item("platform-package-ck-7", PlatformSeedStage.Views, new
            {
                members = pillarViews,
                configurationGenerationDetail = ConfigurationGenerationDetail.Definition,
                listDefaultShape = "table",
                showInListsOwner = "views",
                authoring = new
                {
                    workspace = "platform.workspace.workshop",
                    navigationEntry = new
                    {
                        id = "platform.navigation.views.author",
                        pillar = "views",
                        label = "Create view",
                        surface = "platform.editor.views",
                    },
                    editor = new
                    {
                        id = "platform.editor.views",
                        definitionKind = "ViewDefinition",
                        projections = new[] { "react", "blazor" },
                    },
                },
                dataExchangeAuthoring = new
                {
                    workspace = "platform.workspace.workshop",
                    navigationEntry = new
                    {
                        id = "platform.navigation.data-exchanges.author",
                        pillar = "data-exchanges",
                        label = "Create data exchange",
                        surface = "platform.editor.data-exchange",
                    },
                    editor = new
                    {
                        id = "platform.editor.data-exchange",
                        definitionKind = "DataExchangeDefinition",
                        projections = new[] { "react", "blazor" },
                    },
                    mappingProfile = new
                    {
                        id = "hl:tabular-mapping/v1",
                        schemaUri = "https://schemas.harborline.software/mapping/tabular/v1",
                        documentVersion = "1.0.0",
                    },
                },
            }, "platform-package-ck-6"),
            Item("platform-package-ck-8", PlatformSeedStage.Views, new
            {
                members = new[]
                {
                    new { id = "platform.view.catalogue.dependents", purpose = "who-depends-on-this" },
                    new { id = "platform.view.catalogue.impact", purpose = "impact" },
                    new { id = "platform.view.catalogue.closure", purpose = "closure" },
                },
            }, "platform-package-ck-7"),
            Item("platform-package-ck-9", PlatformSeedStage.AccessDefinitions, new
            {
                roles = new[]
                {
                    new { id = "platform.role.administrator", name = "Administrator" },
                    new { id = "platform.role.auditor", name = "Auditor" },
                },
                grants = Array.Empty<object>(),
            }, "platform-package-ck-8"),
            Item("platform-package-ck-10", PlatformSeedStage.AccessDefinitions, new
            {
                capabilities = new[] { "catalogue:read", "records:read", "audit:read" },
                bindings = new[]
                {
                    new { id = "platform.binding.catalogue-read", operation = "catalogue:read", scope = "/", offeredRoles = new[] { "platform/administrator" } },
                    new { id = "platform.binding.records-read", operation = "records:read", scope = "/", offeredRoles = new[] { "platform/administrator" } },
                    new { id = "platform.binding.audit-read", operation = "audit:read", scope = "/", offeredRoles = new[] { "platform/auditor" } },
                },
                revokeGuard = "not_last_administrator()",
                unansweredGrantRequest = "refuse",
            }, "platform-package-ck-9"),
            Item("platform-package-ck-11", PlatformSeedStage.SealedDefinitions, new
            {
                id = "sys.sched.retention",
                sealedDefinition = true,
                cadence = "nightly",
                execution = "system-work-item",
                action = "dispose-per-type-policy",
            }, "platform-package-ck-10"),
            Item("platform-package-ck-12", PlatformSeedStage.SealedDefinitions, new { disposition = "none", protocols = Array.Empty<object>() }, "platform-package-ck-11"),
            Item("platform-package-ck-13", PlatformSeedStage.GovernanceDefaults, new
            {
                defaults = new object[]
                {
                    new { id = "platform.field-default.email", kinds = new[] { "email" }, personalData = true, confidentiality = "confidential" },
                    new { id = "platform.field-default.phone", kinds = new[] { "phone" }, personalData = true, confidentiality = "confidential" },
                    new { id = "platform.field-default.account-number", kinds = new[] { "account-number" }, masking = "masked" },
                    new { id = "platform.field-default.identifier", kinds = new[] { "identifier", "reference" }, rendering = "mono", personalData = false },
                    new { id = "platform.field-default.currency", kinds = new[] { "currency" }, rendering = "mono-tabular-locale-aware" },
                    new { id = "platform.field-default.attachment", kinds = new[] { "attachment" }, evidenceConditions = "available-unset" },
                    new { id = "platform.field-default.date", kinds = new[] { "date" }, validityWindow = "unset-unless-declared" },
                },
                application = "creation-time-editable",
            }, "platform-package-ck-12"),
            Item("platform-package-ck-14", PlatformSeedStage.GovernanceDefaults, new
            {
                creationGate = "form-only",
                uninterpretableRestriction = "refuse",
                unknownPermittingDefinition = "inert",
                unknownRestrictingDefinition = "refuse",
                offlineConflict = "ask-with-values-authors-instants",
                packageCollision = "list-all-no-default",
                constraintScopeDefaults = new[] { "resource", "occurrence" },
                chainScopeDefault = (string?)null,
                planner = "none",
                required = "unset-condition",
                amendment = "reason-required",
                history = "enabled",
                retention = "unset-warning",
                visibility = "tenant-authority",
                capacity = "exclusive",
                unboundedBookingRead = "400",
                planHorizonDefault = (string?)null,
                notificationChannel = "in-app",
                theme = "dark",
            }, "platform-package-ck-13"),
        });
    }

    private static PlatformPackageItem Item(string id, PlatformSeedStage stage, object payload, params string[] dependencies)
        => new(id, stage, dependencies, PlatformPackageContent.PresentJson(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));
}
