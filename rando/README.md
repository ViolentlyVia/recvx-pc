# CVX Randomizer

A randomizer for **Resident Evil Code: Veronica X** on PC, for the
[recvx-pc](https://github.com/ViolentlyVia/recvx-pc) port. It shuffles items, enemies,
doors and music, and writes the result to a mod folder that `recvx.exe` loads instead of
the original room files. Your game data is never changed.

Work in progress. See [PLAN.md](PLAN.md) for the roadmap.

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
| `generate [--seed <n>] [--mode shuffle\|custom] [--claire-only] [--inventory] [--no-poison] [--doors] [--out <dir>] [--spoiler]` | Randomizes the items (`--inventory`: random starting items for Claire and Chris; `--no-poison`: Nosferatu does not poison; `--doors`: random doors, starting in the second room with its door to a save room); `--out` writes the mod folder (`RDX_LNK.AFS`, `rando.ini`, `rando_doors.txt`, `spoiler.txt`, `graph.dot`, `graph.dgml`, `settings.json`) for recvx-pc's `mod=` |
| `save-rooms [--data <dir>]` | Lists the rooms with an item box or a typewriter |
| `dump-script <RM_xxxx> [--from <hex>] [--to <hex>]` | Lists a room's script opcodes with their file offsets, item names and door targets |
| `verify-data [--all]` | Checks the built-in room data against the game's rooms (see `data/recv/CHANGES.md`) |
| `make-mod --out <dir> [--item <RM_xxxx>:<index>=<item id>]... [--ini <key=value>]...` | Builds a test mod folder for recvx-pc (`mod=` in `recvx.ini`) with hand-made item changes and `rando.ini` keys |

`--data` defaults to the `CVX_DATA` environment variable, then `..\recvx-pc\build_pc\data`.

## Credits

Ported in part from [BioRand](https://github.com/biorand/classic) by Ted John (IntelOrca),
with Code: Veronica information by kapdap. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
