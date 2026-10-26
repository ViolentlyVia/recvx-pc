# Biorand++ (CVX Randomizer)

**Biorand++** is [BioRand](https://github.com/biorand/classic), the classic Resident Evil
randomizer, with a fourth game: **Resident Evil Code: Veronica X** on PC, for the
[recvx-pc](https://github.com/ViolentlyVia/recvx-pc) port. The window is BioRand's;
Resident Evil 1, 2 and 3 are randomized by BioRand's own engine, as in BioRand; CV:X is
randomized by this project's randomizer, which writes a mod folder that `recvx.exe` loads
instead of the original files. Your game data is never changed.

Work in progress. See [PLAN.md](PLAN.md) for the roadmap.

## Using it

Put `cvx-rando.exe` next to `recvx.exe` (the recvx-pc share zip does) and start it. The first
time, it takes your settings from BioRand (its RE 1-3 folders) and from the CVX Randomizer's
earlier window (`cvx-rando.json`: the game and BioRand folders and your CV:X options). The
settings page (the cog) has:

- **Resident Evil 1, 2, 3**: the games' folders, as in BioRand;
- **Resident Evil CV:X (recvx-pc)**: choose `recvx.exe` in its folder;
- **BioRand (data and datapacks)**: BioRand's folder. RE 1-3 need its data; CV:X uses its
  music and characters. A `biorand` folder next to or above the program is found by itself.

**RE 1-3** work as in BioRand: pick the options, **Generate**, then **Start RE** (the game's
mod selection has the randomizer).

**CV:X**: the seed box holds a share code (`CVX1-...`: the seed and every option; **Paste**
one to play someone else's seed). **Generate** writes the seed to `rando\<seed>` in the game
folder and makes it active:

- `recvx.ini` gets `mod=rando\<seed>` and `saves=saves_rando`: random runs share one save
  folder, which a new seed empties (the app asks first when it holds saves, and the game must
  be closed). Your own `saves=` is kept as `saves_normal=` (`*` when you had none) and never
  touched;
- **Start CV:X** runs the game; **Play normal game** (here or in `recvx-config.exe`) removes
  `mod=` and gives you your own saves back; **Spoiler Log** opens `spoiler.txt`.

CV:X's options sit in BioRand's boxes, with its own additions:

- **Randomize Doors**: one character (Claire or Chris) plays the whole run, in 1-4 segments
  ending at one-way story doors, using more or fewer of the rooms (Segment Size);
- **Randomize Items**: the game's own items shuffled, or a **custom item pool** (the
  probabilities, the ammo amount, the weapons allowed); **Include Chris's half**; **Random
  inventory** with **Side pack from the start**. Rooms that change once you have a key item
  (the courtyard's gate, the King Ant Object's rooms, the card readers...) change when you
  get that item wherever it is now (the spoiler's "Story checks");
- **Randomize Enemies**: each room's ordinary enemies become another kind (in their own
  places). As in BioRand, each kind's slider sets how often it comes (None = never) and
  Difficulty how many of one kind a room may hold; all sliders at the middle keep the
  difficulty's own mix. Moth, Spotter, Albinoid, Anatomist and Tyrant are not yet tested in
  the game and start at None. Bats count too (what takes their place starts on the floor below
  them); only bats an event moves stay. **No Nosferatu poison**.
  **Random enemy placements**, as in BioRand: of the rooms that can take them (not boss
  rooms, rooms with enemy events, or save rooms unless **Allow enemies in save rooms**),
  **Rooms with enemies** sets the share that have enemies, from 1 to twice the **average
  number** less one each, in random places on the room's floor; the others have none. The
  places are worked out from each room's walls and floor (`enemy-spots` below draws them),
  away from the entrances and doors. Killed enemies stay dead, as the game's own do.
- **Randomize Music**: tracks swap with others of their kind, from Code: Veronica and the
  other games BioRand has music of (converted to ADX and matched to the game's loudness; the
  seed's folder then has its own `BGM1.AFS`/`BGM2.AFS`, about 250 and 200 MB);
- **Randomize NPCs**: Steve, Rodrigo, Alfred, Alexia and Wesker take each other's looks and
  voices, or the looks and voices of the BioRand characters ticked in the list (right-click:
  RE2 cast, All, None; each line becomes one of that character's clips of about the same
  length, as in BioRand, the subtitles keeping the game's words); Claire and Chris stay themselves. The seed's folder then has its own
  `VOICE1.AFS`/`VOICE2.AFS` (about 70 MB);
- **Change Player Character** (BioRand's box): Claire and Chris become BioRand characters,
  each picked in its drop-down or Random (never both the same): their gameplay model
  (`SYSTEM.AFS` in the seed's folder; needs recvx.exe 0.9.94 or newer), their
  cutscene models, the lines of theirs the randomizer knows (151) and their pain and death
  sounds (BioRand's `data/hurt`) take that character's look and voice;
- in the settings, **Randomize title voice** plays one of BioRand's title calls at the title
  screen, and **Door Skip (RE CVX)** turns on recvx-pc's door skip.

There is one CV:X seed at a time: generating deletes the previous seed's folder and the random
run's saves once the new one is written. The app remembers your choices in
`%AppData%\biorandpp\settings.json` (BioRand's own settings are left alone).

## Projects

| Project | What it is |
|---|---|
| `src/Biorand.App` | Biorand++, `cvx-rando.exe`: BioRand's window (WPF), with the CV:X tile in `MainWindow.Cvx.cs` |
| `external/biorand` | BioRand's source (git submodule, v3.3.2): its engine for RE 1-3 |
| `src/Cvx.Formats` | Game file formats: AFS archives, PRS compression, room files (`RM_xxxx.RDX`), models and textures |
| `src/Cvx.Rando` | The CV:X randomizer: room data, item logic and placement, room patching, the mod folder |
| `src/Cvx.Rando.Cli` | Command-line tool, `cvx-rando-cli.exe`, for inspecting rooms and scripted runs |
| `data/` | Built-in data: `items.json` and the room data in `recv/` (from BioRand, corrected; see `recv/CHANGES.md`) |
| `tests/Cvx.Tests` | Tests; the ones that need game data use `CVX_DATA` or the port's `build_pc\data` (at the repository root) and are skipped when it is missing |

## Building

Needs the .NET 10 SDK, and BioRand's source:

```bash
git submodule update --init --recursive
```
```bash
dotnet build
```
```bash
dotnet test
```

BioRand's data is not part of the build: the program reads it from BioRand's folder.

## Publishing

```bash
powershell -File tools/publish.ps1
```

builds `cvx-rando.exe` as one self-contained file (about 63 MB; players install nothing) in
`artifacts\publish`, with `cvx-rando-notices.txt` (the licences, which travel with the exe), and
copies both next to `recvx.exe` in the port's `build_pc`. `pc/tools/make_share.py`
then puts them in the share zip. `-Target <folder>` copies elsewhere, `-NoCopy` only builds.

Testing aids: `BIORANDPP_SETTINGS=<folder>` keeps the settings elsewhere;
`BIORANDPP_SHOTS=<folder>` saves a picture of the window with each tile selected, checks that
the CV:X tile gives back the settings it shows (`cvx_roundtrip.txt`), and closes.

## Command-line tool

```bash
dotnet run --project src/Cvx.Rando.Cli -- <command>
```

| Command | What it does |
|---|---|
| `list-rooms [--data <dir>]` | Lists every room in `RDX_LNK.AFS` with its compressed and expanded size |
| `dump-room <RM_xxxx> [--data <dir>] [--json]` | Shows a room's enemies, items, doors and other trigger areas |
| `dump-all --out <file.json> [--data <dir>]` | Writes the summary of every room to one JSON file |
| `extract-room <RM_xxxx> --out <file> [--data <dir>]` | Writes a room's expanded file |
| `extract-afs <archive> <entry> --out <file>` | Writes one entry of an AFS archive as it is |
| `roundtrip [--data <dir>] [--out <dir>]` | Rebuilds every room and the archive without changes and checks the result |
| `generate [--seed <n>] [--mode shuffle\|custom] [--claire-only] [--inventory] [--side-pack] [--no-poison] [--doors] [--segments 1-4] [--segment-size 1-8] [--chris] [--enemies] [--enemy-difficulty 0-3] [--enemy-ratios Zombie=4,Hunter=7,...] [--enemy-placements] [--enemy-quantity 0-15] [--enemy-rooms 0-7] [--enemies-in-save-rooms] [--music] [--music-games re2,sh1,...] [--title-call] [--npcs] [--npc-characters leon.re2,...] [--biorand <dir>] [--share <code>] [--out <dir>] [--spoiler]` | Randomizes CV:X; `--out` writes the mod folder (`RDX_LNK.AFS` and the other changed archives, `rando.ini`, `rando_doors.txt`, `spoiler.txt`, `graph.dot`, `graph.dgml`) for recvx-pc's `mod=` |
| `save-rooms [--data <dir>]` | Lists the rooms with an item box or a typewriter |
| `enemy-spots [<RM_xxxx>] [--png <dir>]` | Where random enemy placements may start enemies in each room; `--png` draws each floor from above (walls, the reachable floor, the places, entrances, known enemy places) |
| `dump-enemies [<RM_xxxx>] [--scripts] [--out <dir>]` | Each room's enemies with the model, motion and texture data the game loads for them; `--out` writes them as files |
| `dump-model <RM_xxxx>:<enemy> \| <SYSTEM.AFS>:<entry> \| <file> [--vertices] [--png <dir>]` | A model page: its skeleton, meshes, SKIN weights and textures |
| `dump-pld <file.PLD> [--vertices] [--png <dir>]` | An RE2 player model (BioRand's characters) |
| `render-model <RM_xxxx>:<enemy> \| <file> --out <png> [--arms-down]` | A model's bind pose (or with the arms down) from the front, side and back |
| `make-look-mod --pld <PLD> [--slot <RM_xxxx>:<enemy>] [--out <dir>]` | Testing aid: an NPC entry takes a converted BioRand character |
| `enemy-donors` | For each enemy type and model version, the rooms it can be copied from (for `data/enemies.json`) |
| `swap-enemies <RM_xxxx> --donor <RM_xxxx>:<index> [--only <types>] [--effect <n>] [--out <dir>]` | Testing aid: turns a room's enemies into another room's enemy and writes a mod folder |
| `dump-sounds <RM_xxxx>[,...]` | A room's sound pack banks (MULTSPQ1/2.AFS) |
| `dump-music` | Every music track: discs, length, whether it loops, and the rooms that ask for it (for `data/music.json`) |
| `room-memory [--recompress] [--all]` | The rooms with the least room to grow in the game's room memory |
| `dump-script <RM_xxxx> [--from <hex>] [--to <hex>]` | Lists a room's script opcodes with their file offsets, item names and door targets |
| `verify-data [--all]` | Checks the built-in room data against the game's rooms (see `data/recv/CHANGES.md`) |
| `make-mod --out <dir> [--item <RM_xxxx>:<index>=<item id>]... [--ini <key=value>]...` | Builds a test mod folder for recvx-pc (`mod=` in `recvx.ini`) with hand-made item changes and `rando.ini` keys |

`--data` defaults to the `CVX_DATA` environment variable, then the port's `build_pc\data` at the repository root.

## Credits

[BioRand](https://github.com/biorand/classic) by Ted John (IntelOrca) and its contributors:
Biorand++'s window and its RE 1-3 randomizer, and parts of the CV:X randomizer; Code: Veronica
information by kapdap. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
