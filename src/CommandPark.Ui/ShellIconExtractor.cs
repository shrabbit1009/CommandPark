using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;

namespace CommandPark.Ui;

/// <summary>
/// 走 Windows Shell 取"文件的视觉表现"：
/// <list type="bullet">
///   <item>系统图标（按文件类型/文件夹/驱动器，含快捷方式解析后的图标）</item>
///   <item>缩略图/预览（图片、视频、PDF 等由系统缩略图提供程序生成）</item>
/// </list>
/// 底层是 <c>IShellItemImageFactory</c>，只能在 Windows 上用；取不到时返回 null（调用方回落到图标）。
/// </summary>
internal static class ShellIconExtractor
{
    /// <summary>图标请求尺寸（列表里按 24px 显示，48 足够且是 Shell 常用档位）。</summary>
    public const int IconSize = 48;

    /// <summary>缩略图请求尺寸（留一点余量给高 DPI）。</summary>
    public const int ThumbnailSize = 96;

    /// <summary>
    /// 值得尝试缩略图的扩展名。
    /// 只对"确实可能有缩略图"的类型走一遍 Shell，避免为文本/压缩包白跑，
    /// 也避免第三方缩略图扩展（视频/PDF 常见）拖慢加载。
    /// </summary>
    private static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 图片
        ".png", ".jpg", ".jpeg", ".jpe", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".heic", ".avif", ".svg",
        // 视频
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg",
        // 文档
        ".pdf",
    };

    private static readonly Guid ShellItemImageFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>这个文件是否值得尝试缩略图（只按扩展名判断，不碰文件系统）。</summary>
    public static bool SupportsThumbnail(string path) =>
        ThumbnailExtensions.Contains(Path.GetExtension(path));

    /// <summary>文件 / 文件夹 / 驱动器的系统图标。</summary>
    public static Bitmap? TryGetIcon(string path, int size = IconSize) =>
        TryGet(path, size, ShellItemFlags.IconOnly | ShellItemFlags.BiggerSizeOk | ShellItemFlags.ResizeToFit);

    /// <summary>缩略图；该文件没有缩略图时返回 null（调用方回落到 <see cref="TryGetIcon"/>）。</summary>
    public static Bitmap? TryGetThumbnail(string path, int size = ThumbnailSize) =>
        TryGet(path, size, ShellItemFlags.ThumbnailOnly | ShellItemFlags.BiggerSizeOk | ShellItemFlags.ResizeToFit);

    private static Bitmap? TryGet(string path, int size, ShellItemFlags flags)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) || !Exists(path))
        {
            return null;
        }

        IShellItemImageFactory? factory = null;

        try
        {
            var id = ShellItemImageFactoryId;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out factory);

            if (factory is null)
            {
                return null;
            }

            var result = factory.GetImage(new NativeSize { Width = size, Height = size }, flags, out var bitmapHandle);
            if (result != 0 || bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Win32BitmapConverter.FromBitmap(bitmapHandle);
            }
            finally
            {
                DeleteObject(bitmapHandle);
            }
        }
        catch (Exception)
        {
            // 文件不存在 / 没有缩略图 / Shell 扩展异常：一律当作"取不到"
            return null;
        }
        finally
        {
            if (factory is not null && Marshal.IsComObject(factory))
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Flags]
    private enum ShellItemFlags : uint
    {
        ResizeToFit     = 0x00,
        BiggerSizeOk    = 0x01,
        MemoryOnly      = 0x02,
        IconOnly        = 0x04,
        ThumbnailOnly   = 0x08,
        InCacheOnly     = 0x10,
        CropToSquare    = 0x20,
        WideThumbnails  = 0x40,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, ShellItemFlags flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);
}