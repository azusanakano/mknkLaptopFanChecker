#include <windows.h>
#include <shellapi.h>

#include <string>

namespace {

std::wstring ExecutableDirectory() {
    std::wstring path(32768, L'\0');
    const DWORD length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) return L"";
    path.resize(length);
    const std::wstring::size_type slash = path.find_last_of(L"\\/");
    if (slash == std::wstring::npos) return L"";
    return path.substr(0, slash);
}

std::wstring WindowsErrorMessage(DWORD error) {
    wchar_t* buffer = nullptr;
    const DWORD flags = FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM |
                        FORMAT_MESSAGE_IGNORE_INSERTS;
    const DWORD length = FormatMessageW(flags, nullptr, error, 0,
                                        reinterpret_cast<wchar_t*>(&buffer), 0, nullptr);
    std::wstring result = length && buffer ? std::wstring(buffer, length) : L"不明なエラー";
    if (buffer) LocalFree(buffer);
    while (!result.empty() && (result.back() == L'\r' || result.back() == L'\n' || result.back() == L' ')) {
        result.pop_back();
    }
    return result;
}

bool HasExactArgument(const wchar_t* expected) {
    int count = 0;
    wchar_t** arguments = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!arguments) return false;
    bool found = false;
    for (int index = 1; index < count; ++index) {
        if (_wcsicmp(arguments[index], expected) == 0) {
            found = true;
            break;
        }
    }
    LocalFree(arguments);
    return found;
}

int StartMonitorTask() {
    std::wstring systemDirectory(32768, L'\0');
    const UINT length = GetSystemDirectoryW(systemDirectory.data(), static_cast<UINT>(systemDirectory.size()));
    if (length == 0 || length >= systemDirectory.size()) return 5;
    systemDirectory.resize(length);
    const std::wstring executable = systemDirectory + L"\\schtasks.exe";
    std::wstring command = L"\"" + executable + L"\" /Run /TN \"mknk Laptop CPU Fan Monitor\"";

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE,
                        CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) {
        return static_cast<int>(GetLastError());
    }

    const DWORD wait = WaitForSingleObject(process.hProcess, 15000);
    DWORD exitCode = 0;
    if (wait == WAIT_OBJECT_0) {
        if (!GetExitCodeProcess(process.hProcess, &exitCode)) exitCode = GetLastError();
    } else {
        exitCode = wait == WAIT_TIMEOUT ? 1460 : GetLastError();
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return static_cast<int>(exitCode);
}

}  // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR commandLine, int) {
    // HKCU Run uses this silent watchdog to start the already-registered elevated task.
    // It never launches the sensor process directly, so Windows does not display UAC at logon.
    if (HasExactArgument(L"--start-monitor-task")) return StartMonitorTask();

    const std::wstring root = ExecutableDirectory();
    if (root.empty()) {
        MessageBoxW(nullptr, L"アプリケーションの保存場所を取得できませんでした。",
                    L"mknkLaptopFanChecker", MB_OK | MB_ICONERROR);
        return 2;
    }

    const std::wstring runtimeDirectory = root + L"\\runtime";
    const std::wstring target = runtimeDirectory + L"\\mknkLaptopFanChecker.exe";
    const DWORD attributes = GetFileAttributesW(target.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY)) {
        MessageBoxW(nullptr,
                    L"runtime\\mknkLaptopFanChecker.exe が見つかりません。\r\n"
                    L"ZIPを展開し直し、フォルダー構成を変えずに起動してください。",
                    L"起動ファイルが見つかりません", MB_OK | MB_ICONERROR);
        return 3;
    }

    SHELLEXECUTEINFOW launch{};
    launch.cbSize = sizeof(launch);
    launch.fMask = SEE_MASK_FLAG_NO_UI;
    launch.lpFile = target.c_str();
    launch.lpParameters = commandLine && *commandLine ? commandLine : nullptr;
    launch.lpDirectory = runtimeDirectory.c_str();
    launch.nShow = SW_SHOWNORMAL;

    if (!ShellExecuteExW(&launch)) {
        const DWORD error = GetLastError();
        const std::wstring message = L"CPUファンチェッカーを起動できませんでした。\r\n\r\n" +
                                     WindowsErrorMessage(error) + L"\r\nエラーコード: " +
                                     std::to_wstring(error);
        MessageBoxW(nullptr, message.c_str(), L"起動エラー", MB_OK | MB_ICONERROR);
        return static_cast<int>(error ? error : 4);
    }

    return 0;
}
