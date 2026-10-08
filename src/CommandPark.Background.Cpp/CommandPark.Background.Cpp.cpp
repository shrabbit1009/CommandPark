// -----------------------------------------------------------------------------
// CommandPark.Background.Cpp
//
// CommandPark 的后台常驻程序（由主程序在首次启动时拉起）：
//   * 从程序目录的 commandpark.json（兼容 config.json）读取全局快捷键 hotkey；
//   * 常驻后台监听全局快捷键，按下一次唤起一次主程序；
//   * 在托盘区创建图标，上下文菜单只有“修改配置”和“退出”；
//   * 启动后把自身 PID 写入程序目录下的 PID 文件，供主程序判断后台是否已经在运行；
//   * 单实例：已经有一个后台在运行时直接退出；
//   * hotkey 不填时使用默认快捷键 Ctrl+Alt+D；显式写 null 时直接退出，不做任何事。
//
// 代码组织尽量贴近 C# 风格：PascalCase 方法名、static 工具类、只读风格字段等。
// -----------------------------------------------------------------------------

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif
#ifndef WINVER
#define WINVER 0x0A00
#endif

#include <Windows.h>
#include <shellapi.h>
#include <strsafe.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <format>
#include <optional>
#include <string>
#include <vector>

#pragma comment(lib, "shell32.lib")

// MOD_NOREPEAT：长按快捷键只触发一次（Windows 7 及以上）。
#ifndef MOD_NOREPEAT
#define MOD_NOREPEAT 0x4000
#endif

namespace CommandPark::Background
{
    // =========================================================================
    // 常量
    // =========================================================================

    /// <summary>程序用到的固定常量。</summary>
    class Constants
    {
    public:
        static constexpr wchar_t WindowClassName[] = L"CommandPark.Background.Window";
        static constexpr wchar_t SingleInstanceMutexName[] = L"Local\\CommandPark.Background.SingleInstance";

        static constexpr wchar_t ConfigFileName[] = L"commandpark.json";
        static constexpr wchar_t AlternateConfigFileName[] = L"config.json";
        static constexpr wchar_t PidFileName[] = L"PID";

        static constexpr UINT HotKeyId = 0x4350;              // "CP"
        static constexpr UINT TrayIconId = 1;
        static constexpr UINT TrayCallbackMessage = WM_APP + 1;
        static constexpr UINT TrayMenuEditConfig = 40001;
        static constexpr UINT TrayMenuExit = 40002;

        static constexpr wchar_t ApplicationTitle[] = L"CommandPark";
        static constexpr wchar_t TrayTooltip[] = L"CommandPark 后台运行中";

        /// <summary>默认全局快捷键；必须与主程序 BackgroundLauncher.DefaultHotKey 保持一致。</summary>
        static constexpr wchar_t DefaultHotKey[] = L"Ctrl+Alt+D";
    };

    // =========================================================================
    // 字符串工具
    // =========================================================================

    /// <summary>字符串辅助方法，对应 C# 里的 string 静态方法。</summary>
    class StringHelper
    {
    public:
        static std::wstring Trim(const std::wstring& value)
        {
            const size_t begin = value.find_first_not_of(L" \t\r\n\f\v");
            if (begin == std::wstring::npos)
            {
                return std::wstring();
            }

            const size_t end = value.find_last_not_of(L" \t\r\n\f\v");
            return value.substr(begin, end - begin + 1);
        }

        static std::wstring ToUpperAscii(std::wstring value)
        {
            for (wchar_t& ch : value)
            {
                if (ch >= L'a' && ch <= L'z')
                {
                    ch = static_cast<wchar_t>(ch - L'a' + L'A');
                }
            }

            return value;
        }

        static bool EqualsIgnoreCase(const std::wstring& left, const std::wstring& right)
        {
            return CompareStringOrdinal(
                       left.c_str(), static_cast<int>(left.size()),
                       right.c_str(), static_cast<int>(right.size()),
                       TRUE) == CSTR_EQUAL;
        }

        static std::wstring ToWide(const std::string& utf8)
        {
            if (utf8.empty())
            {
                return std::wstring();
            }

            const int length = MultiByteToWideChar(
                CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
            if (length <= 0)
            {
                return std::wstring();
            }

            std::wstring result(static_cast<size_t>(length), L'\0');
            MultiByteToWideChar(CP_UTF8, 0, utf8.data(), static_cast<int>(utf8.size()),
                                result.data(), length);
            return result;
        }

        static std::vector<std::wstring> Split(const std::wstring& value, wchar_t separator)
        {
            std::vector<std::wstring> parts;
            size_t start = 0;

            while (true)
            {
                const size_t index = value.find(separator, start);
                if (index == std::wstring::npos)
                {
                    parts.push_back(value.substr(start));
                    break;
                }

                parts.push_back(value.substr(start, index - start));
                start = index + 1;
            }

            return parts;
        }
    };

    // =========================================================================
    // 路径与文件工具
    // =========================================================================

    /// <summary>路径辅助方法。</summary>
    class PathHelper
    {
    public:
        static std::wstring GetDirectoryPath(const std::wstring& path)
        {
            const size_t index = path.find_last_of(L"\\/");
            if (index == std::wstring::npos)
            {
                return std::wstring();
            }

            return path.substr(0, index);
        }

        static std::wstring Combine(const std::wstring& directory, const std::wstring& name)
        {
            if (directory.empty())
            {
                return name;
            }

            const wchar_t last = directory.back();
            if (last == L'\\' || last == L'/')
            {
                return directory + name;
            }

            return directory + L"\\" + name;
        }

        static std::wstring GetFullPath(const std::wstring& path)
        {
            if (path.empty())
            {
                return path;
            }

            const DWORD required = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
            if (required == 0)
            {
                return path;
            }

            std::wstring buffer(static_cast<size_t>(required), L'\0');
            const DWORD written = GetFullPathNameW(path.c_str(), required, buffer.data(), nullptr);
            if (written == 0)
            {
                return path;
            }

            buffer.resize(written);
            return buffer;
        }

