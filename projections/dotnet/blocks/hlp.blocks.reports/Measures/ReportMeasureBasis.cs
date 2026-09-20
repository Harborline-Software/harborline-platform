using Harborline.Blocks.MeasureCatalogue;
using Harborline.Blocks.Reports.Inputs;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Crypto;

namespace Harborline.Blocks.Reports.Measures;

/// <summary>
/// The caller's report row basis. Reports still owns the basis, the period and the run envelope;
/// the catalogue reads through this and owns none of them.
/// </summary>
/// <param name="Token">The snapshot marker this evaluation is pinned to.</param>
/// <param name="Source">The caller's read boundary over its own rows.</param>
/// <param name="Tenant">The tenant scope the caller resolved.</param>
/// <param name="RequestedBy">The principal the caller resolved.</param>
public sealed record ReportMeasureBasis(
    string Token,
    IReportQuerySource Source,
    TenantId Tenant,
    PrincipalId RequestedBy) : IMeasureBasis;
