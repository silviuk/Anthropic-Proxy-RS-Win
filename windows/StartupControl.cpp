// Packaged startup integration for the Windows UI. The original proxy is by
// Ivan Zakutnii (m0n0x41d); Windows UI and packaging by Silviu Vlasceanu.
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.ApplicationModel.h>
#include <iostream>
#include <string_view>

int wmain(int argc, wchar_t** argv)
{
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        auto task = winrt::Windows::ApplicationModel::StartupTask::GetAsync(L"AnthropicProxyStartup").get();
        std::wstring_view command = argc > 1 ? argv[1] : L"state";
        if (command == L"enable") task.RequestEnableAsync().get();
        else if (command == L"disable") task.Disable();
        else if (command != L"state") return 2;
        std::wcout << static_cast<int>(task.State()) << std::endl;
        return 0;
    }
    catch (const winrt::hresult_error& error) {
        std::wcerr << error.message().c_str() << std::endl;
        return 1;
    }
}
