using System.IO;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

public sealed class GalleryService
{
    private static readonly HashSet<string> AllowedExtensions =
    [
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v"
    ];

    public Task<IReadOnlyList<MediaItem>> GetRecentMediaAsync(IEnumerable<string> folders, int maxCount, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var files = new List<MediaItem>();

            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    continue;
                }

                IEnumerable<string> directoryFiles;
                try
                {
                    directoryFiles = Directory.EnumerateFiles(folder);
                }
                catch
                {
                    continue;
                }

                foreach (var filePath in directoryFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var extension = Path.GetExtension(filePath);
                    if (!AllowedExtensions.Contains(extension))
                    {
                        continue;
                    }

                    FileInfo fileInfo;
                    try
                    {
                        fileInfo = new FileInfo(filePath);
                    }
                    catch
                    {
                        continue;
                    }

                    files.Add(new MediaItem
                    {
                        FilePath = filePath,
                        DisplayName = Path.GetFileName(filePath),
                        LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                        FileSizeBytes = fileInfo.Exists ? fileInfo.Length : null
                    });
                }
            }

            return (IReadOnlyList<MediaItem>)files
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .Take(maxCount)
                .ToList();
        }, cancellationToken);
    }
}
