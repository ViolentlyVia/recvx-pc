# CVX Randomizer

A randomizer for **Resident Evil Code: Veronica X** on PC, for the
[recvx-pc](https://github.com/ViolentlyVia/recvx-pc) port. It shuffles items, enemies,
doors and music, and writes the result to a mod folder that `recvx.exe` loads instead of
the original room files. Your game data is never changed.

Work in progress. See [PLAN.md](PLAN.md) for the roadmap.

## Using it

Put `cvx-rando.exe` next to `recvx.exe` (the recvx-pc share zip does), or start it and choose
the game folder. Pick the options, then **Generate** (or **Generate and play**):

- the seed is written to `rando\<seed>` in the game folder;
- `recvx.ini` gets `mod=rando\<seed>` and `saves=saves_rando\<seed>`, so the seed has its own
  saves; your own `saves=` is kept as `saves_normal=` (`*` when you had none);
- **Play normal game** (here or in `recvx-config.exe`) removes `mod=` and gives you your own
  saves back.

The share code (`CVX1-...`) holds the seed and every option: paste one to play someone else's
seed. **Spoiler log** opens `spoiler.txt` (where everything is). **Delete old seeds** removes
seed folders other than the one being played; their saves are kept. The app remembers your
choices in `cvx-rando.json` next to it.

## Projects

| Project | What it is |
|---|---|
| `src/Cvx.Formats` | Game file formats: AFS archives, PRS compression, room files (`RM_xxxx.RDX`) |
| `src/Cvx.Rando` | The randomizer: room data, item logic and placement, room patching, the mod folder |
| `src/Cvx.Rando.App` | The Windows app, `cvx-rando.exe` |
| `src/Cvx.Rando.Cli` | Command-line tool, `cvx-rando-cli.exe`, for inspecting rooms and scripted runs |
| `data/` | Built-in data: `items.json` and the room data in `recv/` (from BioRand, corrected; see `recv/CHANGES.md`) |
| `tests/Cvx.Tests` | Tests; the ones that need game data use `CVX_DATA` or `..\recvx-pc\build_pc\data` and are skipped when it is missing |

## Building

Needs the .NET 10 SDK.

```bash
dotnet build
dotnet test
```

## Publishing

```bash
powershell -File tools/publish.ps1
```

builds `cvx-rando.exe` as one self-contained file (about 47 MB; players install nothing) in
`artifacts\publish`, with `cvx-rando-notices.txt` (the licences, which travel with the exe), and
copies both next to `recvx.exe` in `..\recvx-pc\build_pc`. recvx-pc's `pc/tools/make_share.py`
then puts them in the share zip. `-Target <folder>` copies elsewhere, `-NoCopy` only builds.

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
| `roundtrip [--data <dir>] [--out <dir>]` | Rebuilds every room and the archive without changes and checks the result |
| `generate [--seed <n>] [--mode shuffle\|custom] [--claire-only] [--inventory] [--side-pack] [--no-poison] [--doors] [--enemies] [--enemy-difficulty 0-3] [--music] [--out <dir>] [--spoiler]` | Randomizes the items (`--inventory`: random starting items for Claire and Chris; `--side-pack`: both start with the side pack's extra item slots; `--no-poison`: Nosferatu does not poison; `--doors`: random doors, starting in the second room with its door to a save room; `--enemies`: other enemy types in each room, by difficulty 0-3; `--music`: music shuffled within its category); `--out` writes the mod folder (`RDX_LNK.AFS`, `MULTSPQ1/2.AFS` for enemy sounds, `rando.ini`, `rando_doors.txt`, `spoiler.txt`, `graph.dot`, `graph.dgml`, `settings.json`) for recvx-pc's `mod=` |
| `save-rooms [--data <dir>]` | Lists the rooms with an item box or a typewriter |
| `dump-enemies [<RM_xxxx>] [--scripts]` | Each room's enemies with the model, motion and texture data the game loads for them; `--scripts` adds the script opcodes that act on enemies |
| `enemy-donors` | For each enemy type and model version, the rooms it can be copied from (for `data/enemies.json`) |
| `swap-enemies <RM_xxxx> --donor <RM_xxxx>:<index> [--only <types>] [--effect <n>] [--out <dir>]` | Testing aid: turns a room's enemies into another room's enemy and writes a mod folder |
| `dump-sounds <RM_xxxx>[,...]` | A room's sound pack banks (MULTSPQ1/2.AFS) |
| `dump-music` | Every music track: discs, length, whether it loops, and the rooms that ask for it (for `data/music.json`) |
| `room-memory [--recompress] [--all]` | The rooms with the least room to grow in the game's room memory |
| `dump-script <RM_xxxx> [--from <hex>] [--to <hex>]` | Lists a room's script opcodes with their file offsets, item names and door targets |
| `verify-data [--all]` | Checks the built-in room data against the game's rooms (see `data/recv/CHANGES.md`) |
| `make-mod --out <dir> [--item <RM_xxxx>:<index>=<item id>]... [--ini <key=value>]...` | Builds a test mod folder for recvx-pc (`mod=` in `recvx.ini`) with hand-made item changes and `rando.ini` keys |

`--data` defaults to the `CVX_DATA` environment variable, then `..\recvx-pc\build_pc\data`.

## Credits

Ported in part from [BioRand](https://github.com/biorand/classic) by Ted John (IntelOrca),
with Code: Veronica information by kapdap. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
