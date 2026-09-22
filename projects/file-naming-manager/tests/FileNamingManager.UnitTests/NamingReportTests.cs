// Purpose: Verify NamingReport aggregates DuplicateNumber, SequenceGap, and DuplicateFileName findings.
// Inputs: Synthetic parsed-file-name scopes built directly from FileNameParser.Parse.
// Outputs: Assertions on NamingReport.ProjectFindings and Rows.
// Dependencies: FileNamingManager.Core.
// Assumptions: None.
// Validation source: .work/TASK.md "Findings the analyzer must report" paragraph.

using FileNamingManager.Core;

namespace FileNamingManager.UnitTests;

public class NamingReportTests
{
    private static readonly ProjectNumber Project124 = new(124);

    [Fact]
    public void BuildReportsDuplicateNumberAndSequenceGap()
    {
        List<ParsedFileName> scope =
        [
            FileNameParser.Parse("124-A004 Electronics Box Triple Wake_Up (sub-assembly).iam"),
            FileNameParser.Parse("124-A004 Fridge (sub-assembly).iam"),
            FileNameParser.Parse("124-0001 First.ipt"),
            FileNameParser.Parse("124-0003 Third.ipt"),
        ];

        NamingReport report = NamingReport.Build(scope, Project124);

        Assert.Contains(report.ProjectFindings, f => f.Code == FindingCode.DuplicateNumber);
        Assert.Contains(report.ProjectFindings, f => f.Code == FindingCode.SequenceGap);
        Assert.Equal(4, report.Rows.Count);
    }

    [Fact]
    public void BuildReportsDuplicateFileNameAcrossScope()
    {
        List<ParsedFileName> scope =
        [
            FileNameParser.Parse("124-0001 First.ipt"),
            FileNameParser.Parse("124-0001 First.ipt"),
        ];

        NamingReport report = NamingReport.Build(scope, Project124);

        NamingFinding finding = Assert.Single(report.ProjectFindings, f => f.Code == FindingCode.DuplicateFileName);
        Assert.Equal(2, finding.FileNames.Count);
    }

    [Fact]
    public void BuildCanonicalScopeWithNoIssuesHasNoProjectFindings()
    {
        List<ParsedFileName> scope =
        [
            FileNameParser.Parse("124-0001 First.ipt"),
            FileNameParser.Parse("124-0002 Second.ipt"),
        ];

        NamingReport report = NamingReport.Build(scope, Project124);

        Assert.Empty(report.ProjectFindings);
    }

    [Fact]
    public void BuildEmptyScopeProducesEmptyReport()
    {
        NamingReport report = NamingReport.Build([], Project124);

        Assert.Empty(report.Rows);
        Assert.Empty(report.ProjectFindings);
    }
}
