//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include "flash.h"
#include "flash_module.h"
#include "flash_hook.h"

using namespace std;

namespace dragon {

FlashModule::FlashModule()
    : m_flashHook(make_unique<FlashHook>())
{
    // Register hooks
    DWORD registerResult = m_flashHook->Register(*this);
    if (registerResult != ERROR_SUCCESS) {
        MessageBoxA(NULL, "Flash module failed to load", "Flash.Module.Website", MB_OK);
        exit(registerResult);
    }
}

} // dragon