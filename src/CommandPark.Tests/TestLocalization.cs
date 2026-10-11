using System.Runtime.CompilerServices;
using CommandPark.Strings;

namespace CommandPark.Tests;

/// <summary>
/// 把测试固定到中文基准上。
/// <para>
/// 断言里的文案都取自 <c>lang\zh_CN.json</c>，因此必须与开发机的系统 UI 语言无关；
/// 模块初始化器在程序集加载时就把全局字符串链设成「只有 zh_CN」。
/// </para>
/// </summary>
internal static class TestLocalization
{
    [ModuleInitializer]
    internal static void Initialize() =>
        Localization.Configure(
            StringCollectionBuilder.Create()
                                    .WithLanguage("zh_CN")
                                    .WithFallback()
                                    .Build());
}