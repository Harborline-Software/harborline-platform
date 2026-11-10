using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The ADR-0097 platform seed fixture, excluding the two still-open DES-0007 boundaries.</summary>
public static class PlatformPackageSeed
{
    private const string ResourceName = "Harborline.Blocks.BuilderDefinitions.platform-pack.export.json";
    // SemVer 2.0.0 item 8: 2.0.0 removes the published authoring, dataExchangeAuthoring and schedulingAuthoring members
    // (owner ruling 2026-11-04). It writes the ck-14 creationGate form_only (K6); 1.0.0 survives only as a test fixture.
    private const string PackageKey = "harborline.platform";
    private const string PackageRevision = "2.0.0";
    private const string Workshop = "platform.workspace.workshop";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// The identity of the seed's one <see cref="ReleasedNavigationDefinition"/> (DES-0052 layout-eng-24), and the id of
    /// the seed item that carries it.
    /// </summary>
    public const string ReleasedNavigationIdentity = "platform.navigation.workshop";

    /// <summary>The Workshop editor destinations and their navigation entries, released at the seed revision.</summary>
    public static ReleasedNavigationDefinition ReleasedNavigation { get; } = BuildReleasedNavigation();

    // Declared after ReleasedNavigation: static initializers run in text order, and the manifest exports it.
    private static readonly PlatformPackageManifest CanonicalManifest = BuildManifest();

    /// <summary>The canonical dependency-free platform package manifest.</summary>
    public static PlatformPackageManifest Manifest => CanonicalManifest;

    /// <summary>Reads the declared contract window from the checked-in canonical export.</summary>
    public static DefinitionContractWindow ContractWindow => ReadContractWindow(LoadCheckedInExport());

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

    /// <summary>
    /// Reads the record creation default (ck-14 <c>creationGate</c>) from the checked-in export through its
    /// version-specific reader (K6), which reads 1.0.0's <c>form-only</c> and every later revision's <c>form_only</c> as
    /// <see cref="RecordCreationGate.FormOnly"/>.
    /// </summary>
    public static RecordCreationGate CreationGate => PlatformCreationGateCompatibility.ReadCreationGate(LoadCheckedInExport());

    /// <summary>Reads and validates the ck-1 contract declaration from a platform package export.</summary>
    public static DefinitionContractWindow ReadContractWindow(ReadOnlySpan<byte> export)
    {
        try
        {
            using var document = JsonDocument.Parse(export.ToArray());
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw MalformedContract();
            }

            var packageItems = items.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == "platform-package-ck-1")
                .ToArray();
            if (packageItems.Length != 1
                || !packageItems[0].TryGetProperty("content", out var content)
                || !content.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("contract", out var contract)
                || contract.ValueKind != JsonValueKind.Object
                || !contract.TryGetProperty("major", out var majorElement)
                || !majorElement.TryGetInt32(out var major)
                || !contract.TryGetProperty("minor", out var minorElement)
                || !minorElement.TryGetInt32(out var minor)
                || !contract.TryGetProperty("window", out var windowElement)
                || windowElement.ValueKind != JsonValueKind.Array)
            {
                throw MalformedContract();
            }

            var window = windowElement.EnumerateArray().ToArray();
            if (window.Length != 2
                || !window[0].TryGetInt32(out var oldestMajor)
                || !window[1].TryGetInt32(out var newestMajor)
                || major < 0
                || minor < 0
                || oldestMajor < 0
                || newestMajor < 0
                || newestMajor != major
                || oldestMajor > newestMajor
                || (major >= 1 && oldestMajor < 1))
            {
                throw MalformedContract();
            }

