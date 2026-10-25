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

// This hook detours ShellExecuteA to prevent the signout website from showing up on exit. It also
// allows for other websites to be blocked or redirected.
class WebsiteHook : HookAdapter
{
    typedef HINSTANCE(WINAPI* DetourFunc)(HWND, LPCSTR, LPCSTR, LPCSTR, LPCSTR, INT);

public:
    WebsiteHook();
    DWORD Register(ModuleInstance& instance);

    static DetourFunc Target;
    static HINSTANCE WINAPI Detour(HWND, LPCSTR, LPCSTR, LPCSTR, LPCSTR, INT);
};

} // dragon
