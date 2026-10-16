# Room data: origin and changes

`rdt.json` and `enemy.json` come from BioRand (`IntelOrca.Biohazard.BioRand/data/recv/`,
tag `v3.3.2`, MIT; Code: Veronica information by kapdap). BioRand never released its
Code: Veronica support, so the data had not been checked in play. `cvx-rando-cli verify-data`
checks it against the room files; this file lists what that turned up.

How to read the data (worked out while checking it):
- Room keys are BioRand ids `SRRV` in hex with the stage one higher: `10A0` is `RM_0100.RDX`.
- `doors[].id` and `items[].id` are trigger-area (`etcp`) indexes. Room scripts change trigger
  areas at runtime with `set_etc_atari2` (opcode 0x25), so a door or item can be a script's
  version of the trigger area. A door can also be an examine area (type 3) whose event calls
  `set_door_call`.
- `doors[].entranceId` is where you arrive in **this** room when coming in through that door.
  BioRand writes it into whichever door it connects here. (It is not the trigger area's own
  entrance, which is in the other room.)
- `offsets` and `nop` are absolute offsets of script opcodes in the expanded room file.
- Item ids `252`-`255` are items that the script gives; they have no trigger area of their own.
- BioRand used "Handgun Bullets x15" (`type` 12, `amount` 15) as a placeholder for some items it
  had not identified.

## Fixes (2026-10-13)

The room file, or the script that hands the item over, decides what the original item is.

| Room | Entry | Change | Evidence |
|---|---|---|---|
| A050 (RM_9050) | item 13, global 42 | type 107 Alexia's Choker -> 43 Alexander's Pierce | The pickup's script sets the item to 43 (`setb` 0x19B192, `cmpb` 0x19B1AC). The Choker is placed in A200/A201, and door A200 #7 needs both. |
| A100 (RM_9160) | item 253, global 384 | type 12 -> 13 Magnum Bullets | `setb`/`ck_player_item` at its offsets hold 13 (placeholder) |
| A100 (RM_9160) | item 252, global 64 | type 12 -> 113 Socket | its offsets hold 113 (placeholder) |
| 2070 (RM_1070) | item 7, global 169 | type 22 Red Herb -> 21 Green Herb | trigger area 7 picks up item slot 2, a Green Herb |
| 7010 (RM_6010) | item 8, global 362 | type 12 -> 15 Grenade Rounds | slot 3 (placeholder) |
| 7060 (RM_6060) | item 13, global 276 | type 4 Shotgun -> 3 Sniper Rifle | slot 1 |
| 70A0 (RM_6100) | item 3, global 298 | type 12 -> 20 First Aid Spray | slot 3 (placeholder) |
| 3060 (RM_2060) | item 12, global 197 | type 15 -> 21 Green Herb | slot 1 |
| 3060 (RM_2060) | item 13, global 196 | added `offsets: [0x70A84]` | trigger area 13 is an examine area; the script gives the Grenade Rounds with `setb` at 0x70A84 |
| 3060 (RM_2060) | item 5, global 17 | removed | no such pickup: trigger area 5 is an examine area and nothing gives Grenade Rounds there; patching it would overwrite slot 2 (the Silver Dragonfly) |
| 2020 (RM_1020) | item global 159 | id 0 -> 17 | area 0 is stairs; area 17 picks up slot 0 (Handgun Bullets) |
| 4030 (RM_3030) | item global 203 | id 0 -> 3 | area 0 is a door/examine area; area 3 picks up slot 0 (Handgun Bullets) |
| 4120 (RM_3180) | doors 1 and 2 | ids swapped | area 1 leads to 4130 and area 2 to 4100; the entrances match the doors that lead back |
| 4100 (RM_3160) | door 1 | target 40A0 -> 4120 | area 1 leads to RM_3180, whose doors lead back to RM_3160 |
| 2001, 2002 | doorRando | `"type": "exclude"` -> `"category": "exclude"` | typo; BioRand reads `category`, so these rooms were never excluded |

## Fixes (2026-10-14, item randomizer)

| Room | Entry | Change | Evidence |
|---|---|---|---|
| 8060 (RM_7060) | new item 254, global 2001 | Shotgun, `offsets: [0x194370]`, `fixed` | the script gives the Shotgun (`setb` 0x194370) and checks it before two doors; the data required it but had no pickup of it |
| A1A0 (RM_9260) | new item 5, global 2002 | Shotgun, `fixed` | trigger area 5 picks up slot 1, a Shotgun; Chris's items here require it |
| 2000, 2001, 2002 | Green Herb, global 7 | global id -> 2003 | global 7 is also the Piano Roll in 10F0 |
| A080 | Serum (item 7), global 0 | global id -> 2004 | global 0 is also the Assault Rifle in the same room, which hid the Serum from the logic |

