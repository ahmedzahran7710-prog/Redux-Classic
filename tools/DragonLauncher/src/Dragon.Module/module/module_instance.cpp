//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include <Windows.h>
#include <detours.h>

#include "module_instance.h"
#include "hook_adapter.h"

using namespace std;

namespace dragon {

// Initializes detours and attaches hooks that have been registered with the module.
DWORD ModuleInstance::OnInit()
{
    if (DetourIsHelperProcess()) {
        return ERROR_INVALID_OPERATION;
    }

    DetourRestoreAfterWith();
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    for (auto hook : m_hooks)
        DetourAttach(hook.first, hook.second);

    DetourTransactionCommit();
    return ERROR_SUCCESS;
}

// Detaches hooks that have been registered with the module.
DWORD ModuleInstance::OnTerm()
{
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    for (auto hook : m_hooks)
        DetourDetach(hook.first, hook.second);

    DetourTransactionCommit();
    return ERROR_SUCCESS;
}

// Registers a hook by calling into the adapter Register method.
DWORD ModuleInstance::Register(HookAdapter& hook)
{
    return hook.Register(*this);
}

// Adds the hook's target and detour functions to the module's collection of registered hooks.
DWORD ModuleInstance::Register(PVOID* target, PVOID detour)
{
    m_hooks.emplace_back(target, detour);
    return ERROR_SUCCESS;
}

} // dragon
