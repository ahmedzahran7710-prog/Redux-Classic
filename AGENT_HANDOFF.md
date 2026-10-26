# AGENT HANDOFF — MoonQueen (Conquer Online Patch 5187 Clean Base)

This file provides complete context so any AI coding agent or developer can continue building **MoonQueen** seamlessly from the exact current state.

---

## 1. Project Identity & Core Architecture
- **Project & Server Name:** `MoonQueen` (`MoonQueen.sln`)
- **Client Target:** Conquer Online **Patch 5187** (2009 Classic 5-class era: Trojan, Warrior, Archer, Ninja, Taoist)
- **Foundation:** Clean `conquer-online/comet` (`5187` branch) skeleton (`net6.0`, C# 10, `Pomelo.EntityFrameworkCore.MySql 6.0.2`, `BouncyCastle.NetCore 1.9.0`, `StreamJsonRpc 2.12.27`) with all raw Comet 5187 protocol/offset/MariaDB/RPC bugs surgically fixed.
- **Projects in `MoonQueen.sln`:**
  - `src/MoonQueen.Shared` — Shared RPC transfer models (`TransferAuthArgs`) and central branding (`GameBranding.cs`).
  - `src/MoonQueen.Core` — Core math/utilities.
  - `src/MoonQueen.Network` — Async TCP sockets (`TcpServerListener`, `TcpServerActor`, `TcpServerRegistry`), partitioned `PacketProcessor<TClient>`, ciphers (`RC5`, `ScanCodeCipher`, `BlowfishCipher`, `DiffieHellman`), and loopback-hardened JSON-RPC (`RpcServerListener`, `RpcClient`).
  - `src/MoonQueen.Account` — Account authentication server (`Port 9960`), `moonqueen_account` EF Core context, SHA-1 + Salt & phpMyAdmin plain-text password support, and 32-byte `MsgConnectEx` redirect.
  - `src/MoonQueen.Game` — Game world server (`Port 5816`, internal RPC `127.0.0.1:5817`), `moonqueen_game` EF Core context, character registration, login loading sequence, movement, and chat.
  - `tests/MoonQueen.*.Tests` — Unit tests (`net6.0`, xUnit).

---

## 2. Pre-Configured Network & XAMPP Database Settings
- **Hamachi / Public Game IP:** `25.21.212.130`
  - Configured in `sql/MoonQueen_Full_Setup.sql` (`moonqueen_account.realm` -> `Name = 'MoonQueen'`, `GameIPAddress = '25.21.212.130'`, `GamePort = 5816`, `RpcIPAddress = '127.0.0.1'`, `RpcPort = 5817`).
- **Listeners:**
  - `MoonQueen.Account.config` -> `0.0.0.0:9960`
  - `MoonQueen.Game.config` -> Game `0.0.0.0:5816`, RPC `127.0.0.1:5817`
- **XAMPP MySQL (`127.0.0.1:3306`):**
  - Schemas: `moonqueen_account` and `moonqueen_game` (underscore naming avoids phpMyAdmin dot-separator issues).
  - Credentials: `Username = "root"`, `Password = "7710"` (with automatic fallback in `ServerDbContext.Ping()` to `""` if XAMPP uses the default blank root password).
  - One-click import file: `sql/MoonQueen_Full_Setup.sql` (includes default Admin account `admin` / `123456`).

---

## 3. Verified Patch 5187 Protocol Fixes Already Applied
Do **NOT** regress any of the following verified Patch 5187 wire rules:
1. **`DiffieHellman.ProbablePrimes.StartAsync(CancellationToken.None)`** in `MoonQueen.Game/Program.cs` MUST remain started before accepting connections on port `5816` (otherwise `ComputePublicKeyAsync()` blocks `AcceptingAsync()` indefinitely).
2. **`MsgConnectEx (1055)`** in `MoonQueen.Account/Packets/MsgConnectEx.cs`: Exact **32 bytes** (`[4..7] uint Token`, `[8..11] uint 0`, `[12..27] IP[16]`, `[28..31] uint Port`). `Remote.TransferAuth` generates a non-zero **32-bit `uint` token** (`dwData == 0`) required by `ConquerLoader` (`COServer.dat`).
3. **Account Socket Cleanup (`9960`)**: `MoonQueen.Account/Server.cs` cleanly closes the Account socket when `MsgPCNum (1100)` or `MsgConnect (1052 "res.dat")` arrives after `MsgConnectEx`.
4. **`MsgAction (10010)`**: Exact **36-byte** Patch 5187 layout (`CharacterID @ 4`, `Command @ 8`, `Argument @ 12`, `Timestamp @ 16`, `Action @ 20`, `Direction @ 22`, `X @ 24`, `Y @ 26`, `Map @ 28`, `MapColor @ 32`). Sends `MsgMapInfo (1110)` on `LoginSpawn (74)`.
5. **`MsgWalk (10005)`**: Exact **20-byte** Patch 5187 layout (`uint Direction @ 4`, `uint CharacterID @ 8`, `uint Mode @ 12`, `uint Timestamp @ 16`).
6. **`MsgUserInfo (1006)`**: Exact **89-byte** header before strings (`NameDisplayed = 1` at offset `70`, `VIPLevel` (`uint`) at offset `83`, `UserTitle` (`ushort`) at offset `87`, strings at `89`).

---

## 4. Standing Developer & User Workflow Rules
1. **Language & Live Updates:** Always communicate with the user in clear Arabic and provide step-by-step updates while working.
2. **Documentation Folder (`قسم معلومات التعديلات/`):** Every feature or modification MUST include an Arabic `.md` explanation inside `قسم معلومات التعديلات/` (and a bilingual-named `.md` guide whenever Client/DB/external steps are required), written simply for beginners.
3. **Zero Compile Errors:** Always run static/semantic verification across all `.csproj` and `.cs` files before committing so Visual Studio 2022 builds with `0 Errors`.
4. **Workspace Size Discipline:** Keep repository + workspace well below `128 MB` (`bin/`, `obj/`, `.DMap`, `.7z`, `.exe` remain ignored in `.gitignore`).
5. **Git Branching:** Commit on the active Arena session branch, push, open a PR, and merge into `main` so `main` always has the latest ready-to-download code.

---

## 5. Next Planned Expansion Steps (Build Incrementally Upon User Confirmation)
Once the user confirms XAMPP SQL import + first in-game login on `MoonQueen`:
1. **World Screen & DMap Loader:** Load Patch `5187` `.DMap` walkability + portals, `MsgPlayer (10014)` screen spawning between players, and NPC dialogs (`MsgNpcInfo 2030` / `MsgTaskDialog 2032`).
2. **Items & Equipment:** `itemtype` / `items` tables, starting newbie gear, `MsgItemInfo (1008)`, equipping/unequipping, and shops.
3. **Combat, Skills & Monsters:** Physical/magic combat (`MsgInteract 1022`, `MsgMagicEffect 1105`), monster spawning & drops, leveling, and GM commands.
