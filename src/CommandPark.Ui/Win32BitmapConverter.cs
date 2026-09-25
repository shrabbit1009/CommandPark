using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CommandPark.Ui;

/// <summary>
/// 把 Win32 的 HICON / HBITMAP 转成 Avalonia 位图（HICON 与 Shell 缩略图都走这里）。
/// </summary>
internal static class Win32BitmapConverter
{
    private const uint DiNormal = 0x0003;
    private const uint DibRgbColors = 0;

    /// <summary>HICON → 位图。老式图标没有 alpha 通道时用 AND 掩码推透明度。</summary>
    public static Bitmap? FromIcon(IntPtr iconHandle, int size)
    {
        if (!GetIconInfo(iconHandle, out var iconInfo))
        {
            return null;
        }

        var deviceContext = IntPtr.Zero;
        var dibSection    = IntPtr.Zero;
        var previous      = IntPtr.Zero;

        try
        {
            deviceContext = CreateCompatibleDC(IntPtr.Zero);
            if (deviceContext == IntPtr.Zero)
            {
                return null;
            }

            var info = CreateBitmapInfo(size, size);
            dibSection = CreateDIBSection(deviceContext, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (dibSection == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return null;
            }

            previous = SelectObject(deviceContext, dibSection);
            if (!DrawIconEx(deviceContext, 0, 0, iconHandle, size, size, 0, IntPtr.Zero, DiNormal))
            {
                return null;
            }

            var pixels = new byte[size * size * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            if (!HasAlpha(pixels))
            {
                ApplyMaskAlpha(deviceContext, iconInfo.MaskBitmap, size, pixels);
            }

            return Create(pixels, size, size);
        }
        finally
        {
            if (deviceContext != IntPtr.Zero && previous != IntPtr.Zero)
            {
                SelectObject(deviceContext, previous);
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

    /// <summary>HBITMAP → 位图（Shell 缩略图/图标返回的就是 32bpp 预乘 alpha 位图）。</summary>
    public static Bitmap? FromBitmap(IntPtr bitmapHandle)
    {
        if (bitmapHandle == IntPtr.Zero)
        {
            return null;
        }

        var description = new NativeBitmap();
        if (GetObject(bitmapHandle, Marshal.SizeOf<NativeBitmap>(), ref description) == 0)
        {
            return null;
        }

        var width  = description.Width;
        var height = description.Height;

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        var info = CreateBitmapInfo(width, height);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        try
        {
            var deviceContext = CreateCompatibleDC(IntPtr.Zero);
            try
            {
                if (deviceContext == IntPtr.Zero
                    || GetDIBits(deviceContext, bitmapHandle, 0, (uint)height, handle.AddrOfPinnedObject(), ref info, DibRgbColors) == 0)
                {
                    return null;
                }
            }
            finally
            {
                if (deviceContext != IntPtr.Zero)
                {
                    DeleteDC(deviceContext);
                }
            }
        }
        finally
        {
            handle.Free();
        }

        // 少数来源没有 alpha 通道，此时整张图会全透明，按不透明处理
        if (!HasAlpha(pixels))
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        return Create(pixels, width, height);
    }

    /// <summary>BGRA（预乘 alpha）像素 → Avalonia 位图。</summary>
    public static Bitmap Create(byte[] bgraPremultiplied, int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var locked = bitmap.Lock())
        {
            var rowBytes = width * 4;
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(bgraPremultiplied, y * rowBytes, IntPtr.Add(locked.Address, y * locked.RowBytes), rowBytes);
            }
        }

        return bitmap;
    }

    private static BitmapInfo CreateBitmapInfo(int width, int height) => new()
    {
        Header = new BitmapInfoHeader
        {
            Size        = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width       = width,
            Height      = -height,   // 负数 = 自上而下
            Planes      = 1,
            BitCount    = 32,
            Compression = 0,         // BI_RGB
        },
    };

    private static bool HasAlpha(byte[] bgraPixels)
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

        var info = CreateBitmapInfo(size, size);
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
    private struct NativeBitmap
    {
        public int    Type;
        public int    Width;
        public int    Height;
        public int    WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
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
    private static extern int GetObject(IntPtr gdiObject, int size, ref NativeBitmap description);

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