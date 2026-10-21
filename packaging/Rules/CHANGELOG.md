# Changelog

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
