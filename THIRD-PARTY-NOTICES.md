# 第三方组件与许可证

CommandPark 本体以 **MIT** 授权，见仓库根目录的 [LICENSE](LICENSE)。

本文件列出随程序一起分发的第三方组件、它们的许可证与版权声明，以及兼容性结论。
完整许可证文本放在 `licenses\`，随安装包与压缩包一起发布。

> 清单核对日期：2026-10-12。来源：`CommandPark.Ui.deps.json`（Release 配置）+ 各 NuGet 包的
> `.nuspec` 元数据，也就是实际随程序分发的那一批包。升级依赖后请重新核对。

## 结论

- **全部组件都与 MIT 兼容**：许可证只有 MIT、Apache-2.0、BSD-3-Clause、CC0-1.0、SIL OFL-1.1，
  没有 GPL / LGPL / AGPL / SSPL 或其它带商业限制的许可证，可以自由分发（含商业分发）。
- **Apache-2.0**（RestSharp、EverythingSearchClient、Serilog）要求随分发附上许可证文本并保留版权声明
  → 已附 `licenses\Apache-2.0.txt`。
- **BSD-3-Clause**（Antlr4.Runtime.Standard、ANGLE 原生库）要求保留版权声明、条件与免责声明
  → 已附 `licenses\BSD-3-Clause-ANTLR.txt` 与 `licenses\ANGLE-BSD-3-Clause.txt`。
- **字体**（内嵌的 JetBrains Mono 与 Inter）为 SIL OFL-1.1：可以随程序分发、可以商用，
  但不能单独售卖字体本身，也不能用 Reserved Font Name 发布修改版 → 已附两份 OFL 文本。
- **一处待处理**：`NPinyin.Core` 没有声明任何许可证，详见文末「待处理事项」。

## 运行时与框架（多为 AOT 自包含发布自带）

| 组件 | 版本 | 许可证 | 版权 |
| --- | --- | --- | --- |
| .NET 运行时与基础库 | 10.0 | MIT | © .NET Foundation and Contributors |
| Microsoft.Extensions.*（AI / Caching / Configuration / DependencyInjection / Diagnostics / FileProviders / Hosting / Logging / Options / Primitives） | 10.0.x | MIT | © .NET Foundation and Contributors |
| System.ClientModel、System.Memory.Data、System.Numerics.Tensors | 1.x / 10.0.x | MIT | © .NET Foundation and Contributors |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | MIT | © Microsoft Corporation |
| OpenAI（官方 .NET SDK） | 2.14.0 | MIT | © 2026 OpenAI |

## 界面（Avalonia 及生态）

| 组件 | 版本 | 许可证 | 版权 |
| --- | --- | --- | --- |
| Avalonia、Avalonia.Desktop、Avalonia.Win32、Avalonia.Skia、Avalonia.HarfBuzz、Avalonia.Themes.Fluent、Avalonia.Fonts.Inter、Avalonia.Controls.* 等 | 12.1.x | MIT | © 2013-2026 The AvaloniaUI Project（Avalonia Team） |
| Avalonia.Angle.Windows.Natives | 2.1.x | BSD-3-Clause | © 2018 The ANGLE Project Authors（TransGaming / Google / 3DLabs） |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | MIT | © 2019-2026 AvaloniaUI OÜ（**仅 Debug 构建引用，不随发布分发**） |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | © .NET Foundation and Contributors |
| ShadUI | 0.2.4 | MIT | © 2025 Accountech Business Management Services |
| Lucide.Avalonia（矢量图标） | 0.2.23 | MIT / 图标本体 ISC | © Compunet、© Lucide Contributors |
| Xaml.Behaviors.Avalonia 及其子包 | 12.0.7 | MIT | © Wiesław Šoltés 2026 |
| SkiaSharp、HarfBuzzSharp（含 win-x64 原生库） | 3.119.4 / 8.3.1.3 | MIT | © Microsoft Corporation |
| MicroCom.Runtime | 0.11.6 | MIT | © 2021 Nikita Tsukanov |
| Tmds.DBus.Protocol | 0.94.1 | MIT | © Tom Deseyn（仅 Linux 需要） |
| Inter 字体（由 Avalonia.Fonts.Inter 内嵌） | — | SIL OFL-1.1 | © 2016 The Inter Project Authors |
| JetBrains Mono（内嵌于 `Assets\Fonts`） | — | SIL OFL-1.1 | © 2020 The JetBrains Mono Project Authors |

## 搜索、计算与其它

| 组件 | 版本 | 许可证 | 版权 |
| --- | --- | --- | --- |
| EverythingSearchClient（Everything IPC 客户端） | 0.9.0.148 | Apache-2.0 | © 2022-2025 SGrottel |
| Lnk、ExtensionBlocks、GuidMapping（快捷方式/CLSID 解析） | 2026.5.x | MIT | © Eric Zimmerman |
| AngouriMath | 2.5.0 | MIT | © Angouri 2019-2026（WhiteBlackGoose and contributors） |
| GenericTensor | 1.0.4 | MIT | © WhiteBlackGoose |
| HonkSharp | 1.0.3 | MIT | © 2021 WhiteBlackGoose |
| PeterO.Numbers | 1.8.0 | CC0-1.0 | © Peter Occil（已放弃著作权，无需署名） |
| Antlr4.Runtime.Standard | 4.13.1 | BSD-3-Clause | © 2012-2022 The ANTLR Project |
| RestSharp | 114.0.0 | Apache-2.0 | © .NET Foundation and Contributors |
| Serilog | 4.3.1 | Apache-2.0 | © Serilog Contributors |
| NPinyin.Core | 3.0.0 | **未声明**（见文末） | NuGet 未提供版权信息 |

## 构建工具（不随程序分发）

| 工具 | 许可证 | 说明 |
| --- | --- | --- |
| Inno Setup 6 / 7（`installer\CommandPark.iss` 用它生成安装程序） | 修改版 BSD（© Jordan Russell / Martijn Laan），随附 `license.txt` 明确允许「包括商业应用在内」的任意用途 | 用它编译出的安装包可以自由分发。注意：本机装的是 `7.0.1-beta`，编译器横幅会打印 `Non-commercial use only`（同一目录的 `license.txt` 仍写明允许商业使用）；正式发布建议改用稳定版 6.x，或等 Inno Setup 7 正式版确认措辞 |
| Inno Setup 简体中文语言文件（`installer\languages\ChineseSimplified.isl`） | MIT | © 2019-2020 kirakira，<https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation> |

## 待处理事项

1. **`NPinyin.Core` 没有许可证**：NuGet 包里没有 license 字段，上游仓库
   <https://github.com/2881099/NPinyin>（以及更早的 Google Code 工程）也没有 LICENSE 文件，
   但它的代码与拼音数据会随程序分发，存在合规风险。可选处置：
   - 换成明确声明许可证的同类实现（例如 `TinyPinyin.Net`，MIT）；
   - 或自己维护一份「全拼 + 首字母」拼音表（CommandPark 只用到这两种匹配，数据量很小）；
   - 或联系作者补上许可证声明。
   在解决之前，`LICENSE` 里的 MIT 授权只覆盖 CommandPark 自己的代码。
2. **Everything 本体不随本程序分发**，只在用户已安装时通过 IPC 调用，无需附带其许可证。
3. 依赖升级后重新核对本文件：`CommandPark.Ui.deps.json` 是权威来源。
4. 发布时确保 `LICENSE`、`THIRD-PARTY-NOTICES.md` 与 `licenses\` 都在压缩包与安装目录里
   （`CommandPark.Ui.csproj` 已配置为随生成输出复制，构建流程会自动带上）。