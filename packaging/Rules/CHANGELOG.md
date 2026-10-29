# Changelog

## 0.9.42
- **Loud bioscans** (what was meant all along): a security door's bioscan starting — the loudspeaker announcing it — wakes the sleepers within 15 m of the scan (through walls) and stirs those up to 35 m; clear the room before the scan. The door opening (0.9.41) and the elevator landing (0.9.40) are now options, off by default (`DoorOpen`, `AllDoors`, `ElevatorLanding`). `[Level.Doors]` (was `[Level.FirstDoor]` / `[Level.Elevator]`). Host only. Log: `[DoorWake]`.

## 0.9.41
- **The first door is loud** (instead of 0.9.40's loud drop — a misunderstanding): opening the first security door, the one out of the elevator zone, wakes the sleepers within 20 m of it (through walls) and stirs those up to 40 m — the room behind it has to be cleared. `[Level.FirstDoor]` (was `[Level.Elevator]`), `AllDoors` — every security door. Host only. Log: `[DoorWake]`.

## 0.9.40
- **The drop is loud** (new, `[Level.Elevator]`, host): when the elevator lands and the loudspeaker speaks, sleepers within 25 m wake up (through walls) and those up to 50 m stir — the first room has to be cleared. Zones behind closed security doors don't hear it.
- **Falloff curve** — a new gun key `FalloffCurve`: Linear (the game's), Square ((1 − t)² after FalloffFrom) or Hyperbola (FalloffFrom / distance).
- **DRED TIKHON-A 24S**: 7 damage a round (2.4 took the head off but didn't kill), falloff by the square from 25 m to 50 m.

## 0.9.39
- **Shot noise is each gun's own** (the same keys on every gun): `Silent` (no game shot noise), `WakeRadius`, `StirRadius`, `StirWakes`, `ThroughWalls`. DRED GLUKHAR S6 wakes within 8 m and stirs to 20 m, DRED TIKHON 10 / 25 m. `[Weapons.Silent]` is gone (changed radii are carried to both guns); `[Weapons] Enabled` turns the kit's silenced guns on / off.
- **DRED TIKHON-A 24S** (was 20S — 24 rounds a magazine); same gear ID.
- **DRED KHLOPUSHKA GL-10**: a charge-up — hold the trigger 0.4 s (`ChargeTime`, the game's own charge mechanic); a loud shot heard through walls (wakes within 12 m, stirs to 30 m).

## 0.9.38
- **Config, one scheme everywhere**: `[Turrets]` / `[Turrets.<Kind>]` (were `[TurretVariants…]`), `[Items.Grenades]`, `[Items.Syringes]`, and one section per gun with the same keys as every gun — `[Weapons.Glukhar]`, `[Weapons.Tikhon]`, `[Weapons.Khlopushka]` (+ `[Weapons.Silent]` for the quiet shot's radii). Gun keys: Name, Type, Silent, Body, Parts, Stats, SoundFrom, GripFrom, RateFrom, AmmoFrom, RecoilFrom, FireMode, Burst, BurstPause, ShotDelay, Damage, HeadMultiplier, FalloffFrom, FalloffTo, Magazine, Reserve, SpreadHip, SpreadAim — an empty number keeps the stats donor's, `x1.2` multiplies it. Your values are carried over from the old sections once.
- **DRED TIKHON-A 20S**: the 2-round burst kicked — a striker flinched after the first round and the second missed. Now a light, fast SMG burst of 3 (Van Auken LTC5's rate and recoil), 2.4 a round (three in the head kill a striker), **24 in a magazine, 120 in the reserve**.
- **DRED KHLOPUSHKA GL-10**: built on the TechMan Klust 6 — plain iron-sight aim (on the Omneco LRG it aimed like a scope); the grenade flies much smaller; 1.2 s between shots (was 0.6); a slug shotgun's kick (`RecoilFrom`) and a camera shake (`Shake`).
- Shared core: a module's config section may differ from its id, with old sections carried over.

## 0.9.37
- The rule: guns that are part of the game's kit live here, optional ones in GtfoPunk Weapons. **DRED TIKHON-A 20S** (silenced rifle) moved in from Weapons: `[SilencedGun] Rifle`, `RifleSoundFrom`; the same gear ID. Pres MOD 556 front and stats, Receiver_Rifle_3, Köning PR 11 stock, Sight_23, short 556 magazine; Hanaway's rate, a 2-round burst, 20 + 100 rounds, 3.6 a round (both in the head kill a striker); quiet shot.

## 0.9.36
- **Grenade launcher** DRED KHLOPUSHKA GL-10 (new module GrenadeLauncher, special slot): each shot lobs one small contact grenade — the TGL3 sentry's, weaker: 35 damage / 3.5 m (sentry 40 / 5); semi-auto 0.6 s, 10 in a magazine, 30 in the reserve; the gun's bullets do nothing. Look from the user's garage build: Omneco LRG body, Buckland AF6 front, Omneco MG receiver, XDist2 stock and iron sights, Klust 6 drum, flashlight E. Bots carry it but don't launch grenades.

## 0.9.35
- The silenced pistol is now **DRED GLUKHAR S6** (was DRED SILENZIO GP6-S; same gear, a saved loadout keeps it).
- The silenced main-slot guns (DS SMG-S A, DS AR-S — `[SilencedGun] MainGuns`) moved to **GtfoPunk Weapons** as DRED ASSASSIN SMG-SA and DRED ASSASSIN AR-S, next to the new DRED TIKHON-A 20S.
- Bots: quick melee and flashlight focus no longer act on the host's bots (the game counts them as "locally owned" too).

## 0.9.34
- The garage (F8 weapon fitting room, DRED GARAGE PROTOTYPE) and the guns made in it (`[GarageGuns]`, DRED BULL GOLORAT-2) moved to a separate mod, **GtfoPunk Weapons** — weapon building isn't what My Game My Rules is about. Its settings stay in this config file (no longer cleaned up) and are carried over by the new mod on its first start. The silenced guns (GP6-S, DS SMG-S A, DS AR-S) and the sentries stay here.
- Gear log lines are tagged `[GearFactory]`.

## 0.9.33
- Garage: the lobby loadout cards (bots' especially) blinked while the garage was open — the game shows a bar's cards again on its own refreshes and 0.9.32 hid them again every half second. Now they are hidden once, and while the garage is open the game's own "show the loadout" calls are turned into "hide".

## 0.9.32
- Garage: the gun is seen turning (0.9.31 confirmed). While the garage is open, the players' loadout cards in the lobby are hidden with the game's own "hide loadout" (as in the clothes view) and come back when it closes. `[Garage] HideLobbyUi` (on by default).

## 0.9.31
- Garage preview: 0.9.30 found the right camera and layer (`CM_Camera`, layer 31 'MenuDeferredScene'), but the lobby scene is scaled up — the players stand ~2700 units from the camera and are ~1000× a person — so a gun of normal size there was a speck. Now the gun takes the scene's scale (the player model's height / 1.8 m) and stands at 0.8 of the players' distance. New keys `[Garage] PreviewPosition` (screen x,y, default 0.62,0.5), `PreviewDepth` (0.8), `PreviewSize` (1.2), `PreviewSpin` (30°/s). Log: `[Garage] player height …`.

## 0.9.30
- Garage preview, still unseen in 0.9.29: the camera it was placed by was the menu UI camera (at the origin), not the one that draws the lobby players, and the layer came from the wrong renderer of the player model (9 'PlayerSynced'). Now the layer is the most common one among the model's mesh renderers, set on every part of the gun (also the ones that load later), and the camera is the one — even a disabled one — whose view has the player in it and whose mask has that layer. Parts are no longer forced visible. Log: `[Garage] model layers …`, `[Garage] cameras: … → <chosen>`, `[Garage] preview: …`.
- Gear log lines say `[GearFactory] added: …` (were `[Garage]`, confusing next to the garage).

## 0.9.29
- Garage preview: the gun was assembled (log: 23–26 renderers) but not seen — it stood too close to the camera, on a layer of its own, with its parts hidden as gear is until wielded. Now it goes into the lobby scene next to the players, on their layer, at about their depth, ×1.6, with every part shown. Log: `[Garage] preview: camera …, model …`.

## 0.9.28
- Garage: the gun is shown **turning on the right side of the screen** (the lobby characters don't hold their guns): the third-person model is assembled again on every part change and stands in front of the lobby camera; the panel stays on the left. Log: `[Garage] preview on camera …`.

## 0.9.27
- Garage: the prototype is taken from the game's own main-slot list (0.9.25–0.9.26 looked in the lobby backpack and didn't find it — "no prototype"); **F8 puts it in your hands** and opens the panel. Keys: **W S** — part, **A D** — change it (↑↓ and , . work too).

## 0.9.26
- Garage: parts change with **< >** (comma, period) — ← → move the lobby camera; the prototype is also found through the game's "last equipped" gear if the lobby backpack doesn't have it; a log line when no prototype is found in the main slot.

## 0.9.25
- New module **Garage** — a weapon fitting room in the lobby, stage 1 (parts): equip **DRED GARAGE PROTOTYPE** in the main slot, press **F8**, ↑↓ — pick a part (front, receiver, stock, sight, magazine, flashlight), < > (comma, period) — change it; the gun in your hands is rebuilt at once. Each part shows which vanilla guns use it (or "unused in the game"). The combination is kept in `[Garage] Prototype`; Enter writes a ready line to `BepInEx/config/GtfoPunk.Garage.txt`. Next stages: stats and fire mode, then saving as a new gun.

## 0.9.24
- Flashlight focus:
  - works on the **helmet light** too (melee, tools — anything without a gun light);
  - **others see your focus**: it's sent to everyone (Net type 5) and applied to your light on their side, gun or helmet. Everyone needs the module now.

## 0.9.23
- New silenced main-slot gun **DS AR-S** (type "Silenced Rifle"): Drekker Pres MOD 556's front, magazine and stats, Receiver_Rifle_3, Drekker DEL P1's stock, Sight_23; a **2-round burst** by default — 8 damage a round, full to 30 m (falls off to 60 m): both rounds in the head kill a striker, a burst to the body doesn't. B — semi / auto.

## 0.9.22
- New module **FlashlightFocus**: gun flashlight focus, like turning a real flashlight's lens — **hold the flashlight key and turn the mouse wheel**: narrower reaches further and is brighter, wider lights more around you but less far (5 positions; e.g. light B, 35° / 15 m / 0.5: narrowest 18° / 26 m / 0.87, widest 70° / 9 m / 0.29). The wheel doesn't switch weapons while the key is held; a plain press toggles the light as before, on release. Local only. `[FlashlightFocus] Steps`, `MaxFocus`, `ReachExponent`.
- The silenced guns' flashlights are back to their own (the weakened copies and `Flashlight` / `MainFlashlight` are gone).

## 0.9.21
- **DS SMG-S A** — one gun again: TR22 Hanaway's magazine (Mag_556_40rnds_A — the same part as on the HXC), Sight_14, Hanaway's ammo; the HXC / ARBALIST test pair is gone.
- Reference file `BepInEx/config/GtfoPunk.Flashlights.txt` (written once in the lobby): every flashlight setting (range, angle, brightness, colour), which flashlight part uses which, and which gun has which part.

## 0.9.20
- **Frag grenade, fix two**: the C-Foam Grenade's thrown model is a *GlueClusterGrenadeInstance*, not the GlueGrenadeInstance that 0.9.16–0.9.19 hooked — so the frag grenade went on foaming. Both kinds are handled now: no foam, a mine's blast. Log: `GlueClusterGrenadeInstance thrown: … → frag grenade`, `grenade exploded at …`.
- Silenced main-slot guns, round three: only **DS SMG-S A** stays, with Sight_14, in two versions — HXC's straight magazine (**A HXC**) and Arbalist V's (**A ARBALIST**); TR22 Hanaway's ammo (rounds in reserve and magazine — option `ammo=`). The other test guns are gone.
- New module **GarageGuns** — ordinary (loud) garage guns: **DRED BULL GOLORAT-2**, a bullpup rifle, an alternative to the Accrat Golok DA (the old SMG-S C): 3-shot burst, 7 damage, full damage only to 8 m (falls off to 18 m), quick bursts, a bit wider spread; Golok's own sound and flashlight. `[GarageGuns] Guns`, same format as `MainGuns` (+ `type=`).
- TCF3: the tank as tuned in play is the default — one C-Foam Grenade (`Tank = C-Foam Grenade`, offset 0.035,0.025,0.125, rotation 79,90,177, size 1.1); bulb 0.2.

## 0.9.19
- Silenced main-slot guns, round two (picked: SMG-S A; B dropped):
  - **burst by default** — 3 shots, 6 damage, ×0.95 on the head, full damage up to 25 m (falls off to 45 m): two headshots kill a striker at mid range (like one from GP6-S), a body burst doesn't. B switches to semi / auto (FireModes: semi tighter, auto wider);
  - the 24-round magazine (Mag_556_24rnds) on A and D, clip 24;
  - **SMG-S A** in 12 sight variants to compare (Sight_18, 14, 14b, 2, 16, 25, 3, 4, 7, 11, 17, 23 — the sight is in the name);
  - **SMG-S C** — short-range burst on Accrat Golok DA's stats: 7 damage, ×1 head, full damage only to 8 m (falls off to 18 m), quicker bursts (0.18 s), a bit wider spread;
  - **SMG-S D** — Hanaway front, Receiver_Rifle_3, precision stock (Stock_Precision_1), Sight_11;
  - shot sound of the Raptus Treffen 2 (`MainSoundFrom`);
  - per-gun options in `MainGuns` after '|': stats, burst, damage, precision, falloff, delay, clip, spread.
- **Weaker flashlights** on the silenced guns: GP6-S at 50% (`Flashlight`), the main-slot ones at 60% (`MainFlashlight`); each keeps its own flashlight part.

## 0.9.18
- **Silenced main-slot guns** (test variants, like the GP6-S fitting room): rate of fire, damage and magazine of the Van Auken LTC5, the same quiet shot as the silenced pistol (wakes only within 10 m), type "Silenced SMG". Three variants to pick from:
  - **SMG-S A** — LTC5 + Hanaway front (Front_Rifle_2), HXC stock (Stock_Rifle_2), Golok sight (Sight_18);
  - **SMG-S B** — LTC5 + Hanaway front, the unused UZI stock (Stock_UZI_2), J 300 sight (Sight_1);
  - **SMG-S C** — Golok DA body with the unused bullpup front and receiver (Front_Bullpop_2, Receiver_Bullpop_1), bullpup stock, HXC sight (Sight_23); Golok's hand grip.
  Settings `[SilencedGun] MainStatsFrom`, `MainGuns`, `MainWeaponType`.

## 0.9.17
- **Frag grenade fix**: 0.9.16 threw a plain foam grenade — the thrown grenade didn't carry our item ID. It's now recognised by its throw (where and when a frag grenade was thrown) as well as by its ID, and explodes like a mine. Log: `[Grenades] thrown: item …` and `glue grenade thrown: … → frag grenade`.
- **TCF3 foam tank**: one tank instead of two mirrored foam grenades; by default the fog turbine's barrel. New keys `[TurretVariants.Foam] Tank` (item name, "Fog Turbine" — the turbine), `TankMirror` (two mirrored tanks), `TankModelOffset`, `TankModelRotation`, `TankModelScale` (the old TankModel / TankOffset / TankRotation / TankScale are dropped).
- **Bulb on every new sentry**: `Bulb` (brightness, 0 — none) and `BulbOffset` (from the barrel cradle) in each `[TurretVariants.<Kind>]`; the bulb goes out with the battery and follows the config live. On by default on TF I and TF II (0.3).
- Sentry models: the positions tuned in play are the new defaults — TF I, TF II, TB1, TCF3 (launcher), TGL3 (also Range 20, MinRange 3); sentry grenade radius 5 m.
- `TaggedDamageBonus2` → `TaggedDamageBonus` (value carried over).
- The plugin's version (log, watermark) now always matches the package (0.9.16 still reported 0.9.15).

## 0.9.16
- **Frag grenade reworked**: now a copy of the C-Foam Grenade under its own item 4252 ("Frag Grenade" / "Граната") — the foam grenade's model, throw and pickup, a separate item that no longer merges with glow sticks. Explodes after the foam grenade's fuse: 8 m, 120 damage, mine fire and smoke, shake, 30 m noise. The loadout's grenade choice (136) gives it too. Config: `HandBlastRadius` / `HandBlastDamage` → `BlastRadius` / `BlastDamage` (changed values carried over); `HandModels`, `HandDelay`, `DiagnosticDump` removed.
- **Alarm shift from −5**: `Difficulty.AlarmShifts` (default `-5,-4,-3,-2,-1,0` for I…VI) — level I turns even Class V alarms into a plain scan, VI is as in the game. The host's shifts are sent to everyone.
- Sentry types in the weapon list: Searchlight / Fog Repeller / Fog Turbine / Bio-tracker / Foam / Grenade Sentry (`[TurretVariants.<Kind>] Type`).
- **Config cleaned up and made uniform** (old values are carried over):
  - gadget settings moved to sub-sections: `[Loadout.Gadget]` (Key, BatterySeconds, RechargeSeconds, MinCharge, Panel, PanelOpacity, PanelBottom, PanelScale), `[Loadout.BioTracker]` (Range, ThroughWalls, Sound, SweepSeconds), `[Loadout.Thermal]` (Zoom, ZoomLevels, ZoomKey, Palette, PaletteKey, Noise, Glitch, Reticle, Distance), `[Loadout.SoftBoots]` (FallDamage, FallHeight, RunSpeed); `SlotIconSize` → `IconSize`;
  - removed: experiments and diagnostics (GiveIntoBackpack, ItemPictures / ItemPictureSize / ItemPictureTilt / ItemIcons — the model-silhouette renderer is gone, the drawn icons stay; ThermalMaterial, ThermalSearch, ThermalDiagnostics, ThermalOnItemCamera; TurretVariants FogDiagnostics, FogTurbine.ModelSearch; General.TextLanguageAutoOnce);
  - every key no longer used is removed from the file at start (also leftovers of removed modules).
- Fix: sentry grenades and frag grenades forget their object once gone — over a long level a new grenade could get the old one's address and lose its explosion effect.
- Documentation: `docs/GUIDE.md` (English), `docs/GUIDE.ru.md` (Russian); shorter package README.

## 0.9.15
- ScoutKill covers the zoomers (the crawling scouts): the ×3 window is now also any moment a scout charges its scream, and a scout that's alerted (rushing at you) but hasn't screamed yet. Diagnostics: the first scout state changes of each level go to the log (`[ScoutKill] Scout_zoomer#…: A → B (mode …, scream …)`).
- Thermal goggles: the palette picked with X is remembered (ThermalPalette).

## 0.9.14
- Thermal goggles: the test tagging of enemies is removed (ThermalMarksEnemies / ThermalMarkRange are dropped from the config).

## 0.9.13
- Lobby loadout icons twice as big again (Loadout.SlotIconSize = 24; a changed LobbyIconSize is carried over).
- Soft boots icon (`soft_boots.png`).
- New package icon.

## 0.9.12
- Difficulty: **alarm shift** (Difficulty.AlarmMode = 1, the new default; the host's setting is used by everyone). Instead of a cap, every alarm loses scans: I −4, II −3, III −2, IV −1, V as in the game, VI +1 (the last scan once more) — a Class V door stays harder than a Class II one. No scans left — one plain scan without a wave. The class in the name follows. AlarmMode = 0 — the cap as before.
- New gadget **Soft boots** (always on, no battery): running, jumping and hard landings sound like walking to the enemies — sleepers don't hear you run past. The price: falls hurt from a lower height and harder (×1.5 from 70% of the height), running 8% slower (Loadout.BootsFallDamage / BootsFallHeight / BootsRunSpeed). Icon file `soft_boots.png` if you draw one.
- Thermal goggles:
  - the picture breaks up when you take damage (a drop-out, then heavy noise) and gets noisier as the battery runs low; below 15% the signal drops out now and then (ThermalGlitch);
  - palettes: X steps through the thermal palettes found in the game (the log lists them: `[Thermal] palettes: …`); ThermalPalette picks one by name;
  - TEST: ThermalMarksEnemies (off by default) — tags enemies in view like the bio-tracker, every 1.5 s, within 25 m.

## 0.9.11
- **Loadout handed out again**: in 0.9.9–0.9.10 the pack and item were put "straight into the backpack" — the game ignored that, nobody got anything. Back to spawning a pickup and picking it up for the player; the hitches that way came from the checkpoint / migration capture asking those items for their state — handed-out items are now left out of the capture (log: `[Loadout] capture: skipped a handed-out item`). The backpack way stays as an experiment (Loadout.GiveIntoBackpack, off).
- DoorAlarms.KeepScriptedDoors was reset to the default on every start (cleanup ran before the key was read).

## 0.9.10
- Lobby loadout icons twice as big again (Loadout.LobbyIconSize = 12; a changed ItemIconSize is carried over).

## 0.9.9
- **Loadout handout fixed at the root of the hitches**: packs and items now go straight into the backpack (as the game does at drop-in). The old way (spawn a pickup and pick it up) left a broken item replicator: every checkpoint / migration capture threw an error — thousands per level, a log of 150+ MB and hitches. Loadout.GiveAsPickup brings the old way back.
- Difficulty I: an alarm becomes **one plain scan without a wave** (alarm doors no longer lose their scan — some scripted doors never opened). Doors with scripted scans (events on scan start / done), apex and checkpoint doors keep the game's own alarm at every level (DoorAlarms.KeepScriptedDoors).
- Hacking tool: the restriction holds only while a human player in the party has the gadget; otherwise (solo without it, nobody picked it) everyone hacks as in the game — no more softlocks. Looking at a hackable lock without the tool: "HACKING TOOL NEEDED — <who> has it (or a lock melter)".
- Lobby: the loadout icons are upright and twice as big (Loadout.ItemIconSize); right click on a loadout slot — one step back. The text next to DIFFICULTY is smaller and no longer part of the button (PartyRules.DescriptionSize).
- Names in the weapon list: the silenced pistol is a "Silenced Pistol" (SilencedGun.WeaponType), the new sentries have their own type (Fog Repeller Sentry, Searchlight Sentry, … — TurretVariants.<kind>.Type) instead of "High Caliber Pistol" / "Burst Sentry".
- FireModes: semi switched to from an auto / burst weapon is 30% more accurate, auto switched to from a semi / burst weapon is 30% less (SemiSpread / AutoSpread); burst and the weapon's own mode keep its spread. The mode message shows the change.
- Booster syringe: the picked-up syringe no longer leaves a shadow (taken out of the level's culling too). Its effects at the bottom right: the time on its own line, then one effect per line, right above the watermark.
- ScoutKill: logs which state other scout kinds are in when hit outside the window (for the crawling scouts).

## 0.9.8
- Lobby: next to the DIFFICULTY button — what the chosen level means (alarm doors, ammo / pack / items). **Right click** on a lobby rule button steps back.
- Lobby icons: also found next to the DLL (r2modman puts the `icons` folder's files there).
- Booster syringe in a locker: it's picked up as usual (so it works in lockers) and the "picked up" state just removes it — it no longer stays in place after the injection, and doesn't go to the consumable slot.
- Hand grenade: in hand it uses the C-Foam Grenade's own models and throwing animation (the glow stick's hand model never showed the grenade). Grenades.HandModels.
- ScoutKill: the window is longer — from the moment the scout catches you (its feelers) until the scream goes off (it was only the charge-up).
- Map screen: the booster effects are under the legend (left, middle).
- The version line under the game's watermark: added by a hook on the watermark update instead of a scene search every few seconds (a possible source of hitches).

## 0.9.7
- New module **Difficulty**: the lobby button (next to READY FOR CAGE DROP) is now DIFFICULTY, picked by the host, sent to everyone:
  - I Warden's pet — alarm doors just open (no scan), 100% starting ammo (weapons and tool), full pack, full item stack;
  - II Expendable asset — alarms at most 1 scan, 80%; III Prisoner — 2 scans, 60%; IV Survival odds: low — 3, 40%; V The Complex is hungry — 4, 20%; VI Get the f*** out — 5, 0% (melee only);
  - DEFAULT (As the Warden intended) — the game's alarms and ammo, no loadout or gadgets, infection as in the game, artifacts stay. New gear stays available.
  Item counts are rounded (0.4 → none, 0.8 → one). Replaces DoorAlarms.MaxAlarmClass and the ALARM button.
- New gadget **Hacking tool**: only who picked it can use the hacking tool (N takes it out); the others can't (it's put away, with a note) — break the lock. In DEFAULT everyone has it.
- **Lobby icons**: the loadout slots show drawn icons (packs, items, gadgets) from the mod's `icons` folder (`icons/<name>.png`, 256×256, white on transparent; replace a file to change an icon). ItemIcons.
- New module **ScoutKill**: a scout's scream charges up longer (2 s) and meanwhile the scout takes ×3 damage — kill it then and there's no scream and no wave.

## 0.9.6
- **Booster syringe effects didn't work at all**: the game's synced modifier call failed for the player (the log showed "id 0"). Effects are now applied as local modifiers (the log shows each modifier before → after).
- **Silenced pistol** weaker: own copy of the Arid 5 archetype with 15 damage — a headshot kills a striker or a shooter, a body shot doesn't (SilencedGun.Damage; 0 — Arid 5's own).
- Hand grenade: the look is put on the object the throwing script swings and releases.
- Bio-tracker screen: blips like the tool's — a dot, an iris and an outer ring, bigger; awake enemies red and blinking. TARGETS with a big count under it (red when someone is awake).
- The version shows under the game's watermark (bottom right): "GPUNK RULES v0.9.6".
- In-game texts: an old "ru" setting is switched back to auto once (follow the game's language).

## 0.9.5
- **Party rules**: now a button like the lobby's own, to the right of READY FOR CAGE DROP: "ALARM / LEVEL II"; a click steps LEVEL I → … → V → DEFAULT. Only the host changes it. (The framed panel never showed — the lobby camera couldn't be used for placing it.)
- **Gadget panel** above the health bar, drawn in screen space (always the same size): name and key, ON/OFF, the **battery as a progress bar** with percent — for the thermal goggles too. GadgetPanelBottom, GadgetPanelScale, GadgetHudOpacity.
- **Bio-tracker screen** redone after the tool's screen: dark glass in a frame, outer and middle range rings, spokes, the inner gauge arc, glowing white blips (sleeping — dimmer), range marks along the base, TARGETS count and the distance to the nearest one. It sits above the gadget line.
- Hand grenade: the look disappeared in hand — items 136 and 4250 share the same models and the second pass hid the first one's look. Fixed.
- TF I / TF II (and other non-tracking sentries): the faint scan-beam fan and its light are switched off.

## 0.9.4
- Party rules panel didn't show up (the lobby camera isn't in the camera list) — fixed; the same camera search is used for the HUD.
- **Gadget line** in the HUD font, sized to 2% of the screen height whatever the HUD scale (GadgetHudHeight), centered above the health readout; ON/OFF and the battery bar. The bio-tracker screen stands on it.
- **Thermal goggles** didn't switch on if no thermal-sight weapon was loaded in that level: the lens material is now kept from the lobby (and searched again if missing).
- **Booster syringe**: picking it up threw an error and fell back to the normal pickup — fixed. Map screen: effects listed on the left above the legend — "BOOSTER", then one line per effect with a readable name, the total change and time left; several syringes add up, opposite effects cancel out, each keeps its own time. Effect names in English or Russian.
- Syringe effects no longer stay on screen in the menus after leaving the expedition.
- Hand grenade in hand: the look is mounted on the moving part of the model (bone) and kept on the item's layer; details in the log.

## 0.9.3
- Fixed: **TF I / TF II sentries piled up copies of their model** every frame (the set-up was retried after the model was already attached).
- Party rules panel: now in the middle at the top of the lobby, in the game's own font, capitals, framed (the position setting was read wrong — it was stuck in the corner). New keys PanelPlace, PanelTextSize.
- **Bio-tracker screen** restyled after the game's bio-tracker (dark glass, pale lines, red blips), smaller, and by default sits **above health, in place of the booster icons** (BioScreenPlace = health / bottom, BioScreenRadius, BioScreenShift); a battery bar along its base.
- **Gadget line** (name, on/off, battery bar ||||||···· and %) moved to where the stock booster icons were, bigger; the booster icons are hidden (HideBoosterIcons, GadgetHudSize, GadgetHudShift). It used to sit right on top of the health readout because of the same position bug.
- Lobby slots: **text only** by default; model pictures are an option (ItemPictures).
- Hand grenade: the C-Foam look now moves with the throwing hand (it stayed in place and jumped out at the end).
- Grenade explosions: the tripmine's own explosion and flash effects (they weren't found before); an explosion is also caught when the grenade just disappears (the hand grenade didn't always report it).
- In-game texts follow the game's language by default ([General] TextLanguage = auto / en / ru).

## 0.9.2
- New **party rules panel** in the lobby (module PartyRules): settings everyone must share, picked by the host right in the lobby — left click — next value, right click — previous; the others see the host's choice (it's sent over the network, their own config is left alone). First rule: **door alarms** — as in the game, or at most I…V scans (DoorAlarms.MaxAlarmClass). The cap can now be changed between levels, also back to "as in the game" (PanelPosition, PanelTextSize, LobbyPanel).

## 0.9.1
- Infection: from 70% the view **trembles** slightly, stronger towards 100% (TremorFrom, TremorAmount).
- Infection: from 50%, after 4 s of running you **gasp for breath** — a quieter noise that unsettles sleepers nearby (6 m) without waking them (BreathFrom, BreathAfterRun, BreathNoiseRadius).
- Infection: optional, off by default — disinfection removes only part of the infection (DisinfectionStrength, 1 = as in the game) and pauses the growth for a while (DisinfectionPauseMinutes, 0 = no pause).

## 0.9.0
- **Config: every sentry has its own section** — `[TurretVariants.Searchlight]`, `[TurretVariants.FogRepeller]`, `[TurretVariants.FogTurbine]`, `[TurretVariants.BioTracker]`, `[TurretVariants.Foam]`, `[TurretVariants.Grenade]` — with the same keys for each: Enabled, Name, Label, ModelOffset, ModelRotation, ModelScale (+ BatterySeconds, Hum…, Parts, kind-specific settings). Old values are moved over automatically, old keys removed. `[TurretVariants]` keeps only the shared settings.
- Sentries renamed: **DRED GARAGE TL1** (searchlight), **TF I** (fog repeller), **TF II** (fog turbine), **TB1** (bio-tracker), **TCF3** (foam), **TGL3** (grenade launcher). Names and screen labels are in the config.
- **Sentry status light** on every sentry (ours and the stock ones): green — fresh, yellow — half used, orange — low, pulsing red — not working (empty / switched off) (StatusLight, StatusLightStockSentries, StatusLightOffset/Range/Intensity in [TurretVariants]).
- Sentry part models are kept from the lobby (no hitch / missing models on later levels).
- TL1 beam wakes sleepers after 2 s (WakeAfter). Tagged targets: no extra damage / stagger any more (TaggedDamageBonus2 = 1); faster turning, shorter start delay and priority stay.
- **Grenades go off properly**: mine explosion effect, sound and camera shake (BlastEffect, BlastShake); bigger blast — 8 m, 120 damage (HandBlastRadius, HandBlastDamage); the bang wakes and alerts everything within 30 m (BlastNoiseRadius). Sentry grenades — half the shake and noise.
- **Grenades and C-Foam Grenades are carried up to 2** (Stack), one per pickup — no more packs of 12 like glow sticks. The loadout gives 2.
- **Booster syringe is injected on pickup** (InjectOnPickup) — it no longer takes the consumable slot and pushes out the grenade you carry. Active syringe effects (with time left) and infection are listed on the **map screen** (ShowOnMap).
- **Lobby pictures** of packs, items and gadgets: white silhouettes of the models, upright and filling the slot (ItemPictureSize 3, ItemPictureTilt). Replaces the broken weapon-icon renderer approach.
- **Bio-tracker gadget is a screen now**: N switches on a motion-tracker screen at the bottom of the view (like the marine tracker in Aliens vs Predator) — a half-circle facing where you look, a pulse every 1.5 s, enemies within 20 m as blips (sleeping — dim, awake — bright), distance to the nearest one (BioRange, BioSweepSeconds, BioScreenSize/Opacity/Bottom, BioThroughWalls, BioSound). **It no longer tags enemies**, so it gives no sentry bonuses (only TB1 and the game's own bio-tracker do). The old pulse and its cooldown are gone (PerkScanInterval, BioTrackerAuto removed; PerkScan* settings moved to Bio*).
- **Gadget battery**, like the Half-Life flashlight: drains while the gadget is on (90 s), recharges while it's off (45 s), switches the gadget off when empty and won't switch on below 10% (GadgetBatterySeconds, GadgetRechargeSeconds, GadgetMinCharge).
- **Gadget line above health**: "BIO-TRACKER [N] on 73%", "THERMAL [N] off 100%" (GadgetHud, GadgetHudOpacity, GadgetHudOffset).
- New module **Infection**: once infected, infection keeps growing (1% every 2 minutes, up to 95%) until disinfected; from 70% the player coughs every 25–50 s (twice as often near 100%) — a noise sleepers within 10 m notice. Run by the host, coughs are heard by everyone (GrowthPercent, GrowthMinutes, GrowthCap, CoughFrom, CoughIntervalMin/Max, CoughNoiseRadius).
- **Thermal goggles**: no hitch on first use (material found during the elevator drop, drawn once hidden after landing); old-monitor on/off animation with a click.
- Code: TurretVariants split into files per sentry; shared config migration helper.

## 0.8.1
- New module **DoorAlarms**: alarm doors ask for at most **MaxAlarmClass** scans (2 by default — "Class V Alarm" becomes "Class II Alarm"). 0 — as in the game. Everyone needs the same setting.
- FT1 / FT1-T **hum**: sleepers within 6 m hear it through walls (FogHum, FogHumRadius, FogHumInterval); one that hears it for 8 s in a row wakes up (FogHumWakeAfter). The LT2 beam keeps waking sleepers that stay in it for 3 s (LightWakeAfter).
- **Tagged targets**: combat sentries are better against enemies tagged by BT3 or a bio-tracker — ×1.3 damage and stagger, ×1.5 turning speed, half the delay before opening fire, and they pick tagged enemies first (TaggedDamageBonus, TaggedTurnBonus, TaggedStartDelay, TaggedPriority). Uses the game's own tagged-target multipliers of the sentry archetypes.

## 0.8.0
- Loadout: the 3rd slot is now the **GADGET**, used with **N** (GadgetKey):
  - **Thermal goggles** — N on/off, **Z** steps the zoom (ThermalZoomLevels "0,0.5,0.75");
  - **Bio-tracker** — N sends a pulse (tags enemies within 15 m, beep), 8 s cooldown; BioTrackerAuto for the old automatic pulse.
  Keys are ignored while typing in chat.
- Loadout: no empty choices any more — pack, item and gadget always have something (old "None" values fall back to the first option).
- Loadout: lobby slots show rendered pictures of the items (made by the game's weapon icon renderer; ItemPhotos, ItemPhotoSize).
- Sentry GL5 renamed **DRED GARAGE GLF-3**: bursts of 3 grenades (GrenadeMagazine2), 2.5 s reload (GrenadeReload2); the screen shows grenades left.
- CF4 foam reworked: enemies are **slowed** instead of frozen (FoamMaxGlue 0.75 of "stuck"), foam is **acidic** (FoamAcidDps 3), and hits **stagger** enemies, interrupting attacks (FoamStagger, FoamStaggerInterval 2 s). CF4 screen keeps the tank % left.

## 0.7.2
- Loadout: given packs and items arrived empty (0%) — the ammo is now put on the item before it's picked up, and the owner's ammo storage is topped up afterwards (for other players via a network message).
- Loadout: lobby slot repaint error fixed (it spammed the log every second).
- Thermal perk: no magnification (ThermalZoom, default 0), no sight reticle / round border, adjustable noise (ThermalNoise, 2–3 = predator-style). All applied live. Dump file only with ThermalDiagnostics.
- Syringes: the injection hook never fired (the game inlines ApplySyringe) — now hooks the injection coroutine itself; every syringe use is logged.
- Grenades: missing collision / explosion layers of the unreleased grenade are filled in (logged), grenades hit enemies (BounceOffEnemies), sentry grenades get a proximity fuse (TurretProximity 0.8 m).
- Sentries: the screen text no longer wraps (FT1-T label was pushed up); CF4 and GL5 show ammo in % instead of the raw bullet count (256).
- Model positions: CF4 foam gun moved forward (FoamGunOffset2), FT1-T turbine lower and towards the sentry (TurbineSeat3), GL5 launcher parts smaller (GrenadePartsScale2 0.8).
- Hitches: fog repeller and grenade diagnostics no longer run on every level (FogDiagnostics, DiagnosticDump — off by default).

## 0.7.1
- Loadout: new perk **Thermal vision** (EXPERIMENT): the lens material of the game's thermal sights stretched over the whole view, toggled with ThermalKey (N). Options ThermalMaterial / ThermalSearch / ThermalDistance / ThermalOnItemCamera.
- Diagnostic: a few seconds into the first level the mod writes BepInEx/config/GtfoPunk.ThermalDump.txt (thermal sight parts, their materials, shader properties, all shader names).

## 0.7.0
- New module **Loadout**: the three booster slots in the lobby become a loadout. Click a slot to cycle:
  - 1st slot — **pack**: ammo / medi / tool / disinfection (5 charges, PackCharges);
  - 2nd slot — **extra item**: grenade, C-foam grenade, explosive / C-foam trip mine, fog repeller, glow sticks, lock melter, I2-LP syringe (ItemChoices);
  - 3rd slot — **perk**: bio-tracker pulse every 8 s within 15 m, tags enemies and beeps (PerkScanInterval / PerkScanRange / PerkThroughWalls).
  Choices are saved in the config and shown on every player's lobby card. At drop-in the host spawns each player's pack and item next to them and picks them up for them. Bots can get a pack/item too (BotPack, BotItem).
  Stock boosters can't be selected while BlockStockBoosters is on; already selected ones are unselected (not used up).
- Mod-to-mod messaging over the game's network (no GTFO-API needed) — players without the mod can't play with you.

## 0.6.0
- New module **Syringes**: every artifact spot in a level now holds a **Booster Syringe** instead of the artifact (a copy of the IIx syringe — same model and injection animation; not listed in the terminal, so you find it by exploring). Injecting it applies the effects of a random booster from the game data for 5 minutes (SyringeMinutes), synced like real booster effects; negative booster effects included unless KeepDownsides = false. Active effects and time left are shown on the HUD above the watermark and posted to your chat. Several syringes stack. Artifacts (and the booster progress they give) are gone while ReplaceArtifacts is on.

## 0.5.4
- **Battery** for the non-shooting sentries (LT2, FT1, FT1-T, BT3): their ammo now works as a battery that drains while they run — a full one lasts 300 s (BatterySecondsLT2 / FT1 / BT3). The screen shows the charge ("LT2 87%"); at zero the sentry switches off (light out, fog sphere shrinks, no scanning, "OFF"). Recharge like a normal sentry: pick it up, use a tool refill, place it again. Option Battery to turn it off.

## 0.5.3
- Grenade looks like a C-Foam Grenade (in hand, third person, on the floor, in flight) instead of a red glow stick; inventory icon from the C-Foam Grenade too.
- DRED GARAGE GL5 is now a revolver-style launcher: a drum of 6 grenades fired quickly (0.35 s apart), then a 4 s reload; a partly used drum tops up after 4 s without a target (GrenadeMagazine, GrenadeShotDelay, GrenadeReload).
- Fix: GL5 grenades exploded like hand grenades (2 s fuse, 5 m). The sentry now throws its own item (ID 4250, a clone of the grenade), so every player recognizes it: contact fuse, 3.5 m, 40 damage; throw force calibration works.

## 0.5.2
- Fix: a bot whose saved favorite tool was a removed test sentry (LT2-B TEST) couldn't spawn — "missing Category data block" — so the third bot couldn't be picked and the mission couldn't start.
  - Gear and category IDs are now fixed per item (4200 GP6-S, 4201 FT1, 4202 LT2, 4203 FT1-T, 4204 BT3, 4205 CF4, 4206 GL5); removing an item no longer shifts the others.
  - The retired test categories 4207/4208 are kept as searchlight aliases, so old favorites still load.
  - Any bot gear with a missing category is replaced by the stock sentry instead of breaking the spawn.

## 0.5.1
- Config descriptions and log messages are now in English (Russian stays in the in-game localization, `[General] Language = ru`). No behaviour changes; setting keys are unchanged, so existing configs keep their values.

## 0.5.0
- New module **Grenades**: the game's unreleased item 136 (CONSUMABLE_Grenade, ready-made GrenadeBase script) becomes **Frag Grenade / Граната**. Once its models are confirmed loaded, it's added to every level consumable distribution (lockers/boxes) with weight SpawnWeight × average. Hand grenade radius/damage/delay can be overridden (HandRadius, HandDamage, HandDelay). Diagnostics of the item's models go to the log at drop-in.
- New tool **DRED GARAGE GL5** — grenade launcher sentry. Throws real grenades (synced item throw, like a player throw) on an arc with lead; they explode on contact with a smaller blast than a mine (TurretRadius 3.5 m, TurretDamage 40, TurretFuse 0). Won't fire closer than 4 m or when a player is within 4 m of the impact point. Grenade costs 12 bullets. Mine deployer parts in the cradle (GrenadeParts). The throw force self-calibrates from the first shots (logged).
- Option HideSensor: hide the targeting sensor part on turrets with a hidden barrel (it floats in the air).

## 0.4.4
- **Live config**: while the game runs, saving `AmDDRed.GtfoPunk.Rules.cfg` re-reads it and immediately moves the models on already placed turrets (lamp, repeller, turbine, foam canisters, tool parts). Option LiveReload.
- DRED GARAGE LT2: test turrets LT2-A/B/C removed; one lamp with full manual placement — LampOffset3 (x, y, z) and LampRotation4 (first number = roll around the lamp axis, 90 = variant A). Searchlight dimmer (LightIntensity3 = 1.1). Light now actually disturbs enemies in the beam (the noise maker cast was failing silently); a sleeper that stays in the beam for LightWakeAfter (3 s) wakes up.
- DRED GARAGE FT1-T: turbine seated lower (TurbineSeat2).
- DRED GARAGE CF4: glob costs 2 bullets (FoamAmmoPerShot3) — about twice as many shots. The C-Foam Launcher's delivery part is mounted in the barrel cradle instead of the autocannon (FoamGunParts2, FoamGunOffset/Rotation/Scale).
- DRED GARAGE BT3: the bio-tracker's scanner part is mounted in the cradle instead of the barrel (TrackerParts, TrackerPartsOffset/Rotation/Scale).
- Gear parts dump (`GtfoPunk.GearParts.txt`) now also lists tool parts (main, grip, screen, delivery, payload, targeting) with their models.

## 0.4.3
- DRED GARAGE FT1-T: the mini turbine now sits in the sentry's barrel cradle (bottom of the model placed at the hidden barrel's root, option TurbineSeat) instead of lying at the tripod. The turbine model exists only on levels that have a mission fog turbine; elsewhere FT1-T shows the regular fog repeller model. In-memory model search narrowed to the turbine mesh name (TurbineModelSearch2 = defoggerbig) so it can't grab unrelated level geometry.

## 0.4.2
- DRED GARAGE CF4: bursts of 3 globs (FoamBurst, FoamBurstDelay), faster globs (30 m/s, FoamSpeed2), gravity 9 as measured (FoamGravity2), lead on moving targets (FoamLead). If the sentry's target is already glued, shoots the nearest unglued enemy in front instead. Glob cost 4 bullets (FoamAmmoPerShot2), pause between bursts 1.5 s (FoamInterval2). Fire sound. Two C-foam canisters on the barrel (FoamModel, FoamModelOffset, FoamModelRotation, FoamModelScale).
- DRED GARAGE BT3: beeps like a bio-tracker when enemies are in its sector (TrackerSound); the scan and screen counter now run for every player, tagging stays on the host.
- DRED GARAGE FT1: repeller model a bit higher and further back (FogModelOffset2).
- TEST turrets **LT2-A/B/C** to pick the lamp position: A — handle down, pushed back by a third of the lamp; B — handle up, pushed back; C — handle down, pushed forward. Options LampVariantA/B/C (roll degrees, shift in lamp lengths), LampVariant for the main LT2, LampTestTurrets to remove them.

## 0.4.1
- DRED GARAGE LT2: lamp moved lower and forward (option LampOffset2), the lens now glows (small light right in front of the glass; options LampGlow, LampGlowForward).
- DRED GARAGE FT1: repeller model taken from the deployed (standing) variant instead of the tilted pickup one; extra option FogModelRotation for fine-tuning.
- Fog-free radius of FT1 and FT1-T set to 10 m, as measured on the real repeller and turbine (options FogRange2, TurbineRange2).
- DRED GARAGE FT1-T: item 133 has no meshes in its loaded models, so the turbine model is now also searched among all meshes in memory (option TurbineModelSearch); the fog dump file lists what was found.

## 0.4.0
- New tool **DRED GARAGE BT3** — bio-tracker turret. Doesn't shoot; every 3 s scans its sector (90°, 25 m, through walls) and tags enemies like a handheld bio-tracker. Screen shows how many enemies are in the sector. Options TrackerTurret, TrackerRange, TrackerAngle, TrackerInterval, TrackerThroughWalls.
- New tool **DRED GARAGE CF4** — C-foam sentry. Lobs foam globs at its target instead of bullets (arcing shot, every 1.2 s, up to 20 m), skips enemies already glued, each glob costs 6 bullets of sentry ammo. Options FoamTurret, FoamInterval, FoamSpeed, FoamGravity, FoamAmmoPerShot, FoamExpand, FoamRange, FoamSkipGlued.
- Both are added after FT1-T, so existing turret IDs don't change. Everyone in the lobby needs the same version.

## 0.3.1
- Measures the real fog repeller / fog turbine parameters (radius, duration, grow/shrink time) 5 s after drop-in; writes them to the log and `BepInEx/config/GtfoPunk.FogRepellers.txt`.

## 0.3.0
- TEST turret **DRED GARAGE FT1-T**: miniature copy of the mission fog repeller turbine on the sentry, smaller fog-free sphere (5 m). Options FogTurbineTurret, TurbineScale, TurbineOffset, TurbineRange.

## 0.2.9
- DRED SILENZIO GP6-S: arm pose and left-hand grip taken from Mastaba R66 (option PoseFrom) — the reload hand was missing because Arid's grip expects a magazine. Test variant B removed.
- DRED GARAGE LT2: lamp turned another 90° (option LampRotation3).
- DRED GARAGE FT1: repeller model stands upright even on slopes.

## 0.2.8
- DRED GARAGE LT2: lamp anchored to the barrel root (not the muzzle point), turned lens-forward (option LampRotation2).
- Temporary test pistol "GP6-S TEST B" (Mastaba body + 3RB stock, Arid stats) to find where the reload hand goes missing.

## 0.2.7
- Silenced pistol renamed: **DRED SILENZIO GP6-S** (old config value is migrated, loadout slot kept).

## 0.2.6
- AM DRED GP1-S: hand animations from Mastaba R66 (the reload hand was missing).
- DRED GARAGE LT2: the lamp sits at the base of the (hidden) barrel instead of floating at the muzzle point.

## 0.2.5
- Silenced pistol: one gun left — **AM DRED GP1-S** = Bataldo 3RB body + Mastaba R66 top (Arid 5 damage, S49 sound). Option renamed to `SilencedGuns`.
- DRED GARAGE LT2: the flashlight model is taken from the variant that actually has meshes (the third-person one was empty).

## 0.2.4
- Silenced pistols reworked: damage from Shelling Arid 5, sound from Shelling S49; three looks: GP1-S (Nano body + Arid top), GP2-S (Bataldo 3RB + Arid top), GP3-S (3RB + Mastaba top). Option `Guns`. Fitting room removed.
- Turrets: wait for parts to assemble before setup, re-hide the gun barrel for a few seconds, force-enable the lamp/repeller model meshes; diagnostic log lines.

## 0.2.3
- GP1-S fitting room: removed Silencer_1 variants (the part has no model in the game files; it broke icon rendering for the rest). New full-kit variants M/N/O (Treffen, Steigro, Golok looks).
- Turrets: the autogun barrel is hidden (option HideGun).
- DRED GARAGE FT1: no scanning or rotation; fog repeller model on top; dim warm lamp (FogLamp).
- DRED GARAGE LT2: light source moved in front of the lamp model (no glare), lower default brightness.

## 0.2.2
- AM DRED GP1-S is now based on Shelling S49 (low-power pistol). "Fitting room": 12 look variants GP1-S A…L with different fronts and the unused Silencer_1 attachment (option PreviewVariants).

## 0.2.1
- DRED GARAGE LT2: long-range flashlight model on top, screen shows "LT2" instead of ammo (FT1 shows "FT1"), the beam now disturbs enemies (option LightDisturbs: Detectable / PulseOnly / Off).
- AM DRED GP1-S: parts are now set with one option `Parts` ("12=Accrat ND6, 21=8"); default is a plain Arid 5 copy.
- Garage: `GtfoPunk.GearList.txt` now shows part names; new `GtfoPunk.GearParts.txt` lists every part, category and item.

## 0.2.0
- New weapon **AM DRED GP1-S** (SilencedGun): silenced pistol. Wakes enemies within 10 m only, farther ones just stir.
- Turrets renamed: **DRED GARAGE FT1** (fog), **DRED GARAGE LT2** (light). Fixed: turrets were not added (game data error).
- QuickMelee: tap = shove, hold = charged attack; inputs are now injected into the game's input reader too.
- FireModes: burst only for weapons that have it natively (option BurstForAll).
- FullAmmoOnStart: starting ammo = max ammo in player data (packs can't fill above the pack cap). Bots included.
- "Garage": writes `BepInEx/config/GtfoPunk.GearList.txt` with all gear and parts, for building new guns.

## 0.1.0
- FullAmmoOnStart, FireModes, QuickMelee, TurretVariants (Fog Repeller Turret, Searchlight Turret).
