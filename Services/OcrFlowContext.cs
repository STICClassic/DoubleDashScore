using DoubleDashScore.Models;

namespace DoubleDashScore.Services;

public sealed class OcrFlowContext
{
    public int GameNightId { get; set; }
    public ParsedCounters? Pending { get; set; }
    public string? PhotoPath { get; set; }

    public void Clear()
    {
        GameNightId = 0;
        Pending = null;
        PhotoPath = null;
    }
}
