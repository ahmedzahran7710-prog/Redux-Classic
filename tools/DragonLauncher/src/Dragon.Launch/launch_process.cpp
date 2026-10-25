//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include "launch.h"
#include "launch_process.h"

#include <filesystem>
#include <regex>
#include <tlhelp32.h>

using namespace std;

namespace dragon
{

// Starts the client process in a suspended state. For injection to work: the client, injector,
// and injected DLL all have to be the same architecture. If the client and injector process aren't
// the same architecture, then this function will fail.
DWORD StartProcess(
    const LPCSTR clientDirectory, 
    const LPCSTR fileName,
    const string arguments,
    PROCESS_INFORMATION& processInfo)
{
    // Create the process
    filesystem::path clientPath(clientDirectory);
    filesystem::path exePath(fileName);
    filesystem::path fullPath(clientPath / exePath);

    STARTUPINFO startupInfo;
    ZeroMemory(&startupInfo, sizeof(startupInfo));
    ZeroMemory(&processInfo, sizeof(processInfo));
    startupInfo.cb = sizeof(startupInfo);

    wstring processFilePath(fullPath.c_str());
    wstring processArgs(arguments.begin(), arguments.end());
    wstring commandLine = processFilePath + L" " + processArgs;

    if (!CreateProcess(NULL, LPWSTR(commandLine.c_str()), NULL, NULL, FALSE, CREATE_SUSPENDED,
        NULL, LPWSTR(clientPath.c_str()), &startupInfo, &processInfo)) {
        return GetLastError();
    }

    // Check the architecture of the running process
    BOOL parentWow64, childWow64;
    IsWow64Process(GetCurrentProcess(), &parentWow64);
    IsWow64Process(processInfo.hProcess, &childWow64);

    if (!parentWow64 || !childWow64) {
        TerminateProcess(processInfo.hProcess, ERROR_PROCESS_ABORTED);
        CloseHandle(processInfo.hProcess);
        CloseHandle(processInfo.hThread);
        return ERROR_PROCESS_ABORTED;
    }

    return ERROR_SUCCESS;
}

// Finds all modules that match a pattern in the current working directory of the injector.
DWORD FindModules(vector<string>& modules)
{
    regex exp("Dragon\\.Module\\..*\\.dll");
    for (const auto& entry : std::filesystem::directory_iterator(".")) {
        if (entry.is_regular_file()) {
            const string& filename = entry.path().filename().string();

            if (regex_search(filename, exp)) {
                auto absolutePath = std::filesystem::absolute(entry.path());
                printf("Discovered %s\n", absolutePath.string().c_str());
                modules.emplace_back(absolutePath.string());
            }
        }
    }

    return modules.size() == 0 
        ? ERROR_FILE_NOT_FOUND 
        : ERROR_SUCCESS;
}

// Injects modules into the running process using QueueUserAPC, which adds an asynchronous procedure
// call (APC) to a thread's APC queue. The procedure call we force the client to load is LoadLibrary
// from kernel32. Multiple modules can be queued on the thread's APC queue at once before resuming.
DWORD LoadModules(
    const vector<string>& modules,
    const PROCESS_INFORMATION& processInfo,
    std::vector<LPVOID>& addresses)
{
    for (const string& moduleFileName : modules) {

        // Allocate memory for writing
        LPVOID addr = VirtualAllocEx(
            processInfo.hProcess, 
            NULL, 
            moduleFileName.length() + 1, 
            MEM_COMMIT | MEM_RESERVE, 
            PAGE_EXECUTE_READWRITE);
        if (addr == nullptr) {
            return GetLastError();
        }

        // Write the name of the module to that address
        addresses.push_back(addr);
        bool writeSuccess = WriteProcessMemory(
            processInfo.hProcess, 
            addr, 
            moduleFileName.c_str(),
            moduleFileName.length() + 1,
            NULL);
        if (!writeSuccess) {
            return GetLastError();
        }

        // Queue module library load on the primary process thread
        HMODULE kernel32 = GetModuleHandle(L"kernel32");
        if (kernel32 == NULL) {
            return ERROR_INVALID_FUNCTION;
        }

        PAPCFUNC loadFunc = (PAPCFUNC)GetProcAddress(kernel32, "LoadLibraryA");
        if (loadFunc == NULL) {
            return ERROR_INVALID_FUNCTION;
        }

        DWORD result = QueueUserAPC(loadFunc, processInfo.hThread, (ULONG_PTR)addr);
        if (result != 0) {
            printf("Queued loading %s\n", moduleFileName.c_str());
        }
        else {
            return GetLastError();
        }
    }

    return ERROR_SUCCESS;
}

// Resumes the client process and waits for all modules to be loaded. After all modules are loaded,
// virtual memory used to call LoadLibraryA will be cleaned up / released.
DWORD ResumeProcess(
    const std::vector<std::string>& modules,
    const PROCESS_INFORMATION& processInfo,
    std::vector<LPVOID>& addresses)
{
    ResumeThread(processInfo.hThread);

    bool readyToFreeMemory = false;
    wregex exp(L"Dragon\\.Module\\..*\\.dll");

    while(!readyToFreeMemory) {
        Sleep(100);

        // Get snapshot of loaded modules
        HANDLE snapshot = INVALID_HANDLE_VALUE;
        snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, processInfo.dwProcessId);
        if (snapshot == INVALID_HANDLE_VALUE) {
            return ERROR_PROCESS_ABORTED;
        }

        MODULEENTRY32 entry;
        entry.dwSize = sizeof(MODULEENTRY32);
        if (!Module32First(snapshot, &entry)) {
            CloseHandle(snapshot);
            return ERROR_PROCESS_ABORTED;
        }

        // Iterate through modules and count matches
        int matches = 0;
        do {
            wstring moduleName(entry.szModule);
            if (regex_match(entry.szModule, exp)) {
                matches++;
            }
        } 
        while (Module32Next(snapshot, &entry));
        readyToFreeMemory = matches == modules.size();
    }

    // Free virtual memory used by the launcher
    for (PVOID addr : addresses) {
        VirtualFreeEx(processInfo.hProcess, addr, 0, MEM_RELEASE);
    }

    return ERROR_SUCCESS;
}

} // dragon
