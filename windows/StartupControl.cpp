// Packaged startup integration for the Windows UI. The original proxy is by
// Ivan Zakutnii (m0n0x41d); Windows UI and packaging by Silviu Vlasceanu.
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.ApplicationModel.h>
#include <chrono>

template <typename Operation>
auto Result(const Operation& operation)
{
    if (operation.wait_for(std::chrono::seconds(5)) == winrt::Windows::Foundation::AsyncStatus::Started) {
        operation.Cancel();
        winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_TIMEOUT));
    }
    return operation.GetResults();
}

// Keep the API call in the app process so it uses the app's package identity.
// The managed caller dispatches this to a background thread.
extern "C" __declspec(dllexport) HRESULT WINAPI ControlStartup(int operation, int* state) noexcept
{
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        struct ApartmentScope {
            ~ApartmentScope() { winrt::uninit_apartment(); }
        } apartment;
        auto task = Result(winrt::Windows::ApplicationModel::StartupTask::GetAsync(L"AnthropicProxyStartup"));
        if (operation == 1) {
            *state = static_cast<int>(Result(task.RequestEnableAsync()));
        } else {
            if (operation == 2) task.Disable();
            *state = static_cast<int>(task.State());
        }
        return S_OK;
    } catch (...) {
        return winrt::to_hresult();
    }
}
