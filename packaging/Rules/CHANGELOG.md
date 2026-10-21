# Changelog

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
