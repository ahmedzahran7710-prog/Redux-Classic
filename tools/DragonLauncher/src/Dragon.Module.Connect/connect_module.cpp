//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include "connect.h"
#include "connect_module.h"
#include "connect_hook.h"

using namespace std;

namespace dragon {

ConnectModule::ConnectModule()
    : m_connectHook(make_unique<ConnectHook>())
{
    // Register hooks
    DWORD registerResult = m_connectHook->Register(*this);
    if (registerResult != ERROR_SUCCESS) {
        MessageBoxA(NULL, "Connect module failed to load", "Dragon.Module.Connect", MB_OK);
        exit(registerResult);
    }
}

} // dragon