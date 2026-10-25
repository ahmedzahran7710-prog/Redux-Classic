//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include "flash.h"
#include "flash_hook.h"
#include "module/module_instance.h"

using namespace std;

namespace dragon {

FlashHook::DetourFunc FlashHook::Target;

// Register the hook with the module instance.
DWORD FlashHook::Register(ModuleInstance& instance)
{
    HMODULE module = GetModuleHandleA("kernelbase.dll");
    if (module == NULL) {
        exit(GetLastError());
    }

    FARPROC procAddr = GetProcAddress(module, "LoadLibraryExW");
    if (procAddr == NULL) {
        exit(GetLastError());
    }

    Target = reinterpret_cast<DetourFunc>(procAddr);
    return instance.Register(&(PVOID&)Target, FlashHook::Detour);
}

// Detour LoadLibraryExW and replace the module name on load if the module is Flash.
HMODULE WINAPI FlashHook::Detour(LPCWSTR lpLibFileName, HANDLE hFile, DWORD dwFlags)
{
    if (wcsstr(lpLibFileName, L"Flash.ocx")) {
        lpLibFileName = L"./Flash.ocx";
    }

    return Target(lpLibFileName, hFile, dwFlags);
}

} // dragon
