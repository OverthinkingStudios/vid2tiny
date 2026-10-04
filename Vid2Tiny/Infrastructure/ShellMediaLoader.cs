using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Vid2Tiny.Infrastructure;

public sealed record ShellMedia(BitmapSource? Thumbnail, TimeSpan? Duration)
{
    public static readonly ShellMedia Empty = new(null, null);
}

/// <summary>
/// Thumbnails and durations as Explorer shows them, read from the Windows Shell on a dedicated worker thread.
/// </summary>
public static class ShellMediaLoader
{
    private const int ThumbnailSize = 256;
    private const int CacheCapacity = 300;
    private const int ThumbnailOnly = 0x8;

    private static readonly PropertyKey MediaDuration = new(new Guid("64440490-4C8B-11D1-8B70-080036B11A03"), 3);

    private static readonly object Gate = new();
    private static readonly List<Request> Pending = [];
    private static readonly SemaphoreSlim Signal = new(0);
    private static readonly Dictionary<string, LinkedListNode<CacheEntry>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<CacheEntry> Recency = new();

    static ShellMediaLoader()
    {
        var worker = new Thread(ProcessRequests) { IsBackground = true, Name = nameof(ShellMediaLoader) };
        // Shell thumbnail providers are apartment-threaded COM objects.
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public static bool TryGetCached(string path, DateTime stamp, [NotNullWhen(true)] out ShellMedia? media)
    {
        lock (Gate)
        {
            return TryGetCachedLocked(Key(path, stamp), out media);
        }
    }

    public static Task<ShellMedia> LoadAsync(string path, DateTime stamp, CancellationToken token)
    {
        var request = new Request(path, Key(path, stamp), token);
        lock (Gate)
        {
            Pending.Add(request);
        }

        Signal.Release();
        return request.Completion.Task;
    }

    private static void ProcessRequests()
    {
        while (true)
        {
            Signal.Wait();

            Request request;
            ShellMedia? media;
            lock (Gate)
            {
                // Newest first: rows that just scrolled into view matter more than the ones scrolled past.
                request = Pending[^1];
                Pending.RemoveAt(Pending.Count - 1);
                TryGetCachedLocked(request.Key, out media);
            }

            if (request.Token.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.Token);
                continue;
            }

            if (media is null)
            {
                try
                {
                    media = Load(request.Path);
                }
                catch (Exception)
                {
                    media = ShellMedia.Empty;
                }

                lock (Gate)
                {
                    AddToCacheLocked(request.Key, media);
                }
            }

            request.Completion.TrySetResult(media);
        }
    }

    private static ShellMedia Load(string path)
    {
        var iid = typeof(IShellItem2).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item) != 0 || item is null)
        {
            return ShellMedia.Empty;
        }

        try
        {
            return new ShellMedia(LoadThumbnail((IShellItemImageFactory)item), LoadDuration(item));
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    private static BitmapSource? LoadThumbnail(IShellItemImageFactory factory)
    {
        if (factory.GetImage(new NativeSize(ThumbnailSize, ThumbnailSize), ThumbnailOnly, out var bitmap) != 0)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(bitmap);
        }
    }

    private static TimeSpan? LoadDuration(IShellItem2 item)
    {
        var key = MediaDuration;
        return item.GetUInt64(ref key, out var hundredNanoseconds) == 0 && hundredNanoseconds > 0
            ? TimeSpan.FromTicks((long)hundredNanoseconds)
            : null;
    }

    private static bool TryGetCachedLocked(string key, [NotNullWhen(true)] out ShellMedia? media)
    {
        if (!Cache.TryGetValue(key, out var node))
        {
            media = null;
            return false;
        }

        Recency.Remove(node);
        Recency.AddFirst(node);
        media = node.Value.Media;
        return true;
    }

    private static void AddToCacheLocked(string key, ShellMedia media)
    {
        if (Cache.ContainsKey(key))
        {
            return;
        }

        Cache[key] = Recency.AddFirst(new CacheEntry(key, media));
        if (Cache.Count > CacheCapacity)
        {
            Cache.Remove(Recency.Last!.Value.Key);
            Recency.RemoveLast();
        }
    }

    private static string Key(string path, DateTime stamp) => $"{path}|{stamp.Ticks}";

    private sealed record CacheEntry(string Key, ShellMedia Media);

    private sealed record Request(string Path, string Key, CancellationToken Token)
    {
        public TaskCompletionSource<ShellMedia> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem2? item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid FormatId, int PropertyId);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    // The vtable layout must match shobjidl_core.h exactly, so unused members cannot be removed.
    [ComImport]
    [Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IntPtr parent);
        void GetDisplayName(int form, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IntPtr other, uint hint, out int order);
        void GetPropertyStore(int flags, ref Guid iid, out IntPtr store);
        void GetPropertyStoreWithCreateObject(int flags, IntPtr createObject, ref Guid iid, out IntPtr store);
        void GetPropertyStoreForKeys(IntPtr keys, uint count, int flags, ref Guid iid, out IntPtr store);
        void GetPropertyDescriptionList(ref PropertyKey type, ref Guid iid, out IntPtr list);
        void Update(IntPtr bindContext);
        void GetProperty(ref PropertyKey key, IntPtr value);
        void GetCLSID(ref PropertyKey key, out Guid clsid);
        void GetFileTime(ref PropertyKey key, out long fileTime);
        void GetInt32(ref PropertyKey key, out int value);
        void GetString(ref PropertyKey key, out IntPtr value);
        void GetUInt32(ref PropertyKey key, out uint value);
        [PreserveSig]
        int GetUInt64(ref PropertyKey key, out ulong value);
        void GetBool(ref PropertyKey key, out int value);
    }
}
