using CommunityToolkit.Mvvm.ComponentModel;
using DoubleDashScore.Models;
using DoubleDashScore.Services;
using DoubleDashScore.Views;

namespace DoubleDashScore.ViewModels;

public partial class OcrCaptureViewModel : ObservableObject
{
    private readonly IOcrService _ocr;
    private readonly PhotoStorageService _photos;
    private readonly OcrFlowContext _context;
    private readonly IApiKeyStore _keys;

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

    public async Task CapturePhotoAsync(int gameNightId, CancellationToken ct = default)
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
            pick = await MediaPicker.Default.PickPhotoAsync().ConfigureAwait(true);
        }
        if (pick is null) return;

        string photoPath;
        await using (var stream = await pick.OpenReadAsync().ConfigureAwait(true))
        {
            photoPath = await _photos.SaveAsync(stream, pick.FileName, ct).ConfigureAwait(true);
        }

        ParsedCounters parsed;
        try
        {
            await using var stream = File.OpenRead(photoPath);
            parsed = await _ocr.RecognizeAsync(stream, ct).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            parsed = new ParsedCounters(
                Enumerable.Range(0, 4).Select(i => new PlayerSlotCounters(i, 0, 0, 0, 0)).ToList(),
                0,
                new List<string> { ex.Message });
        }
        catch (PlatformNotSupportedException ex)
        {
            await page.DisplayAlertAsync("OCR ej tillgängligt", ex.Message, "OK").ConfigureAwait(true);
            return;
        }

        _context.GameNightId = gameNightId;
        _context.Pending = parsed;
        _context.PhotoPath = photoPath;

        await Shell.Current.GoToAsync("OcrPreviewPage").ConfigureAwait(true);
    }
}
