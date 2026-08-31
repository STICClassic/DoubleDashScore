using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

/// <summary>
/// Resultat av en OCR-avläsning. Vid fel bär <see cref="ErrorMessage"/> ett
/// färdigt sv-SE-meddelande som vyn kan visa rakt av — kategoriseringen sker
/// i servicen, inte i ViewModel:en. Samma mönster som <see cref="SyncResult"/>.
/// </summary>
public sealed record OcrResult(bool Success, ParsedCounters? Counters, string? ErrorMessage)
{
    public static OcrResult Ok(ParsedCounters counters) => new(true, counters, null);

    public static OcrResult Fail(string message) => new(false, null, message);
}

public interface IOcrService
{
    Task<OcrResult> RecognizeAsync(Stream image, CancellationToken ct = default);
}
