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

class WebsiteHook;

// When a player closes the Conquer Online client, the client will ask Windows to navigate to a
// signout webpage using the default browser. This signout page is baked into the executable. This
// module helps in either preventing popups or redirecting requests to different URLs.
class WebsiteModule : public ModuleInstance
{
public:
    WebsiteModule();

private:
    std::unique_ptr<WebsiteHook> m_websiteHook;
};

} // dragon
