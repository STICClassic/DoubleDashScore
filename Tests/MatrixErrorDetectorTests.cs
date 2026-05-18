using DoubleDashScore.Services;
using Xunit;

namespace DoubleDashScore.Tests;

public class MatrixErrorDetectorTests
{
    private static IReadOnlyList<MatrixErrorDetector.MatrixCells> Players(
        (int a, int b, int c, int d) p1,
        (int a, int b, int c, int d) p2,
        (int a, int b, int c, int d) p3,
        (int a, int b, int c, int d) p4) =>
        new[]
        {
            new MatrixErrorDetector.MatrixCells(p1.a, p1.b, p1.c, p1.d),
            new MatrixErrorDetector.MatrixCells(p2.a, p2.b, p2.c, p2.d),
            new MatrixErrorDetector.MatrixCells(p3.a, p3.b, p3.c, p3.d),
            new MatrixErrorDetector.MatrixCells(p4.a, p4.b, p4.c, p4.d),
        };

    [Fact]
    public void Detect_AllCorrect_NoErrors()
    {
        var errors = MatrixErrorDetector.Detect(
            Players((4, 4, 4, 4), (4, 4, 4, 4), (4, 4, 4, 4), (4, 4, 4, 4)),
            expectedTracks: 16);

        Assert.All(errors, e => Assert.Equal(MatrixErrorDetector.CellErrors.None, e));
    }

    [Fact]
    public void Detect_OneWrongCell_MarksOnlyThatCell()
    {
        var errors = MatrixErrorDetector.Detect(
            Players((4, 4, 4, 4), (4, 4, 4, 2), (4, 4, 4, 4), (4, 4, 4, 4)),
            expectedTracks: 16);

        Assert.Equal(MatrixErrorDetector.CellErrors.None, errors[0]);
        Assert.Equal(new MatrixErrorDetector.CellErrors(false, false, false, true), errors[1]);
        Assert.Equal(MatrixErrorDetector.CellErrors.None, errors[2]);
        Assert.Equal(MatrixErrorDetector.CellErrors.None, errors[3]);
    }

    [Fact]
    public void Detect_TwoWrongColumnsTwoWrongRows_MarksIntersectionsOnly()
    {
        var errors = MatrixErrorDetector.Detect(
            Players((4, 4, 4, 4), (3, 4, 4, 4), (4, 4, 4, 3), (4, 4, 4, 4)),
            expectedTracks: 16);

        Assert.Equal(MatrixErrorDetector.CellErrors.None, errors[0]);
        Assert.Equal(new MatrixErrorDetector.CellErrors(true, false, false, true), errors[1]);
        Assert.Equal(new MatrixErrorDetector.CellErrors(true, false, false, true), errors[2]);
        Assert.Equal(MatrixErrorDetector.CellErrors.None, errors[3]);
    }

    [Fact]
    public void Detect_EmptyMatrix_NoErrors()
    {
        var errors = MatrixErrorDetector.Detect(
            Players((0, 0, 0, 0), (0, 0, 0, 0), (0, 0, 0, 0), (0, 0, 0, 0)),
            expectedTracks: 16);

        Assert.All(errors, e => Assert.Equal(MatrixErrorDetector.CellErrors.None, e));
    }

    [Fact]
    public void Detect_OnlyOneColumnFilled_NoErrors()
    {
        var errors = MatrixErrorDetector.Detect(
            Players((4, 4, 4, 4), (0, 0, 0, 0), (0, 0, 0, 0), (0, 0, 0, 0)),
            expectedTracks: 16);

        Assert.All(errors, e => Assert.Equal(MatrixErrorDetector.CellErrors.None, e));
    }

    [Fact]
    public void Detect_InvalidTrackCount_NoErrors()
    {
        var errors = MatrixErrorDetector.Detect(
            Players((4, 4, 4, 4), (4, 4, 4, 4), (4, 4, 4, 4), (4, 4, 4, 4)),
            expectedTracks: 0);

        Assert.All(errors, e => Assert.Equal(MatrixErrorDetector.CellErrors.None, e));
    }
}
