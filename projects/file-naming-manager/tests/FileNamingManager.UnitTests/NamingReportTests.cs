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

    /// <summary>
    /// A drawing follows its model's number, so a companion pair is never a second owner of it and raises
    /// no DuplicateNumber finding. But a lone drawing at 0080 still occupies 0080 in the real folder tree:
    /// it raises the series maximum like any numbered file, so the SequenceGap finding must stop before
    /// 80 rather than list 80 itself as missing.
    /// </summary>
    [Fact]
    public void BuildReportsNoDuplicateForACompanionDrawingAndNoGapAtALoneDrawingsNumber()
    {
        List<ParsedFileName> scope =
        [
            FileNameParser.Parse("124-0001 Foo.ipt"),
            FileNameParser.Parse("124-0001 Foo.idw"),
            FileNameParser.Parse("124-0080 X.idw"),
        ];

        NamingReport report = NamingReport.Build(scope, Project124);

        Assert.DoesNotContain(report.ProjectFindings, f => f.Code == FindingCode.DuplicateNumber);

        NamingFinding gapFinding = Assert.Single(report.ProjectFindings, f => f.Code == FindingCode.SequenceGap);
        Assert.DoesNotContain("80", gapFinding.Message);
    }

    [Fact]
    public void BuildEmptyScopeProducesEmptyReport()
    {
        NamingReport report = NamingReport.Build([], Project124);

        Assert.Empty(report.Rows);
        Assert.Empty(report.ProjectFindings);
    }
}
