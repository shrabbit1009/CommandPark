namespace CommandPark.Utils.Clicks;

/// <summary>
/// htt 文件的默认位置与读写辅助。
/// <para>
/// 默认写到 <c>{AppData}\shRabbit\CommandPark\clicks.htt</c>
/// （即 <c>%APPDATA%\shRabbit\CommandPark\clicks.htt</c>）；目录不可用时回落到程序目录。
/// 写入采用“先写临时文件、再原子替换”，避免关闭时崩溃留下半个文件。
/// </para>
/// </summary>
public static class HintClickStoreFile
{
    /// <summary>默认文件名。</summary>
    public const string FileName = "clicks.htt";

    /// <summary>应用数据目录下的厂商目录名。</summary>
    public const string VendorDirectoryName = "shRabbit";

    /// <summary>应用数据目录下的应用目录名。</summary>
    public const string AppDirectoryName = "CommandPark";

    /// <summary>返回默认的 htt 文件完整路径。</summary>
    public static string DefaultPath()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Path.Combine(AppContext.BaseDirectory, FileName);
        }

        return Path.Combine(baseDirectory, VendorDirectoryName, AppDirectoryName, FileName);
    }

    /// <summary>
    /// 从文件读取；文件不存在或损坏时返回一个空实例，不影响启动。
    /// </summary>
    public static HintClickStore Load(string path, int maxEntries = HintClickConfig.DefaultMaxEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new HintClickStore(new HashToTimes(maxEntries));
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            return HintClickStore.LoadAsync(stream, maxEntries).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 损坏 / 无权限：当作没有历史记录，绝不因为权重文件拦截启动。
            return new HintClickStore(new HashToTimes(maxEntries));
        }
    }

    /// <summary>把当前内容原子地写回文件。</summary>
    public static void Save(HintClickStore store, string path)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(
                   tempPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 4096))
        {
            store.SaveAsync(stream).GetAwaiter().GetResult();
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