        static bool FileExists(const std::wstring& path)
        {
            return !path.empty() && GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES;
        }
    };

    /// <summary>文件读写辅助方法。</summary>
    class FileHelper
    {
    public:
        static bool TryReadAllBytes(const std::wstring& path, std::string& content)
        {
            content.clear();

            const HANDLE handle = CreateFileW(
                path.c_str(), GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (handle == INVALID_HANDLE_VALUE)
            {
                return false;
            }

            LARGE_INTEGER size = {};
            if (!GetFileSizeEx(handle, &size) || size.QuadPart < 0 || size.QuadPart > 16 * 1024 * 1024)
            {
                CloseHandle(handle);
                return false;
            }

            content.resize(static_cast<size_t>(size.QuadPart));

            size_t offset = 0;
            bool success = true;
            while (offset < content.size())
            {
                const DWORD chunk = static_cast<DWORD>(
                    std::min<size_t>(content.size() - offset, 1u << 20));
                DWORD read = 0;
                if (!ReadFile(handle, content.data() + offset, chunk, &read, nullptr) || read == 0)
                {
                    success = false;
                    break;
                }

                offset += read;
            }

            CloseHandle(handle);
            if (!success)
            {
                content.clear();
            }

            return success;
        }

        static bool WriteAllBytes(const std::wstring& path, const void* data, size_t size)
        {
            const HANDLE handle = CreateFileW(
                path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                FILE_ATTRIBUTE_NORMAL, nullptr);
            if (handle == INVALID_HANDLE_VALUE)
            {
                return false;
            }

            const std::uint8_t* bytes = static_cast<const std::uint8_t*>(data);
            size_t remaining = size;
            bool success = true;

            while (remaining > 0)
            {
                const DWORD chunk = static_cast<DWORD>(
                    std::min<size_t>(remaining, 1u << 20));
                DWORD written = 0;
                if (!WriteFile(handle, bytes, chunk, &written, nullptr) || written == 0)
                {
                    success = false;
                    break;
                }

                bytes += written;
                remaining -= written;
            }

            CloseHandle(handle);
            return success;
        }

        static bool WriteAllText(const std::wstring& path, const std::string& text)
        {
            return WriteAllBytes(path, text.data(), text.size());
        }
    };

    // =========================================================================
    // 极简 JSON 扫描器
    // =========================================================================

    /// <summary>
    /// 极简 JSON 扫描器：只需要读取顶层对象的某个属性（字符串或 null）。
    /// 兼容 UTF-8 BOM、// 与 /* */ 注释、尾随逗号。
    /// </summary>
    class JsonScanner
    {
    public:
        explicit JsonScanner(const std::string& text)
            : _text(text), _index(0)
        {
        }

        bool TryReadTopLevelProperty(
            const std::string& propertyName,
            bool& found,
            bool& isNull,
            std::string& value,
            std::string& error)
        {
            found = false;
            isNull = false;
            value.clear();
            error.clear();
            _index = 0;

            SkipWhitespace();
            if (!Match('{'))
            {
                error = "JSON 根节点必须是对象。";
                return false;
            }

            SkipWhitespace();
            if (Match('}'))
            {
                return true;
            }

            while (true)
            {
                SkipWhitespace();

                std::string key;
                if (!ReadString(key, error))
                {
                    return false;
                }

                SkipWhitespace();
                if (!Match(':'))
                {
                    error = "JSON 属性名后缺少冒号。";
                    return false;
                }

                SkipWhitespace();

                if (!found && EqualsIgnoreCaseAscii(key, propertyName))
                {
                    found = true;

                    if (MatchLiteral("null"))
                    {
                        isNull = true;
                    }
                    else if (Peek() == '"')
                    {
                        if (!ReadString(value, error))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // 非字符串一律视为“未设置快捷键”。
                        if (!SkipValue(error))
                        {
                            return false;
                        }

                        isNull = true;
                        value.clear();
                    }
                }
                else
                {
                    if (!SkipValue(error))
                    {
                        return false;
                    }
                }

                SkipWhitespace();
                if (Match(','))
                {
                    SkipWhitespace();
                    if (Match('}'))
                    {
                        return true;
                    }

                    continue;
                }

                if (Match('}'))
                {
                    return true;
                }

                error = "JSON 对象格式不正确。";
                return false;
            }
        }

    private:
        const std::string& _text;
        size_t _index;

        char Peek() const
        {
            return _index < _text.size() ? _text[_index] : '\0';
        }

        bool Match(char expected)
        {
            if (Peek() != expected)
            {
                return false;
            }

            ++_index;
            return true;
        }

        bool MatchLiteral(const char* literal)
        {
            const size_t length = std::strlen(literal);
            if (_text.compare(_index, length, literal) != 0)
            {
                return false;
            }

            _index += length;
            return true;
        }

        void SkipWhitespace()
        {
            while (_index < _text.size())
            {
                const char ch = _text[_index];
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n' || ch == '\f' || ch == '\v')
                {
                    ++_index;
                    continue;
                }

                if (ch == '/' && _index + 1 < _text.size())
                {
                    const char next = _text[_index + 1];
                    if (next == '/')
                    {
                        _index += 2;
                        while (_index < _text.size() && _text[_index] != '\n')
                        {
                            ++_index;
                        }

                        continue;
                    }

                    if (next == '*')
                    {
                        _index += 2;
                        while (_index + 1 < _text.size() &&
                               !(_text[_index] == '*' && _text[_index + 1] == '/'))
                        {
                            ++_index;
                        }

                        if (_index + 1 < _text.size())
                        {
                            _index += 2;
                        }

                        continue;
                    }
                }

                break;
            }
        }

