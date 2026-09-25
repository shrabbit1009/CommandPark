namespace CommandPark;

public static class ComputeSimilarityHelper
{ 
    /// <summary>
    /// 简单计算相似。权重：50% 序列相似，50% 仅出现字符串。
    /// </summary>
    public static float ComputeSimilarity(string input, string target)
    {
        var aResult = LongestCommonSubstring(input, target);
        var bResult = LcsLength(target, input);

        return (float)((float)aResult * .5 + (float)bResult * .5);
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
}