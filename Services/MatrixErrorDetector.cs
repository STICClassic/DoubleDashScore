namespace DoubleDashScore.Services;

public static class MatrixErrorDetector
{
    public sealed record MatrixCells(int First, int Second, int Third, int Fourth);

    public sealed record CellErrors(bool First, bool Second, bool Third, bool Fourth)
    {
        public static readonly CellErrors None = new(false, false, false, false);
    }

    public static IReadOnlyList<CellErrors> Detect(
        IReadOnlyList<MatrixCells> players,
        int expectedTracks)
    {
        var n = players.Count;
        var none = Enumerable.Repeat(CellErrors.None, n).ToList();

        if (expectedTracks <= 0) return none;

        var columnsWithData = players.Count(p =>
            p.First != 0 || p.Second != 0 || p.Third != 0 || p.Fourth != 0);
        if (columnsWithData < 2) return none;

        var badCols = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var sum = players[i].First + players[i].Second + players[i].Third + players[i].Fourth;
            badCols[i] = sum != expectedTracks;
        }

        var firstSum = players.Sum(p => p.First);
        var secondSum = players.Sum(p => p.Second);
        var thirdSum = players.Sum(p => p.Third);
        var fourthSum = players.Sum(p => p.Fourth);
        var badFirstRow = firstSum != expectedTracks;
        var badSecondRow = secondSum != expectedTracks;
        var badThirdRow = thirdSum != expectedTracks;
        var badFourthRow = fourthSum != expectedTracks;

        var result = new List<CellErrors>(n);
        for (int i = 0; i < n; i++)
        {
            result.Add(new CellErrors(
                First: badCols[i] && badFirstRow,
                Second: badCols[i] && badSecondRow,
                Third: badCols[i] && badThirdRow,
                Fourth: badCols[i] && badFourthRow));
        }
        return result;
    }
}