        static bool EqualsIgnoreCaseAscii(const std::string& left, const std::string& right)
        {
            if (left.size() != right.size())
            {
                return false;
            }

            for (size_t index = 0; index < left.size(); ++index)
            {
                char a = left[index];
                char b = right[index];
                if (a >= 'A' && a <= 'Z')
                {
                    a = static_cast<char>(a - 'A' + 'a');
                }

                if (b >= 'A' && b <= 'Z')
                {
                    b = static_cast<char>(b - 'A' + 'a');
                }

                if (a != b)
                {
                    return false;
                }
            }

            return true;
        }

        bool ReadString(std::string& value, std::string& error)
        {
            if (!Match('"'))
            {
                error = "JSON 字符串缺少起始引号。";
                return false;
            }

            value.clear();

            while (true)
            {
                if (_index >= _text.size())
                {
                    error = "JSON 字符串缺少结束引号。";
                    return false;
                }

                const char ch = _text[_index++];
                if (ch == '"')
                {
                    return true;
                }

                if (ch != '\\')
                {
                    value.push_back(ch);
                    continue;
                }

                if (_index >= _text.size())
                {
                    error = "JSON 转义序列不完整。";
                    return false;
                }

                const char escaped = _text[_index++];
                switch (escaped)
                {
                case '"': value.push_back('"'); break;
                case '\\': value.push_back('\\'); break;
                case '/': value.push_back('/'); break;
                case 'b': value.push_back('\b'); break;
                case 'f': value.push_back('\f'); break;
                case 'n': value.push_back('\n'); break;
                case 'r': value.push_back('\r'); break;
                case 't': value.push_back('\t'); break;
                case 'u':
                {
                    unsigned int code = 0;
                    if (!ReadHex4(code, error))
                    {
                        return false;
                    }

                    if (code >= 0xD800 && code <= 0xDBFF &&
                        _index + 1 < _text.size() && _text[_index] == '\\' && _text[_index + 1] == 'u')
                    {
                        _index += 2;

                        unsigned int low = 0;
                        if (!ReadHex4(low, error))
                        {
                            return false;
                        }

                        if (low >= 0xDC00 && low <= 0xDFFF)
                        {
                            code = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00);
                        }
                    }

                    AppendUtf8(value, code);
                    break;
                }
                default:
                    error = "JSON 中出现未知的转义序列。";
                    return false;
                }
            }
        }

        bool ReadHex4(unsigned int& value, std::string& error)
        {
            if (_index + 4 > _text.size())
            {
                error = "JSON \\u 转义不完整。";
                return false;
            }

            value = 0;
            for (int index = 0; index < 4; ++index)
            {
                const char ch = _text[_index++];
                unsigned int digit = 0;
                if (ch >= '0' && ch <= '9')
                {
                    digit = static_cast<unsigned int>(ch - '0');
                }
                else if (ch >= 'a' && ch <= 'f')
                {
                    digit = static_cast<unsigned int>(ch - 'a' + 10);
                }
                else if (ch >= 'A' && ch <= 'F')
                {
                    digit = static_cast<unsigned int>(ch - 'A' + 10);
                }
                else
                {
                    error = "JSON \\u 转义包含非法字符。";
                    return false;
                }

                value = (value << 4) | digit;
            }

            return true;
        }

        static void AppendUtf8(std::string& output, unsigned int code)
        {
            if (code <= 0x7F)
            {
                output.push_back(static_cast<char>(code));
            }
            else if (code <= 0x7FF)
            {
                output.push_back(static_cast<char>(0xC0 | (code >> 6)));
                output.push_back(static_cast<char>(0x80 | (code & 0x3F)));
            }
            else if (code <= 0xFFFF)
            {
                output.push_back(static_cast<char>(0xE0 | (code >> 12)));
                output.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
                output.push_back(static_cast<char>(0x80 | (code & 0x3F)));
            }
            else
            {
                output.push_back(static_cast<char>(0xF0 | (code >> 18)));
                output.push_back(static_cast<char>(0x80 | ((code >> 12) & 0x3F)));
                output.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
                output.push_back(static_cast<char>(0x80 | (code & 0x3F)));
            }
        }

        bool SkipValue(std::string& error)
        {
            SkipWhitespace();

            const char ch = Peek();
            if (ch == '"')
            {
                std::string discarded;
                return ReadString(discarded, error);
            }

            if (ch == '{')
            {
                return SkipObject(error);
            }

            if (ch == '[')
            {
                return SkipArray(error);
            }

            if (MatchLiteral("true") || MatchLiteral("false") || MatchLiteral("null"))
            {
                return true;
            }

            if (ch == '-' || ch == '+' || (ch >= '0' && ch <= '9'))
            {
                SkipNumber();
                return true;
            }

            error = "JSON 值格式不正确。";
            return false;
        }

        bool SkipObject(std::string& error)
        {
            if (!Match('{'))
            {
                error = "JSON 对象格式不正确。";
                return false;
            }

            SkipWhitespace();
            if (Match('}'))
            {
                return true;
            }

            while (true)
            {
                SkipWhitespace();

                std::string key;
                if (!ReadString(key, error))
                {
                    return false;
                }

                SkipWhitespace();
                if (!Match(':'))
                {
                    error = "JSON 属性名后缺少冒号。";
                    return false;
                }

                if (!SkipValue(error))
                {
                    return false;
                }

                SkipWhitespace();
                if (Match(','))
                {
                    SkipWhitespace();
                    if (Match('}'))
                    {
                        return true;
                    }

                    continue;
                }

                if (Match('}'))
                {
                    return true;
                }

                error = "JSON 对象格式不正确。";
                return false;
            }
        }

