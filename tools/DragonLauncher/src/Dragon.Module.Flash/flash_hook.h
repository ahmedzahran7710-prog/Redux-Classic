//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#pragma once

#include "module/hook_adapter.h"

namespace dragon {

// This hook detours LoadLibraryExW and replaces the module name being loaded if the module is 
// ActiveX Flash. Credits go to Ultimation for the idea and initial example.
class FlashHook : HookAdapter
{
    typedef HMODULE(WINAPI* DetourFunc)(LPCWSTR, HANDLE, DWORD);

public:
    DWORD Register(ModuleInstance& instance);

    static DetourFunc Target;
    static HMODULE WINAPI Detour(LPCWSTR, HANDLE, DWORD);
};

} // dragon
