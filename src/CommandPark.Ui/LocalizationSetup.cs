using System;
using CommandPark.Strings;

namespace CommandPark.Ui;

/// <summary>
/// 启动阶段配置界面语言：读 <c>commandpark.json</c> 的 <c>language</c> 节点，
/// 再从程序目录的 <c>lang\</c> 目录按「当前语言 → 回退顺序 → 中文基准」构建字符串链。
/// <para>
/// 必须在任何窗口（XAML 标记扩展）与后台提示之前调用。
/// </para>
/// </summary>
internal static class LocalizationSetup
{
    /// <summary>按配置文件配置全局字符串提供者；失败时保留默认的懒加载行为。</summary>
    public static void Configure()
    {
        try
        {
            Localization.Configure(StringCollectionBuilder.Create().ReadConfig(ConfigFile.Load()).Build());
        }
        catch (Exception)
        {
            // 配置或语言文件损坏不能拦住启动：退回默认行为（缺键时显示键路径）。
        }
    }
}