        bool SkipArray(std::string& error)
        {
            if (!Match('['))
            {
                error = "JSON 数组格式不正确。";
                return false;
            }

            SkipWhitespace();
            if (Match(']'))
            {
                return true;
            }

            while (true)
            {
                if (!SkipValue(error))
                {
                    return false;
                }

                SkipWhitespace();
                if (Match(','))
                {
                    SkipWhitespace();
                    if (Match(']'))
                    {
                        return true;
                    }

                    continue;
                }

                if (Match(']'))
                {
                    return true;
                }

                error = "JSON 数组格式不正确。";
                return false;
            }
        }

        void SkipNumber()
        {
            while (_index < _text.size())
            {
                const char ch = _text[_index];
                if ((ch >= '0' && ch <= '9') || ch == '-' || ch == '+' ||
                    ch == '.' || ch == 'e' || ch == 'E')
                {
                    ++_index;
                }
                else
                {
                    break;
                }
            }
        }
    };

    // =========================================================================
    // 配置读取
    // =========================================================================

    /// <summary>快捷键配置的读取结果。</summary>
    struct HotKeyConfigResult
    {
        /// <summary>是否启用后台热键监听；显式写 null / 空字符串时为 false。</summary>
        bool Enabled = false;

        /// <summary>快捷键文本，例如 Ctrl+Alt+D。</summary>
        std::wstring Value;

        /// <summary>读取或解析配置时的错误信息；为空表示没有错误。</summary>
        std::wstring Error;
    };

    /// <summary>读取程序目录中的 commandpark.json（兼容 config.json）。</summary>
    class ConfigReader
    {
    public:
        static std::wstring ResolveConfigPath(const std::wstring& directory)
        {
            const std::wstring primary = PathHelper::Combine(directory, Constants::ConfigFileName);
            if (PathHelper::FileExists(primary))
            {
                return primary;
            }

            const std::wstring alternate = PathHelper::Combine(directory, Constants::AlternateConfigFileName);
            if (PathHelper::FileExists(alternate))
            {
                return alternate;
            }

            // 文件都不存在时返回默认路径，方便“修改配置”直接打开它。
            return primary;
        }

        static HotKeyConfigResult ReadHotKey(const std::wstring& configPath)
        {
            std::string json;
            if (!FileHelper::TryReadAllBytes(configPath, json))
            {
                // 没有配置文件：使用默认快捷键。
                return CreateDefaultResult();
            }

            // 去掉 UTF-8 BOM。
            if (json.size() >= 3 &&
                static_cast<unsigned char>(json[0]) == 0xEF &&
                static_cast<unsigned char>(json[1]) == 0xBB &&
                static_cast<unsigned char>(json[2]) == 0xBF)
            {
                json.erase(0, 3);
            }

            JsonScanner scanner(json);

            bool found = false;
            bool isNull = false;
            std::string value;
            std::string error;

            if (!scanner.TryReadTopLevelProperty("hotkey", found, isNull, value, error))
            {
                HotKeyConfigResult result = CreateDefaultResult();
                result.Error = L"配置文件格式不正确：" + StringHelper::ToWide(error);
                return result;
            }

            if (!found)
            {
                // 配置里没写 hotkey：使用默认快捷键。
                return CreateDefaultResult();
            }

            if (isNull)
            {
                // 显式写 null：关闭后台热键监听。
                return HotKeyConfigResult();
            }

            const std::wstring text = StringHelper::Trim(StringHelper::ToWide(value));
            if (text.empty())
            {
                return HotKeyConfigResult();
            }

            HotKeyConfigResult result;
            result.Enabled = true;
            result.Value = text;
            return result;
        }

    private:
        static HotKeyConfigResult CreateDefaultResult()
        {
            HotKeyConfigResult result;
            result.Enabled = true;
            result.Value = Constants::DefaultHotKey;
            return result;
        }
    };

    // =========================================================================
    // 全局快捷键解析
    // =========================================================================

    /// <summary>一条全局快捷键定义。</summary>
    struct HotKeyDefinition
    {
        /// <summary>修饰键（MOD_CONTROL / MOD_ALT / MOD_SHIFT / MOD_WIN）。</summary>
        UINT Modifiers = 0;

        /// <summary>虚拟键码。</summary>
        UINT VirtualKey = 0;

        /// <summary>原始显示文本，例如 Ctrl+Alt+Space。</summary>
        std::wstring Display;
    };

    /// <summary>把“修饰符+按键”文本解析为 RegisterHotKey 需要的参数。</summary>
    class HotKeyParser
    {
    public:
        static bool TryParse(const std::wstring& text, HotKeyDefinition& hotKey, std::wstring& error)
        {
            error.clear();

            const std::vector<std::wstring> parts = StringHelper::Split(StringHelper::Trim(text), L'+');

            UINT modifiers = 0;
            UINT virtualKey = 0;
            bool hasKey = false;

            for (const std::wstring& rawPart : parts)
            {
                const std::wstring part = StringHelper::ToUpperAscii(StringHelper::Trim(rawPart));
                if (part.empty())
                {
                    error = L"快捷键中出现空的按键片段。";
                    return false;
                }

                if (part == L"CTRL" || part == L"CONTROL")
                {
                    modifiers |= MOD_CONTROL;
                    continue;
                }

                if (part == L"ALT")
                {
                    modifiers |= MOD_ALT;
                    continue;
                }

                if (part == L"SHIFT")
                {
                    modifiers |= MOD_SHIFT;
                    continue;
                }

                if (part == L"WIN" || part == L"WINDOWS" || part == L"META" || part == L"SUPER")
                {
                    modifiers |= MOD_WIN;
                    continue;
                }

                if (hasKey)
                {
                    error = L"快捷键只能包含一个主按键。";
                    return false;
                }

                if (!TryResolveVirtualKey(part, virtualKey))
                {
                    error = L"无法识别的按键：" + rawPart;
                    return false;
                }

                hasKey = true;
            }

            if (!hasKey)
            {
                error = L"快捷键缺少主按键，例如 Ctrl+Alt+Space。";
                return false;
            }

            if (modifiers == 0)
            {
                error = L"快捷键至少需要一个修饰键（Ctrl / Alt / Shift / Win）。";
                return false;
            }

            hotKey.Modifiers = modifiers;
            hotKey.VirtualKey = virtualKey;
            hotKey.Display = StringHelper::Trim(text);
            return true;
        }

