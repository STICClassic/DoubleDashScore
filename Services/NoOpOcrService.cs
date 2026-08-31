namespace DoubleDashScore.Services;

public sealed class NoOpOcrService : IOcrService
{
    internal const string NotSupportedMessage =
        "OCR är endast tillgängligt på Android i den här builden. " +
        "Fyll i resultaten manuellt.";

    public Task<OcrResult> RecognizeAsync(Stream image, CancellationToken ct = default)
        => Task.FromResult(OcrResult.Fail(NotSupportedMessage));
}
