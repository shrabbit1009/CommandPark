using CommandPark.Strings;

namespace CommandPark.Ai;

/// <summary>
/// AI 模式使用的系统提示词。
/// <para>
/// 文案放在语言文件里（<c>ai.prompt.*</c>），跟随界面语言：用中文界面时让模型用中文回答，
/// 用英文界面时让它用英文回答。
/// </para>
/// </summary>
public static class AiPrompts
{
    /// <summary>
    /// Hint 筛选模式的系统提示词。
    /// <para>
    /// 关键约束：最后一行必须且只能是一个单行 JSON 字符串数组，元素是工具返回过的短哈希；
    /// 不允许任何包装（代码块、对象字段、额外说明行都会破坏解析）。
    /// </para>
    /// </summary>
    /// <param name="maxResults">最终最多挑选的结果条数。</param>
    public static string HintFilter(int maxResults) =>
        Localization.Format("ai.prompt.hintFilter", maxResults);

    /// <summary>AI 聊天模式的系统提示词。</summary>
    public static string Chat => Localization.Get("ai.prompt.chat");
}