    private:
        struct NamedKey
        {
            const wchar_t* Name;
            UINT VirtualKey;
        };

        struct SymbolKey
        {
            wchar_t Symbol;
            UINT VirtualKey;
        };

        static bool TryResolveVirtualKey(const std::wstring& token, UINT& virtualKey)
        {
            virtualKey = 0;

            if (token.empty())
            {
                return false;
            }

            if (token.size() == 1)
            {
                const wchar_t ch = token[0];
                if ((ch >= L'A' && ch <= L'Z') || (ch >= L'0' && ch <= L'9'))
                {
                    virtualKey = static_cast<UINT>(ch);
                    return true;
                }

                size_t symbolCount = 0;
                const SymbolKey* symbols = GetSymbolKeys(symbolCount);
                for (size_t index = 0; index < symbolCount; ++index)
                {
                    if (symbols[index].Symbol == ch)
                    {
                        virtualKey = symbols[index].VirtualKey;
                        return true;
                    }
                }
            }

            // F1..F24
            if (token.size() >= 2 && token[0] == L'F')
            {
                unsigned int number = 0;
                if (TryParseUInt(token.substr(1), number) && number >= 1 && number <= 24)
                {
                    virtualKey = 0x70 + (number - 1);
                    return true;
                }
            }

            // NUM0..NUM9 / NUMPAD0..NUMPAD9
            const wchar_t* prefixes[] = { L"NUMPAD", L"NUM" };
            for (const wchar_t* prefix : prefixes)
            {
                const size_t length = std::wcslen(prefix);
                if (token.size() == length + 1 &&
                    token.compare(0, length, prefix) == 0 &&
                    token[length] >= L'0' && token[length] <= L'9')
                {
                    virtualKey = 0x60 + static_cast<UINT>(token[length] - L'0');
                    return true;
                }
            }

            size_t namedCount = 0;
            const NamedKey* named = GetNamedKeys(namedCount);
            for (size_t index = 0; index < namedCount; ++index)
            {
                if (StringHelper::EqualsIgnoreCase(named[index].Name, token))
                {
                    virtualKey = named[index].VirtualKey;
                    return true;
                }
            }

            return false;
        }

        static bool TryParseUInt(const std::wstring& value, unsigned int& result)
        {
            if (value.empty())
            {
                return false;
            }

            unsigned int number = 0;
            for (const wchar_t ch : value)
            {
                if (ch < L'0' || ch > L'9')
                {
                    return false;
                }

                number = number * 10 + static_cast<unsigned int>(ch - L'0');
                if (number > 100000)
                {
                    return false;
                }
            }

            result = number;
            return true;
        }

        static const NamedKey* GetNamedKeys(size_t& count)
        {
            static const NamedKey keys[] =
            {
                { L"BACKSPACE", 0x08 }, { L"BACK", 0x08 },
                { L"TAB", 0x09 },
                { L"ENTER", 0x0D }, { L"RETURN", 0x0D },
                { L"PAUSE", 0x13 },
                { L"CAPSLOCK", 0x14 },
                { L"ESC", 0x1B }, { L"ESCAPE", 0x1B },
                { L"SPACE", 0x20 }, { L"SPACEBAR", 0x20 },
                { L"PAGEUP", 0x21 }, { L"PRIOR", 0x21 },
                { L"PAGEDOWN", 0x22 }, { L"NEXT", 0x22 },
                { L"END", 0x23 },
                { L"HOME", 0x24 },
                { L"LEFT", 0x25 },
                { L"UP", 0x26 },
                { L"RIGHT", 0x27 },
                { L"DOWN", 0x28 },
                { L"PRINTSCREEN", 0x2C }, { L"PRTSC", 0x2C },
                { L"INSERT", 0x2D }, { L"INS", 0x2D },
                { L"DELETE", 0x2E }, { L"DEL", 0x2E },
                { L"NUMLOCK", 0x90 },
                { L"SCROLLLOCK", 0x91 },
                { L"VOLUMEMUTE", 0xAD },
                { L"VOLUMEDOWN", 0xAE },
                { L"VOLUMEUP", 0xAF },
                { L"MEDIANEXTTRACK", 0xB0 },
                { L"MEDIAPREVTRACK", 0xB1 },
                { L"MEDIASTOP", 0xB2 },
                { L"MEDIAPLAYPAUSE", 0xB3 },
                { L"MULTIPLY", 0x6A },
                { L"ADD", 0x6B },
                { L"SEPARATOR", 0x6C },
                { L"SUBTRACT", 0x6D },
                { L"DECIMAL", 0x6E },
                { L"DIVIDE", 0x6F },
                { L"PLUS", 0xBB },
                { L"MINUS", 0xBD },
            };

            count = sizeof(keys) / sizeof(keys[0]);
            return keys;
        }

        static const SymbolKey* GetSymbolKeys(size_t& count)
        {
            static const SymbolKey keys[] =
            {
                { L';', 0xBA },
                { L'=', 0xBB },
                { L',', 0xBC },
                { L'-', 0xBD },
                { L'.', 0xBE },
                { L'/', 0xBF },
                { L'\x60', 0xC0 },
                { L'[', 0xDB },
                { L'\\', 0xDC },
                { L']', 0xDD },
                { L'\'', 0xDE },
            };

            count = sizeof(keys) / sizeof(keys[0]);
            return keys;
        }
    };

