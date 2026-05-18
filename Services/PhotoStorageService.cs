namespace DoubleDashScore.Services;

public sealed class PhotoStorageService
{
    private const string PhotosFolder = "photos";

    public async Task<string> SaveAsync(Stream source, string sourceFileName, CancellationToken ct = default)
    {
        var photosDir = Path.Combine(FileSystem.AppDataDirectory, PhotosFolder);
        Directory.CreateDirectory(photosDir);

        var extension = Path.GetExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";

        var fileName = $"photo-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(photosDir, fileName);

        await using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(dst, ct).ConfigureAwait(false);
        }
        return destination;
    }
}
