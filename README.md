# GtfoPunk My Game My Rules

Gameplay overhaul for GTFO: party difficulty, a lobby loadout with gadgets, new sentries and a silenced pistol, a frag grenade, booster syringes, infection, killable scouts. Everyone in the lobby must have it; not for playing with randoms.

A plugin for [BepInEx 6 IL2CPP](https://thunderstore.io/c/gtfo/p/BepInEx/BepInExPack_GTFO/) (GTFO). Guide: [docs/GUIDE.md](docs/GUIDE.md) · [docs/GUIDE.ru.md](docs/GUIDE.ru.md). Package page text: [packaging/Rules/README.md](packaging/Rules/README.md), version history: [packaging/Rules/CHANGELOG.md](packaging/Rules/CHANGELOG.md), publishing: [docs/RELEASE.md](docs/RELEASE.md).

## Install (players)
Zip from releases → r2modman → *Settings → Import local mod*. Requires BepInExPack_GTFO.

## Build
- .NET SDK 6+ (target `net6.0`).
- A `lib/` folder with game references (not in the repo — these are game and BepInEx files). Copy from an r2modman profile:
  - `BepInEx/core/*.dll` (0Harmony, BepInEx.Core, BepInEx.Unity.IL2CPP, BepInEx.Unity.Common, BepInEx.Preloader.Core, Il2CppInterop.*)
  - `BepInEx/interop/*.dll` (Modules-ASM, GameData-ASM, SNet_ASM, Il2Cppmscorlib, Il2CppSystem*, UnityEngine.*Module, Unity.TextMeshPro, Shards-ASM, CellSound-ASM, Wwise-ASM, …)
  - `interop` appears after the first game launch with BepInEx.
- `./build.sh` → `dist/GtfoPunk_MyGameMyRules-<version>.zip` (ready package for r2modman/Thunderstore). Other library path: `dotnet build Rules/GtfoPunk.Rules.csproj -c Release -p:GameLibs=/path/to/lib`.

## Layout
- `Shared/Core` — the shared GtfoPunk core (identical in both repositories): modules with individual config toggles, Client/Host/Everyone marking, auto-disable when a mod with the same feature is installed, en/ru in-game language.
- `Rules/Modules` — one file per feature.
- `packaging/Rules` — manifest, README, CHANGELOG, package icon.

## Conventions
- Code comments, config descriptions and log messages are in English. Russian lives only in localization (`Lang.T(en, ru)`) and in the Russian section of the player README.

Related repository: gtfo-punk-qol-mod (quality of life, no balance changes).

## License
MIT — see [LICENSE](LICENSE).
