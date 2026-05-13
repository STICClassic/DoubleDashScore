namespace DoubleDashScore.Services;

public sealed class FileSystemOcrDiagnosticsSink : IOcrDiagnosticsSink
{
    public const string FolderName = "api-debug";

    public void Save(string filename, string content)
    {
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, FolderName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, filename), content);
        }
        catch
        {
        }
    }
}