            return new DefinitionContractWindow(major, minor, oldestMajor);
        }
        catch (JsonException)
        {
            throw MalformedContract();
        }
        catch (InvalidOperationException)
        {
            throw MalformedContract();
        }
    }

    private static InvalidDataException MalformedContract() => new("platform-package-contract-malformed");

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
            labelKey = $"workshop.{pillar}",
        }).ToArray();
        // Complete bodies from harborline-api 0a7a163c, platform pack 1.6.1 (T-670).
        var viewBodies = JsonNode.Parse("""
            {"platform.list.asset-types":{"key":"platform.list.asset-types","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Record types","parameters":{"entityType":"AssetTypeDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.forms":{"key":"platform.list.forms","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Forms","parameters":{"entityType":"FormDefinition","fields":[{"id":"formId","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"cascadeLayer","label":"Cascade layer"}],"actions":[{"id":"author","label":"Author and validate pack","operation":"pack.validate","inputForm":"platform.pack.author"},{"id":"export","label":"Export signed pack","operation":"pack.export"},{"id":"verify","label":"Verify pack","operation":"pack.verify"},{"id":"install","label":"Install pack","operation":"pack.install"},{"id":"activate","label":"Activate pack","operation":"pack.activate"},{"id":"create","label":"Create record","operation":"record.create","input":"active-pack.property-form"},{"id":"read","label":"Read record","operation":"record.read"}]}},"platform.list.workflows":{"key":"platform.list.workflows","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Workflows","parameters":{"entityType":"WorkflowDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.standards":{"key":"platform.list.standards","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Standards","parameters":{"entityType":"StandardsCatalog","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.defaults":{"key":"platform.list.defaults","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Defaults","parameters":{"entityType":"CascadeDefaults","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.terminology":{"key":"platform.list.terminology","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Terminology","parameters":{"entityType":"TerminologyOverride","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.documents":{"key":"platform.list.documents","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Documents","parameters":{"entityType":"TemplateDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.taxonomies":{"key":"platform.list.taxonomies","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Taxonomies","parameters":{"entityType":"TaxonomyDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.reports":{"key":"platform.list.reports","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Reports","parameters":{"entityType":"ReportDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.data-exchanges":{"key":"platform.list.data-exchanges","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Data exchanges","parameters":{"entityType":"DataExchangeDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.standing-rules":{"key":"platform.list.standing-rules","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Standing rules","parameters":{"entityType":"StandingRuleDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.schedules":{"key":"platform.list.schedules","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Schedules","parameters":{"entityType":"ScheduleDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.list.views":{"key":"platform.list.views","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Views","parameters":{"entityType":"ViewDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.asset-types":{"key":"platform.health.asset-types","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Record type health","parameters":{"entityType":"AssetTypeDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.forms":{"key":"platform.health.forms","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Form health","parameters":{"entityType":"FormDefinition","fields":[{"id":"formId","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"cascadeLayer","label":"Cascade layer"}]}},"platform.health.workflows":{"key":"platform.health.workflows","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Workflow health","parameters":{"entityType":"WorkflowDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.standards":{"key":"platform.health.standards","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Standards health","parameters":{"entityType":"StandardsCatalog","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.defaults":{"key":"platform.health.defaults","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Defaults health","parameters":{"entityType":"CascadeDefaults","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.terminology":{"key":"platform.health.terminology","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Terminology health","parameters":{"entityType":"TerminologyOverride","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.documents":{"key":"platform.health.documents","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Document health","parameters":{"entityType":"TemplateDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.taxonomies":{"key":"platform.health.taxonomies","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Taxonomy health","parameters":{"entityType":"TaxonomyDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.reports":{"key":"platform.health.reports","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Report health","parameters":{"entityType":"ReportDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.data-exchanges":{"key":"platform.health.data-exchanges","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Data exchange health","parameters":{"entityType":"DataExchangeDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.standing-rules":{"key":"platform.health.standing-rules","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Standing rule health","parameters":{"entityType":"StandingRuleDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.schedules":{"key":"platform.health.schedules","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Schedule health","parameters":{"entityType":"ScheduleDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.health.views":{"key":"platform.health.views","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"View health","parameters":{"entityType":"ViewDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.asset-types":{"key":"platform.browse.asset-types","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse record types","parameters":{"entityType":"AssetTypeDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.forms":{"key":"platform.browse.forms","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse forms","parameters":{"entityType":"FormDefinition","fields":[{"id":"formId","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"cascadeLayer","label":"Cascade layer"}],"actions":[{"id":"author","label":"Author and validate pack","operation":"pack.validate","inputForm":"platform.pack.author"},{"id":"export","label":"Export signed pack","operation":"pack.export"},{"id":"verify","label":"Verify pack","operation":"pack.verify"},{"id":"install","label":"Install pack","operation":"pack.install"},{"id":"activate","label":"Activate pack","operation":"pack.activate"},{"id":"create","label":"Create record","operation":"record.create","input":"active-pack.property-form"},{"id":"read","label":"Read record","operation":"record.read"}]}},"platform.browse.workflows":{"key":"platform.browse.workflows","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse workflows","parameters":{"entityType":"WorkflowDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.standards":{"key":"platform.browse.standards","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse standards","parameters":{"entityType":"StandardsCatalog","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.defaults":{"key":"platform.browse.defaults","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse defaults","parameters":{"entityType":"CascadeDefaults","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.terminology":{"key":"platform.browse.terminology","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse terminology","parameters":{"entityType":"TerminologyOverride","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.documents":{"key":"platform.browse.documents","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse documents","parameters":{"entityType":"TemplateDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.taxonomies":{"key":"platform.browse.taxonomies","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse taxonomies","parameters":{"entityType":"TaxonomyDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.reports":{"key":"platform.browse.reports","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse reports","parameters":{"entityType":"ReportDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.data-exchanges":{"key":"platform.browse.data-exchanges","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse data exchanges","parameters":{"entityType":"DataExchangeDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.standing-rules":{"key":"platform.browse.standing-rules","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse standing rules","parameters":{"entityType":"StandingRuleDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.schedules":{"key":"platform.browse.schedules","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse schedules","parameters":{"entityType":"ScheduleDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}},"platform.browse.views":{"key":"platform.browse.views","version":"1.0.0","tenant":"bootstrap","schemaVersion":1,"viewKind":"layout.table","title":"Browse views","parameters":{"entityType":"ViewDefinition","fields":[{"id":"id","label":"Key"},{"id":"title","label":"Title"},{"id":"version","label":"Version"},{"id":"status","label":"Status"}]}}}
            """)!;
        var pillarViews = pillars.SelectMany(pillar => new[] { "list", "health", "browse" }.Select(kind => new
        {
            id = $"platform.{kind}.{pillar}",
            pillar,
            kind,
            shape = "table",
            body = viewBodies[$"platform.{kind}.{pillar}"],
        })).ToArray();

        return new PlatformPackageManifest(1, PackageKey, PackageRevision, new[]
        {
            Item("platform-package-ck-1", PlatformSeedStage.PackageRecord, new
            {
                id = PackageKey,
                provenance = new { kind = "platform" },
                version = PackageRevision,
                contract = new { major = 1, minor = 0, window = new[] { 1, 1 } },
            }),
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
            Item("platform-package-ck-5", PlatformSeedStage.Workspace, new { id = Workshop, name = "Workshop", labelKey = "workshop.workspace" }, "platform-package-ck-4"),
            Item("platform-package-ck-6", PlatformSeedStage.Navigation, new { members = navigation }, "platform-package-ck-5"),
            // The workspace and its thirteen entries stay at ck-5 and ck-6 (ADR-0102 decision 5). This item names them
            // and adds what neither carries: the one group and the inspector panel set.
            Definition("platform.workshop", PlatformSeedStage.Navigation, "NavWorkspaceConfig", """
                {"seedWorkspaces":[{"id":"workshop","workspaceItem":"platform-package-ck-5","groups":[{"id":"definitions","labelKey":"workshop.definitions","entriesItem":"platform-package-ck-6"}]}],
                 "panelSet":[{"id":"inspector","binding":"panels.inspector.toggle","shortcut":"mod+shift+i","defaultWidth":360,"minimumHeight":180,"defaultOpen":false}]}
                """, "platform-package-ck-6"),
            new PlatformPackageItem(ReleasedNavigationIdentity, PlatformSeedStage.Navigation, ["platform-package-ck-6"],
                ReleasedNavigationDefinitionPackageExporter.Export(ReleasedNavigation).Content),
            Item("platform-package-ck-7", PlatformSeedStage.Views, new
            {
                members = pillarViews,
                configurationGenerationDetail = ConfigurationGenerationDetail.Definition,
                configurationActivationDetail = ConfigurationActivationDetail.Definition,
                configurationActivationStatuses = ConfigurationActivationDetail.Statuses,
                configurationProposalDetail = ConfigurationProposalDetail.Definition,
                configurationProposalStatuses = ConfigurationProposalDetail.Statuses,
                verificationRunDetail = VerificationDetail.Definition,
                verificationRunStatuses = VerificationDetail.Statuses,
                verificationCatalogue = new
                {
                    key = VerificationCatalog.Reference.Key,
                    revision = VerificationCatalog.Reference.Revision,
                    digest = VerificationCatalog.Reference.Digest,
                    actions = VerificationCatalog.Actions.Select(action => new
                    {
                        actionId = action.ActionId, version = action.Version,
                        parameters = action.Parameters, channels = action.Channels,
                    }),
                    predicates = VerificationCatalog.Predicates.Select(predicate => new
                    {
                        predicateId = predicate.PredicateId, version = predicate.Version, channel = predicate.Channel,
                        expects = predicate.Expects.ToString(), requiresTarget = predicate.RequiresTarget,
                    }),
                },
                listDefaultShape = "table",
                showInListsOwner = "views",
                // What the released navigation definition does not carry, each at a named home (owner ruling
                // 2026-11-04). DataExchange's TabularMappingProfile.Family names the mapping profile's family.
                dataExchangeMappingProfile = new
                {
                    id = "hl:tabular-mapping/v1",
                    schemaUri = "https://schemas.harborline.software/mapping/tabular/v1",
                    documentVersion = "1.0.0",
                },
                schedulingCreateActions = new[]
                {
                    new { id = "platform.action.schedules.create", kind = "schedule", definitionKind = "ScheduleDefinition" },
                    new { id = "platform.action.protocols.create", kind = "protocol", definitionKind = "Protocol" },
                    new { id = "platform.action.constraints.create", kind = "constraint", definitionKind = "Constraint" },
                },
                // Scheduling editor defaults the records rule on, and nothing else: problem class and planner start
                // at none (scheduling-ck-10, L1149), the plan horizon has no default and is authored
                // (ADR-0097 ruling 5), no constraint scope is preselected and chain is never chosen
                // implicitly (L1148), and Protocol instances are domain-package-owned with none in the
                // platform seed (ADR-0097 ruling 3). The grammar itself is the definition kinds' own.
                schedulingEditorDefaults = new
                {
                    problemClass = "None",
                    planner = "none",
                    planHorizon = (string?)null,
                    constraintScopes = Array.Empty<string>(),
                    protocolInstanceOwner = "domain-package",
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
            // Form text is the public wire shape, {defaultLocale, values}, that the api's ValidateReleasedFormText requires.
            Definition("platform.detail.form", PlatformSeedStage.SealedDefinitions, "FormDefinition", """
                {"catalogueFieldSource":{"capabilityId":"forms.catalogue-field-source","coordinateSchemaVersion":1,"sourceMappingSchemaVersion":1,"sourceKind":"FormDefinition","fields":[{"fieldId":"formId","source":"catalogue.entry.formId"},{"fieldId":"title","source":"catalogue.entry.title"},{"fieldId":"version","source":"catalogue.entry.version"},{"fieldId":"cascadeLayer","source":"catalogue.entry.cascadeLayer"}]},"overlay":{"title":{"defaultLocale":"en","values":{"en":"Form details"}},"fields":{"formId":{"label":{"defaultLocale":"en","values":{"en":"Form ID"}},"controlHint":"text","readOnly":true,"piiSensitivity":"None"},"title":{"label":{"defaultLocale":"en","values":{"en":"Title"}},"controlHint":"text","readOnly":true,"piiSensitivity":"None"},"version":{"label":{"defaultLocale":"en","values":{"en":"Version"}},"controlHint":"text","readOnly":true,"piiSensitivity":"None"},"cascadeLayer":{"label":{"defaultLocale":"en","values":{"en":"Cascade layer"}},"controlHint":"text","readOnly":true,"piiSensitivity":"None"}},"sections":[{"id":"details","title":{"defaultLocale":"en","values":{"en":"Details"}},"fields":["formId","title","version","cascadeLayer"]}],"rules":[]},"fieldsMeta":{"formId":{"type":"text","required":false},"title":{"type":"text","required":false},"version":{"type":"text","required":false},"cascadeLayer":{"type":"text","required":false}}}
                """, "platform-package-ck-2"),
            Definition("platform.pack.author", PlatformSeedStage.SealedDefinitions, "FormDefinition", """
                {"overlay":{"title":{"defaultLocale":"en","values":{"en":"Author a domain pack"}},"description":{"defaultLocale":"en","values":{"en":"Enter a pack export document containing a record type and its bound form, then validate it."}},"fields":{"packJson":{"label":{"defaultLocale":"en","values":{"en":"Pack document"}},"controlHint":"textarea","piiSensitivity":"None"}},"sections":[{"id":"document","title":{"defaultLocale":"en","values":{"en":"Pack"}},"fields":["packJson"]}],"rules":[]},"fieldsMeta":{"packJson":{"type":"textarea","required":true}}}
                """, "platform-package-ck-2"),
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
                creationGate = PlatformCreationGateCompatibility.SeedSpelling(PackageRevision),
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
            Definition("platform.defaults.pack-author", PlatformSeedStage.GovernanceDefaults, "CascadeDefaults", """
                {"schemaVersion":1,"title":"Pack author change tracking","defaults":[{"recordType":"platform.pack.author","trackChanges":true}]}
                """, "platform.pack.author"),
        })
        {
            // platform.detail.form opts in to catalogue fields, so the pack must declare the capability exactly once
            // (harborline-api packages/foundation-forms/Contracts/catalogue-field-source-v1.md, "Producer declaration and
            // exact member locations"). A body's capabilityId does not stand in for it.
            CapabilityRequirements = ["forms.catalogue-field-source"],
        };
    }

    // The three Workshop editors the 1.0.0 seed declared in ck-7's authoring objects, with the same ids and labels.
    // platform.editor.views stays the surface id: app links use ?surface=platform.editor.views.
    // Rules is the fourth (T-592, DES-0018 rules-eng-24): released here, and kept hidden by both app lanes until Rules
    // transport ships (ADR 0005, ADR-0079).
    private static ReleasedNavigationDefinition BuildReleasedNavigation()
    {
        string[] lanes = ["react", "blazor"];
        return new(ReleasedNavigationIdentity, PackageRevision, Workshop,
            new("platform.surface-catalogue.workshop",
            [
                new("platform.editor.views", "Views editor"),
                new("platform.editor.data-exchange", "Data exchange editor"),
                new("platform.editor.scheduling", "Scheduling editor"),
                new("platform.editor.rules", "Rules editor"),
            ]),
            [
                new("platform.navigation.views.author", "Create view", "views", "platform.editor.views"),
                new("platform.navigation.data-exchanges.author", "Create data exchange", "data-exchanges", "platform.editor.data-exchange"),
                new("platform.navigation.schedules.author", "Create schedule", "schedules", "platform.editor.scheduling"),
                new("platform.navigation.standards.author", "Create rule", "standards", "platform.editor.rules"),
            ],
            [
                new("platform.editor.views", "platform.editor.views", ["ViewDefinition"], lanes),
                new("platform.editor.data-exchange", "platform.editor.data-exchange", ["DataExchangeDefinition"], lanes),
                new("platform.editor.scheduling", "platform.editor.scheduling", ["ScheduleDefinition", "Protocol", "Constraint"], lanes),
                new("platform.editor.rules", "platform.editor.rules", [nameof(DefinitionKind.Rules)], lanes),
            ]);
    }

    // T-667 route 1's item shape: the producer states the transport's content kind by name, and the api converts mechanically.
    private static PlatformPackageItem Definition(string key, PlatformSeedStage stage, string contentKind, string body, params string[] dependencies)
        => Item(key, stage, new { definitionKey = key, packageKey = PackageKey, contentKind, body = JsonNode.Parse(body) }, dependencies);

    private static PlatformPackageItem Item(string id, PlatformSeedStage stage, object payload, params string[] dependencies)
        => new(id, stage, dependencies, PlatformPackageContent.PresentJson(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));
}
