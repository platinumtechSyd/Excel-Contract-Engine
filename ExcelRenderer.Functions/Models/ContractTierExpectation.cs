namespace ExcelRenderer.Functions.Models;

/// <summary>Which inner contract shape Rewst (or callers) expect before normalization.</summary>
public enum ContractTierExpectation
{
    /// <summary>Accept tier 1 if root has <c>workbook</c>, else tier 2 if root has <c>sheets</c>.</summary>
    Any = 0,

    /// <summary>Require root <c>workbook</c> (tier 1).</summary>
    Tier1Workbook = 1,

    /// <summary>Require root <c>sheets</c> array (tier 2).</summary>
    Tier2Sheets = 2
}
