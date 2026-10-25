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
| TurretVariants | Everyone | New sentry tools: **DRED GARAGE TF I** (stationary fog repeller) and **TF II** (fog turbine), **TL1** (searchlight that tracks enemies, wakes sleepers it holds in the beam), **TB1** (bio-tracker: tags enemies in its sector, through walls; combat sentries prefer tagged targets), **TCF3** (foam sentry: slowing, acidic foam), **TGL3** (grenade launcher). TF/TL/TB don't shoot and run on a battery. Each sentry has its own config section `[TurretVariants.<Kind>]`. Every sentry gets a status light: green — full, yellow — half, red — not working. |
| Grenades | Everyone | The game's unreleased grenade (item 136) appears in level lockers/boxes, carried up to 2; a loud 8 m blast that wakes the area. The TGL3 sentry throws it. |
| Syringes | Everyone | Booster syringes lie where artifacts used to be; picking one up injects the effects of a random booster for 5 minutes (listed on the map screen). |
| Infection | Everyone | Infection keeps growing (1% / 2 min) until disinfected; from 70% you cough — sleepers nearby notice. |
| DoorAlarms | Everyone | Alarm doors ask for at most 2 scans (MaxAlarmClass; 0 — as in the game). The host changes it in the lobby rules panel. |
| PartyRules | Everyone | Lobby panel with the shared rules (door alarms, …): the host clicks to change, everyone gets the host's choice. |
| Loadout | Everyone | The lobby booster slots become a loadout: click to pick a resource pack, an extra item and a gadget (key N: thermal goggles, or a bio-tracker screen at the bottom of the view; both run on a rechargeable battery); the host hands them out at drop-in. Stock boosters are switched off. |
| SilencedGun | Everyone | Silenced pistol **DRED SILENZIO GP6-S**: Bataldo 3RB body + Mastaba top, Arid 5 damage, S49 sound. Wake enemies only within 10 m. |

---

## По-русски
Геймплейные изменения. Играть только с друзьями, у которых пакет тоже стоит.

| Фича | Кому нужна | Что делает |
|---|---|---|
| FullAmmoOnStart | Всем | Полный боезапас (и инструмент) на старте, ботам тоже. |
| FireModes | Каждому | B — режим огня: одиночный / авто (+ очередь, если она родная). |
| QuickMelee | Каждому | V — достать милишку, оттолкнуть, вернуть оружие. Держать — заряженный удар. |
| TurretVariants | Всем | Новые турели: DRED GARAGE TF I и TF II (туман), TL1 (прожектор, будит тех, кого держит в луче), TB1 (биотрекер — метит врагов в секторе, сквозь стены; боевые турели бьют помеченных первыми), TCF3 (пенострел — замедляющая кислотная пена), TGL3 (гранатомёт). У каждой турели свой раздел конфига `[TurretVariants.<Вид>]`. На всех турелях огонёк: зелёный — полная, жёлтый — половина, красный — не работает. |
| Grenades | Всем | Невыпущенная граната игры (предмет 136) — в шкафчиках/ящиках уровня, носится до 2 штук; громкий взрыв 8 м будит округу. Ею стреляет турель TGL3. |
| Syringes | Всем | Шприцы-усилители лежат на месте артефактов; подбор = укол, эффекты случайного бустера на 5 минут (видны на экране карты). |
| Infection | Всем | Инфекция растёт сама (1% за 2 мин), пока не продезинфицируешься; с 70% кашляешь — спящие рядом слышат. |
| DoorAlarms | Всем | Двери с тревогой просят не больше 2 сканов (MaxAlarmClass; 0 — как в игре). Хост меняет на панели правил в лобби. |
| PartyRules | Всем | Панель «правила партии» в лобби (тревога дверей, …): хост кликает, всем приходит его выбор. |
| Loadout | Всем | Слоты бустеров в лобби — снаряжение: кликом выбираешь пачку, доп. предмет и гаджет (клавиша N: тепловизор или экран биотрекера внизу; оба на перезаряжаемой батарее); хост выдаёт всё при высадке. Штатные бустеры отключены. |
| SilencedGun | Всем | Бесшумный пистолет DRED SILENZIO GP6-S (корпус 3RB + верх Mastaba, урон Arid 5, звук S49): будят только в 10 м. |
