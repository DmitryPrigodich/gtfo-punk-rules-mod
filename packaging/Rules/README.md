# GtfoPunk My Game My Rules

Gameplay changes for GTFO. **This changes the game:** play only with friends, and **everyone in the lobby should have it installed** (some features work if only the host has them, see the table).

- Does not require GTFO-API.
- Every feature has its own toggle in `BepInEx/config/AmDDRed.GtfoPunk.Rules.cfg`.
- Lobby loadout icons are PNG files in the mod's `icons` folder — replace one to change it.
- In-game text language: `[General] TextLanguage = auto` — as the game's language (or `en` / `ru`).
- Meant to be played together with **GtfoPunk QoL** (client-side quality of life, separate package).

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
| Difficulty | Everyone | DIFFICULTY button in the lobby (host): I–VI set alarm doors (I — one plain scan without a wave, II–VI — 1–5 scans; scripted doors stay as in the game), starting ammo, pack fill and item count (100% … 0%); DEFAULT — the game's rules, no loadout or gadgets. What the level means is written next to the button; right click — back. |
| DoorAlarms | Everyone | Alarm doors by difficulty (level I — they just open). |
| ScoutKill | Everyone | From the moment a scout catches you until it screams it takes ×3 damage (and the charge-up is longer) — kill it and there's no wave. |
| PartyRules | Everyone | The lobby buttons for the shared rules: the host clicks, everyone gets the host's choice. |
| Loadout | Everyone | The lobby booster slots become a loadout: click to pick a resource pack, an extra item and a gadget (key N: thermal goggles, a bio-tracker screen or the hacking tool — only its owner can hack; goggles and screen run on a rechargeable battery); the host hands them out at drop-in. Stock boosters are switched off. |
| SilencedGun | Everyone | Silenced pistol **DRED SILENZIO GP6-S**: Bataldo 3RB body + Mastaba top, 15 damage (headshot kills a striker), S49 sound. Wakes enemies only within 10 m. Shown as "Silenced Pistol". |

---

## По-русски
Геймплейные изменения. Играть только с друзьями, у которых пакет тоже стоит. Рассчитан на игру вместе с **GtfoPunk QoL**.

| Фича | Кому нужна | Что делает |
|---|---|---|
| FullAmmoOnStart | Всем | Полный боезапас (и инструмент) на старте, ботам тоже. |
| FireModes | Каждому | B — режим огня: одиночный / авто (+ очередь, если она родная). |
| QuickMelee | Каждому | V — достать милишку, оттолкнуть, вернуть оружие. Держать — заряженный удар. |
| TurretVariants | Всем | Новые турели: DRED GARAGE TF I и TF II (туман), TL1 (прожектор, будит тех, кого держит в луче), TB1 (биотрекер — метит врагов в секторе, сквозь стены; боевые турели бьют помеченных первыми), TCF3 (пенострел — замедляющая кислотная пена), TGL3 (гранатомёт). У каждой турели свой раздел конфига `[TurretVariants.<Вид>]`. На всех турелях огонёк: зелёный — полная, жёлтый — половина, красный — не работает. |
| Grenades | Всем | Невыпущенная граната игры (предмет 136) — в шкафчиках/ящиках уровня, носится до 2 штук; громкий взрыв 8 м будит округу. Ею стреляет турель TGL3. |
| Syringes | Всем | Шприцы-усилители лежат на месте артефактов; подбор = укол, эффекты случайного бустера на 5 минут (видны на экране карты). |
| Infection | Всем | Инфекция растёт сама (1% за 2 мин), пока не продезинфицируешься; с 70% кашляешь — спящие рядом слышат. |
| Difficulty | Всем | Кнопка СЛОЖНОСТЬ в лобби (хост): I–VI задают тревогу дверей (I — один обычный скан без волны, II–VI — 1–5 сканов; скриптовые двери как в игре), стартовые патроны, заполнение пачки и количество предметов (100% … 0%); ПО УМОЛЧАНИЮ — правила игры, без снаряжения и гаджетов. Что значит уровень — написано рядом с кнопкой; ПКМ — назад. |
| DoorAlarms | Всем | Тревога дверей по сложности (уровень I — двери просто открываются). |
| ScoutKill | Всем | С момента, как скаут тебя поймал, и до крика он получает ×3 урона (и крик дольше заряжается) — успел убить, волны нет. |
| PartyRules | Всем | Кнопки общих правил в лобби: хост кликает, всем приходит его выбор. |
| Loadout | Всем | Слоты бустеров в лобби — снаряжение: кликом выбираешь пачку, доп. предмет и гаджет (клавиша N: тепловизор, экран биотрекера или инструмент взлома — взламывать может только его владелец; тепловизор и экран на батарее); хост выдаёт всё при высадке. Штатные бустеры отключены. |
| SilencedGun | Всем | Бесшумный пистолет DRED SILENZIO GP6-S (корпус 3RB + верх Mastaba, урон 15 — в голову убивает страйкера, звук S49): будит только в 10 м. |