    // =========================================================================
    // 进程辅助
    // =========================================================================

    /// <summary>进程相关辅助方法。</summary>
    class ProcessHelper
    {
    public:
        static std::wstring GetCurrentExecutablePath()
        {
            DWORD size = MAX_PATH;
            std::wstring buffer(size, L'\0');

            while (true)
            {
                const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), size);
                if (length == 0)
                {
                    return std::wstring();
                }

                if (length < size)
                {
                    buffer.resize(length);
                    return buffer;
                }

                size *= 2;
                buffer.assign(size, L'\0');
            }
        }

        static bool TryReadPid(const std::wstring& pidPath, DWORD& pid)
        {
            pid = 0;

            std::string text;
            if (!FileHelper::TryReadAllBytes(pidPath, text))
            {
                return false;
            }

            unsigned long long value = 0;
            bool hasDigit = false;

            for (const char ch : text)
            {
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
                {
                    if (hasDigit)
                    {
                        break;
                    }

                    continue;
                }

                if (ch < '0' || ch > '9')
                {
                    return false;
                }

                hasDigit = true;
                value = value * 10 + static_cast<unsigned long long>(ch - '0');
                if (value > 0xFFFFFFFFull)
                {
                    return false;
                }
            }

            if (!hasDigit || value == 0)
            {
                return false;
            }

            pid = static_cast<DWORD>(value);
            return true;
        }

        static bool WritePidFile(const std::wstring& pidPath, DWORD pid)
        {
            const std::string text = std::format("{}", pid);
            return FileHelper::WriteAllText(pidPath, text);
        }

        /// <summary>
        /// 把进程工作集压到最小，降低常驻内存。
        /// 页面只是转入后备列表，需要时会被重新换入，属于“让数字更诚实”的做法。
        /// </summary>
        static void TrimWorkingSet()
        {
            SetProcessWorkingSetSize(
                GetCurrentProcess(), static_cast<SIZE_T>(-1), static_cast<SIZE_T>(-1));
        }

        static void DeletePidFileIfOwned(const std::wstring& pidPath, DWORD ownerPid)
        {
            DWORD pid = 0;
            if (TryReadPid(pidPath, pid) && pid == ownerPid)
            {
                DeleteFileW(pidPath.c_str());
            }
        }

