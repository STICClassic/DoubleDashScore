using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class OcrCaptureViewModel : ObservableObject
{
    private readonly IOcrService _ocr;
    private readonly PhotoStorageService _photos;
    private readonly OcrFlowContext _context;
    private readonly IApiKeyStore _keys;

    internal const string OcrFailureTitle = "OCR misslyckades";

    public OcrCaptureViewModel(
        IOcrService ocr,
        PhotoStorageService photos,
        OcrFlowContext context,
        IApiKeyStore keys)
    {
        _ocr = ocr;
        _photos = photos;
        _context = context;
        _keys = keys;
    }

    public async Task CapturePhotoAsync(
        int gameNightId,
        Action<bool>? onOcrLoading = null,
        CancellationToken ct = default)
    {
        var page = Shell.Current.CurrentPage;

        var apiKey = await _keys.GetAsync(ct).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await page.DisplayAlertAsync(
                "API-nyckel saknas",
                "Sätt din Anthropic API-nyckel i Inställningar först.",
                "OK").ConfigureAwait(true);
            await Shell.Current.GoToAsync("ApiKeySettingsPage").ConfigureAwait(true);
            return;
        }

        var choice = await page.DisplayActionSheetAsync(
            "Foto av poängtavla",
            "Avbryt",
            null,
            "Ta foto",
            "Från galleri").ConfigureAwait(true);

        FileResult? pick = null;
        if (choice == "Ta foto")
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await page.DisplayAlertAsync("Kamera saknas", "Enheten har ingen kamera.", "OK").ConfigureAwait(true);
                return;
            }
            pick = await MediaPicker.Default.CapturePhotoAsync().ConfigureAwait(true);
        }
        else if (choice == "Från galleri")
        {
            // PickPhotoAsync (singular) är [Obsolete] sedan MAUI uppdaterade
            // till PickPhotosAsync som stödjer multi-select. Vi vill bara
            // ha en bild → ta första (eller null om användaren avbröt).
            // FirstOrDefault på tom IEnumerable returnerar null, samma
            // beteende som PickPhotoAsync vid avbryt.
            var picks = await MediaPicker.Default.PickPhotosAsync().ConfigureAwait(true);
            pick = picks?.FirstOrDefault();
        }
        if (pick is null) return;

        string photoPath;
        await using (var stream = await pick.OpenReadAsync().ConfigureAwait(true))
        {
            photoPath = await _photos.SaveAsync(stream, pick.FileName, ct).ConfigureAwait(true);
        }

        OcrResult result;
        try
        {
            onOcrLoading?.Invoke(true);
            try
            {
                await using var stream = File.OpenRead(photoPath);
                result = await _ocr.RecognizeAsync(stream, ct).ConfigureAwait(true);
            }
            finally
            {
                // Alltid av — även vid fel — så vyn aldrig fastnar i loading.
                onOcrLoading?.Invoke(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Servicen kategoriserar allt den kan; hamnar vi här är det något
            // oväntat utanför HTTP-lagret (t.ex. fil-I/O på fotot).
            Debug.WriteLine($"[OcrCapture] unexpected {ex.GetType().Name}: {ex.Message}");
            result = OcrResult.Fail(
                "OCR:n misslyckades. Fyll i resultaten manuellt. " +
                $"Tekniskt fel: {ex.GetType().Name} {ex.Message}");
        }

        if (!result.Success || result.Counters is null)
        {
            // Ingen navigering vidare: användaren stannar kvar på kvällsvyn och
            // kan starta om skanningen eller mata in omgången manuellt.
            await page.DisplayAlertAsync(
                OcrFailureTitle,
                result.ErrorMessage ?? "OCR:n misslyckades. Fyll i resultaten manuellt.",
                "OK").ConfigureAwait(true);
            return;
        }

        _context.GameNightId = gameNightId;
        _context.Pending = result.Counters;
        _context.PhotoPath = photoPath;

        await Shell.Current.GoToAsync("OcrPreviewPage").ConfigureAwait(true);
    }
}
