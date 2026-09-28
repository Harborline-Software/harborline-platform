using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Foundation.Definitions;

/// <summary>A definition's declared application-contract version.</summary>
public sealed record DefinitionContractVersion(int Major, int Minor);

/// <summary>The application-contract version and the oldest major release it admits.</summary>
public sealed record DefinitionContractWindow(int Major, int Minor, int OldestMajor)
{
    /// <summary>Returns a refusal when <paramref name="declared"/> is absent, invalid, or outside this window.</summary>
    public DefinitionRefusal? Check(DefinitionContractVersion? declared, string? target)
    {
        if (declared is null || declared.Major < 0 || declared.Minor < 0)
        {
            return new DefinitionRefusal("definition.contract.missing", "/envelope/contract", target);
        }

        var outsideWindow = declared.Major > Major
            || declared.Major < OldestMajor
            || (declared.Major == Major && declared.Minor > Minor)
            || (Major == 0 && declared.Major == 0 && declared.Minor != Minor);

        return outsideWindow
            ? new DefinitionRefusal("definition.contract.out_of_window", "/envelope/contract", target)
            : null;
    }
}
