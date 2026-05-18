using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public sealed class NoOpOcrService : IOcrService
{
    public Task<ParsedCounters> RecognizeAsync(Stream image, CancellationToken ct = default)
    {
        throw new PlatformNotSupportedException(
            "OCR är endast tillgängligt på Android i den här builden.");
    }
}
