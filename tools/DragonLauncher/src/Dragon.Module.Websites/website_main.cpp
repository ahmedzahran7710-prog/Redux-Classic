//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include "website.h"
#include "website_module.h"

#include <memory>

using namespace std;

static unique_ptr<dragon::WebsiteModule> s_instance;

// The entry point into the module. When the library gets attached using Dragon, it calls this
// function using an existing or new thread. The program also calls this function when unloading
// the module.
bool APIENTRY DllMain(HMODULE hModule, DWORD dwReason, LPVOID lpReserved)
{
    if (dwReason == DLL_PROCESS_ATTACH) {
        s_instance = make_unique<dragon::WebsiteModule>();
        DWORD result = s_instance->OnInit();
        if (result != ERROR_SUCCESS) {
            exit(result);
        }
    }
    else if (dwReason == DLL_PROCESS_DETACH) {
        s_instance->OnTerm();
    }

    return true;
}
