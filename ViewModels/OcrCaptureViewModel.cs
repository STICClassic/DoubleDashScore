using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DoubleDashScore.Models;
using DoubleDashScore.Services;

namespace DoubleDashScore.ViewModels;

public partial class OcrCaptureViewModel : ObservableObject
{
    private readonly IOcrService _ocr;
    private readonly PhotoStorageService _photos;
    private readonly OcrFlowContext _context;

    public OcrCaptureViewModel(
        IOcrService ocr,
        PhotoStorageService photos,
        OcrFlowContext context)
    {
        _ocr = ocr;
        _photos = photos;
        _context = context;
    }

    public async Task CapturePhotoAsync(int gameNightId, CancellationToken ct = default)
    {
        var page = Shell.Current.CurrentPage;
        var choice = await page.DisplayActionSheetAsync(
            "Foto av poängtavla",
            "Avbryt",
            null,
            "Ta foto",
            "Från galleri").ConfigureAwait(true);

        FileResult? pick = null;
        try
        {
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

            Models.OcrResult ocrResult;
            await using (var stream = File.OpenRead(photoPath))
            {
                ocrResult = await _ocr.RecognizeAsync(stream, ct).ConfigureAwait(true);
            }

            var parsed = OcrParser.Parse(ocrResult);
            _context.GameNightId = gameNightId;
            _context.Pending = parsed;
            _context.PhotoPath = photoPath;

            await Shell.Current.GoToAsync("OcrPreviewPage").ConfigureAwait(true);
        }
        catch (PlatformNotSupportedException ex)
        {
            await page.DisplayAlertAsync("OCR ej tillgängligt", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Foto-/OCR-fel", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    public async Task DebugCaptureAsync(CancellationToken ct = default)
    {
        var page = Shell.Current.CurrentPage;
        var choice = await page.DisplayActionSheetAsync(
            "Debug OCR — välj bild",
            "Avbryt",
            null,
            "Ta foto",
            "Från galleri").ConfigureAwait(true);

        FileResult? pick = null;
        try
        {
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

            var debugDir = Path.Combine(FileSystem.AppDataDirectory, "ocr-debug");
            Directory.CreateDirectory(debugDir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var photoExt = Path.GetExtension(pick.FileName);
            if (string.IsNullOrWhiteSpace(photoExt)) photoExt = ".jpg";
            var photoPath = Path.Combine(debugDir, $"ocr-debug-{timestamp}{photoExt}");
            var jsonPath = Path.Combine(debugDir, $"ocr-debug-{timestamp}.json");

            await using (var src = await pick.OpenReadAsync().ConfigureAwait(true))
            await using (var dst = new FileStream(photoPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await src.CopyToAsync(dst, ct).ConfigureAwait(true);
            }

            OcrResult ocrResult;
            await using (var stream = File.OpenRead(photoPath))
            {
                ocrResult = await _ocr.RecognizeAsync(stream, ct).ConfigureAwait(true);
            }

            var dump = new
            {
                timestamp,
                ocrResult.ImageWidth,
                ocrResult.ImageHeight,
                tokenCount = ocrResult.Tokens.Count,
                tokens = ocrResult.Tokens.Select(t => new
                {
                    t.Text,
                    t.Box.Left,
                    t.Box.Top,
                    t.Box.Right,
                    t.Box.Bottom,
                    t.Box.CenterX,
                    t.Box.CenterY,
                }),
            };
            var json = JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonPath, json, ct).ConfigureAwait(true);

            await Share.Default.RequestAsync(new ShareMultipleFilesRequest
            {
                Title = $"OCR-debug {timestamp}",
                Files = new List<ShareFile>
                {
                    new ShareFile(jsonPath),
                    new ShareFile(photoPath),
                },
            }).ConfigureAwait(true);
        }
        catch (PlatformNotSupportedException ex)
        {
            await page.DisplayAlertAsync("OCR ej tillgängligt", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Debug-OCR-fel", ex.Message, "OK").ConfigureAwait(true);
        }
    }
}
