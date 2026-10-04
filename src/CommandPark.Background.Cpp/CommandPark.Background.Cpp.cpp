#include <Windows.h>
#include <format>
#include <string>

void SimpleMessageBox(LPCWSTR content, LPCWSTR title)
{
    MessageBoxExW(nullptr, content, title, MB_OK, 0);
}

int wmain(int argc, wchar_t* argv[])   // 注意是 wmain 和 wchar_t*
{
    SimpleMessageBox(L"测试，通过 windows.h 调用 MessageBoxExW", L"测试标题");

    if (argc > 0)
    {
        std::wstring msg = std::format(L"第一个参数是\"{}\"", argv[0]);  // argv[0] 是 wchar_t*
        SimpleMessageBox(msg.c_str(), L"传递参数");
    }

    return 0;
}