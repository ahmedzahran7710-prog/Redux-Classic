//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#include "connect.h"
#include "connect_hook.h"
#include "module/module_instance.h"

#include <fstream>
#include <ws2tcpip.h>

using namespace std;
using json = nlohmann::json;

namespace dragon {

ConnectHook::DetourFunc ConnectHook::Target;

// Settings
static bool accountServerRedirect;
static string accountServerIP;
static int accountServerPort;
static bool gameServerRedirect;
static string gameServerIP;
static int gameServerPort;

// Load settings from the JSON file if it exists.
ConnectHook::ConnectHook()
{
    // Open the settings file
    ifstream settingsFile("Dragon.Module.Connect.json");
    if (!settingsFile.is_open()) {
        return;
    }

    // Parse json
    json settings;
    settingsFile >> settings;
    settings["account_server"]["enable_redirect"].get_to(accountServerRedirect);
    settings["account_server"]["ip"].get_to(accountServerIP);
    settings["account_server"]["port"].get_to(accountServerPort);
    settings["game_server"]["enable_redirect"].get_to(gameServerRedirect);
    settings["game_server"]["ip"].get_to(gameServerIP);
    settings["game_server"]["port"].get_to(gameServerPort);
}

// Register the hook with the module instance.
DWORD ConnectHook::Register(ModuleInstance& instance)
{
    HMODULE module = GetModuleHandleA("ws2_32.dll");
    if (module == NULL) {
        exit(GetLastError());
    }

    FARPROC procAddr = GetProcAddress(module, "connect");
    if (procAddr == NULL) {
        exit(GetLastError());
    }

    Target = reinterpret_cast<DetourFunc>(procAddr);
    return instance.Register(&(PVOID&)Target, ConnectHook::Detour);
}

// Detour connect from WinSock2 and replace the address and port.
int WINAPI ConnectHook::Detour(SOCKET s, sockaddr_in* name, int namelen)
{
    int port = ntohs(name->sin_port);

    if (accountServerRedirect && port >= 9950 && port <= 9980) {
        InetPtonA(AF_INET, accountServerIP.c_str(), &name->sin_addr.s_addr);
        name->sin_port = htons(accountServerPort);
    }
    else if (gameServerRedirect && port >= 5810 && port <= 5820) {
        InetPtonA(AF_INET, gameServerIP.c_str(), &name->sin_addr.s_addr);
        name->sin_port = htons(gameServerPort);
    }

    return Target(s, name, namelen);
}

} // dragon
