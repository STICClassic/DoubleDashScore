using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public interface IOcrService
{
    Task<ParsedCounters> RecognizeAsync(Stream image, CancellationToken ct = default);
}
