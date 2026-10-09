using System;
using CommandPark.Utils.Clicks;

namespace CommandPark.Ui;

/// <summary>
/// 点击权重的进程内入口。
/// <para>
/// 启动时读取本地 htt 文件、退出时写回；<see cref="Store"/> 会被注入到默认 Hint Provider，
/// 让“点击越多、结果越靠前”生效。读取使用懒加载：只有真正访问 <see cref="Store"/> 时才读盘，
/// 单独启动后台程序（不显示窗口）的路径不会做无谓的 IO。
/// </para>
/// </summary>
internal static class ClickHistory
{
    private static readonly object Gate = new();

    private static HintClickStore? _store;

    /// <summary>htt 文件路径；默认为 <see cref="HintClickStoreFile.DefaultPath"/>。</summary>
    public static string Path { get; set; } = HintClickStoreFile.DefaultPath();

    /// <summary>当前点击权重存储（首次访问时从本地读取）。</summary>
    public static HintClickStore Store
    {
        get
        {
            var store = _store;
            if (store is not null)
            {
                return store;
            }

            lock (Gate)
            {
                return _store ??= Load();
            }
        }
    }

    /// <summary>在启动阶段主动触发一次加载（不显示窗口的路径不要调用）。</summary>
    public static void Preload() => _ = Store;

    /// <summary>把当前内容写回本地；退出阶段写盘失败不影响退出。</summary>
    public static void Save()
    {
        // 从未访问过 Store，说明这次进程根本没用到点击权重，无需创建空文件。
        var store = _store;
        if (store is null)
        {
            return;
        }

        try
        {
            HintClickStoreFile.Save(store, Path);
        }
        catch (Exception)
        {
            // 关闭阶段写盘失败（磁盘满 / 无权限等）不能拦住程序退出。
        }
    }

    private static HintClickStore Load()
    {
        int maxEntries;
        try
        {
            maxEntries = HintClickConfig.ReadMaxEntries(ConfigFile.Load());
        }
        catch (Exception)
        {
            // 配置损坏不能影响权重加载：退回默认上限。
            maxEntries = HintClickConfig.DefaultMaxEntries;
        }

        return HintClickStoreFile.Load(Path, maxEntries);
    }
}
