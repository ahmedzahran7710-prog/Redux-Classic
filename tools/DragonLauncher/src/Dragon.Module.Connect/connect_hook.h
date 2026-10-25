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

// This hook detours WinSock2's connect function to redirect the client to a new IP and port. It
// only changes the IP and port for the account server and game server if enabled.
class ConnectHook : HookAdapter
{
    typedef int(WINAPI* DetourFunc)(SOCKET, sockaddr_in*, int);

public:
    ConnectHook();
    DWORD Register(ModuleInstance& instance);

    static DetourFunc Target;
    static int WINAPI Detour(SOCKET, sockaddr_in*, int);
};

} // dragon
