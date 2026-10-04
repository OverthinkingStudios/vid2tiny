using System.IO;

namespace Vid2Tiny.Infrastructure;

public static class PathUtil
{
    public static string? NormalizeFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool IsSameOrInside(string path, string folder) =>
        path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
        (path.Length == folder.Length ||
         path[folder.Length] == Path.DirectorySeparatorChar ||
         Path.EndsInDirectorySeparator(folder));
}
