namespace DoubleDashScore.Models;

public sealed record OcrBoundingBox(int Left, int Top, int Right, int Bottom)
{
    public double CenterX => (Left + Right) / 2.0;
    public double CenterY => (Top + Bottom) / 2.0;
}

public sealed record OcrToken(string Text, OcrBoundingBox Box);
