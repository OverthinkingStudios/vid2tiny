using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Vid2Tiny.Infrastructure;

/// <summary>
/// Loads file thumbnails using the same Windows Shell API as Explorer (IShellItemImageFactory).
/// </summary>
public static class ShellThumbnailLoader
{
    private static readonly Guid ShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8b6cfb16287d");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8b6cfb16287d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    private const int SIIGBF_RESIZETOFIT = 0x00000000;
    private const int SIIGBF_BIGGERSIZEOK = 0x00000010;

    public static BitmapSource? Load(string path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var riid = ShellItemImageFactoryGuid;
        var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref riid, out var factory);
        if (hr != 0 || factory is null)
        {
            return null;
        }

        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var size = new SIZE { cx = width, cy = height };
            factory.GetImage(size, SIIGBF_RESIZETOFIT | SIIGBF_BIGGERSIZEOK, out hBitmap);
            if (hBitmap == IntPtr.Zero)
            {
                return null;
            }

            var bmp = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }

            Marshal.ReleaseComObject(factory);
        }
    }
}
