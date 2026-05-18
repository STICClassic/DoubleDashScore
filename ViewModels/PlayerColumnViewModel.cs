using CommunityToolkit.Mvvm.ComponentModel;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class PlayerColumnViewModel : ObservableObject
{
    public PlayerColumnViewModel(int playerId, string playerName)
    {
        PlayerId = playerId;
        PlayerName = playerName;
    }

    public int PlayerId { get; }
    public string PlayerName { get; }

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
