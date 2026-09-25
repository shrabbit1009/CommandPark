using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CommandPark.Ui;

/// <summary>
/// 从可执行文件里取出"最大的那张"图标，并转成 Avalonia 位图。
/// 仅在 Windows 上可用，其它平台/失败时返回 null。
/// </summary>
internal static class ProgramIconExtractor
{
    private const uint DiNormal = 0x0003;
    private const uint DibRgbColors = 0;

    /// <summary>候选尺寸，从大到小，取系统能给出的最大一档。</summary>
    private static readonly int[] CandidateSizes = [256, 128, 96, 64, 48, 32, 16];

    public static Bitmap? TryExtract(string executablePath)
    {
        if (!OperatingSystem.IsWindows()
            || string.IsNullOrWhiteSpace(executablePath)
            || !File.Exists(executablePath))
        {
            return null;
        }

        foreach (var size in CandidateSizes)
        {
            var iconHandle = ExtractIcon(executablePath, size);
            if (iconHandle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var bitmap = ToBitmap(iconHandle, size);
                if (bitmap is not null)
                {
                    return bitmap;
                }
            }
            finally
            {
                DestroyIcon(iconHandle);
            }
        }

        return null;
    }

    /// <summary>PrivateExtractIcons 能按指定尺寸取图标（ExtractIconEx 只能拿到 32x32）。</summary>
    private static IntPtr ExtractIcon(string path, int size)
    {
        var count = PrivateExtractIcons(path, 0, size, size, out var iconHandle, IntPtr.Zero, 1, 0);
        return count > 0 ? iconHandle : IntPtr.Zero;
    }

    private static Bitmap? ToBitmap(IntPtr iconHandle, int size)
    {
        if (!GetIconInfo(iconHandle, out var iconInfo))
        {
            return null;
        }

        var deviceContext = IntPtr.Zero;
        var dibSection    = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;

        try
        {
            deviceContext = CreateCompatibleDC(IntPtr.Zero);
            if (deviceContext == IntPtr.Zero)
            {
                return null;
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size        = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width       = size,
                    Height      = -size,   // 负数 = 自上而下，省掉一次翻转
                    Planes      = 1,
                    BitCount    = 32,
                    Compression = 0,       // BI_RGB
                },
            };

            dibSection = CreateDIBSection(deviceContext, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (dibSection == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            previousBitmap = SelectObject(deviceContext, dibSection);
            if (!DrawIconEx(deviceContext, 0, 0, iconHandle, size, size, 0, IntPtr.Zero, DiNormal))
            {
                return null;
            }

            var pixels = new byte[size * size * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            if (!HasAlphaChannel(pixels))
            {
                // 老式图标没有 alpha 通道（画出来 alpha 全是 0），用 AND 掩码推透明度
                ApplyMaskAlpha(deviceContext, iconInfo.MaskBitmap, size, pixels);
            }

            return ToAvaloniaBitmap(pixels, size);
        }
        finally
        {
            if (deviceContext != IntPtr.Zero && previousBitmap != IntPtr.Zero)
            {
                SelectObject(deviceContext, previousBitmap);
            }

            if (dibSection != IntPtr.Zero)
            {
                DeleteObject(dibSection);
            }

            if (deviceContext != IntPtr.Zero)
            {
                DeleteDC(deviceContext);
            }

            if (iconInfo.ColorBitmap != IntPtr.Zero)
            {
                DeleteObject(iconInfo.ColorBitmap);
            }

            if (iconInfo.MaskBitmap != IntPtr.Zero)
            {
                DeleteObject(iconInfo.MaskBitmap);
            }
        }
    }

    private static bool HasAlphaChannel(byte[] bgraPixels)
    {
        for (var i = 3; i < bgraPixels.Length; i += 4)
        {
            if (bgraPixels[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>用图标的 AND 掩码补 alpha：掩码为白 → 透明，为黑 → 不透明。</summary>
    private static void ApplyMaskAlpha(IntPtr deviceContext, IntPtr maskBitmap, int size, byte[] bgraPixels)
    {
        if (maskBitmap == IntPtr.Zero)
        {
            return;
        }

        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size        = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width       = size,
                Height      = -size,
                Planes      = 1,
                BitCount    = 32,   // 让 GDI 把 1bpp 掩码展开成 32bpp 黑/白
                Compression = 0,
            },
        };

        var maskPixels = new byte[size * size * 4];
        var handle = GCHandle.Alloc(maskPixels, GCHandleType.Pinned);

        try
        {
            if (GetDIBits(deviceContext, maskBitmap, 0, (uint)size, handle.AddrOfPinnedObject(), ref info, DibRgbColors) == 0)
            {
                return;
            }
        }
        finally
        {
            handle.Free();
        }

        for (var pixel = 0; pixel < size * size; pixel++)
        {
            var offset = pixel * 4;
            var transparent = maskPixels[offset] != 0 || maskPixels[offset + 1] != 0 || maskPixels[offset + 2] != 0;

            if (transparent)
            {
                bgraPixels[offset]     = 0;
                bgraPixels[offset + 1] = 0;
                bgraPixels[offset + 2] = 0;
            }

            bgraPixels[offset + 3] = transparent ? (byte)0 : (byte)255;
        }
    }

    private static Bitmap ToAvaloniaBitmap(byte[] bgraPixels, int size)
    {
        // DrawIconEx 输出的是预乘 alpha，正好对应 AlphaFormat.Premul
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var locked = bitmap.Lock())
        {
            var rowBytes = size * 4;
            for (var y = 0; y < size; y++)
            {
                Marshal.Copy(bgraPixels, y * rowBytes, IntPtr.Add(locked.Address, y * locked.RowBytes), rowBytes);
            }
        }

        return bitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int    IsIcon;
        public uint   HotspotX;
        public uint   HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint   Size;
        public int    Width;
        public int    Height;
        public ushort Planes;
        public ushort BitCount;
        public uint   Compression;
        public uint   SizeImage;
        public int    XPelsPerMeter;
        public int    YPelsPerMeter;
        public uint   ColorsUsed;
        public uint   ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint             Colors;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string fileName,
        int    iconIndex,
        int    cxIcon,
        int    cyIcon,
        out IntPtr icon,
        IntPtr iconId,
        uint   iconCount,
        uint   flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(
        IntPtr deviceContext,
        int    x,
        int    y,
        IntPtr icon,
        int    width,
        int    height,
        uint   step,
        IntPtr brush,
        uint   flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfo info,
        uint   usage,
        out IntPtr bits,
        IntPtr section,
        uint   offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr        deviceContext,
        IntPtr        bitmap,
        uint          startScan,
        uint          scanLines,
        IntPtr        bits,
        ref BitmapInfo info,
        uint          usage);
}