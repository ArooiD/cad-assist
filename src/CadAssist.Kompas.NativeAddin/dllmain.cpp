#include <windows.h>
#include <shellapi.h>

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    return TRUE;
}

extern "C" __declspec(dllexport) int WINAPI CadAssistInitialize()
{
    ShellExecuteW(
        nullptr,
        L"open",
        L"C:\\cad-assist-test\\app\\CadAssist.Kompas.Interop.exe",
        L"--model-path C:\\cad-assist-test\\test.m3d --show-task-window",
        nullptr,
        SW_SHOWNORMAL);

    return 1;
}
