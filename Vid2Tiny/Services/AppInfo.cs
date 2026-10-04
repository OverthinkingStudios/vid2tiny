using System.Reflection;

namespace Vid2Tiny.Services;

public static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string Name { get; } = Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;

    public static string Version { get; } = Assembly.GetName().Version!.ToString(3);

    public static string Title { get; } = $"{Name} v{Version}";
}
