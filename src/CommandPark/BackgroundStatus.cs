namespace CommandPark;

/// <summary>
/// 后台程序（全局热键）在当前进程中的可用状态。
/// <para>
/// 主程序在启动阶段写入不可用原因；<see cref="Sources.WelcomeSource"/> 在首屏最后据此追加一条提示。
/// 后台可用（或已拉起 / 已在运行）时保持为 <see langword="null"/>。
/// </para>
/// </summary>
public static class BackgroundStatus
{
    /// <summary>后台热键不可用的原因；为 <see langword="null"/> 表示可用。</summary>
    public static string? UnavailableReason { get; set; }
}
