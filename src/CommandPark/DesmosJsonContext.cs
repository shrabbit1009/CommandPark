using System.Text.Json.Serialization;
using CommandPark.Utils.SourceHelpers;

namespace CommandPark;

/// <summary>Desmos DTO 的 Native AOT source-generated JSON 元数据。</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DesmosHelper.DesmosBodyDto))]
[JsonSerializable(typeof(DesmosHelper.DesmosStateDto))]
internal partial class DesmosJsonContext : JsonSerializerContext;