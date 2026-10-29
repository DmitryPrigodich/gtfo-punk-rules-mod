# GtfoPunk My Game My Rules — guide

Gameplay overhaul for GTFO (BepInEx 6, IL2CPP). [Русская версия](GUIDE.ru.md).

- **Everyone in the lobby needs the mod.** It changes game data (items, sentries, level generation, alarms), and the
  host's choices are sent to everyone. Mixed lobbies (some with, some without) are not supported.
- Made to be played together with **GtfoPunk QoL** (client-side quality of life, separate package).
- Settings: `BepInEx/config/AmDDRed.GtfoPunk.Rules.cfg`. Every feature has `Enabled` in its own section.
  The rules that must match for everyone (difficulty, alarm mode) are picked by the host in the lobby and synced.
- In-game text: English or Russian — `[General] TextLanguage = auto` follows the game's language (`en` / `ru` to force).

## Contents
1. [Difficulty](#difficulty) · 2. [Alarm doors](#alarm-doors) · 3. [Loadout](#loadout) · 4. [Gadgets](#gadgets) ·
5. [Frag grenade](#frag-grenade) · 6. [Sentries](#sentries) · 7. [Silenced pistol and rifle](#silenced-pistol-and-rifle) · [Grenade launcher](#grenade-launcher) ·
8. [Booster syringes](#booster-syringes) · 9. [Infection](#infection) · 10. [Scouts](#scouts) ·
11. [Fire modes and quick melee](#fire-modes-and-quick-melee) · 12. [Config reference](#config-reference) ·
13. [Multiplayer and compatibility](#multiplayer-and-compatibility) · 14. [Troubleshooting](#troubleshooting)

---

## Difficulty

The **DIFFICULTY** button sits in the lobby to the right of READY FOR CAGE DROP. The host clicks it (left — next,
right — back); everyone else sees the host's choice. The text next to the button says what the level means.

| Level | Alarm doors (shift mode, default) | Starting ammo, pack fill, item count |
|---|---|---|
| I — Warden's pet | −5 scans: every alarm becomes a plain scan, no wave | 100% |
| II — Expendable asset | −4 scans | 80% |
| III — Prisoner (default) | −3 scans | 60% |
| IV — Survival odds: low | −2 scans | 40% |
| V — The Complex is hungry | −1 scan | 20% |
| VI — Get the f*** out | as in the game | 0% — melee only |
| DEFAULT — as the Warden intended | as in the game | as in the game; no loadout, no gadgets, no infection growth, artifacts stay |

New gear (sentries, the silenced pistol, the frag grenade in levels) is available in every mode, DEFAULT included.

Settings `[Difficulty]`:
- `Level` — the host's level (0 — DEFAULT, 1–6). The button writes it.
- `AlarmMode` — `1` shift (default), `0` cap (I — no scans, II — at most 1 … VI — at most 5).
- `AlarmShifts` — shifts for I…VI, default `-5,-4,-3,-2,-1,0`. A positive number adds scans (repeats the last one).

## Alarm doors

`[DoorAlarms]` applies the difficulty to security doors with an alarm:
- An alarm keeps its scans minus the shift (or up to the cap). The door's text is renamed to match (Class V → Class II).
- An alarm with no scans left becomes **one plain scan without a wave**.
- `KeepScriptedDoors = true` (default): doors whose scan starts or finishes a scripted event, apex doors and
  checkpoint doors keep the game's own alarm — changing them can break the level's script (e.g. R8B1).
- Non-alarm scans are never touched.

## Loadout

The three booster slots in the lobby become a loadout. Click a slot to step forward, right-click — back.
Choices are saved in the config and sent to everyone; the host hands everything out a few seconds after the drop.

| Slot | Choices |
|---|---|
| PACK | none, ammo, medi, tool, disinfection (a full pack = 5 charges × difficulty share) |
| ITEM | frag grenade, C-foam grenade, explosive trip mine, C-foam trip mine, fog repeller, glow sticks, lock melter, I2-LP syringe — amount = the biggest stack found in levels × difficulty share |
| GADGET | bio-tracker screen, thermal goggles, hacking tool, soft boots |

- Stock boosters can't be selected while the loadout is on (nothing is consumed); their effects live on as
  [booster syringes](#booster-syringes). `HideBoosterIcons` hides the stock booster icons above health.
- Bots: the host gives each bot `BotPack` / `BotItem` (none by default).
- `ItemChoices` — which items the ITEM slot cycles through (item IDs). `IconSize` — icon size in the slots.
- The slot icons are PNG files shipped with the mod (white on transparent, 256×256); replace a file to change it.

## Gadgets

The gadget key is **N** (`[Loadout.Gadget] Key`). The panel above the health bar shows the gadget, its state and battery.

- **Bio-tracker screen** — a small tracker display on the panel: every 1.5 s a sweep shows enemies within 20 m (through
  walls), sleepers dimmer, with the distance to the nearest. It only shows — it doesn't tag. `[Loadout.BioTracker]`.
- **Thermal goggles** — the thermal sight's picture over the whole view. **Z** steps through zoom levels, **X** through
  colour palettes (the game's own heat gradients; the choice is remembered). Taking damage breaks the picture up; a dying
  battery adds noise and signal drop-outs. `[Loadout.Thermal]`.
- **Hacking tool** — while any human player has this gadget, only they can use the hacking tool (N takes it out); the
  others get "HACKING TOOL NEEDED — <name> has it (or a lock melter)" at a hackable lock. If nobody picked it, everyone
  hacks as in the game, so a lock never blocks the way.
- **Soft boots** — always on, no battery: running, jumping and hard landings make only walking noise for enemies. The
  price: falls hurt ×1.5 and from 70% of the usual height, running is 8% slower. `[Loadout.SoftBoots]`.

Battery (screen and goggles): 90 s of use, 45 s to recharge from empty, won't switch on below 10%
(`[Loadout.Gadget] BatterySeconds`, `RechargeSeconds`, `MinCharge`).

## Frag grenade

A copy of the C-Foam Grenade (same model, throw and pickup) under its own item **4252**, named "Frag Grenade" /
"Граната". It's a separate item: it doesn't stack with foam grenades or glow sticks.
- Explodes after the foam grenade's fuse: 8 m radius, 120 damage at the centre (falls off to the edge; **hurts players**),
  fire and smoke of a mine, camera shake, and a 30 m noise that wakes and alerts enemies.
- Found in level consumable boxes and lockers (`InLevels`, `SpawnWeight`); a player carries up to 2 (`Stack`, also
  applies to the C-foam grenade).
- Settings `[Items.Grenades]`: `NameEn`, `NameRu`, `BlastRadius`, `BlastDamage`, `BlastEffect`, `BlastShake`, `BlastNoiseRadius`.

## Sentries

New tools in the tool slot (in the gear list as "DRED GARAGE …"). Every sentry gets a small status light:
green — full, yellow — half used, red — not working.

| Name | Type | What it does |
|---|---|---|
| DRED GARAGE TL1 | Searchlight Sentry | Turns after enemies in its sector; the beam disturbs sleepers and wakes one held in it for 2 s. Doesn't shoot. |
| DRED GARAGE TF I | Fog Repeller Sentry | A fog-free sphere (10 m) around it; its hum disturbs sleepers nearby. Doesn't shoot. |
| DRED GARAGE TF II | Fog Turbine Sentry | The same with a scaled-down turbine model. Doesn't shoot. |
| DRED GARAGE TB1 | Bio-tracker Sentry | Scans its sector every 3 s (25 m, through walls) and tags enemies. Doesn't shoot. |
| DRED GARAGE TCF3 | Foam Sentry | Lobs acidic foam: slows (doesn't freeze), 3 dmg/s acid, staggers attacks. Uses sentry ammo. |
| DRED GARAGE TGL3 | Grenade Sentry | Bursts of 3 contact grenades (3.5 m, 40 dmg), won't fire near players or closer than 4 m. Uses sentry ammo. |

- Non-shooting sentries run on their ammo as a battery; at zero they switch off. Recharge: pick up, refill with a tool
  pack, place again (`[Turrets] Battery`).
- Combat sentries (stock and ours) prefer tagged enemies, turn to them faster and open fire sooner.
- Every sentry has its own section `[Turrets.<Kind>]` with the same core keys: `Enabled`, `Name`, `Label`, `Type`,
  model position (`ModelOffset`, `ModelRotation`, `ModelScale`; `Parts` where gear parts sit in the cradle), a dim warm
  bulb (`Bulb`, `BulbOffset`; goes out with the battery), `BatterySeconds` for non-shooting kinds — plus its own numbers.
  TCF3's foam tank: `Tank` (an item's model, "Fog Turbine" — the turbine's barrel), `TankMirror`, `TankModelOffset`,
  `TankModelRotation`, `TankModelScale`. `LiveReload = true` re-reads the config while
  the game runs, so model positions can be tuned live.

## Silenced pistol and rifle

**DRED GLUKHAR S6** (type "Silenced Pistol") — a Bataldo 3RB body with a Mastaba top, the Shelling Arid 5's magazine
and rate of fire, 15 damage (a headshot kills a striker or a shooter, a body shot doesn't), the S49's sound.
A shot wakes sleepers only within 8 m (`WakeRadius`), disturbs them up to 20 m (`StirRadius`), beyond that — silence.
Each gun has its own section with the same keys as every gun (`[Weapons.Glukhar]`, `[Weapons.Tikhon]`): name, type,
body and parts, donors (stats, sound, grip, rate, ammo, recoil) and numbers — fire mode, burst, delays, damage, head
multiplier, falloff, magazine, reserve, spread; an empty number keeps the stats donor's, `x1.2` multiplies it — and its
shot noise: `Silent` (no game shot noise), `WakeRadius` (wakes within), `StirRadius` (stirs up to), `StirWakes` (stirred
ones may wake after shots in a row), `ThroughWalls`. GLUKHAR wakes within 8 m and stirs to 20 m, TIKHON 10 / 25 m.

**DRED TIKHON-A 24S** (main slot, type "Silenced Rifle") — the same quiet shot (Raptus Treffen 2's sound): Pres MOD 556
front, Receiver_Rifle_3, Köning PR 11's stock, Sight_23, the short 556 magazine, flashlight B; Van Auken LTC5's light, fast
**burst of 3**, **24 in a magazine, 120 in the reserve**, 7 damage a round; full damage to 25 m, then it falls by the
square (`FalloffCurve = Square`) and is gone by 50 m.

The optional guns — DRED ASSASSIN SMG-SA, DRED ASSASSIN AR-S, DRED BULL GOLORAT-2 — and the weapon fitting room (F8 in the lobby) are in a separate mod, **GtfoPunk Weapons**: the guns that are part of the game's kit live here, optional ones there.

## Grenade launcher

**DRED KHLOPUSHKA GL-10** (special slot, type "Grenade Launcher") — built on the TechMan Klust 6 (plain iron-sight aim):
Buckland AF6's front, Omneco's machine-gun receiver, Buckland XDist2's stock and iron sights, Klust 6's drum, flashlight E.
Every shot lobs one tiny contact grenade — the TGL3 sentry's, a bit weaker: **35 damage at the centre, 3.5 m**, fading
with distance (a striker dies within about 1.5 m of the blast; a big one takes about four direct hits). Semi-auto,
a **0.4 s charge** (hold the trigger, `ChargeTime`) and **1.2 s** between shots, a slug shotgun's kick and a camera
shake; **10 in a magazine, 30 in the reserve**. A loud shot heard through walls (wakes within 12 m, stirs to 30 m). The
blast is loud and hurts players too — don't fire point blank. Bots carry it but don't fire grenades. `[Weapons.Khlopushka]`:
`Name`, `Type`, `Body`, `Parts`, `RecoilFrom`, `Magazine`, `Reserve`, `ShotDelay`, `GrenadeDamage`, `GrenadeRadius`, `GrenadeSpeed`, `GrenadeSize`, `Loft`, `Shake`, `ChargeTime`, `Silent`, `WakeRadius`, `StirRadius`, `StirWakes`, `ThroughWalls`.

## Booster syringes

Every artifact spot gets a **booster syringe** instead (a copy of the IIx syringe, not in the terminal — look around).
Picking it up injects it at once: the effects of a random booster for 5 minutes (`SyringeMinutes`). Active effects are
listed above the watermark and on the map screen (Tab). `KeepDownsides` keeps boosters' negative effects.

## Infection

- Once infected, it grows by 1% every 2 minutes (up to 95%) until disinfected.
- From 50% running leaves you gasping (a quiet noise that unsettles sleepers within 6 m).
- From 70% you cough now and then (sleepers within 10 m notice; a few coughs in a row can wake them) and the view
  trembles slightly.
- `DisinfectionStrength` / `DisinfectionPauseMinutes` can weaken disinfection (off by default).

## Loud bioscans

When a security door's bioscan starts, the loudspeaker announces it — and that is noise: sleepers within 15 m of the
scan wake up (through walls), those up to 35 m stir. Clear the room before you start the scan. Zones behind closed doors
don't hear it. Optional (off by default): `DoorOpen` — the first security door opening (or every one, `AllDoors`), and
`ElevatorLanding` — the cage landing, make the same noise. Host only. `[Level.Doors]` `ScanStart`, `DoorOpen`,
`AllDoors`, `ElevatorLanding`, `Delay`, `WakeRadius`, `StirRadius`, `StirWakes`.

## Scouts

From the moment a scout catches you until its scream goes off it takes **×3 damage** and the charge-up lasts at least
2 s — kill it in that window and there's no scream and no wave (`[ScoutKill] ChargeupDamage`, `ScreamChargeup`).
The crawling "zoomer" scouts get a window too (while they charge the scream) — still being verified in play.

## Fire modes and quick melee

- **B** switches fire mode: semi / auto (+ burst on weapons that have it natively; `BurstForAll` for all). A non-native
  mode changes the spread: semi 30% tighter, auto 30% wider. Off if ExtraWeaponCustomization is installed.
- **V** — quick melee: tap — shove, hold — charged hit; then your gun comes back.

## Flashlight focus

Hold the flashlight key and turn the mouse wheel: narrower — reaches further and brighter, wider — lights more around
you but less far (5 positions from the gun's own light). A plain press toggles the light as usual (on release). The
wheel doesn't switch weapons while the key is held. Works on the helmet light too; the others see your beam as you set it. `[FlashlightFocus]`.

## Config reference

| Section | Keys |
|---|---|
| `[General]` | `TextLanguage` (auto / en / ru), `RespectOtherMods` |
| `[Difficulty]` | `Level`, `AlarmMode`, `AlarmShifts` |
| `[DoorAlarms]` | `KeepScriptedDoors` |
| `[FullAmmoOnStart]` | `IncludeTool` |
| `[Loadout]` | `Pack`, `Item`, `Perk`, `ItemChoices`, `PackCharges`, `ItemAmount`, `BotPack`, `BotItem`, `GiveDelay`, `BlockStockBoosters`, `HideBoosterIcons`, `IconSize` |
| `[Loadout.Gadget]` | `Key`, `BatterySeconds`, `RechargeSeconds`, `MinCharge`, `Panel`, `PanelOpacity`, `PanelBottom`, `PanelScale` |
| `[Loadout.BioTracker]` | `Range`, `ThroughWalls`, `Sound`, `SweepSeconds` |
| `[Loadout.Thermal]` | `Zoom`, `ZoomLevels`, `ZoomKey`, `Palette`, `PaletteKey`, `Noise`, `Glitch`, `Reticle`, `Distance` |
| `[Loadout.SoftBoots]` | `FallDamage`, `FallHeight`, `RunSpeed` |
| `[Items.Grenades]` | `NameEn`, `NameRu`, `InLevels`, `SpawnWeight`, `Stack`, `BlastRadius`, `BlastDamage`, `BlastEffect`, `BlastShake`, `BlastNoiseRadius`, `TurretRadius`, `TurretDamage`, `TurretFuse`, `TurretProximity`, `BounceOffEnemies` |
| `[Turrets]` | `LiveReload`, `HideGun`, `HideSensor`, `Battery`, `TaggedDamageBonus`, `TaggedTurnBonus`, `TaggedStartDelay`, `TaggedPriority`, `StatusLight`, `StatusLightStockSentries`, `StatusLightOffset`, `StatusLightRange`, `StatusLightIntensity` |
| `[Turrets.<Kind>]` | Searchlight, FogRepeller, FogTurbine, BioTracker, Foam, Grenade — see the descriptions in the file |
| `[Weapons]` | `Enabled` (the kit's silenced guns) |
| `[Weapons.Glukhar]`, `[Weapons.Tikhon]` | `Enabled`, `Name`, `Type`, `Body`, `Parts`, `Stats`, `SoundFrom`, `GripFrom`, `RateFrom`, `AmmoFrom`, `RecoilFrom`, `FireMode`, `Burst`, `BurstPause`, `ShotDelay`, `Damage`, `HeadMultiplier`, `FalloffFrom`, `FalloffTo`, `FalloffCurve`, `Magazine`, `Reserve`, `SpreadHip`, `SpreadAim`, `Silent`, `WakeRadius`, `StirRadius`, `StirWakes`, `ThroughWalls` |
| `[Level.Doors]` | `Enabled`, `ScanStart`, `DoorOpen`, `AllDoors`, `ElevatorLanding`, `Delay`, `WakeRadius`, `StirRadius`, `StirWakes` |
| `[Weapons.Khlopushka]` | `Name`, `Type`, `Body`, `Parts`, `RecoilFrom`, `Magazine`, `Reserve`, `ShotDelay`, `GrenadeDamage`, `GrenadeRadius`, `GrenadeSpeed`, `GrenadeSize`, `Loft`, `Shake`, `ChargeTime`, `Silent`, `WakeRadius`, `StirRadius`, `StirWakes`, `ThroughWalls` |
| `[FlashlightFocus]` | `Steps`, `MaxFocus`, `ReachExponent` |
| `[Items.Syringes]` | `ReplaceArtifacts`, `SyringeMinutes`, `KeepDownsides`, `InjectOnPickup`, `ShowOnMap` |
| `[Infection]` | `Growth*`, `Cough*`, `Breath*`, `Tremor*`, `Disinfection*` |
| `[ScoutKill]` | `ScreamChargeup`, `ChargeupDamage` |
| `[FireModes]` | `Key`, `Modes`, `BurstShots`, `BurstDelay`, `SemiSpread`, `AutoSpread`, `BurstForAll` |
| `[QuickMelee]` | `Key` |
| `[PartyRules]` | `LobbyPanel`, `DescriptionSize` |

Keys that are no longer used are removed from the file at start (old values are carried over to renamed keys).

## Multiplayer and compatibility

- Synced by the mod: difficulty and alarm settings (host), everyone's loadout choice, ammo top-ups, coughs.
- The host hands out the loadout, runs infection and the enemies (scouts, noise of boots and the silenced pistol).
- Fixed IDs (never reused): items 4250 (sentry grenade), 4251 (booster syringe), 4252 (frag grenade); gear 4200
  (silenced pistol), 4201–4208 (sentries). A bot whose saved gear is missing gets stock gear (QoL `BotGearGuard`).
- Requires BepInExPack_GTFO. GTFO-API is not needed.
- `RespectOtherMods = true`: a module turns itself off if a known mod with the same feature is installed.

## Troubleshooting

- Logs: `BepInEx/LogOutput.log` (lines start with `[Loadout]`, `[Grenades]`, `[Difficulty]` …).
- Reference lists written at start: `BepInEx/config/GtfoPunk.GearList.txt`, `GtfoPunk.GearParts.txt`; in the lobby — `GtfoPunk.Flashlights.txt` (flashlight settings and which gun has which).
- A level that seems stuck behind an alarm door: check that `KeepScriptedDoors = true`; DEFAULT difficulty plays the
  game's own alarms.
