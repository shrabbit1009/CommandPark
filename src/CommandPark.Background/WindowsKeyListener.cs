using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CommandPark.Background;

/// <summary>
/// 基于 Win32 <c>RegisterHotKey</c> 实现的 Windows 全局快捷键监听器。
/// <para>
/// <see cref="Key"/> 使用“修饰符+按键”格式，例如 <c>Ctrl+Alt+P</c>、<c>Win+Shift+S</c>、<c>Ctrl+F12</c>。
/// 修饰符：Ctrl/Control、Alt、Shift、Win/Windows；按键：A-Z、0-9、F1-F24、Enter/Esc/方向键等命名按键，
/// 以及常用符号（大小写不敏感，至少需要一个修饰键）。
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsKeyListener : IKeyListener, IDisposable
{
    /// <summary>热键标识，只需在同一线程（消息队列）内唯一。</summary>
    private const int HotKeyId = 0x4350; // "CP"

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private const uint WmQuit = 0x0012;
    private const uint WmHotKey = 0x0312;
    private const uint WmUser = 0x0400;
    private const uint PmNoRemove = 0x0000;

    private readonly object _gate = new();

    private string _key = string.Empty;
    private uint _modifiers;
    private uint _virtualKey;
    private bool _hasKey;

    private Thread? _listenerThread;
    private uint _listenerThreadId;
    private bool _isListening;
    private bool _disposed;

    /// <summary>
    /// 全局快捷键，格式为“修饰符+按键”，例如 <c>"Ctrl+Alt+P"</c>。
    /// 设置时会立即校验格式，格式错误抛出 <see cref="FormatException"/>。
    /// </summary>
    public string Key
    {
        get => _key;
        set
        {
            var (modifiers, virtualKey) = ParseShortcut(value);

            lock (_gate)
            {
                if (_listenerThread is { IsAlive: true })
                {
                    throw new InvalidOperationException("监听进行中，请先调用 StopListening() 再修改快捷键。");
                }

                _key = value.Trim();
                _modifiers = modifiers;
                _virtualKey = virtualKey;
                _hasKey = true;
            }
        }
    }

    /// <summary>
    /// 按住快捷键时是否重复触发，默认 <c>false</c>（长按只触发一次）。
    /// </summary>
    public bool RepeatWhileHeld { get; set; }

    /// <summary>
    /// 当前是否正在监听。线程安全，可在任意线程读取。
    /// </summary>
    public bool IsListening
    {
        get
        {
            lock (_gate)
            {
                return _isListening;
            }
        }
    }

    /// <summary>
    /// 快捷键被按下时触发。事件在监听线程（后台线程）上引发，非 UI 线程。
    /// </summary>
    public event EventHandler? KeyPressed;

    /// <summary>
    /// 开始监听全局快捷键，内部会启动一个后台消息循环线程，本方法立即返回。
    /// </summary>
    /// <exception cref="InvalidOperationException">未设置 <see cref="Key"/>，或已在监听中，或注册热键被系统拒绝（例如快捷键已被占用）。</exception>
    /// <exception cref="PlatformNotSupportedException">当前不是 Windows 平台。</exception>
    /// <exception cref="ObjectDisposedException">对象已释放。</exception>
    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WindowsKeyListener 只支持 Windows 平台。");
        }

        Exception? startupError = null;
        using var ready = new ManualResetEventSlim(initialState: false);

        Thread thread;

        lock (_gate)
        {
            if (_listenerThread is { IsAlive: true })
            {
                throw new InvalidOperationException("监听已启动，请先调用 StopListening()。");
            }

            if (!_hasKey)
            {
                throw new InvalidOperationException($"请先设置 {nameof(Key)} 属性，例如 \"Ctrl+Alt+P\"。");
            }

            var modifiers = _modifiers | (RepeatWhileHeld ? 0u : ModNoRepeat);
            var virtualKey = _virtualKey;
            var displayName = _key;

            thread = new Thread(() =>
            {
                try
                {
                    ListenCore(displayName, modifiers, virtualKey, ready);
                }
                catch (Exception ex)
                {
                    // 把启动阶段（或消息循环中）的异常回报给 StartListening 的调用方。
                    Interlocked.CompareExchange(ref startupError, ex, null);
                    ready.Set();
                }
            })
            {
                IsBackground = true,
                Name = $"CommandPark.WindowsKeyListener({displayName})",
            };

            _listenerThread = thread;
            _listenerThreadId = 0;
            thread.Start();
        }

        // 等待监听线程完成注册，避免把注册失败悄悄吞掉。
        ready.Wait(TimeSpan.FromSeconds(10));

        if (Volatile.Read(ref startupError) is { } error)
        {
            thread.Join(TimeSpan.FromSeconds(5));
            lock (_gate)
            {
                _listenerThread = null;
            }

            throw new InvalidOperationException($"启动全局快捷键监听失败：{error.Message}", error);
        }
    }

    /// <summary>
    /// 停止监听并注销全局快捷键。
    /// <para>
    /// 若在监听线程内调用（例如在 <see cref="KeyPressed"/> 处理函数中调用），则只投递退出消息，
    /// 不做等待，避免自己 Join 自己造成死锁。
    /// </para>
    /// </summary>
    public void StopListening()
    {
        Thread? thread;
        uint threadId;

        lock (_gate)
        {
            thread = _listenerThread;
            threadId = _listenerThreadId;

            if (thread is null)
            {
                return;
            }

            if (!thread.IsAlive)
            {
                _listenerThread = null;
                _isListening = false;
                return;
            }
        }

        // 注意：不能用 Environment.CurrentManagedThreadId 与 Win32 线程 ID 比较，
        // 两者是完全不同的编号体系，这里必须比较 Thread 实例本身。
        var isListenerThread = ReferenceEquals(Thread.CurrentThread, thread);

        if (threadId == 0)
        {
            // 极端情况下与 StartListening 并发：监听线程可能还没建立消息队列，短暂等待其就绪。
            for (var i = 0; i < 100 && threadId == 0 && thread.IsAlive; i++)
            {
                Thread.Sleep(10);
                lock (_gate)
                {
                    threadId = _listenerThreadId;
                }
            }
        }

        if (threadId != 0 && !PostThreadMessage(threadId, WmQuit, 0, 0) && !isListenerThread)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "向监听线程投递退出消息失败。");
        }

        if (isListenerThread)
        {
            return;
        }

        if (!thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("等待全局快捷键监听线程退出超时。");
        }

        lock (_gate)
        {
            if (ReferenceEquals(_listenerThread, thread))
            {
                _listenerThread = null;
            }

            _isListening = false;
        }
    }

    /// <summary>停止监听并释放资源。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            StopListening();
        }
        catch (TimeoutException)
        {
            // 释放阶段不再向上抛异常。
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 解析“修饰符+按键”格式的快捷键字符串，返回 Win32 修饰符位与虚拟键码。
    /// </summary>
    /// <param name="shortcut">例如 <c>"Ctrl+Alt+P"</c>、<c>"Win+Shift+S"</c>。</param>
    /// <exception cref="ArgumentException"><paramref name="shortcut"/> 为 null 或空白。</exception>
    /// <exception cref="FormatException">格式或修饰符/按键无法识别。</exception>
    public static (uint Modifiers, uint VirtualKey) ParseShortcut(string shortcut)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcut);

        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            throw new FormatException($"快捷键 \"{shortcut}\" 格式无效，应形如 \"Ctrl+Alt+P\"（至少需要一个修饰键加一个按键）。");
        }

        var modifiers = 0u;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= ParseModifier(parts[i], shortcut);
        }

        var keyName = parts[^1];
        if (!TryResolveVirtualKey(keyName, out var virtualKey))
        {
            throw new FormatException($"无法识别快捷键 \"{shortcut}\" 中的按键 \"{keyName}\"。");
        }

        return (modifiers, virtualKey);
    }

    private static uint ParseModifier(string token, string shortcut) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => ModControl,
        "alt" or "menu" => ModAlt,
        "shift" => ModShift,
        "win" or "windows" or "meta" or "super" or "cmd" => ModWin,
        _ => throw new FormatException($"无法识别快捷键 \"{shortcut}\" 中的修饰键 \"{token}\"。"),
    };

    private static bool TryResolveVirtualKey(string name, out uint virtualKey)
    {
        if (NamedKeys.TryGetValue(name, out virtualKey))
        {
            return true;
        }

        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }

            if (SymbolKeys.TryGetValue(c, out virtualKey))
            {
                return true;
            }
        }

        virtualKey = 0;
        return false;
    }

    /// <summary>监听线程主体：注册热键、跑消息循环，退出前注销热键。</summary>
    private void ListenCore(string displayName, uint modifiers, uint virtualKey, ManualResetEventSlim ready)
    {
        // 促使当前线程创建消息队列，这样 StopListening 的 PostThreadMessage 才能生效。
        PeekMessage(out _, nint.Zero, WmUser, WmUser, PmNoRemove);

        lock (_gate)
        {
            _listenerThreadId = GetCurrentThreadId();
        }

        if (!RegisterHotKey(nint.Zero, HotKeyId, modifiers, virtualKey))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"注册全局快捷键 \"{displayName}\" 失败（可能已被其它程序占用）。");
        }

        lock (_gate)
        {
            _isListening = true;
        }

        ready.Set();

        try
        {
            while (true)
            {
                var result = GetMessage(out var message, nint.Zero, 0, 0);

                // 0 = 收到 WM_QUIT，-1 = 出错，两种情况都结束监听。
                if (result <= 0)
                {
                    break;
                }

                if (message.Message == WmHotKey && message.WParam == HotKeyId)
                {
                    RaiseKeyPressed();
                }
            }
        }
        finally
        {
            UnregisterHotKey(nint.Zero, HotKeyId);

            lock (_gate)
            {
                _isListening = false;
                _listenerThreadId = 0;
            }
        }
    }

    private void RaiseKeyPressed() => KeyPressed?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------------
    // 按键名称表
    // ------------------------------------------------------------------

    private static readonly Dictionary<string, uint> NamedKeys = CreateNamedKeys();

    private static readonly Dictionary<char, uint> SymbolKeys = new()
    {
        [';'] = 0xBA,  // VK_OEM_1
        ['='] = 0xBB,  // VK_OEM_PLUS
        [','] = 0xBC,  // VK_OEM_COMMA
        ['-'] = 0xBD,  // VK_OEM_MINUS
        ['.'] = 0xBE,  // VK_OEM_PERIOD
        ['/'] = 0xBF,  // VK_OEM_2
        ['`'] = 0xC0,  // VK_OEM_3
        ['['] = 0xDB,  // VK_OEM_4
        ['\\'] = 0xDC, // VK_OEM_5
        [']'] = 0xDD,  // VK_OEM_6
        ['\''] = 0xDE, // VK_OEM_7
    };

    private static Dictionary<string, uint> CreateNamedKeys()
    {
        var keys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["BACKSPACE"] = 0x08,
            ["BACK"] = 0x08,
            ["TAB"] = 0x09,
            ["ENTER"] = 0x0D,
            ["RETURN"] = 0x0D,
            ["PAUSE"] = 0x13,
            ["CAPSLOCK"] = 0x14,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["SPACE"] = 0x20,
            ["SPACEBAR"] = 0x20,
            ["PAGEUP"] = 0x21,
            ["PRIOR"] = 0x21,
            ["PAGEDOWN"] = 0x22,
            ["NEXT"] = 0x22,
            ["END"] = 0x23,
            ["HOME"] = 0x24,
            ["LEFT"] = 0x25,
            ["UP"] = 0x26,
            ["RIGHT"] = 0x27,
            ["DOWN"] = 0x28,
            ["PRINTSCREEN"] = 0x2C,
            ["PRTSC"] = 0x2C,
            ["INSERT"] = 0x2D,
            ["INS"] = 0x2D,
            ["DELETE"] = 0x2E,
            ["DEL"] = 0x2E,
            ["NUMLOCK"] = 0x90,
            ["SCROLLLOCK"] = 0x91,
            ["VOLUMEMUTE"] = 0xAD,
            ["VOLUMEDOWN"] = 0xAE,
            ["VOLUMEUP"] = 0xAF,
            ["MEDIANEXTTRACK"] = 0xB0,
            ["MEDIAPREVTRACK"] = 0xB1,
            ["MEDIASTOP"] = 0xB2,
            ["MEDIAPLAYPAUSE"] = 0xB3,
            ["MULTIPLY"] = 0x6A,
            ["ADD"] = 0x6B,
            ["SEPARATOR"] = 0x6C,
            ["SUBTRACT"] = 0x6D,
            ["DECIMAL"] = 0x6E,
            ["DIVIDE"] = 0x6F,
            ["PLUS"] = 0xBB,
            ["MINUS"] = 0xBD,
        };

        for (var i = 1; i <= 24; i++)
        {
            keys[$"F{i}"] = (uint)(0x6F + i); // F1 = 0x70 ... F24 = 0x87
        }

        for (var i = 0; i <= 9; i++)
        {
            var vk = (uint)(0x60 + i); // VK_NUMPAD0 = 0x60
            keys[$"NUM{i}"] = vk;
            keys[$"NUMPAD{i}"] = vk;
        }

        return keys;
    }

    // ------------------------------------------------------------------
    // Win32 互操作
    // ------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}