//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include "website.h"
#include "website_module.h"
#include "website_hook.h"

#include <sstream>

using namespace std;

namespace dragon {

WebsiteModule::WebsiteModule()
    : m_websiteHook(make_unique<WebsiteHook>())
{
    // Register hooks
    DWORD registerResult = m_websiteHook->Register(*this);
    if (registerResult != ERROR_SUCCESS) {
        MessageBoxA(NULL, "Website module failed to load", "Dragon.Module.Website", MB_OK);
        exit(registerResult);
    }
}

} // dragon