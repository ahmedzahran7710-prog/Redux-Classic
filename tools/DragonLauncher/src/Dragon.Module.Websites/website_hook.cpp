//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include "website.h"
#include "website_hook.h"
#include "module/module_instance.h"

#include <fstream>
#include <regex>
#include <shellapi.h>

using namespace std;
using json = nlohmann::json;

namespace dragon {

WebsiteHook::DetourFunc WebsiteHook::Target;

// Regular expressions
static regex s_home("co\\..*\\.com$");
static regex s_forum("bbs\\.co\\..*\\.com");
static regex s_credit("credit\\..*\\.com\\/purchase$");
static regex s_signup("account\\..*\\.com\\/common\\/signup\\.aspx");
static regex s_signout("co\\..*\\.com\\/signout");

// Settings
static string s_homeString;
static string s_forumString;
static string s_creditString;
static string s_signupString;
static string s_signoutString;

// Load settings from the JSON file if it exists.
WebsiteHook::WebsiteHook()
{
    // Open the settings file
    ifstream settingsFile("Dragon.Module.Websites.json");
    if (!settingsFile.is_open()) {
        return;
    }

    // Parse json
    json settings;
    settingsFile >> settings;
    settings["home"].get_to(s_homeString);
    settings["forum"].get_to(s_forumString);
    settings["credit"].get_to(s_creditString);
    settings["signup"].get_to(s_signupString);
    settings["signout"].get_to(s_signoutString);
}

// Register the hook with the module instance.
DWORD WebsiteHook::Register(ModuleInstance& instance)
{
    HMODULE module = GetModuleHandleA("Shell32.dll");
    if (module == NULL) {
        exit(GetLastError());
    }

    FARPROC procAddr = GetProcAddress(module, "ShellExecuteA");
    if (procAddr == NULL) {
        exit(GetLastError());
    }

    Target = reinterpret_cast<DetourFunc>(procAddr);
    return instance.Register(&(PVOID&)Target, WebsiteHook::Detour);
}

// Detour ShellExecuteA and replace arguments for different websites.
HINSTANCE WINAPI WebsiteHook::Detour(
    HWND hwnd, LPCSTR lpOperation, LPCSTR lpFile, 
    LPCSTR lpParameters, LPCSTR lpDirectory, INT nShowCmd)
{
    if (regex_search(lpFile, s_home)) {
        lpFile = s_homeString.c_str();
    }
    else if (regex_search(lpFile, s_credit)) {
        lpFile = s_creditString.c_str();
    }
    else if (regex_search(lpFile, s_forum)) {
        lpFile = s_forumString.c_str();
    }
    else if (regex_search(lpFile, s_signup)) {
        lpFile = s_signupString.c_str();
    }
    else if (regex_search(lpFile, s_signout)) {
        lpFile = s_signoutString.c_str();
    }

    if (strlen(lpFile) == 0) {
        return (HINSTANCE)SE_ERR_FNF;
    }

    return Target(hwnd, lpOperation, lpFile, lpParameters, lpDirectory, nShowCmd);
}

} // dragon
