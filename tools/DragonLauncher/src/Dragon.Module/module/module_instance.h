//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#pragma once

#include <vector>

namespace dragon {

class HookAdapter;

// The module instance contains procedures and hooks for creating a feature. The module is loaded
// into the game client at runtime using DllMain once the module is installed by the launcher. 
// Hooks register with the instance and get committed with OnInit.
class ModuleInstance
{
public:
    virtual ~ModuleInstance() = default;

    DWORD OnInit();
    DWORD OnTerm();

    DWORD Register(HookAdapter& hook);
    DWORD Register(PVOID* target, PVOID detour);

private:
    std::vector<std::pair<PVOID*, PVOID>> m_hooks;
};

} // dragon