using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoubleDashScore.Data;

namespace DoubleDashScore.ViewModels;

public partial class ImportPreviewViewModel : ObservableObject
{
    private readonly ImportPreview _preview;

    public ImportPreviewViewModel(ImportPreview preview)
    {
        _preview = preview;
        PlayersLine = $"Spelare i Excel-filen: {string.Join(", ", preview.PlayerNamesInExcelColumnOrder)}";
        FileTotalLine = $"Kvällar i filen: {preview.TotalNightsInFile}";
        SnapshotLine = preview.SnapshotWillBeReplaced
            ? "Totalscore-snapshoten kommer att ersättas."
            : string.Empty;
        MissingLine = preview.MissingNightNumbersInFile.Count > 0
            ? BuildMissingLine(preview.MissingNightNumbersInFile)
            : string.Empty;
        UpdateNightSummary();
    }

    [ObservableProperty]
    private bool _overwrite;

    [ObservableProperty]
    private string _playersLine = string.Empty;

    [ObservableProperty]
    private string _fileTotalLine = string.Empty;

    [ObservableProperty]
    private string _nightSummaryLine = string.Empty;

    [ObservableProperty]
    private string _snapshotLine = string.Empty;

    [ObservableProperty]
    private string _missingLine = string.Empty;

    public event Action<ImportChoice?>? CompletionRequested;

    partial void OnOverwriteChanged(bool value) => UpdateNightSummary();

    [RelayCommand]
    private void Confirm() => CompletionRequested?.Invoke(new ImportChoice(Overwrite));

    [RelayCommand]
    private void Cancel() => CompletionRequested?.Invoke(null);

    private void UpdateNightSummary()
    {
        var existingCount = _preview.TotalNightsInFile - _preview.NewNightsToImport;
        NightSummaryLine = Overwrite
            ? $"{_preview.NewNightsToImport} nya kvällar att importera, {existingCount} befintliga skrivs över med nya värden"
            : $"{_preview.NewNightsToImport} nya kvällar att importera, {existingCount} befintliga hoppas över";
    }

    private static string BuildMissingLine(IReadOnlyList<int> missing)
    {
        var sample = string.Join(", ", missing.Take(10));
        if (missing.Count > 10) sample += ", …";
        return $"Varning — hopp i numreringen: {sample}";
    }
}

public sealed record ImportChoice(bool Overwrite);
