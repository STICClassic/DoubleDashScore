namespace DoubleDashScore.Models;

public sealed record OcrResult(
    IReadOnlyList<OcrToken> Tokens,
    int ImageWidth,
    int ImageHeight);
