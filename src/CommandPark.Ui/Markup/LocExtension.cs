using System;
using Avalonia.Markup.Xaml;
using CommandPark.Strings;

namespace CommandPark.Ui.Markup;

/// <summary>
/// XAML 标记扩展：<c>{loc:Loc ui.main.searching}</c> 直接取语言文件里的字符串。
/// <para>
/// 取值发生在 XAML 加载（窗口构造）时，此时本地化已经配置好；界面语言在运行期不会切换，
/// 因此这里不需要额外的通知机制。键缺失时回退成键路径，便于一眼看出漏翻译的地方。
/// </para>
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    /// <summary>字符串路径，例如 <c>ui.ai.title</c>。</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Localization.Get(Key);
}