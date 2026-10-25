//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#pragma once

namespace dragon {

class ModuleInstance;

// Hook interface for registering hooks with a module instance.
class HookAdapter
{
public:
    virtual DWORD Register(ModuleInstance& instance) = 0;
};

} // dragon
