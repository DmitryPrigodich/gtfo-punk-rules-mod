# Changelog

## 0.9.4
- Party rules panel didn't show up (the lobby camera isn't in the camera list) — fixed; the same camera search is used for the HUD.
- **Gadget line** in the HUD font, sized to 2% of the screen height whatever the HUD scale (GadgetHudHeight), centered above the health readout; ON/OFF and the battery bar. The bio-tracker screen stands on it.
- **Thermal goggles** didn't switch on if no thermal-sight weapon was loaded in that level: the lens material is now kept from the lobby (and searched again if missing).
- **Booster syringe**: picking it up threw an error and fell back to the normal pickup — fixed. Map screen: effects listed on the left above the legend — "BOOSTER", then one line per effect with a readable name, the total change and time left; several syringes add up, opposite effects cancel out, each keeps its own time. Effect names in English or Russian.
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
