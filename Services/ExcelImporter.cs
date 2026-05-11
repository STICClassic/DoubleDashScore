using System.Globalization;
using ClosedXML.Excel;
using DoubleDashScore.Data;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class ExcelImporter
{
    private const int BlockStrideRows = 9;
    private const int NightBlockFirstRow = 1;

    private const string Section1ColLabel = "A";
    private const string Section1ColTotalTracks = "F";
    private static readonly string[] Section1PlayerCols = { "B", "C", "D", "E" };

    private const int Section2FirstRow = 55;
    private const int Section2LastRow = 99;
    private const int Section2FirstNightNumber = 35;
    private static readonly string[] Section2PlayerCols = { "V", "W", "X", "Y" };

    private const int Section3HeaderRow = 55;
    private const int Section3ColLabel = 27; // AA
    private static readonly int[] Section3PlayerColumns = { 28, 29, 30, 31 }; // AB, AC, AD, AE

    public static ParsedExcelImport Parse(Stream xlsxStream, IReadOnlyList<Player> activePlayers)
    {
        ArgumentNullException.ThrowIfNull(xlsxStream);
        ArgumentNullException.ThrowIfNull(activePlayers);
        if (activePlayers.Count != 4)
        {
            throw new InvalidOperationException(
                $"Förväntade 4 aktiva spelare, hittade {activePlayers.Count}.");
        }

        using var workbook = new XLWorkbook(xlsxStream);
        var ws = workbook.Worksheets.First();

        var excelNames = ReadExcelPlayerNames(ws);
        var colToPlayerId = MapColumnsToPlayers(excelNames, activePlayers);

        var aggregates = new List<HistoricalNightAggregate>();
        var seenNightNumbers = new HashSet<int>();
        for (int blockRow = NightBlockFirstRow; ; blockRow += BlockStrideRows)
        {
            if (!TryParseNightBlock(ws, blockRow, colToPlayerId, out var blockAggregates, out var nightNumber))
            {
                break;
            }
            if (!seenNightNumbers.Add(nightNumber))
            {
                throw new InvalidDataException($"Kvällsblock med nummer {nightNumber} förekommer mer än en gång.");
            }
            aggregates.AddRange(blockAggregates);
        }

        if (aggregates.Count == 0)
        {
            throw new InvalidDataException("Inga kvällsblock hittades i Excel-filen.");
        }

        var placements = ParseSection2Placements(ws, colToPlayerId);
        var snapshot = ParseSection3Snapshot(ws, activePlayers);

        return new ParsedExcelImport(
            PlayerNamesInExcelColumnOrder: excelNames,
            NightAggregates: aggregates,
            RoundPlacements: placements,
            PositionTotalsSnapshot: snapshot);
    }

    private static IReadOnlyList<string> ReadExcelPlayerNames(IXLWorksheet ws)
    {
        var namesRow = NightBlockFirstRow + 1;
        var names = new string[4];
        for (int i = 0; i < 4; i++)
        {
            names[i] = ws.Cell(namesRow, Section1PlayerCols[i]).GetString().Trim();
            if (string.IsNullOrWhiteSpace(names[i]))
            {
                throw new InvalidDataException(
                    $"Spelarnamn saknas i cell {Section1PlayerCols[i]}{namesRow}.");
            }
        }
        return names;
    }

    private static IReadOnlyDictionary<int, int> MapColumnsToPlayers(
        IReadOnlyList<string> excelNames,
        IReadOnlyList<Player> activePlayers)
    {
        var nameToId = activePlayers.ToDictionary(
            p => p.Name.Trim(),
            p => p.Id,
            StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<int, int>();
        for (int i = 0; i < 4; i++)
        {
            if (!nameToId.TryGetValue(excelNames[i], out var id))
            {
                throw new InvalidDataException(
                    $"Spelaren {excelNames[i]} finns inte i appen. Döp om en spelare till '{excelNames[i]}' först, eller redigera Excel-filen.");
            }
            map[i] = id;
        }
        return map;
    }

    private static bool TryParseNightBlock(
        IXLWorksheet ws,
        int blockRow,
        IReadOnlyDictionary<int, int> colToPlayerId,
        out IReadOnlyList<HistoricalNightAggregate> aggregates,
        out int nightNumber)
    {
        aggregates = Array.Empty<HistoricalNightAggregate>();
        nightNumber = 0;

        var headerCell = ws.Cell(blockRow, Section1ColLabel);
        if (headerCell.IsEmpty()) return false;

        var headerText = headerCell.GetString().Trim();
        if (string.IsNullOrEmpty(headerText)) return false;
        if (!TryParseNightNumber(headerText, out nightNumber))
        {
            throw new InvalidDataException(
                $"Cell {Section1ColLabel}{blockRow}: förväntade 'Kväll N', hittade '{headerText}'.");
        }

        var totalTracks = ws.Cell(blockRow, Section1ColTotalTracks).GetValue<int>();
        if (totalTracks <= 0)
        {
            throw new InvalidDataException(
                $"Kväll {nightNumber}: ogiltigt antal banor ({totalTracks}).");
        }

        var firsts = new int[4];
        var seconds = new int[4];
        var thirds = new int[4];
        var fourths = new int[4];

        for (int pos = 1; pos <= 4; pos++)
        {
            var row = blockRow + 1 + pos; // blockRow+2 .. blockRow+5
            var labelText = ws.Cell(row, Section1ColLabel).GetString().Trim();
            if (labelText != pos.ToString(CultureInfo.InvariantCulture))
            {
                throw new InvalidDataException(
                    $"Kväll {nightNumber}: cell {Section1ColLabel}{row} förväntades vara '{pos}' men hittade '{labelText}'.");
            }
            for (int col = 0; col < 4; col++)
            {
                var count = ws.Cell(row, Section1PlayerCols[col]).GetValue<int>();
                switch (pos)
                {
                    case 1: firsts[col] = count; break;
                    case 2: seconds[col] = count; break;
                    case 3: thirds[col] = count; break;
                    case 4: fourths[col] = count; break;
                }
            }
        }

        for (int col = 0; col < 4; col++)
        {
            var sum = firsts[col] + seconds[col] + thirds[col] + fourths[col];
            if (sum != totalTracks)
            {
                throw new InvalidDataException(
                    $"Kväll {nightNumber}: spelare i kolumn {Section1PlayerCols[col]} har {sum} banor men förväntat {totalTracks}.");
            }
        }

        var list = new List<HistoricalNightAggregate>(4);
        for (int col = 0; col < 4; col++)
        {
            list.Add(new HistoricalNightAggregate
            {
                NightNumber = nightNumber,
                PlayerId = colToPlayerId[col],
                FirstPlaces = firsts[col],
                SecondPlaces = seconds[col],
                ThirdPlaces = thirds[col],
                FourthPlaces = fourths[col],
                TotalTracks = totalTracks,
                CreatedAt = DateTime.UtcNow,
            });
        }
        aggregates = list;
        return true;
    }

    private static bool TryParseNightNumber(string headerText, out int nightNumber)
    {
        nightNumber = 0;
        const string prefix = "Kväll ";
        if (!headerText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var numberPart = headerText[prefix.Length..].Trim();
        return int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out nightNumber);
    }

    private static IReadOnlyList<HistoricalRoundPlacement> ParseSection2Placements(
        IXLWorksheet ws,
        IReadOnlyDictionary<int, int> colToPlayerId)
    {
        var placements = new List<HistoricalRoundPlacement>();
        for (int row = Section2FirstRow; row <= Section2LastRow; row++)
        {
            var nightNumber = Section2FirstNightNumber + (row - Section2FirstRow);
            var perPlayerLists = new List<int>[4];
            for (int col = 0; col < 4; col++)
            {
                var cell = ws.Cell(row, Section2PlayerCols[col]);
                if (cell.IsEmpty())
                {
                    perPlayerLists[col] = new List<int>();
                    continue;
                }
                var value = cell.GetValue<double>();
                try
                {
                    perPlayerLists[col] = ParsePlacements(value).ToList();
                }
                catch (InvalidDataException ex)
                {
                    throw new InvalidDataException(
                        $"Kväll {nightNumber}, cell {Section2PlayerCols[col]}{row}: {ex.Message}");
                }
            }

            var counts = perPlayerLists.Select(l => l.Count).Distinct().ToList();
            if (counts.Count > 1)
            {
                throw new InvalidDataException(
                    $"Kväll {nightNumber}: olika antal placeringar per spelare ({string.Join("/", perPlayerLists.Select(l => l.Count))}). Antingen har alla placeringar eller ingen.");
            }

            for (int col = 0; col < 4; col++)
            {
                for (int idx = 0; idx < perPlayerLists[col].Count; idx++)
                {
                    placements.Add(new HistoricalRoundPlacement
                    {
                        NightNumber = nightNumber,
                        PlayerId = colToPlayerId[col],
                        RoundIndex = idx + 1,
                        Position = perPlayerLists[col][idx],
                        CreatedAt = DateTime.UtcNow,
                    });
                }
            }
        }
        return placements;
    }

    private static IReadOnlyList<int> ParsePlacements(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < 1e-9)
        {
            var n = (int)Math.Round(value);
            ValidatePosition(n, value);
            return new[] { n };
        }

        var text = value.ToString("0.#####", CultureInfo.InvariantCulture);
        var parts = text.Split('.');
        if (parts.Length != 2)
        {
            throw new InvalidDataException($"Värdet '{value}' kan inte tolkas som placeringar.");
        }
        var first = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var second = int.Parse(parts[1], CultureInfo.InvariantCulture);
        ValidatePosition(first, value);
        ValidatePosition(second, value);
        return new[] { first, second };
    }

    private static void ValidatePosition(int p, double originalValue)
    {
        if (p < 1 || p > 4)
        {
            throw new InvalidDataException($"Placering {p} (från värdet '{originalValue}') är utanför 1-4.");
        }
    }

    private static IReadOnlyList<HistoricalPositionTotalsSnapshot> ParseSection3Snapshot(
        IXLWorksheet ws,
        IReadOnlyList<Player> activePlayers)
    {
        var nameToId = activePlayers.ToDictionary(
            p => p.Name.Trim(),
            p => p.Id,
            StringComparer.OrdinalIgnoreCase);

        var snapshotColToPlayerId = new Dictionary<int, int>();
        for (int i = 0; i < 4; i++)
        {
            var col = Section3PlayerColumns[i];
            var name = ws.Cell(Section3HeaderRow, col).GetString().Trim();
            if (!nameToId.TryGetValue(name, out var id))
            {
                throw new InvalidDataException(
                    $"Totalscore-sektionen: spelaren '{name}' i kolumn {col} finns inte i appen.");
            }
            snapshotColToPlayerId[i] = id;
        }

        var firsts = new int[4];
        var seconds = new int[4];
        var thirds = new int[4];
        var fourths = new int[4];

        for (int pos = 1; pos <= 4; pos++)
        {
            var row = Section3HeaderRow + pos;
            for (int i = 0; i < 4; i++)
            {
                var count = ws.Cell(row, Section3PlayerColumns[i]).GetValue<int>();
                switch (pos)
                {
                    case 1: firsts[i] = count; break;
                    case 2: seconds[i] = count; break;
                    case 3: thirds[i] = count; break;
                    case 4: fourths[i] = count; break;
                }
            }
        }

        var snapshot = new List<HistoricalPositionTotalsSnapshot>(4);
        for (int i = 0; i < 4; i++)
        {
            snapshot.Add(new HistoricalPositionTotalsSnapshot
            {
                PlayerId = snapshotColToPlayerId[i],
                Firsts = firsts[i],
                Seconds = seconds[i],
                Thirds = thirds[i],
                Fourths = fourths[i],
                CreatedAt = DateTime.UtcNow,
            });
        }
        return snapshot;
    }
}
