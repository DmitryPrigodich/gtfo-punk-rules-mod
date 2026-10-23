# GtfoPunk My Game My Rules

Gameplay changes for GTFO. **This changes the game:** play only with friends, and **everyone in the lobby should have it installed** (some features work if only the host has them, see the table).

- Does not require GTFO-API.
- Every feature has its own toggle in `BepInEx/config/AmDDRed.GtfoPunk.Rules.cfg`.
- In-game text language: `[General] Language = en` (or `ru`).
- Client-side QoL lives in a separate package: **GtfoPunk QoL**.

## Features
| Feature | Who needs it | What it does |
|---|---|---|
| FullAmmoOnStart | Everyone | Everyone (bots too) starts the level with full ammo and tool. |
| FireModes | Each player | B switches fire mode: semi / auto (+ burst on weapons that have it). Turns itself off if ExtraWeaponCustomization is installed. |
| QuickMelee | Each player | V: draw melee, shove, return to your gun. Hold V for a charged hit. |
| TurretVariants | Everyone | New sentry tools: **DRED GARAGE FT1** (stationary fog repeller, infinite), **LT2** (spotlight that tracks enemies), **BT3** (bio-tracker turret: tags enemies in its sector every few seconds, through walls), **CF4** (C-foam sentry: lobs foam globs instead of bullets, uses sentry ammo), **GLF-3** (grenade launcher sentry). FT1/LT2/BT3 don't shoot. |
| Grenades | Everyone | The game's unreleased grenade (item 136) appears in level lockers/boxes; the GLF-3 sentry throws it. |
| Syringes | Everyone | Booster syringes lie where artifacts used to be; injecting one gives the effects of a random booster for 5 minutes. |
| Loadout | Everyone | The lobby booster slots become a loadout: click to pick a resource pack, an extra item and a gadget (thermal goggles or bio-tracker, key N); the host hands them out at drop-in. Stock boosters are switched off. |
| SilencedGun | Everyone | Silenced pistol **DRED SILENZIO GP6-S**: Bataldo 3RB body + Mastaba top, Arid 5 damage, S49 sound. Wake enemies only within 10 m. |

---

## По-русски
Геймплейные изменения. Играть только с друзьями, у которых пакет тоже стоит.

| Фича | Кому нужна | Что делает |
|---|---|---|
| FullAmmoOnStart | Всем | Полный боезапас (и инструмент) на старте, ботам тоже. |
| FireModes | Каждому | B — режим огня: одиночный / авто (+ очередь, если она родная). |
| QuickMelee | Каждому | V — достать милишку, оттолкнуть, вернуть оружие. Держать — заряженный удар. |
| TurretVariants | Всем | Новые турели: DRED GARAGE FT1 (туман), LT2 (прожектор), BT3 (биотрекер — метит врагов в секторе, сквозь стены), CF4 (пенострел — клеит врагов пеной, тратит патроны турели), GLF-3 (гранатомёт). |
| Grenades | Всем | Невыпущенная граната игры (предмет 136) — в шкафчиках/ящиках уровня; ею стреляет турель GLF-3. |
| Syringes | Всем | Шприцы-усилители лежат на месте артефактов; укол даёт эффекты случайного бустера на 5 минут. |
| Loadout | Всем | Слоты бустеров в лобби — снаряжение: кликом выбираешь пачку, доп. предмет и гаджет (тепловизор или биотрекер, клавиша N); хост выдаёт всё при высадке. Штатные бустеры отключены. |
| SilencedGun | Всем | Бесшумный пистолет DRED SILENZIO GP6-S (корпус 3RB + верх Mastaba, урон Arid 5, звук S49): будят только в 10 м. |
