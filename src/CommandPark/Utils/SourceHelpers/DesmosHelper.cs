using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RestSharp;

namespace CommandPark.Utils.SourceHelpers;

/// <summary>
/// 逆向的 Desmos 快照获取。可从 LaTeX 转换为 Desmos 快照链接，供直接跳转。
/// </summary>
public class DesmosHelper
{
    /// <summary>
    /// 生成 Desmos 图表快照 URL。
    /// </summary>
    /// <param name="latex">LaTeX 公式</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>URL</returns>
    /// <remarks>
    /// 可通过 AngouriMath 提供的 String 类型扩展方法 <c>Latexize()</c> 进行普通表达式和 LaTeX 表达式的转换。
    /// </remarks>
    public static async Task<string> GenerateDesmosSnapshotUrl(string latex, CancellationToken cancellationToken = default)
    {
        using var client = new RestClient();
        var dto = new DesmosBodyDto
        {
            State = JsonSerializer.Serialize(new DesmosStateDto(latex), DesmosJsonContext.Default.DesmosStateDto),
        };

        var req = new RestRequest("https://www.desmos.com/api/v1/calculator-shared/save", Method.Post);
        req.AddStringBody(JsonSerializer.Serialize(dto, DesmosJsonContext.Default.DesmosBodyDto), DataFormat.Json);

        var response = await client.ExecuteAsync(req, cancellationToken);

        return $"https://www.desmos.com/calculator/{dto.Hash}";
    }

    /// <summary>
    /// 生成固定长度的随机字符串
    /// </summary>
    /// <param name="len">字符串长度</param>
    /// <returns>使用默认随机数生成器生成，可能包含 0~9, a~z，随机出现。</returns>
    private static string RandomString(int len)
    {
        const string set = "0123456789abcdefghijklmnopqrstuvwxyz";
        var          str = new char[len];

        for (int i = 0; i < len; i++)
        {
            str[i] = set[Random.Shared.Next(0, set.Length)];
        }

        return new string(str);
    }

    public class DesmosBodyDto
    {
        /// <summary>
        /// 生成一个 10 位随机字符串即可。
        /// </summary>
        [JsonPropertyName("hash")] public string Hash { get; set; } = RandomString(10);

        /// <summary>
        /// 语言，按系统自动设定。
        /// </summary>
        [JsonPropertyName("lang")] public string Lang { get; set; } = CultureInfo.CurrentUICulture.IetfLanguageTag;

        /// <summary>
        /// 产品，固定值。
        /// </summary>
        [JsonPropertyName("product")] public string Product { get; set; } = "graphing";

        /// <summary>
        /// 存放了编辑的图表 JSON 状态数据。
        /// </summary>
        [JsonPropertyName("state")] public string State { get; set; } = "{}";

        /// <summary>
        /// 缩略图 Base64 PNG 字符串。这里不需要，因此置最小 PNG。
        /// </summary>
        [JsonPropertyName("thumbnailData")] public string ThumbnailData { get; set; } = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABAQAAAAA3bvkkAAAACklEQVR4AWNgAAAAAgABc3UBGAAAAABJRU5ErkJggg==";

        /// <summary>
        /// 连接类型，固定值。
        /// </summary>
        [JsonPropertyName("type")] public string Type { get; set; } = "quick-link";
    }

    /// <summary>
    /// Desmos 图表状态
    /// </summary>
    /// <param name="latex">LaTeX 表达式</param>
    public class DesmosStateDto(string latex)
    {
        public DesmosStateDto() : this(string.Empty)
        {

        }

        /// <summary>
        /// 版本，固定值。
        /// </summary>
        [JsonPropertyName("version")] public int Version { get; set; } = 11;

        /// <summary>
        /// 随机种子，固定值。
        /// </summary>
        [JsonPropertyName("randomSeed")] public string RandomSeed { get; set; } = RandomString(32);

        /// <summary>
        /// 图视口信息，固定值。
        /// </summary>
        [JsonPropertyName("graph")]
        public Graph Graph { get; set; } = new()
        {
            V12ViewportLatexStash = new V12ViewportLatexStash
            {
                Xmax = "10",
                Xmin = "-10",
                Ymin = "-6.305858987090367",
                Ymax = "6.305858987090367"
            },
            Viewport = new Viewport
            {
                Xmax = 10,
                Xmin = -10,
                Ymin = -6.305858987090367,
                Ymax = 6.305858987090367
            }
        };

        /// <summary>
        /// 核心，存放了表达式列表。
        /// </summary>
        [JsonPropertyName("expressions")]
        public Expressions Expressions { get; set; } = new()
        {
            List =
            [
                new ListData
                {
                    Type  = "expression",
                    Id    = "1",
                    Color = "#c74440",
                    Latex = latex
                }
            ]
        };

        /// <summary>
        /// 固定值
        /// </summary>
        [JsonPropertyName("includeFunctionParametersInRandomSeed")]
        public bool IncludeFunctionParametersInRandomSeed { get; set; } = true;

        /// <summary>
        /// 固定值
        /// </summary>
        [JsonPropertyName("doNotMigrateMovablePointStyle")]
        public bool DoNotMigrateMovablePointStyle { get; set; } = true;

        /// <summary>
        /// 固定值
        /// </summary>
        [JsonPropertyName("backmigrated")] public int Backmigrated { get; set; } = 14;
    }

    #region 子 DTO 定义
    public class Expressions
    {
        [JsonPropertyName("list")] public List<ListData> List { get; set; } = [];
    }

    public class Graph
    {
        [JsonPropertyName("viewport")] public Viewport Viewport { get; set; } = new ();

        [JsonPropertyName("__v12ViewportLatexStash")]
        public V12ViewportLatexStash V12ViewportLatexStash { get; set; } = new ();
    }

    public class ListData
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

        [JsonPropertyName("color")] public string Color { get; set; } = string.Empty;

        [JsonPropertyName("latex")] public string Latex { get; set; } = string.Empty;
    }

    public class V12ViewportLatexStash
    {
        [JsonPropertyName("xmin")] public string Xmin { get; set; } = string.Empty;

        [JsonPropertyName("xmax")] public string Xmax { get; set; } = string.Empty;

        [JsonPropertyName("ymin")] public string Ymin { get; set; } = string.Empty;

        [JsonPropertyName("ymax")] public string Ymax { get; set; } = string.Empty;
    }

    public class Viewport
    {
        [JsonPropertyName("xmin")] public int Xmin { get; set; }

        [JsonPropertyName("ymin")] public double Ymin { get; set; }

        [JsonPropertyName("xmax")] public int Xmax { get; set; }

        [JsonPropertyName("ymax")] public double Ymax { get; set; }
    }
    #endregion
}