Global ids name a pickup, not an entry: the same id in two rooms is one pickup. That covers
room variants and also rooms Claire and Chris both visit (`7010`/`A010`, `4000`/`80C0`, ...),
where the game shares the picked-up flag, so whoever comes first takes it.

Why the Shotguns are `fixed`: RM_7060's give is part of a puzzle the data does not describe
(its script also checks the Shotgun as the used item and has a `player_item_lost_ex` for it),
so it is not moved until that is understood.

## Fixes (2026-10-14/15, door randomizer playtests)

| Room | Entry | Change | Evidence |
|---|---|---|---|
| 2090 (RM_1090) | door 1 (to 20A0) | `requires: [33]` | the bookcase passage opens from the office with the Gold Lugers; from the corridor it is a wall |
| 3040 (RM_2040), 3050 (RM_2050) | the doors between them | `doorRando: false` | on a fresh game RM_2050's door plays a scene that sends the player to RM_2030, and RM_2040's does nothing |
| 5011 (RM_4011) | door 1 (the plane) | `randomize: false` | arriving through its pair puts the player on the gangway, which only the bridge connects to the hangar |
| 10B0 (RM_0110) | `doorRando` nop | `0x208F98-0x208FD6` -> `0x208F98-0x208FD4` | the range ended on the `else`'s body, the only line that makes area 11 the door to RM_0120; it now removes the `if`/`else` and keeps the door |
| 3040 (RM_2040) | door 2 (the attic ladder, to 3060) | `lock: "unblock"` | coming down the ladder leaves the player on the bed canopy until the King Ant / Music Box Plate puzzle on the floor lowers it; kept, and closed from the attic for the logic |
| 5050 (RM_4050), 20E0 (RM_1140) | `doorRando` | `category: exclude` | the two files are one lift: area 3 rides it, and area 0's exit leads to RM_4030 or RM_1130 by the level the player is at, not by the file; a random door through it came back somewhere else |
| 6000 (RM_5000) | door 0 (to 5010) | `doorRando: false` | with random doors the plane starts the flight to Antarctica when it loads; only the plane door may lead in |
| 20C0 (RM_1120) | `doorRando` nop | `0x1580B4-0x1580D8` removed | it is the stairs' climb event (procedure 2 calls the door to RM_1000); without it the stairs cannot be climbed |

Nop ranges include the opcode at their end (they end on the `endif` or `else` that closes what
they remove; CV writes `if ... endif else <body>`, the `else` jumping over its body).
`verify-data` now reports a door mode nop that removes every door setup of a trigger area the
room file does not make a door. RM_1100's door to RM_2000 needs map flag 0x46, which the
cutscene its nop removes would set: `RoomTweaks` sets it when the room starts with random doors.

## Open (check in the game before relying on them, Phase 6)

`verify-data` still reports these; the door randomizer must not trust them yet.

- **Door targets that disagree with the room file:**
  - 80F0 #0 (data: 8150; the room says 80E0, and RM_7140 leads back);
  - A0F0 #1 (data: A1A0; the room says A100);
  - A1A0 #0 (data: A0F0; the room and its script say A200);
  - A1B0 #1 (data: A150; the script says A160).
- **No door to patch:**
  - A0F0 #0 is a stairs area, not a door;
  - 8180 (RM_7240) has no trigger areas at all (both doors already have `randomize: false`).
- **Script door calls that go elsewhere:** 20E0 and 5050 point at each other's `set_door_call`s; 8010's offsets point at calls to stage 3 rooms; 8150 #16; 9010 #3.
- **Event doors with no matching `set_door_call` in the room:** 4080 #5, 4090 #0, 4090 #2, 6000 #2.
- **Arrival entrance matches no door leading back:** 6000 #2, 8010 #2, 80D0 #2, 8170 #1, A0A0 #0, A0F0 #0, A100 #3.

## Rooms the data does not cover

40 of the 205 rooms are not in `rdt.json`:
- RM_5500-RM_5840 are the Battle Game rooms (game mode 3 starts in stage 5 room 50).
- RM_9340-RM_9370, RM_9091 and RM_8030 are cutscene or ending rooms with no pickups or doors.
- Room variants: some variants of a mapped room are missing (RM_1121, RM_1122, RM_3010, RM_4012, RM_7030, RM_9301, RM_9302).
  - RM_3010's Handgun Bullets are the same pickup as RM_3011's.
  - **The item randomizer must give an unmapped variant's pickup the same new item as the matching pickup (same item at the same position) in the mapped variant.**
