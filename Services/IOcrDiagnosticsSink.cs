namespace DoubleDashScore.Services;

public interface IOcrDiagnosticsSink
{
    void Save(string filename, string content);
}
