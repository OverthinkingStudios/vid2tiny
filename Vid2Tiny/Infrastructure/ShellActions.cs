using System.Diagnostics;

namespace Vid2Tiny.Infrastructure;

public static class ShellActions
{
    public static void RevealInExplorer(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();

    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
