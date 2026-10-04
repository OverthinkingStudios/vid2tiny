using System.IO;
using Vid2Tiny.Infrastructure;
using Vid2Tiny.Models;

namespace Vid2Tiny.Services;

public sealed class GalleryService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v"
    };

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

    public Task<IReadOnlyList<SourceVideo>> FindSourceVideosAsync(IReadOnlyList<string> roots, string? excludedFolder, CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<SourceVideo>>(() =>
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var videos = new List<SourceVideo>();

            foreach (var root in roots.Where(Directory.Exists))
            {
                var rootTag = Path.GetFileName(root) is { Length: > 0 } name ? name : root;
                // An output folder at or above a root would otherwise hide that whole root.
                var excludeHere = excludedFolder is not null && !PathUtil.IsSameOrInside(root, excludedFolder);

                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!AllowedExtensions.Contains(file.Extension) ||
                        (excludeHere && PathUtil.IsSameOrInside(file.DirectoryName!, excludedFolder!)) ||
                        !seen.Add(file.FullName))
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(root, file.FullName);
                    var separator = relative.IndexOf(Path.DirectorySeparatorChar);
                    var tag = separator > 0 ? relative[..separator] : rootTag;
                    videos.Add(new SourceVideo(file.FullName, tag, file.CreationTimeUtc, file.LastWriteTimeUtc, file.Length));
                }
            }

            return videos;
        }, cancellationToken);
    }
}
