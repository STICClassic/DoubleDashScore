using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public interface IOcrService
{
    Task<OcrResult> RecognizeAsync(Stream image, CancellationToken ct = default);
}
