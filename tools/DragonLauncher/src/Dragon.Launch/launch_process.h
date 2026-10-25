//
// Spirited © All Rights Reserved
//
// This source code is govered by a BSD-3 software license, included in the top directory
// of the project. Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the license is met.
//

#pragma once

#include <string>
#include <vector>

namespace dragon
{

DWORD StartProcess(
    const LPCSTR clientDirectory,
    const LPCSTR fileName,
    const std::string arguments,
    PROCESS_INFORMATION& processInfo);

DWORD FindModules(
    std::vector<std::string>& modules);

DWORD LoadModules(
    const std::vector<std::string>& modules, 
    const PROCESS_INFORMATION& processInfo,
    std::vector<LPVOID>& addresses);

DWORD ResumeProcess(
    const std::vector<std::string>& modules,
    const PROCESS_INFORMATION& processInfo,
    std::vector<LPVOID>& addresses);

} // dragon