        /// <summary>用 ShellExecute 打开主程序（等价于双击）。</summary>
        static bool LaunchProgram(const std::wstring& programPath, std::wstring& error)
        {
            error.clear();

            const std::wstring directory = PathHelper::GetDirectoryPath(programPath);
            const HINSTANCE result = ShellExecuteW(
                nullptr,
                L"open",
                programPath.c_str(),
                nullptr,
                directory.empty() ? nullptr : directory.c_str(),
                SW_SHOWNORMAL);

            if (reinterpret_cast<INT_PTR>(result) <= 32)
            {
                error = std::format(
                    L"启动主程序失败（错误码 {}）：\n{}",
                    reinterpret_cast<INT_PTR>(result),
                    programPath);
                return false;
            }

            return true;
        }
    };

    // =========================================================================
    // 后台程序本体
    // =========================================================================

    /// <summary>
    /// 常驻后台程序：隐藏窗口 + 全局快捷键 + 托盘图标 + 消息循环。
    /// </summary>
    class BackgroundApplication
    {
    public:
        BackgroundApplication(
            std::wstring mainProgramPath,
            std::wstring configPath,
            HotKeyDefinition hotKey)
            : _mainProgramPath(std::move(mainProgramPath))
            , _configPath(std::move(configPath))
            , _hotKey(std::move(hotKey))
        {
        }

        BackgroundApplication(const BackgroundApplication&) = delete;
        BackgroundApplication& operator=(const BackgroundApplication&) = delete;

        /// <summary>注册窗口类、创建隐藏窗口、注册快捷键、创建托盘图标。</summary>
        bool TryInitialize(std::wstring& error)
        {
            error.clear();

            _instance = GetModuleHandleW(nullptr);
            _taskbarCreatedMessage = RegisterWindowMessageW(L"TaskbarCreated");

            WNDCLASSEXW windowClass = {};
            windowClass.cbSize = sizeof(windowClass);
            windowClass.lpfnWndProc = &BackgroundApplication::WindowProcedure;
            windowClass.hInstance = _instance;
            windowClass.lpszClassName = Constants::WindowClassName;

            if (RegisterClassExW(&windowClass) == 0 && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
            {
                error = std::format(L"注册窗口类失败（错误码 {}）。", GetLastError());
                return false;
            }

            _classRegistered = true;

            _window = CreateWindowExW(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                Constants::WindowClassName,
                Constants::ApplicationTitle,
                WS_POPUP,
                0, 0, 0, 0,
                nullptr, nullptr, _instance, this);

            if (_window == nullptr)
            {
                error = std::format(L"创建后台窗口失败（错误码 {}）。", GetLastError());
                return false;
            }

            if (RegisterHotKey(_window, Constants::HotKeyId, _hotKey.Modifiers | MOD_NOREPEAT, _hotKey.VirtualKey) == FALSE)
            {
                error = std::format(
                    L"注册快捷键“{}”失败（错误码 {}），可能已被其他程序占用。",
                    _hotKey.Display,
                    GetLastError());
                return false;
            }

            _hotKeyRegistered = true;

            // 托盘图标失败不致命：快捷键仍然可用。
            AddTrayIcon();

            return true;
        }

        /// <summary>进入消息循环，返回进程退出码。</summary>
        int Run()
        {
            MSG message = {};

            while (true)
            {
                const BOOL result = GetMessageW(&message, nullptr, 0, 0);
                if (result <= 0)
                {
                    break;
                }

                TranslateMessage(&message);
                DispatchMessageW(&message);
            }

            return static_cast<int>(message.wParam);
        }

        /// <summary>释放快捷键、托盘图标、窗口与窗口类。</summary>
        void Shutdown()
        {
            if (_hotKeyRegistered && _window != nullptr)
            {
                UnregisterHotKey(_window, Constants::HotKeyId);
                _hotKeyRegistered = false;
            }

            RemoveTrayIcon();

            if (_window != nullptr)
            {
                DestroyWindow(_window);
                _window = nullptr;
            }

            if (_classRegistered)
            {
                UnregisterClassW(Constants::WindowClassName, _instance);
                _classRegistered = false;
            }
        }

    private:
        // ---- 字段 ----
        std::wstring _mainProgramPath;
        std::wstring _configPath;
        HotKeyDefinition _hotKey;

        HINSTANCE _instance = nullptr;
        HWND _window = nullptr;
        HICON _trayIcon = nullptr;
        bool _ownsTrayIcon = false;
        bool _classRegistered = false;
        bool _hotKeyRegistered = false;
        bool _trayIconAdded = false;
        UINT _taskbarCreatedMessage = 0;
        NOTIFYICONDATAW _trayData = {};

        // ---- 窗口过程 ----
        static LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
        {
            BackgroundApplication* self = nullptr;

            if (message == WM_NCCREATE)
            {
                const auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
                self = static_cast<BackgroundApplication*>(create->lpCreateParams);
                SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
                self->_window = window;
            }
            else
            {
                self = reinterpret_cast<BackgroundApplication*>(GetWindowLongPtrW(window, GWLP_USERDATA));
            }

            if (self != nullptr)
            {
                return self->HandleMessage(message, wParam, lParam);
            }

            return DefWindowProcW(window, message, wParam, lParam);
        }

        LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam)
        {
            if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
            {
                // 资源管理器重启后需要重新添加托盘图标。
                _trayIconAdded = false;
                AddTrayIcon();
                return 0;
            }

            switch (message)
            {
            case WM_HOTKEY:
                if (static_cast<UINT>(wParam) == Constants::HotKeyId)
                {
                    InvokeMainProgram();
                }

                return 0;

            case Constants::TrayCallbackMessage:
                return HandleTrayMessage(lParam);

            case WM_COMMAND:
                return HandleMenuCommand(LOWORD(wParam));

            case WM_CLOSE:
                DestroyWindow(_window);
                return 0;

            case WM_DESTROY:
                RemoveTrayIcon();
                PostQuitMessage(0);
                return 0;

            default:
                return DefWindowProcW(_window, message, wParam, lParam);
            }
        }

        // ---- 托盘 ----
        bool AddTrayIcon()
        {
            if (_trayIconAdded || _window == nullptr)
            {
                return _trayIconAdded;
            }

            if (_trayIcon == nullptr)
            {
                _trayIcon = LoadTrayIcon();
            }

            NOTIFYICONDATAW data = {};
            data.cbSize = sizeof(data);
            data.hWnd = _window;
            data.uID = Constants::TrayIconId;
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = Constants::TrayCallbackMessage;
            data.hIcon = _trayIcon;

            const std::wstring tooltip = std::format(
                L"{}（{}）", Constants::TrayTooltip, _hotKey.Display);
            StringCchCopyW(data.szTip, ARRAYSIZE(data.szTip), tooltip.c_str());

            if (Shell_NotifyIconW(NIM_ADD, &data) == FALSE)
            {
                return false;
            }

            _trayData = data;
            _trayIconAdded = true;
            return true;
        }

        void RemoveTrayIcon()
        {
            if (_trayIconAdded)
            {
                Shell_NotifyIconW(NIM_DELETE, &_trayData);
                _trayIconAdded = false;
            }

            if (_ownsTrayIcon && _trayIcon != nullptr)
            {
                DestroyIcon(_trayIcon);
                _trayIcon = nullptr;
                _ownsTrayIcon = false;
            }
        }

        /// <summary>托盘图标优先使用主程序图标，取不到则退回系统默认图标。</summary>
        HICON LoadTrayIcon()
        {
            if (!_mainProgramPath.empty())
            {
                SHFILEINFOW info = {};
                const UINT flags = SHGFI_ICON | SHGFI_SMALLICON;
                if (SHGetFileInfoW(_mainProgramPath.c_str(), 0, &info, sizeof(info), flags) != 0 &&
                    info.hIcon != nullptr)
                {
                    _ownsTrayIcon = true;
                    return info.hIcon;
                }
            }

            return LoadIconW(nullptr, IDI_APPLICATION);
        }

        LRESULT HandleTrayMessage(LPARAM lParam)
        {
            switch (static_cast<UINT>(lParam))
            {
            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                ShowTrayMenu();
                return 0;

            case WM_LBUTTONDBLCLK:
                InvokeMainProgram();
                return 0;

            default:
                return 0;
            }
        }

        void ShowTrayMenu()
        {
            const HMENU menu = CreatePopupMenu();
            if (menu == nullptr)
            {
                return;
            }

            AppendMenuW(menu, MF_STRING, Constants::TrayMenuEditConfig, L"修改配置");
            AppendMenuW(menu, MF_STRING, Constants::TrayMenuExit, L"退出");

            POINT cursor = {};
            GetCursorPos(&cursor);

            // TrackPopupMenu 需要窗口在前台，否则菜单可能不消失。
            SetForegroundWindow(_window);

            TrackPopupMenu(
                menu,
                TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN,
                cursor.x, cursor.y,
                0, _window, nullptr);

            DestroyMenu(menu);
            PostMessageW(_window, WM_NULL, 0, 0);
        }

        LRESULT HandleMenuCommand(UINT commandId)
        {
            switch (commandId)
            {
            case Constants::TrayMenuEditConfig:
                OpenConfigFile();
                return 0;

            case Constants::TrayMenuExit:
                DestroyWindow(_window);
                return 0;

            default:
                return 0;
            }
        }

        /// <summary>“修改配置”：用默认程序打开 JSON 配置，失败时退回记事本。</summary>
        void OpenConfigFile()
        {
            const std::wstring path = _configPath;

            if (!PathHelper::FileExists(path))
            {
                // 文件不存在时打开程序目录，便于用户自行新建。
                const std::wstring directory = PathHelper::GetDirectoryPath(
                    ProcessHelper::GetCurrentExecutablePath());

                ShellExecuteW(_window, L"open", directory.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
                return;
            }

            const HINSTANCE result = ShellExecuteW(
                _window, L"open", path.c_str(), nullptr, nullptr, SW_SHOWNORMAL);

            if (reinterpret_cast<INT_PTR>(result) <= 32)
            {
                const std::wstring arguments = L"\"" + path + L"\"";
                ShellExecuteW(
                    _window, L"open", L"notepad.exe", arguments.c_str(), nullptr, SW_SHOWNORMAL);
            }
        }

        /// <summary>唤起主程序：每次调用启动一次（等价于双击主程序）。</summary>
        void InvokeMainProgram()
        {
            std::wstring error;
            if (!ProcessHelper::LaunchProgram(_mainProgramPath, error))
            {
                MessageBoxW(_window, error.c_str(), Constants::ApplicationTitle, MB_ICONERROR | MB_OK);
            }

            // ShellExecute 可能把一些页面带进程里，唤起后重新压一次工作集。
            ProcessHelper::TrimWorkingSet();
        }
    };

    // =========================================================================
    // 入口
    // =========================================================================

    /// <summary>对应 C# 的 Program 类。</summary>
    class Program
    {
    public:
        static int Main()
        {
            const std::vector<std::wstring> arguments = GetCommandLineArguments();

            if (arguments.size() < 2)
            {
                MessageBoxW(
                    nullptr,
                    L"用法：CommandPark.Background.Cpp.exe <主程序完整路径>",
                    Constants::ApplicationTitle,
                    MB_ICONERROR | MB_OK);
                return 2;
            }

            const std::wstring mainProgramPath =
                PathHelper::GetFullPath(StringHelper::Trim(arguments[1]));
            if (!PathHelper::FileExists(mainProgramPath))
            {
                const std::wstring message = std::format(L"找不到主程序：\n{}", mainProgramPath);
                MessageBoxW(nullptr, message.c_str(), Constants::ApplicationTitle, MB_ICONERROR | MB_OK);
                return 2;
            }

            const std::wstring executablePath = ProcessHelper::GetCurrentExecutablePath();
            const std::wstring directory = PathHelper::GetDirectoryPath(executablePath);
            const std::wstring configPath = ConfigReader::ResolveConfigPath(directory);
            const std::wstring pidPath = PathHelper::Combine(directory, Constants::PidFileName);

            // 读取快捷键配置。
            const HotKeyConfigResult config = ConfigReader::ReadHotKey(configPath);
            if (!config.Error.empty())
            {
                MessageBoxW(
                    nullptr, config.Error.c_str(), Constants::ApplicationTitle,
                    MB_ICONWARNING | MB_OK);
            }

            // 快捷键被显式关闭（null / 空字符串）：后台无事可做，直接退出。
            if (!config.Enabled)
            {
                return 0;
            }

            HotKeyDefinition hotKey;
            std::wstring hotKeyError;
            if (!HotKeyParser::TryParse(config.Value, hotKey, hotKeyError))
            {
                const std::wstring message = std::format(
                    L"快捷键配置无效：{}\n\n{}", config.Value, hotKeyError);
                MessageBoxW(nullptr, message.c_str(), Constants::ApplicationTitle, MB_ICONERROR | MB_OK);
                return 3;
            }

            // 单实例：已经有一个后台在运行就直接退出，由主程序决定是否需要再拉起。
            const HANDLE mutex = CreateMutexW(nullptr, TRUE, Constants::SingleInstanceMutexName);
            if (mutex == nullptr)
            {
                return 0;
            }

            if (GetLastError() == ERROR_ALREADY_EXISTS)
            {
                CloseHandle(mutex);
                return 0;
            }

            const int exitCode = RunBackground(mainProgramPath, configPath, pidPath, hotKey);
            CloseHandle(mutex);
            return exitCode;
        }

    private:
        static std::vector<std::wstring> GetCommandLineArguments()
        {
            std::vector<std::wstring> arguments;

            int count = 0;
            LPWSTR* items = CommandLineToArgvW(GetCommandLineW(), &count);
            if (items == nullptr)
            {
                return arguments;
            }

            arguments.reserve(static_cast<size_t>(count));
            for (int index = 0; index < count; ++index)
            {
                arguments.emplace_back(items[index]);
            }

            LocalFree(items);
            return arguments;
        }

        static int RunBackground(
            const std::wstring& mainProgramPath,
            const std::wstring& configPath,
            const std::wstring& pidPath,
            const HotKeyDefinition& hotKey)
        {
            BackgroundApplication application(mainProgramPath, configPath, hotKey);

            std::wstring error;
            if (!application.TryInitialize(error))
            {
                MessageBoxW(nullptr, error.c_str(), Constants::ApplicationTitle, MB_ICONERROR | MB_OK);
                return 4;
            }

            const DWORD processId = GetCurrentProcessId();
            ProcessHelper::WritePidFile(pidPath, processId);

            // 初始化完成后驻留；把工作集压到最小，减少常驻内存。
            ProcessHelper::TrimWorkingSet();

            const int exitCode = application.Run();

            application.Shutdown();
            ProcessHelper::DeletePidFileIfOwned(pidPath, processId);
            return exitCode;
        }
    };
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int)
{
    return CommandPark::Background::Program::Main();
}
