# GtfoPunk My Game My Rules

Gameplay overhaul for GTFO: party difficulty, a lobby loadout with gadgets, new sentries and a silenced pistol,
a frag grenade, booster syringes, living infection, killable scouts.

- **Everyone in the lobby needs it.** It changes the game — play with friends who have it too, not with randoms.
- Made to be played together with **GtfoPunk QoL** (client-side quality of life, separate package).
- Every feature toggles in `BepInEx/config/AmDDRed.GtfoPunk.Rules.cfg`; shared rules are picked by the host in the lobby.
- Text in English or Russian (`[General] TextLanguage = auto` follows the game's language). GTFO-API not needed.

Full guide (every feature and setting): [English](https://github.com/DmitryPrigodich/gtfo-punk-rules-mod/blob/main/docs/GUIDE.md) · [Русский](https://github.com/DmitryPrigodich/gtfo-punk-rules-mod/blob/main/docs/GUIDE.ru.md)

## Key features
| | |
|---|---|
| **Difficulty I–VI** | DIFFICULTY button in the lobby (host). Alarm doors lose scans (I: −5 — every alarm is a plain scan, … VI: as in the game); starting ammo, packs and items 100% … 0%. DEFAULT — the game's own rules. |
| **Loadout** | The booster slots become PACK / ITEM / GADGET; the host hands them out at drop-in. Stock boosters are off. |
| **Gadgets** (key N) | Bio-tracker screen · thermal goggles (Z zoom, X palettes) · hacking tool (only its owner hacks) · soft boots (quiet running, harder falls). |
| **Frag grenade** | A C-Foam Grenade that explodes: 8 m, 120 damage, loud. In lockers and boxes, carry 2. |
| **Sentries** | DRED GARAGE TL1 searchlight · TF I / TF II fog repellers · TB1 bio-tracker · TCF3 foam · TGL3 grenade launcher. Status light on every sentry. |
| **Silenced pistol** | DRED GLUKHAR S6: 15 damage, wakes only within 10 m. |
| **Booster syringes** | Instead of artifacts: inject on pickup, a random booster's effects for 5 minutes. |
| **Infection** | Grows until disinfected; gasping from 50%, coughing and tremor from 70% — sleepers hear you. |
| **Scouts** | ×3 damage from the moment a scout catches you until it screams — kill it and there's no wave. |
| **Fire modes / quick melee** | B — semi / auto / burst; V — shove or charged hit, then back to the gun. |

---

## По-русски

Переработка геймплея GTFO: сложность для всей группы, снаряжение с гаджетами в лобби, новые турели и бесшумный
пистолет, граната, шприцы-усилители, живая инфекция, скаутов можно успеть убить.

- **Нужен всем в лобби.** Меняет игру — играть с друзьями, у которых он тоже стоит, не с рандомами.
- Рассчитан на игру вместе с **GtfoPunk QoL** (отдельный пакет удобств).
- Каждая фича включается в `BepInEx/config/AmDDRed.GtfoPunk.Rules.cfg`; общие правила хост выбирает в лобби.

Подробное руководство: [по-русски](https://github.com/DmitryPrigodich/gtfo-punk-rules-mod/blob/main/docs/GUIDE.ru.md).

| | |
|---|---|
| **Сложность I–VI** | Кнопка СЛОЖНОСТЬ в лобби (хост). Тревоги теряют сканы (I: −5 — любая тревога становится обычным сканом, … VI: как в игре); патроны, пачки и предметы 100% … 0%. ПО УМОЛЧАНИЮ — правила игры. |
| **Снаряжение** | Слоты бустеров — ПАЧКА / ПРЕДМЕТ / ГАДЖЕТ; хост выдаёт при высадке. Штатные бустеры выключены. |
| **Гаджеты** (клавиша N) | Экран биотрекера · тепловизор (Z — зум, X — палитры) · инструмент взлома (взламывает только владелец) · мягкие ботинки (тихий бег, но больнее падать). |
| **Граната** | Пеногрената, которая взрывается: 8 м, 120 урона, громко. В шкафчиках и ящиках, носится 2. |
| **Турели** | DRED GARAGE TL1 прожектор · TF I / TF II от тумана · TB1 биотрекер · TCF3 пена · TGL3 гранатомёт. Огонёк состояния на всех турелях. |
| **Бесшумный пистолет** | DRED GLUKHAR S6: урон 15, будит только в 10 м. |
| **Шприцы-усилители** | Вместо артефактов: укол при подборе, эффекты случайного бустера на 5 минут. |
| **Инфекция** | Растёт, пока не продезинфицируешься; с 50% одышка, с 70% кашель и дрожь — спящие слышат. |
| **Скауты** | ×3 урона с момента, как скаут поймал, до крика — успел убить, волны нет. |
| **Режимы огня / быстрый удар** | B — одиночный / авто / очередь; V — толчок или заряженный удар, потом снова оружие. |
