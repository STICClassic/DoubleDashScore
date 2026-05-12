using System.Globalization;
using ClosedXML.Excel;
using DoubleDashScore.Data;
using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public static class ExcelImporter
{
    private const int BlockStrideRows = 8;
    private const int NightBlockFirstRow = 2;
    private const int GlobalHeaderRow = 1;

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

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(xlsxStream);
        }
        catch (Exception ex) when (ex is not InvalidDataException && ex is not InvalidOperationException)
        {
            throw new InvalidDataException(
                $"Kunde inte öppna Excel-filen ({ex.GetType().Name}): {ex.Message}", ex);
        }

        using (workbook)
        {
            IXLWorksheet ws;
            try
            {
                ws = workbook.Worksheets.First();
            }
            catch (Exception ex) when (ex is not InvalidDataException && ex is not InvalidOperationException)
            {
                throw new InvalidDataException(
                    $"Kunde inte öppna första arbetsbladet ({ex.GetType().Name}): {ex.Message}", ex);
            }

            return ParseWorksheet(ws, activePlayers);
        }
    }

    private static ParsedExcelImport ParseWorksheet(IXLWorksheet ws, IReadOnlyList<Player> activePlayers)
    {
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
        var names = new string[4];
        for (int i = 0; i < 4; i++)
        {
            names[i] = ReadCachedString(ws.Cell(GlobalHeaderRow, Section1PlayerCols[i])).Trim();
            if (string.IsNullOrWhiteSpace(names[i]))
            {
                throw new InvalidDataException(
                    $"Spelarnamn saknas i cell {Section1PlayerCols[i]}{GlobalHeaderRow}.");
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
        var headerText = ReadCachedString(headerCell).Trim();
        if (string.IsNullOrEmpty(headerText)) return false;
        if (!TryParseNightNumber(headerText, out nightNumber))
        {
            throw new InvalidDataException(
                $"Cell {Section1ColLabel}{blockRow}: förväntade 'Kväll N', hittade '{headerText}'.");
        }

        var totalTracks = ReadCachedInt(ws.Cell(blockRow, Section1ColTotalTracks));
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
            var row = blockRow + pos; // blockRow+1 .. blockRow+4
            var labelText = ReadCachedString(ws.Cell(row, Section1ColLabel)).Trim();
            if (labelText != pos.ToString(CultureInfo.InvariantCulture))
            {
                throw new InvalidDataException(
                    $"Kväll {nightNumber}: cell {Section1ColLabel}{row} förväntades vara '{pos}' men hittade '{labelText}'.");
            }
            for (int col = 0; col < 4; col++)
            {
                var count = ReadCachedInt(ws.Cell(row, Section1PlayerCols[col]));
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
                var cached = ReadCachedCellValue(cell);
                if (cached.IsBlank)
                {
                    perPlayerLists[col] = new List<int>();
                    continue;
                }
                var value = ConvertCachedToDouble(cell, cached);
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
            var name = ReadCachedString(ws.Cell(Section3HeaderRow, col)).Trim();
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
                var count = ReadCachedInt(ws.Cell(row, Section3PlayerColumns[i]));
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

    // Alla cellläsningar går via CachedValue så att ClosedXML aldrig försöker evaluera
    // en lagrad formel. Excel-källan innehåller R1C1-stilformler (t.ex. SUM(R[-4]C:R[-1]C))
    // som ClosedXML:s A1-parser inte hanterar — vi vill bara åt det senast beräknade värdet.
    //
    // Varje read wrappas i try/catch så att vi rapporterar exakt vilken cell som triggar
    // ett ClosedXML-fel; annars är felmeddelandet bara "Unexpected token ..." utan kontext.
    private static XLCellValue ReadCachedCellValue(IXLCell cell)
    {
        string address;
        try
        {
            address = cell.Address.ToString() ?? "<okänd>";
        }
        catch
        {
            address = "<okänd>";
        }

        try
        {
            return cell.CachedValue;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Fel vid läsning av cell {address}: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    private static int ReadCachedInt(IXLCell cell)
    {
        var v = ReadCachedCellValue(cell);
        if (v.IsBlank) return 0;
        if (v.IsNumber) return (int)Math.Round(v.GetNumber());
        if (v.IsText && int.TryParse(v.GetText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }
        throw new InvalidDataException(
            $"Cell {cell.Address}: kunde inte tolka cachat värde som heltal (typ: {v.Type}).");
    }

    private static double ReadCachedDouble(IXLCell cell)
    {
        var v = ReadCachedCellValue(cell);
        return ConvertCachedToDouble(cell, v);
    }

    private static double ConvertCachedToDouble(IXLCell cell, XLCellValue v)
    {
        if (v.IsNumber) return v.GetNumber();
        if (v.IsText && double.TryParse(v.GetText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        throw new InvalidDataException(
            $"Cell {cell.Address}: kunde inte tolka cachat värde som tal (typ: {v.Type}).");
    }

    private static string ReadCachedString(IXLCell cell)
    {
        var v = ReadCachedCellValue(cell);
        if (v.IsBlank) return string.Empty;
        if (v.IsText) return v.GetText();
        if (v.IsNumber) return v.GetNumber().ToString(CultureInfo.InvariantCulture);
        if (v.IsBoolean) return v.GetBoolean().ToString();
        return v.ToString();
    }
}
