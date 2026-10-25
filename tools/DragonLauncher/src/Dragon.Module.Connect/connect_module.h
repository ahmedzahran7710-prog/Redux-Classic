//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#pragma once

#include "module/module_instance.h"

#include <memory>

namespace dragon {

class ConnectHook;

// The server.dat file, responsible to telling the client which servers are available and how to
// connect to them, is encrypted in later patches. A simple hack around this is to detour the 
// Winsock2 API's connect function.
class ConnectModule : public ModuleInstance
{
public:
    ConnectModule();

private:
    std::unique_ptr<ConnectHook> m_connectHook;
};

} // dragon
