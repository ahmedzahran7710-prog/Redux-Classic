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

class FlashHook;

// Adobe Flash's end of life was December 31, 2020. Flash content began being blocked on 
// January 12, 2021. Conquer Online uses Flash for its login, loading, and character creation 
// screen. As a temporary fix, this module loads ActiveX Flash from the client folder rather than
// the system's installation. This will stop working once Flash is removed from Windows.
class FlashModule : public ModuleInstance
{
public:
    FlashModule();

private:
    std::unique_ptr<FlashHook> m_flashHook;
};

} // dragon
