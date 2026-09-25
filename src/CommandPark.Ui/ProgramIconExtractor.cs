using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;

namespace CommandPark.Ui;

/// <summary>
/// 取可执行文件自带的图标（PrivateExtractIcons 能按尺寸取，ExtractIconEx 只能拿到 32x32）。
/// 仅在 Windows 上可用，其它平台/失败时返回 null。
/// </summary>
internal static class ProgramIconExtractor
{
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
                var bitmap = Win32BitmapConverter.FromIcon(iconHandle, size);
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

    private static IntPtr ExtractIcon(string path, int size)
    {
        var count = PrivateExtractIcons(path, 0, size, size, out var iconHandle, IntPtr.Zero, 1, 0);
        return count > 0 ? iconHandle : IntPtr.Zero;
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
}