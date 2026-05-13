using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public sealed class NoOpOcrService : IOcrService
{
    public Task<OcrResult> RecognizeAsync(Stream image, CancellationToken ct = default)
    {
        throw new PlatformNotSupportedException(
            "OCR är endast tillgängligt på Android. Den här plattformen saknar ML Kit-binding.");
    }
}
