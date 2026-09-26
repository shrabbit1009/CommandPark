namespace CommandPark;

/// <summary>
/// <see cref="IHintWithPrefix"/> 的公共前缀解析。
/// <para>
/// 每个源自己声明前缀（Everything 是 <c>#</c> / <c>et:</c>，已安装程序是 <c>ap:</c> / <c>app:</c>），
/// 规则统一为：命中前缀 → 去掉前缀后再搜索，并给该源的结果**加权**；
/// 配合 <see cref="IHintWithPrefix.PrefixMode"/>：false 时不带前缀也搜索（带前缀只是加权），
/// true 时只有带前缀才触发。
/// </para>
/// </summary>
public static class HintPrefix
{
    /// <summary>
    /// 尝试从查询里剥掉前缀。
    /// </summary>
    /// <param name="query">用户输入的原始查询。</param>
    /// <param name="rawQuery">剥离前缀后的查询（没命中前缀时等于原查询）。</param>
    /// <param name="prefixes">该源支持的前缀（长前缀优先匹配，例如 "app:" 先于 "ap:"）。</param>
    /// <returns>是否命中前缀。</returns>
    public static bool TryStrip(string query, out string rawQuery, params string[] prefixes)
    {
        var trimmed = query.Trim();

        // 长前缀优先：避免 "app:" 被 "ap:" 抢先匹配
        var ordered = prefixes.Where(p => !string.IsNullOrEmpty(p)).OrderByDescending(p => p.Length);

        foreach (var prefix in ordered)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rawQuery = trimmed[prefix.Length..].Trim();
                return true;
            }
        }

        rawQuery = query;
        return false;
    }
}