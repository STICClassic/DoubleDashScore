using CommunityToolkit.Mvvm.ComponentModel;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class PlayerColumnViewModel : ObservableObject
{
    public PlayerColumnViewModel(int playerId, string playerName, int slotIndex = 0)
    {
        PlayerId = playerId;
        _playerName = playerName;
        SlotIndex = slotIndex;
    }

    /// <summary>GameCube-position 0-3 (P1-P4). Bär vilken kolumn ett tap gjordes på.</summary>
    public int SlotIndex { get; }

    /// <summary>
    /// Sättbar: i OCR-förhandsgranskningen kan användaren byta vilken spelare
    /// som satt på positionen, och då ska rubriken följa med.
    /// </summary>
    [ObservableProperty]
    private int _playerId;

    [ObservableProperty]
    private string _playerName;

    /// <summary>
    /// Spelarfärg för rubriken, från <see cref="PlayerColors"/>. Null = tema-default
    /// (manuell inmatning sätter ingen färg).
    /// </summary>
    [ObservableProperty]
    private Color? _nameColor;

    [ObservableProperty]
    private string _firstPlacesText = "0";

    [ObservableProperty]
    private string _secondPlacesText = "0";

    [ObservableProperty]
    private string _thirdPlacesText = "0";

    [ObservableProperty]
    private string _fourthPlacesText = "0";

    [ObservableProperty]
    private bool _firstPlaceHasError;

    [ObservableProperty]
    private bool _secondPlaceHasError;

    [ObservableProperty]
    private bool _thirdPlaceHasError;

    [ObservableProperty]
    private bool _fourthPlaceHasError;

    public void SetCellErrors(MatrixErrorDetector.CellErrors errors)
    {
        FirstPlaceHasError = errors.First;
        SecondPlaceHasError = errors.Second;
        ThirdPlaceHasError = errors.Third;
        FourthPlaceHasError = errors.Fourth;
    }

    public bool TryGetCounts(out (int first, int second, int third, int fourth) counts)
    {
        counts = default;
        if (!TryParseNonNegative(FirstPlacesText, out var first)) return false;
        if (!TryParseNonNegative(SecondPlacesText, out var second)) return false;
        if (!TryParseNonNegative(ThirdPlacesText, out var third)) return false;
        if (!TryParseNonNegative(FourthPlacesText, out var fourth)) return false;
        counts = (first, second, third, fourth);
        return true;
    }

    private static bool TryParseNonNegative(string? text, out int value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }
        if (int.TryParse(text, out value) && value >= 0)
        {
            return true;
        }
        value = 0;
        return false;
    }
}
