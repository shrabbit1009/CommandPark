namespace CommandPark.Utils;

/// <summary>
/// 统一的文本相关度评分。
/// <para>
/// 返回值范围为 0..1，并按匹配质量分层：完全匹配 &gt; 前缀匹配 &gt; 词边界/包含匹配 &gt; 模糊匹配。
/// 调用方可以在结果上叠加自己的权重（例如 <c>ap:</c> 前缀提升），但不能改变同一查询下的质量顺序。
/// </para>
/// </summary>
public static class ComputeSimilarityHelper
{
    private const float ExactMatchScore = 1.00f;

    private const float PrefixBaseScore = 0.88f;
    private const float PrefixCoverageWeight = 0.10f;

    private const float ContainsBaseScore = 0.62f;
    private const float ContainsCoverageWeight = 0.20f;
    private const float WordBoundaryBonus = 0.06f;
    private const float PositionPenaltyWeight = 0.04f;

    private const float FuzzySequenceWeight = 0.30f;
    private const float FuzzySubstringWeight = 0.25f;
    private const float FuzzyTargetCoverageWeight = 0.05f;
    private const float FuzzyMaxScore = 0.60f;

    /// <summary>
    /// 计算输入与目标文本的相关度。大小写不敏感，首尾空格会被忽略。
    /// </summary>
    public static float ComputeSimilarity(string input, string target)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(target))
        {
            return 0f;
        }

        var query = input.Trim();
        var candidate = target.Trim();
        if (query.Length == 0 || candidate.Length == 0)
        {
            return 0f;
        }

        // 完全相同必须是唯一最高优先级；旧实现中 cmd 与 jcmd 都是 3 分，导致顺序取决于 PATH。
        if (string.Equals(query, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return ExactMatchScore;
        }

        var index = candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase);

        // 前缀匹配：同时考虑查询覆盖目标文本的比例。
        // 例如 "cmd.exe" 会比 "jcmd.exe" 更相关。
        if (index == 0)
        {
            var coverage = (float)query.Length / candidate.Length;
            return PrefixBaseScore + PrefixCoverageWeight * coverage;
        }

        // 包含匹配：越靠前、覆盖比例越高、位于词边界时分数越高。
        if (index > 0)
        {
            var coverage = (float)query.Length / candidate.Length;
            var positionPenalty = PositionPenaltyWeight * index / candidate.Length;
            var boundaryBonus = IsWordBoundary(candidate, index) ? WordBoundaryBonus : 0f;

            return Math.Clamp(
                ContainsBaseScore + ContainsCoverageWeight * coverage + boundaryBonus - positionPenalty,
                0f,
                PrefixBaseScore - 0.01f);
        }

        // 不连续匹配作为兜底：保留原来的 LCS/最长公共子串能力，但明确压低权重。
        var lcsLength = LcsLength(query, candidate);
        if (lcsLength == 0)
        {
            return 0f;
        }

        var sequenceCoverage = (float)lcsLength / query.Length;
        var substringCoverage = (float)LongestCommonSubstring(query, candidate) / query.Length;
        var targetCoverage = (float)lcsLength / candidate.Length;

        var fuzzyScore =
            FuzzySequenceWeight * sequenceCoverage +
            FuzzySubstringWeight * substringCoverage +
            FuzzyTargetCoverageWeight * targetCoverage;

        return Math.Clamp(fuzzyScore, 0f, FuzzyMaxScore);
    }

    public static int LcsLength(string a, string b)
    {
        // 确保 b 是较短的那个，减少空间
        if (a.Length < b.Length) (a, b) = (b, a);

        int   m    = a.Length, n = b.Length;
        int[] prev = new int[n + 1];
        int[] curr = new int[n + 1];

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
                curr[j] = a[i - 1] == b[j - 1]
                    ? prev[j - 1] + 1
                    : Math.Max(prev[j], curr[j - 1]);

            (prev, curr) = (curr, prev);
            Array.Clear(curr, 0, n + 1);
        }

        return prev[n];
    }

    public static int LongestCommonSubstring(string a, string b)
    {
        int   n    = a.Length, m = b.Length, maxLen = 0;
        int[] prev = new int[m + 1];

        for (int i = 1; i <= n; i++)
        {
            // 必须从右往左遍历，避免覆盖上一行的值
            for (int j = m; j >= 1; j--)
            {
                prev[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : 0;
                maxLen  = Math.Max(maxLen, prev[j]);
            }

            prev[0] = 0;
        }

        return maxLen;
    }

    private static bool IsWordBoundary(string value, int index)
    {
        if (index <= 0)
        {
            return true;
        }

        var previous = value[index - 1];
        var current = value[index];

        return !char.IsLetterOrDigit(previous) ||
               (char.IsLower(previous) && char.IsUpper(current));
    }
}
