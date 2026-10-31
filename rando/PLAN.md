# CVX Randomizer — implementation plan

## Context

The owner has a native PC port of **Resident Evil Code: Veronica X**: `recvx-pc` (sibling repo `..\recvx-pc`, branch `pc-port`, built from the recvx-decomp decompilation, US PS2 disc SLUS-20184). It ships with a C/Win32 settings app, `recvx-config.exe`. They want a second companion app: a **randomizer** like BioRand (items, enemies, doors, music, starting inventory) for this PC port.

A copy of BioRand (binaries only, MIT, © Ted John / IntelOrca) is in `CVXRandomizer\biorand\`. Exploration showed **BioRand already contains a partly finished Code Veronica randomizer**. It is unadvertised, credits "kapdap" for the CV information, and targets the same US PS2 disc (SLUS-20184). It has room data for 165 rooms (doors, key requirements, item pickups with patch offsets, door-rando byte ranges) and a full ISO → AFS → PRS → room-file edit pipeline. Its source is public at `github.com/IntelOrca/biorand`. The job is to **port that CV pipeline to C#/.NET 10, retarget it from "rebuild a PS2 ISO" to "write a mod folder for recvx-pc", fill the gaps (enemies beyond stage 1, music, PC-side replacements for its PS2-executable patches) and wrap it in a WinForms app.**

### Owner decisions (fixed, do not revisit)
| Topic | Decision |
|---|---|
| v1 features | Items (key + non-key), enemies (full cross-room type swaps), doors, music shuffle, starting inventory |
| How changes reach the game | Randomizer writes changed files to a **mod folder**; recvx-pc gets a small "mod folder overrides data files" hook plus randomizer options it reads |
| BioRand | Port its algorithms and CV data; credit it (MIT notice) |
| Stack | C# **.NET 10**; the window is **Avalonia 11** (WinForms until 2026-10-20), published as a **self-contained single-file exe** (players install nothing) |
| Repo | This folder (`CVXRandomizer`) becomes its own git repo; only the hooks go into recvx-pc |
| Shipping | Bundled in recvx-pc's share zip (`pc/tools/make_share.py`) |
| Coverage | Whole game in one logic graph (Claire's Rockfort + Antarctica, Chris's part) |
| Outputs | Spoiler log, seed+settings share code, door graph export, one-click "Generate and play" |
| Saves | Separate save folder per seed |

### Rules for both repos (owner rules)
- **Never mention Claude or any AI tool** anywhere: no `Co-Authored-By`, no "Generated with" footers, nothing in docs or code comments. This overrides any harness attribution reminder.
- Commit only when asked; push only when asked. Never commit game files (`*.iso`, `data/`, extracted rooms, `*.AFS`, `*.RDX`). Check `git status` before every commit.
- Commit style: recvx-pc uses `PC: <what>` / `Docs: <what>`. CVXRandomizer uses `Rando: <what>` / `Docs: <what>`. Short subject line, then a body explaining why.
- recvx-pc: upstream `src/` stays as is. Every PC change goes inside `#ifdef TARGET_PC`. Read `recvx-pc\pickup.md` before touching that repo.

---

## Key facts from exploration (the executing agent should rely on these)

### How recvx-pc loads data (`..\recvx-pc`)
- **Data folder:** `pc\src\pc_cdvd.c` (`data_open`, `data_find`, `data_file_at`, `read_sectors`). The folder is chosen by `RECVX_DATA`, then `data=` in `recvx.ini`, then `<exe>\data`. It requires `data\files.idx`, one line per file: `start_sector size path`. The sector numbers are virtual keys found by binary search. There is no loose-file override today.
- **Room files:** all 205 rooms live in `RDX_LNK.AFS`.
  - Names come from `rdx_files[205]` in `src\ps2\veronica\prog\ps2_dvd_image.c`, as `RM_<stage 0-9><room 2 decimal digits><case>`.
  - `OpenAfsIsoFile` (`adxwrap.c`) opens AFS partition 17. The file is decompressed with `Expand()` (`expand.c`, LZ with LSB-first flag bits and a 0x2000 window, i.e. Sega PRS-like), then parsed by `bhSetRoom`/`bhFinishRoom` (`room.c`).
  - The room name is built in `system.c` around line 1654 (`rm_%1d%02d%1d.rdx`).
- **AFS layout constraints:** `gdFsInit` (`ps2_sg_gd.c`) reads only the first sector and assumes the entries are packed back-to-back, 2048-aligned, from entry 0's offset (0x80000). A rebuilt `RDX_LNK.AFS` must keep:
  - exactly 205 entries in the same order;
  - contiguous 2048-aligned packing;
  - a room header version float ≥ 1.72.

  An all-literal compression stream is valid input for `Expand()`.
- **Memory:** the compressed room is loaded at `sys->endp - sz` and expanded into the same pool. Watch growth from enemy-asset copying, and log the size per room.
- **Room data structures:**
  - `RDT_WORK` header (`include\ps2\veronica\prog\system.h`) points to `ROM_WORK` (`types.h`), which holds `enep/ene_n` (enemies), `itmp/itm_n` (items), `etcp/etc_n` (trigger areas, i.e. AOTs), `posp/pos_n` (entry positions) and `evtp` (scripts).
  - Enemies and items are 0x24-byte `ETTY_WORK` entries (`flg`, `id`, `type`, position, `ay`, …). **The item is `id` (offset 4)**: `bhSetItem` (`objitm.c`) copies `itp->id`, and the pickup code reads it. BioRand writes new items into `type` (offset 6) instead and pairs that with `HackItemPickup`, which makes the pickup read offset 6; that only works if every pickup in the game is rewritten (all 615 have `type` 0). We write `id` (`RdxItem.ItemId`) and need no pickup hook (verified in game).
  - Doors are `etcp` triggers with `type==0`: `prm0` = stage, `prm1` = room, `prm2` = entry position, `prm3` = animation.
  - Item pickups are `etcp` triggers with `type==4`, where `prm0` indexes `itmp`.
- **Items in code:**
  - The pickup amount is **not** stored per pickup. It comes from `getbulletmax[itemId][gm_mode]` in `src\ps2\veronica\prog\item.c`.
  - Pickup handling: `bhCheckExmAtari` (`hitchk.c` ~4588–4870), then `GetItem`/`ItemSet` (`sub1.c`).
  - Starting inventory: `AllItemInit` / `ExtraGameItemInit` (`sub1.c`). Slot = `(id<<16)|count`.
  - Item names are in `SYSMES.ALD`. Map markers use `itm_no` (`map.c` ~309); check whether they need updating.
- **Scripts:** the 256-opcode table is `bhScenarioJmpT` (`event.c`). Opcodes that matter:
  - `bhItmCk` (14), `bhUseItemCheck` (16, key checks), `bhEneSetCk` (34), `bhItmSetCk` (35);
  - `bhSetDoorCall` (51), `bhEtcAtariSet` (11);
  - `bhItemGetGet` (238) / `bhItemGetGetEx` (254) for scripted gives.
- **Enemies:** spawned by `bhSetEnemy` (`eneset.c`). The type indexes `bhJumpEnemy[100]`, and each `enNN.c` has a `// ENEMY:` comment naming it. Rooms embed only their own enemy models and motions (`bhSetEneMdl`/`bhSetEneMtn`), so cross-room swaps must copy model, motion and texture data.
- **Settings:** the game reads `recvx.ini` with `pcIniGet`/`pcIniGetInt` (`pc\src\pc_platform.c` ~286); use these for new keys. `recvx-config` (`pc\config\recvx_config.c`) keeps lines it doesn't manage. `recvx.exe` takes no command-line arguments.
- **Testing aids:**
  - `debug_save=1` (F5 opens the save screen anywhere), `skip_intro=1`, `skip_doors=1`, F9 back to title, hold Numpad 0 to fast-forward.
  - `RECVX_INPUT` scripted input, `RECVX_REPLAY`, `RECVX_SHOTS` screenshots, `RECVX_CONSOLE=1`.
  - `recvx.log` records stack traces. Room loads print `read file = RM_xxxx.RDX`.
  - Use a separate test folder when the owner's game is running (see `pickup.md`, "Working while the owner's game is running").
- **Build:** `python pc/tools/build.py`. Sources and flags are in `pc\tools\common.py`. Release zip: `pc/tools/make_share.py`.

### What BioRand has for CV (`CVXRandomizer\biorand\`)
- **Data** (JSON with `//` comments and trailing commas; read with `ReadCommentHandling=Skip, AllowTrailingCommas=true`):
  - `data\recv\rdt.json`: `beginEndRooms` (`{"start":"1000","end":"A1E0","doorRando":true}`) and 165 `rooms`. Each room has:
    - `doors[]`: `id` = AOT index, `target`, `entranceId`, `requires` = item IDs, `condition` = flag expression, `lock`, `noReturn`, `isBridgeEdge`, `offsets`, `randomize`;
    - `items[]`: `id`, `itemId`, `globalId`, `type`, `amount`, `priority` normal/low/fixed/hidden, `requires`, `offsets` = places the item type is repeated in scripts, `nop` byte ranges, `doorRando` variants;
    - `doorRando` (`nop` byte ranges or `category` exclude/box/segment/bridge/static);
    - `enemies` (only 2 rooms).

    All offsets are into the **decompressed** room file of SLUS-20184.
  - `data\recv\enemy.json`: spawn points `{room,x,y,z,d,f}`, only 50 points in stage-1 rooms.
  - `data\recv\bg.png` is a cosmetic background and not needed.
- **Room ID mapping (verified against `rdx_files`):** BioRand ID `SRRV` (all hex) → file `RM_<S-1><RR as 2-digit decimal><V>.RDX`.
  - Examples: `1000`→`RM_0000`, `10A0`→`RM_0100`, `1100`→`RM_0160`, `A1E0`→`RM_9300`, `A201`→`RM_9321`.
  - 205 files vs 165 mapped rooms: audit the 40 unmapped rooms.
- **Code to port** (`IntelOrca.Biohazard*.dll`; find the matching source files in the GitHub repo):
  - Namespace `IntelOrca.Biohazard.BioRand.RECV`: `ReCvRandomiser`, `ReCvDoorHelper` (SetFlag/Nop/Patch), `ReCvItemHelper`, `ReCvEnemyHelper` (safe types: Zombie 1, Zombie Dog 4, Hunter 5, Bat 7, Bandersnatch 9, Tyrant 19), `EnemyRandomiser.HarvestEnemyAssets` / `CvEnemyAssets`.
  - Shared engine: `PlayGraph`, `DoorRandomiser`, `ItemRandomiser`, `EnemyRandomiser`, `BgmRandomiser`, `Map`/`MapRoomDoor`/`MapRoomItem`, `RandoConfig`.
  - Formats (`IntelOrca.Biohazard`): `RdtCv` (+ `Builder`; tables Enemies/Items/Aots/Script/Models/Motions/Textures…), `Prs`, `AfsFile` (wraps AFSLib), `ScdReader.ReadScriptCv`, `BioCvConstantTable`, `ReCvItemIds` (0–153, 255), `ReCvEnemyIds`.
  - **PS2-executable patches** that BioRand applies to `SLUS_201.84`. They must be re-done as C code in recvx-pc, because the port never runs that executable:
    - `SetItemQuantityPickup`: rewrites the **per-item-type** pickup amount table at `0x35BCC0` (256 items x 4 ints, one per difficulty: `getbulletmax` in `item.c`) for ammo, ink ribbons and the rocket launcher. There is no per-pickup amount anywhere in CV;
    - `HackItemPickup`: `0x266E30` is `lhu v1,4(a0)` in `bhCheckExmAtari`; writing `0x06` makes the pickup read the item from offset 6 (`type`) instead of 4 (`id`). Not needed with our approach (see above);
    - `SetFirstInventoryItem`, `SetSpecialSlotItem` (starting inventory);
    - `DisableNosferatuPoison`;
    - `FixRifleStacking`;
    - `SetDoorSkip` (already exists as `skip_doors=1`).

    Read their source to learn **where BioRand stores the amount in the room item entry** (likely an `Unk` field of `RdtCv.Item`) and mirror that.

---

## Architecture

```
CVXRandomizer/                      (git repo, .NET 10, net10.0-windows)
  CVXRandomizer.sln
  src/Cvx.Formats/      AFS read/write (layout rules above), PRS compress/decompress,
                        RDX room parse/rebuild (port of RdtCv), CV script reader (ScdReader CV),
                        files.idx reader
  src/Cvx.Rando/        Map model + JSON loading, PlayGraph, Item/Door/Enemy/Bgm/Inventory
                        randomisers, RandoSettings, seeded RNG, share code,
                        output writers (mod folder, rando.ini, spoiler, .dgml/.dot)
  src/Cvx.Rando.App/    the window, Avalonia (exe name: cvx-rando.exe)
  src/Cvx.Rando.Cli/    headless generator for tests/automation (same core)
  data/                 rdt.json, enemy.json (from BioRand, extended), items.json (names, kinds,
                        stack sizes), enemies.json (types, difficulty weights, asset sources),
                        bgm.json (track categories)
  tests/Cvx.Tests/      xUnit
  THIRD_PARTY_NOTICES.md (BioRand MIT text + kapdap credit), LICENSE (MIT), README.md
```

- The data files are embedded as resources so the app stays a single exe.
- Get BioRand's source by cloning `IntelOrca/biorand` into a folder **outside** the repo (e.g. `..\biorand-src`) at a pinned commit; note the commit hash in `THIRD_PARTY_NOTICES.md`. Ported files keep BioRand's copyright line in their header comment.
- **Determinism:** use our own PRNG (e.g. PCG32), never `System.Random`, and keep iteration orders stable (sorted keys), so a seed, settings and generator version always give byte-identical output.

### Contract between the randomizer and recvx-pc
Generating writes to `<game dir>\rando\<seed>\`:
- `RDX_LNK.AFS`: the rebuilt room archive (only file overridden in v1; others may follow).
- `rando.ini`: `version=1`, `seed=`, starting inventory, BGM remap and patch toggles (keys defined in phase 2). Pickup amounts are per item type, so they go here too (`pickup_amounts=`).
- `spoiler.txt`, `graph.dgml`, `graph.dot`, `share.txt`.

The randomizer then edits `recvx.ini` (preserving all other lines, same rules as `ini_set` in `recvx_config.c`):
- `mod=rando\<seed>`
- `saves=saves_rando\<seed>`

"Play normal game" in the app removes both keys. The app keeps the last N seeds' folders and offers to delete older ones.

---

## Phases

Each phase ends in a working, testable state. Phases 1–2 are the foundation; do not start randomisation before the identity round trip boots in game.

### Phase 0: Bootstrap
- `git init` in CVXRandomizer.
- `.gitignore`: `bin/ obj/ biorand/` (keep the BioRand binary copy local only; it is ~3 GB with `biorand.zip`), plus `*.iso *.AFS *.RDX data_extracted/`.
- Create the solution with 4 projects and the test project; turn on nullable and warnings-as-errors.
- Clone the BioRand source outside the repo at a pinned commit.
- Add a `README.md` stub, `LICENSE`, `THIRD_PARTY_NOTICES.md`.

### Phase 1: File formats and the identity round trip (`Cvx.Formats`)
1. **files.idx + AFS reader/writer.**
   - Writer rules: same entry count and order, 2048 alignment, contiguous from 0x80000 (or the original first offset), name table preserved as in the original.
   - Unit test: read `RDX_LNK.AFS` from the owner's `..\recvx-pc\build_pc\data`, rewrite it unchanged, and require byte-identical output.
2. **PRS** (port BioRand's `Prs`).
   - Test: decompress all 205 rooms.
   - Recompress each, then decompress again with **our own port of the game's `Expand()`** (`expand.c`) to prove the game will accept it.
3. **RDX parser and builder** (port `RdtCv` + `Builder`).
   - Test: for all 205 rooms, parse then rebuild gives identical bytes (or at least identical decompressed content).
   - Dump a summary per room: enemies, items, door AOTs with targets.
4. **CLI commands:** `cvx-rando dump-room RM_0100`, `dump-all --out json` and `verify-data`. These are the authoring and debug tools for every later phase.

### Phase 2: recvx-pc hooks (in `..\recvx-pc`, `#ifdef TARGET_PC`, separate `PC:` commits)
1. **Mod overlay** in `pc_cdvd.c`:
   - New `mod=` key (relative to the exe, or absolute) plus `RECVX_MOD`.
   - When `data_open` loads `files.idx`, any file found at the same relative path under the mod folder replaces that entry's path and size, and gets a fresh virtual sector range after the last file (so a larger file never overlaps).
   - Log each override in `recvx.log`.
   - Works for any file, though v1 only needs `RDX_LNK.AFS`.
   - Check the `gdFsInit` path reads the overridden file.
2. **`rando.ini` reader** in a new `pc/src/pc_rando.c`. It loads `<mod>\rando.ini` if present, using the same parsing as `pcIniGet`, and exposes:
   - **Pickup amounts per item type:** `pickup_amounts=id:count,...` overrides `getbulletmax[id][*]` (`item.c`) at start-up, like BioRand's `SetItemQuantityPickup`.
   - **Starting inventory:** `start_items=id:count,id:count,...` and `start_special=`. Applied in `AllItemInit` on New Game.
   - `no_nosferatu_poison=1` and `fix_rifle_stacking=1`, ported from BioRand's executable patches by reading what they changed.
   - **BGM remap:** `bgm_map=src:dst,...`. Find the BGM play call (ADX in `BGM1/2.AFS`, see `pc_adx_rna.c` and the game's BGM request function) and remap the track index there. This avoids rebuilding the large audio archives.
   - `version=` check: warn in `recvx.log` and show a message box if the version is unsupported.
3. **Testing aid** `RECVX_WARP=<stage>,<room>,<pos>`: after the title, jump to a room. Set `sys->stg_no/rom_no/pos_no` and call `bhSetDoorDemo()`. This is essential for verifying randomized rooms without long playthroughs.
4. **recvx-config:** a "Randomizer…" button that starts `cvx-rando.exe` next to it, if present. Show "Randomizer active: seed X" when `mod=` is set, with a "Play normal game" button that clears `mod=`/`saves=`.
5. **`make_share.py`:** include `cvx-rando.exe` when it exists at a configured path (argument or env var). The README section for the zip mentions it.
6. Update `README.md` (ini keys table: `mod=`, `rando.ini` keys), `pc/README.md` (`RECVX_MOD`, `RECVX_WARP`) and `pickup.md`.

**Gate:** an identity mod folder (the AFS rebuilt unchanged) boots, and a new game reaches gameplay. A test mod with one hand-edited pickup (e.g. Handgun Bullets → Green Herb, amount 7 in the first room with an item) shows the new item and amount in game.

### Phase 3: Data import and validation
- Embed `rdt.json` and `enemy.json` from BioRand. Map room IDs using the verified rule (`SRRV` → `RM_<S-1><RR decimal><V>`), centralised in one `RoomId` type (done in Phase 1: `Cvx.Formats.RoomId` / `RoomTable`).
- `verify-data` checks, for every room:
  - the AOT ids exist and have the right type (door=0, item=4);
  - each item's `type` equals the byte at every listed `offset`;
  - door targets exist in `rdx_files`;
  - `nop` ranges fall inside the script region.

  Fix or flag any mismatch. All are expected to pass, since it is the same disc.
- (Done; see Status.) Audit the 40 rooms in the AFS that are not in `rdt.json`: cutscene-only, duplicate variants, or missing data. Add entries where they have items or doors that matter, using `dump-room`.
- Write `items.json`: ID → name (from `ReCvItemIds`, cross-checked against `SYSMES.ALD` names and `itemdata[]` in `item.c`), kind (key/weapon/ammo/health/ink/document/other), max stack, the ammo each weapon uses, and the Claire/Chris inventory rules.

### Phase 4: Item randomizer
- Port `PlayGraph` and `ItemRandomiser` and the CV parts of `ReCvItemHelper`.
- **Modes:**
  - shuffle the original items, or a custom pool with ratios for gunpowder/ammo/health/ink and average ammo amount;
  - include documents;
  - allow alternative routes;
  - safe key placement around points of no return (`noReturn`/bridge edges in `rdt.json`);
  - only place ammo for weapons that are placed.
- **Write:** change item `type`/amount in `itmp`, patch every listed `offset` (scripted checks and gives), apply the `nop` ranges, rebuild the room, recompress, rebuild the AFS.
- Handle scripted gives (`globalId >= 1000`, e.g. Hemostatic, Lockpick) the way BioRand does.
- Check whether map item markers (`map.c` `itm_no`) need patching.
- Spoiler log section: per room, original item → new item and amount; key items in route order.

### Phase 5: Starting inventory and fixes
- Settings: random inventory on/off and up to two starting weapons; the rest random ammo/health/ink. Separate rules for Claire's start and Chris's start, if Chris's inventory init is a separate code path (check `ExtraGameItemInit` and the scenario switch).
- Write `start_items` to `rando.ini`. Set `no_nosferatu_poison` and `fix_rifle_stacking` whenever the item rando is on, as BioRand does.

### Phase 6: Door randomizer
- Port `DoorRandomiser` (segments, segment size, box-room reachability, loop-back locking, bridge rooms from the `isBridgeEdge` door `"id": 255`).
- **Write:** change the door AOT `prm0/prm1/prm2` (target stage/room/entry) in both rooms and any `entrance{}` overrides. Apply `doorRando.nop` ranges and lock patches (`lock: side` → `ReCvDoorHelper` SetFlag/Patch).
- The item rando reruns over the new graph (BioRand's order: doors first, then items on the resulting `PlayGraph`).
- Outputs: `graph.dgml` (Visual Studio) and `graph.dot` (Graphviz). If the app finds `dot.exe`, it also renders a PNG; otherwise it just writes the `.dot`.

### Phase 7: Enemy randomizer (full cross-room swaps)
- `enemies.json`: per type, difficulty weight, the rooms its assets can be copied from, and size/height limits. Start from BioRand's six safe types (1, 4, 5, 7, 9, 19). Add others (Moth 6, BlackWidow 3, Albinoid, Hunter variants…) only after testing each.
- Bosses, scripted enemies and `restricted` rooms are never changed. Keep BioRand's `maxDifficulty`/`nop` rules.
- **Asset copying:** port `HarvestEnemyAssets`/`CvEnemyAssets`. Copy model, motion and texture blocks from a source room into the target room and fix up the `ROM_WORK` offsets.
  - Enforce a per-room size budget. Measure the real limit from `sys->endp` and the room pool in Phase 2 testing, then write it into the generator.
  - On budget overflow, fall back to a type already present.
- **Spawn points:** BioRand only has stage-1 points. For the remaining stages:
  1. by default, reuse each room's original enemy positions (from `dump-room`), changing only the type;
  2. optionally add points from extended `enemy.json` entries, authored with the help of `RECVX_WARP` + `dump-room`.
- Difficulty slider weights the types; spoiler log lists per room.
- **Crash testing:** a CLI `generate --seeds 1..200` sweep checks the size budget and that every changed room parses. In-game, use `RECVX_WARP` to visit each changed room for a few seeds (script it with `RECVX_INPUT` + `RECVX_SHOTS`, read `recvx.log` for crashes).

### Phase 8: Music shuffle
- `bgm.json`: list CV's BGM tracks (indices from the game's BGM request function / `BGM1/2.AFS` entries) with a category: safe/calm, creepy, danger, boss, story. Do not shuffle story or cutscene tracks.
- Shuffle within each category and write `bgm_map` to `rando.ini`. Custom-music import is out of scope for v1.

### Phase 9: WinForms app and outputs
- **Game folder:** auto-detect `cvx-rando.exe`'s own folder or a picker. It needs `recvx.exe` + a set-up data folder (read the data path from `recvx.ini` `data=`). If game data isn't set up, offer to open `recvx-config.exe`.
- **Main window:**
  - seed box + "Random" button;
  - tabs: Items / Enemies / Doors / Inventory / Music / Output (BioRand-like options with tooltips);
  - Generate, Generate and play (`Process.Start recvx.exe`), Play normal game, Open folder;
  - a progress bar (generation runs on a background task).
- **Share code:** version + seed + settings packed into a short base64url string (`CVX1-...`). Paste to load. Optionally a QR code (the QRCoder package is MIT).
- **Settings persistence:** `cvx-rando.json` next to the exe. Errors from a failed graph ("try another seed or larger segments") are shown as plain messages.
- Spoiler, dgml and dot are written per seed. "Show spoiler" opens `spoiler.txt`.

### Phase 10: Packaging
- `dotnet publish src/Cvx.Rando.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`.
  - No trimming (the data files are read with reflection-based JSON). The size is about 46 MB.
  - x64 is fine because it is a separate process from the 32-bit game.
- A `tools/publish.ps1` copies the result where `make_share.py` expects it.
- The share zip README gets a "Randomizer" section; the app's About box carries the BioRand/kapdap credit.

---

## Critical files

**New (CVXRandomizer):**
- `src/Cvx.Formats/{Afs,Prs,Rdx,ScdCv,FilesIdx}*.cs`
- `src/Cvx.Rando/*`
- `src/Cvx.Rando.App/*`
- `data/*.json`

**Modified (recvx-pc):**
- `pc/src/pc_cdvd.c` (mod overlay)
- new `pc/src/pc_rando.c` + `pc/include/pc_rando.h`
- `pc/tools/common.py` (add the source)
- hooks in `src/ps2/veronica/prog/{sub1.c,hitchk.c,item.c}`, the BGM request function and the Nosferatu poison code (all `#ifdef TARGET_PC`)
- `pc/src/pc_platform.c` (`RECVX_WARP`)
- `pc/config/recvx_config.c` (Randomizer button/status)
- `pc/tools/make_share.py`
- `README.md`, `pc/README.md`, `pickup.md`

**Reference only:**
- `CVXRandomizer\biorand\data\recv\*.json`
- BioRand source (`ReCvRandomiser`, `RdtCv`, `Prs`, `AfsFile`, `PlayGraph`, `*Randomiser`)
- `recvx-pc\src\ps2\veronica\prog\{expand.c,room.c,eneset.c,event.c,item.c,ps2_dvd_image.c,adxwrap.c,ps2_sg_gd.c}`

## Verification
1. **Unit tests** (`dotnet test`), using the owner's extracted data at `..\recvx-pc\build_pc\data` (skip when absent):
   - AFS round trip is byte-identical;
   - all 205 rooms decompress, recompress, and expand with the ported `Expand()`;
   - RDX parse/rebuild round trip;
   - `verify-data` passes;
   - the same seed and settings give identical output twice;
   - 200-seed sweep: every seed either generates or fails with a clear "unsatisfiable" message, never an exception, and every key item is reachable in `PlayGraph` (a reachability checker independent of the placer).
2. **Port hook tests** in a scratch test folder (copy of `recvx.exe`, `SDL2.dll`, an ini with absolute `data=`, `mod=`; see `pickup.md`):
   - identity mod boots;
   - the edited-pickup mod shows the new item and amount;
   - `start_items` applies on New Game;
   - `bgm_map` audibly or in the log (`RECVX_SNDLOG=1`) remaps.
3. **In game per feature:**
   - `RECVX_WARP` into changed rooms with `RECVX_SHOTS` screenshots;
   - read `recvx.log` for crashes;
   - check the first-room item matches `spoiler.txt`;
   - door rando: walk through 3–4 doors and compare with `graph.dot`.
4. **Owner playthroughs:** a full run on one item-only seed, then an items+enemies seed, then a door seed, sending `recvx.log` + `last_run_input.txt` on problems.

---

## Status

### Phase 0: done (2026-10-13)
- Git repo (branch `main`, nothing committed yet), `CVXRandomizer.slnx` with `Cvx.Formats`, `Cvx.Rando`, `Cvx.Rando.App` (`cvx-rando.exe`), `Cvx.Rando.Cli` (`cvx-rando-cli.exe`), `Cvx.Tests`.
- BioRand source cloned to `..iorand-src` at `v3.3.2` (`79229a24`, biohazard-utils `b88e24c9`), the same version as the binaries in `biorand\`.

### Phase 1: done (2026-10-13)
- `Cvx.Formats`: `AfsArchive`/`AfsWriter` (streaming, keeps the packed 2048-byte layout), `Prs` (hash-chain compressor), `Expand` (C# port of the game's decoder), `RoomId`/`RoomTable` (205 rooms, BioRand id mapping), `GameData`, `RoomArchive`, `Rdx/RdxFile` (+ `Builder`, `WithPatch`, `WithRecord`), structs named after the game's.
- CLI: `list-rooms`, `dump-room`, `dump-all`, `extract-room`, `roundtrip [--out]`, `bench-prs`.
- Results on the owner's data:
  - all 205 rooms parse; rebuilding each unchanged gives **identical bytes**;
  - rewriting `RDX_LNK.AFS` unchanged gives an **identical file**;
  - every room recompressed by `Prs.Compress` expands back exactly with the ported `Expand()`. Our output is 100.6% of Capcom's size in total (worst room RM_7100, +30 KB). If Phase 2 shows room memory is tight, write an optimal-parse compressor; otherwise only recompress rooms that changed (already how `RoomArchive.WriteModified` works);
  - the original rooms hold 759 enemies, 615 items, 462 item trigger areas and 277 door trigger areas (from `dump-all`), so Phase 7 can use original enemy positions everywhere, not just BioRand's 50 stage-1 points.
- 30 tests pass (`dotnet test`, about 30 s with game data).
- `roundtrip --out <dir>` writes an `RDX_LNK.AFS` with every room recompressed: use it as the identity mod for the Phase 2 gate.

### Phase 2: done (2026-10-13), uncommitted in recvx-pc
- recvx-pc (all game-code changes inside `#ifdef TARGET_PC`, upstream lines untouched; details in its `pickup.md`, "Randomizer support"):
  - `pc/src/pc_cdvd.c` `mod_open`: `mod=` / `RECVX_MOD` overlay; larger files move to new sector ranges.
  - new `pc/src/pc_rando.c` + `pc/include/pc_rando.h`: `rando.ini` keys `version`, `seed`, `pickup_amounts`, `fix_rifle_stacking`, `no_nosferatu_poison`, `start_special_claire/chris`, `start_items_claire/chris` (up to 8, replace the defaults), `bgm_map`; `RECVX_WARP`.
  - `pcIniFileGet`/`pcIniFileGetInt` in `pc_platform.c`; `getbulletmax`/`itemdata` writable on PC (item.h/item.c); hooks in sub1.c (`AllItemInit`), en15.c (`PoisonAttack`), event.c (music commands + `bhSetNextRoomBgm`), system.c (`bhFirstGameStart`).
  - recvx-config: status line with the seed, "Randomizer..." (starts `cvx-rando.exe`, then closes), "Play normal game".
  - make_share.py `--rando <exe>` (or `build_pc/cvx-rando.exe`).
  - README.md / pc/README.md document `mod=`, `saves_normal=`, `rando.ini`, `RECVX_MOD`, `RECVX_WARP`.
- **Contract for the randomizer app (Phase 9):** write `mod=rando\<seed>` and `saves=saves_rando` (per-seed `saves_rando\<seed>` until 2026-10-16, see below); before the first change keep the player's own value as `saves_normal=<value>` (`*` when there was no `saves=`), and don't overwrite an existing `saves_normal=`. "Play normal game" = remove `mod=`, restore `saves=` from `saves_normal=` (remove it for `*`), remove `saves_normal=`.
- CLI `make-mod --out <dir> [--item RM_xxxx:<index>=<id>]... [--ini key=value]...` builds test mod folders.
- Verified in a scratch test folder (copy of recvx.exe, `data=` absolute, `mod=`):
  - all 205 rooms recompressed by us: boots, and RM_7100, RM_9350, RM_0030, RM_9060, RM_0020, RM_5580 load and render via `RECVX_WARP` (so compressed-size growth is not a memory problem for the biggest rooms);
  - RM_7110 item 1 Door Knob -> Handgun Bullets with `pickup_amounts=12:77`: prompt says Handgun Bullets, inventory shows 77;
  - `start_items_claire=21:1,31:3`: inventory has a Green Herb and 3 Ink Ribbons instead of the manual;
  - `bgm_map`: RM_7110's track 96 played as 97;
  - recvx-config shows the seed; "Play normal game" removes `mod=` and restores `saves=`.
  - Not verified in play: `no_nosferatu_poison`, `fix_rifle_stacking`, Chris's starting items, the "Randomizer..." button (needs `cvx-rando.exe`).
- Testing tips: `RECVX_WARP=7,11,4` puts Claire on top of a pickup (RM_7110 entrance 4); with `newgame.txt` plus `status+90 cross 8` / `status+170 start 8` the item is taken and the inventory shown. Fresh warped games have no weapons, so enemies kill Claire after about a minute.

### Phase 3: done (2026-10-13)
- `Cvx.Formats/Script`: `CvScriptReader` + `CvOpcodes` (BioRand's opcode size and name tables; every script in all 205 rooms walks with known sizes, no fallbacks), `AotStates` (each trigger area's room state plus every `set_etc_atari2` write in its scripts).
- `Cvx.Rando/Data`: `RoomMap` (port of BioRand's `Map`; `OffsetSpec` for offsets/ranges), `ItemCatalog` (`data/items.json`, generated once from BioRand's item helper and the game's `itemdata`), `RandoData` (embedded `data/items.json`, `data/recv/rdt.json`, `data/recv/enemy.json`), `DataValidator`.
- CLI `verify-data [--all]` and `dump-script <room>`.
- **Facts that later phases must use** (details in `data/recv/CHANGES.md`):
  - Room scripts rewrite trigger areas with `set_etc_atari2` (0x25: id, attr u16, prm0-3, type). Many doors are examine areas (type 3) whose event calls `set_door_call` (0x33: target stage/room/entrance in bytes 4-6). So door rando must work like BioRand's `OverrideDoor` (a per-frame script that catches the trigger-area flag and calls the door), not only by editing the trigger area.
  - Trigger-area types: 0 door, 1 stairs, 2 climb/jump, 3 examine, 4 item (hitchk.c `bhCheckExmAtari`).
  - `entranceId` in the data = arrival entrance in the door's own room (BioRand's DoorRandomiser line 659). 333 of 340 doors match the doors leading back.
  - The entrance position table (`posp`) can be shorter than the entrance numbers in use; room scripts place the player themselves (they read `pos_no`, script variable 15).
  - BioRand's "Handgun Bullets x15" entries can be placeholders; the room file or the giving script is the truth.
- Data fixed: 8 item types, 2 trigger-area ids, 1 added script offset, 1 bogus item removed, 2 door targets and 1 id swap, 2 `doorRando` typos. 7 door errors and 24 warnings stay open for Phase 6 (listed in CHANGES.md, asserted by a test).
- 40 rooms are not in the data: the Battle Game rooms (RM_55xx-58xx), cutscene rooms, and some room variants. **Phase 4 must mirror item changes into unmapped variants** (same item at the same position as the mapped variant, e.g. RM_3010/RM_3011).
- 38 tests pass.

### Phase 4: done (2026-10-14), committed `c8ef7ec`
- `Cvx.Rando`: `Rng` (PCG32, pinned by a test), `RandoSettings`, `Items/ItemGraph` (rooms, doors, pickups by global id), `Items/ItemPlacer`, `Items/Playthrough` (independent check), `Items/ItemPatcher`, `Generator` (mod folder: `RDX_LNK.AFS` with only the changed rooms, `rando.ini`, `spoiler.txt`, `settings.json`), `Spoiler`.
- CLI `generate [--seed] [--mode shuffle|custom] [--claire-only] [--out] [--spoiler]`.
- **Logic rules** (differences from BioRand):
  - Claire and Chris have separate inventories; the logic starts over at the handover (`8080 -> A090`, a one-way bridge).
  - Keys a half needs but has no pickup of are handed over by the game (Chris: Album, Extinguisher).
  - Pickups that keep their item (fixed, and key items no requirement mentions) grant it when reached, and the logic waits for them instead of placing copies.
  - A global id is one pickup across room files. Chris's keys only go into pickups that are his alone, and shared pickups holding a key Chris needs keep it.
  - The logic runs to `A1E0` (the end of the game) in both modes; BioRand's non-door data stopped at `A090`.
  - One-way doors are taken only once everything before them is unlocked, so keys never end up behind a point of no return.
- **Item modes:** shuffle (the game's own non-key items moved; spare key pickups become Green Herbs) or custom (each allowed weapon once, then ammo for those weapons, healing, ink and gunpowder by ratio; per-type pickup amounts in `rando.ini`).
- **Patching:** the room item table's `ETTY_WORK.id` (no pickup hook needed), the item byte of every listed script opcode, and the data's `nop` ranges. Opcode 0xF4 is the game's `bhComNext` (advance one byte, keep running), so it is a safe one-byte no-op.
- Unmapped room variants get the same new item, matched by original item and position.
- **Data fixes:** two missing Shotgun pickups, two colliding global ids (`data/recv/CHANGES.md`).
- **Tests:** 48 pass, including 600 seeds (shuffle, custom, Claire only) that all complete in the independent playthrough, determinism, and only item bytes changing in rooms.
- **In game** (test folder, seed 7): the RM_7110 pickup gave Bow Gun Arrows instead of the Door Knob. RM_9240, RM_7040, RM_6010 and RM_0000 load without problems.
- **Known limits:**
  - An item on the floor still shows the original item's model; the prompt and the pickup are the new item.
  - Writing the mod folder takes about 40 s (copying the 588 MB archive and recompressing about 130 rooms); a recompression cache can come later.
  - The map's item checks only look at the inventory (`map.c`), so they need no change.

### Phase 5: done (2026-10-14), committed `15b165d`
- `Items/StartInventory` (after BioRand's RandomizeStartInventory), made separately for Claire and Chris:
  - a Combat Knife;
  - up to two weapons by kind (`StartWeapon1/2`: none, random, sidearm, primary, powerful), loaded, with a 75% chance of spare ammo;
  - up to four healing items;
  - a 50% chance of 1-2 ink ribbons;
  - 8 slots in all, with two-slot weapons first (itemdata type bit 0x100; the game sorts them to the top).
- Claire keeps the lighter in her special slot (her first cutscene needs it). Chris's special slot stays empty, as in the game.
- `rando.ini`: `start_items_claire=` / `start_items_chris=` and `no_nosferatu_poison=1` (setting `NoNosferatuPoison`, off by default; turn it on with random doors as BioRand did).
- Custom mode does not place Claire's starting weapons again, but still places their ammo.
- `items.json`: ammo for the Sub Machine Gun (M Gun Bullets) and the M-100P (Calico Bullets, magazine 100).
- CLI `generate --inventory --no-poison`. 54 tests pass.
- **In game** (seed 5): Claire's inventory showed the Sub Machine Gun at the top in two slots, then the knife, the Handgun (15), Handgun Bullets 40, M Gun Bullets and two mixed herbs. Chris (warped with player 1) had his knife, Handgun and two mixed herbs, and an empty special slot.
- **Not verified in play:** the Nosferatu option. Also, in a warped game, Start did not open the status screen (the pickup screen was used instead); look into it if it matters for testing.

### Phase 6: done (2026-10-14), committed `5f46b5d`
- **Doors are redirected by the engine, not by editing rooms.** The randomizer writes `rando_doors.txt` to the mod folder, one line per door: `stage room case area old_stage old_room  new_stage new_room new_case entrance`. recvx-pc matches it where the game sets `sys->door`: `bhCheckExmAtari` for static door areas (`hitchk.c`, area = trigger area index) and `bhSetDoorDemo` for script doors (`room.c`, area = `sys->etc_idx`; 255 matches any area). The new case is applied when the room loads (`sys->rcase` is global and only scripts set it). Room variants the data does not list get the same lines as their mapped variant.
- **Start:** a new game starts in RM_0010 (BioRand `1010`, `start=0,1,0,0` in `rando.ini`), skipping the cell. The door back to the cell leads back into RM_0010, and the other door always leads to a save room with another door to continue through.
- **Save rooms** (`Doors/SaveRooms`): rooms whose script opens the save screen (`set` of `cb_flg` bit 0x200000, group 10 index 10) plus the data's `box` rooms. The 18 typewriter rooms are listed in code because scanning takes 8 s; a test checks the list against the room files.
- **Layout** (`Doors/DoorRandomizer`, after BioRand's DoorRandomiser):
  - parts of the game between one-way doors are randomized separately;
  - doors that stay: `randomize: false`, one-way, bridges, locks, room or flag conditions, doors into or out of excluded rooms, and their reverse doors;
  - rooms joined by kept doors that the logic can use both ways form a block; a tree joins every block from the part's entry (preferring keyless doors), the rest pair up at random, and an odd one out leads back into its own room;
  - when a part runs out of free doors (Antarctica does), the rooms left over keep their own doors and the part is tried again;
  - with `--claire-only`, Chris's doors stay.
- **Logic:** door layouts are made until one the item placer finishes and `Playthrough` confirms (up to 200; usually the first few). Random doors turn on random items: with the game's own items, keys end up behind the doors they open.
- **Room tweaks** (`RoomTweaks`, BioRand's ReCvDoorHelper.Begin), applied whenever items are randomized: Rodrigo's lighter and gift fixes, the Steve/Alfred cutscene, Steve at the airport, the 1021/1031/4011 room choices (items only), and with random doors the story flag sets (prepended to the room's start procedure, which rebuilds the room) and the player switch in 70A0. Each room's door-mode nop ranges are applied too, except ranges that contain a door call or room case change: BioRand blanked scripted doors and rewrote them another way, and the engine redirect needs them.
- **Outputs:** `rando_doors.txt`, `start=` in `rando.ini`, a Doors section in `spoiler.txt`, and the room graph as `graph.dot` (Graphviz) and `graph.dgml` (Visual Studio) with the start, end and save rooms coloured.
- CLI `generate --doors`. 72 tests pass (door seed sweeps with and without Chris, start to save room, two-way pairing, every room still reachable, determinism, the typewriter list, rebuilt rooms).
- **recvx-pc:** door redirects in `hitchk.c` and `room.c`, `pcRandoDoor` / `pcRandoTakePendingCase` / `start=` in `pc_rando.c`, a fallback to case 0 when a room variant file does not exist (`system.c`), and the testing aid `RECVX_TEST_DOOR=<area>[,<presses>]`: on that action press it uses static door area `<area>`, or points the action probe at any other area (examine areas).
- **In game** (seed 2): a new game starts in RM_0010; its door leads to the RM_1080 office (typewriter). Static doors from RM_0050 to the RM_3011 variant, RM_2050 to the RM_0021 variant (case 1), and Chris's RM_9060 to RM_9170 entrance 2 all load the right room at the right entrance.
- **Not verified in play:** script doors (walk-in and examine areas that call `set_door_call`): they go through the same lookup as static doors, but a warped test cannot meet their story conditions. The 7 known door data errors (CHANGES.md) are unchanged. A full door seed playthrough is the owner's to do.

### Phase 7: done (2026-10-14), committed `b96b6d2`
- **How the game loads enemies** (room.c `bhSetRDT`, `bhSetEneMdl`, `bhFinishRoom`; `Rdx/RdxEnemyLayout`):
  - models (`hed01`): the room's, one page per enemy, then objects (from the fifth) and items. Pages hold relative offsets and are relocated in place, so each enemy needs its own copy;
  - motions (`hed02`): one entry per enemy work; the first enemy of a type carries the type's block (a zombie's is ~940 KB), later ones are 0 and share it;
  - textures (`hed04`): the room's, then one entry per textured model chunk the first time a texture slot is used, then objects, items (one per item id), effects. The slot is `sys->et_lp[type][model version][chunk]`, declared [100][16][16] and indexed without bounds, so zombie model versions above 15 share slots with Black Widows;
  - every room with enemies matches this exactly (`dump-enemies`).
- **Room memory** (`room-memory`, `Expand.FitsRoomPool`): recvx-pc reads a packed room to the top of an 11,160,448-byte pool and expands it in place from the bottom; the output must never overtake unread input. Every room has at least 2.2 MB to spare. After setup the room's textures are handed back, so ~8 MB is free then. `RoomArchive.WriteModified` refuses a room that would not fit. recvx-pc logs each room's numbers (`system.c`).
- **Found and fixed a Phase 4 bug:** pickups share textures by item id (`sys->it_lp[id]`), so changing pickup ids changed how many texture entries the loader reads and shifted every texture after them (42-47 of ~130 changed rooms per seed). `Rdx/RdxItemTextures` rebuilds the item texture entries for the new ids, each pickup keeping its model's texture.
- **Swaps** (`Rdx/RdxEnemyRewriter`): an enemy gets a donor enemy's type, model version, behaviour (`type` field, 0 = normal), model page, its type's motion block and its textures; positions, floors, work numbers and script indexes stay.
- **Randomizer** (`Enemies/EnemyRandomizer`, `data/enemies.json`): per room one new kind, weighted by difficulty 0-3 and limited per room (BioRand's limits). Replaceable: Zombie, Black Widow, Cerberus, Hunter/Sweeper, Bandersnatch. Kinds placed: those plus bats. Rooms keep their enemies when their scripts act on particular enemies (item links, rising zombies, synced motions...: 11 opcodes), when the data restricts them, or when no kind fits (texture slot clash, memory).
  - Enemies a room event takes control of (`load_work`/`load_work_ex`/`load_work2` with kind 1 = an enemy) keep their type: the event plays the room's own animations on them (`common_ctr` sets `mnwP` to `sys->rmthp`), which only fit the original skeleton. Found in game: RM_0031's zombies as bats crashed in the bat's collision code (no motion data). Many rooms pose their enemies this way, so about 35 rooms change per seed.
- **Sounds** (`SoundPacks`, `Enemies/EnemySounds`): enemies play from bank 3 (type 2) of the room's sound pack (MULTSPQ1/2.AFS, sdfunc.c `CallEnemySe`). A room whose enemies all became one kind gets the donor room's bank; the mod folder gets the changed MULTSPQ archives. Every pack rebuilds byte for byte.
- CLI `generate --enemies --enemy-difficulty 0-3`; `dump-enemies`, `enemy-donors`, `swap-enemies`, `dump-sounds`, `room-memory`.
- **In game:** RM_0060's zombies as Cerberus dogs attack Claire; Hunters (RM_7190) killed Claire; zombies in the former Bandersnatch room RM_1060 attack. A sweep warped into every changed room of seed 3 (36 rooms, Claire and Chris, with the changed sound packs loaded): all load and keep running. The first sweep, before event-controlled enemies were kept, found the RM_0031 crash.
- **Not verified:** that the swapped enemy sounds are right (needs listening); bats were never caught on camera in the sweeps; difficulty balance.
- **Later:** more rooms could change if an event's enemy kept its type while the rest change (done) and if more kinds were checked (Moth, Albinoid, Spotter); BioRand's 50 extra stage-1 positions (`enemy.json`) are not used.

### Phase 8: done (2026-10-14), committed `b61e581`
- **How music plays:** room scripts ask for a track (`bgm_on`, `bgm_on_2`, `bgm_on_ex`, `bgm_on_2_ex`: byte 1; `set_next_room_bgm`: byte 6) and the track number is an entry of BGM1.AFS (rooms of stages 0-5) or BGM2.AFS (stages 6-9). Each disc's archive only holds its own tracks; the others are 72-byte placeholders. recvx-pc already remaps the requests (Phase 2, `bgm_map`).
- **Catalog** (`data/music.json`, `dump-music`): every track's discs, length, whether it loops (ADX header) and the rooms that ask for it. One-shot tracks are stingers and event music and stay. Looping ones are sorted into Area (18), Save room (1: only typewriter rooms ask for it), Tension (12 short loops in enemy rooms) and Boss (13). BioRand never randomized Code: Veronica's music, so the categories are new; listen and move tracks between lists to refine them.
- **Shuffle** (`Music/MusicRandomizer`): a random permutation within each category, where a track only becomes one that every disc it is on also has (scripts only ask for tracks their disc has: tested). The swaps go to rando.ini `bgm_map`; no game file changes.
- CLI `generate --music`; spoiler section; 3 tests.
- **Not verified in play:** listening (the categories come from the scripts, not from hearing every track).
- **Fix found by the owner's playthrough:** BioRand's item-mode tweaks made RM_0010 and RM_0050 always load RM_0021/RM_0031, which skipped the graveyard explosion and zombies rising (RM_0020) and the RM_0030 event. The game loads those event versions once the RM_0080 pickup is taken (item flag 4, kept by the item rando since the flag belongs to the spot) and returns to RM_0021/RM_0031 after; the tweaks are no longer applied (random doors never had them).

### Phase 9: done (2026-10-14), committed `666dc15`
- **Window** (`src/Cvx.Rando.App`, `MainForm`): the game folder (this program's folder, the last one used, or chosen), whether the data is set up and whether a seed is active; the seed with a Random button; the share code with Copy and Paste; tabs Items, Inventory, Enemies, Doors, Music; Generate, Generate and play, Play normal game, Open folder, Spoiler log (asks first), Delete old seeds (keeps the active one and every save folder); a status line with each step, and About with the BioRand/kapdap credit. Built in code with auto-sizing layout panels, so it scales with the display. (Replaced on 2026-10-20: see "Window redesign".)
- **Game folder** (`Setup/GameFolder`): recvx.ini edited with recvx-config's rules (lines kept, last value wins, a key's last line replaced and the others removed). Generating writes `rando\<seed>` and sets `mod=rando\<seed>`, `saves=saves_rando\<seed>`, keeping the player's own `saves=` in `saves_normal=` (`*` for none) once; Play normal game puts it back. Generation runs on a background task; the app refuses to rewrite the seed the running game is playing.
- **Share code** (`Setup/ShareCode`): `CVX1-` and the settings that differ from the defaults, as deflated JSON in base64url (about 40 characters for a plain seed).
- **Settings** are remembered in `cvx-rando.json` next to the program (AppData when that folder is read-only).
- **recvx-pc:** `saves=` and `data=` are now relative to the exe like `mod=` (they were relative to the folder the game was started from), and every level of the save folder is created (`saves_rando\<seed>`).
- 5 tests (recvx.ini round trip back to the player's saves, share codes). In a test game folder, Generate wrote seed 77 (items, enemies, music) in 78 s and set recvx.ini as above.
- **Later:** QR code for share codes; rendering graph.dot with Graphviz when installed.

### Phase 10: done (2026-10-14)
- `tools/publish.ps1`: `dotnet publish` win-x64, self-contained, single file, compressed, native libraries inside, no trimming and no symbols: `cvx-rando.exe`, 46 MB (Avalonia; 47 MB with WinForms). It also writes `cvx-rando-notices.txt` (LICENSE and THIRD_PARTY_NOTICES: BioRand's MIT notice must travel with copies) and copies both to `..\recvx-pc\build_pc` (or `-Target`).
- The app has an icon, shows its version in the title, and its About box can show the licences (embedded).
- recvx-pc `make_share.py` includes the notices with the exe (and refuses to package the exe without them); the share zip's README mentions share codes. The zip: recvx.exe, recvx-config.exe, SDL2.dll, cvx-rando.exe, the notices, README: 45 MB.
- Checked: the published exe, copied next to a test game's recvx.exe, found the game by itself and generated a seed; recvx-config's "Randomizer..." starts it from build_pc.

### Fix from the owner's playtest (2026-10-14)
- Door seed 968047 (share code) dead-ended: the start's save room RM_0160 led through its kept door into a fenced corner of RM_0080 behind the Eagle Plate door. The room data lists the plate only on RM_0080's side (the side the normal game comes from), so the logic counted the corner as the whole room.
- `ItemGraph.SameKeysBothWays`: a door that keeps its original pairing needs its keys from both sides (24 such doors list keys on one side only). Stricter logic only; the 600-seed item sweeps still pass.
- The door randomizer only joins rooms into one block through kept doors that open both ways without keys, so the start's way out cannot depend on a key door.
- When a part runs out of free doors, the stranded rooms keep their doors and the doors into them, and a room left with no free door joins them (RM_4001 is entered from RM_4010 but leads to RM_4011).
- CLI `generate --share <code>` reproduces a share code; a regression test checks the Eagle Plate door in both modes.

### Second playtest fix (2026-10-14)
- Door seed 394125 dead-ended in RM_1090's hidden corridor: RM_1080 -> RM_2000 -> RM_1100 -> the corridor, where the bookcase is a wall. It opens from the office side (the Gold Lugers display case), and the office's own door had been used to continue the door tree.
- `lock: "unblock"` (BioRand: the door opens from this side) now means the way back through a kept door is closed for the item logic (`ItemGraph.OneSideOnly`; doors marked on both sides, RM_0040/RM_0160, stay two-way). The data gives RM_1090's passage door the Gold Lugers.
- The door randomizer no longer joins rooms through these doors. That split left more one-door blocks than free doors (53 blocks, 96 free doors), so the tree now counts a block behind a kept door it can walk through as reached: right away for doors without keys, and with keys only when the free doors run out (the item logic checks those keys). About 2 layout attempts per seed instead of 1.2; no failures in 60 seeds.
- Regression test: the corridor cannot reach the office, and the passage needs the Gold Lugers, in both modes.

### Third playtest fix (2026-10-14)
- Door seed 389349 dead-ended at RM_2050 (residence): its door to RM_2040 is a script event, and on a fresh game it plays a scene that sends the player to RM_2030 instead. With random doors that went through the redirected RM_2030 door, back to where the player came from. RM_2040's door back to RM_2050 does nothing on a fresh game. (Found by replaying the owner's `last_run_input.txt` in a copy of the game.)
- Both doors are `doorRando: false` in the room data: random doors leave them out (no edge for the logic, not paired), and their partner doors are randomized as usual. The item mode is unchanged. (Keeping them as locked pairs instead took two free doors each and left the Rockfort part short of doors for the tree.)
- **Door sweep** (scratch tool, not in the repo): for each of the 318 door-mode doors, a test game warps next to it on a fresh game, uses its trigger area (`RECVX_TEST_DOOR`) and records the next room. Unmodified game: 243 reach the data's target, 22 go elsewhere, 38 do nothing (plus script-only doors, area 255, which it cannot use). Rerun of the 60 under a door-mode mod without redirects (script tweaks applied): 5 more are fine.
- Not acted on, because a fresh warp is not the game's real state: doors that need a key (RM_1070's two, items 67/68) or a story flag (RM_1080's door to RM_1090 timed out but worked in the owner's play) and rooms whose script changes trigger areas by story state (RM_3180). **Open, for the next door playtests:** doors that reloaded their own room or went elsewhere: 4020 #1, 6010 #0, 7000 #1, 8010 #1/#2, 80C0 #1, 80F0 #0, 8100 #0-#2, 8150 #2, 8170 #1, 8180 #0, 9040 #0, A0A0 #0, A0F0 #1, A1A0 #0 (several are the known data errors listed in CHANGES.md); doors that did nothing: 1031 #2, 2020 #2/#4, 20E0 #1, 3060 #2, 4080 #5, 40C0 #1/#2, 40F0 #1, 5011 #1, 5020 #2, 5050 #0, 6000 #1, 8031 #2, 80B0 #3, 8180 #1, A0C0 #0, A0F0 #0, A100 #0/#2.

### Fourth playtest fix (2026-10-15)
- Door seed 79634 stranded the player on RM_4011's gangway beside the plane: RM_3010's door was paired with the plane door, so the player arrived where the plane leads back to, and the rest of the hangar only connects once the bridge is set from the other side. The plane door (Army, Navy and Air Force Proofs) is `randomize: false` now, so it keeps its pair with RM_5000 and no random door arrives on the gangway.

### Fifth playtest fix (2026-10-15)
- Door seed 79634 softlocked around RM_1120. Door mode script changes broke three doors that work in the normal game (found with a door sweep under a door-mode mod without redirects, compared with the unmodified game, and checked again after the fix):
  - RM_1120's stairs up to RM_1000: the room data's door mode nop `0x1580B4-0x1580D8` is the climb event itself (procedure 2 calls the door). Removed from the data.
  - RM_1100's door to RM_2000: the room only sets it up when map flag 0x46 is set, which the cutscene its door mode nop removes would do (RM_2000 sets it too). `RoomTweaks` sets the flag when RM_1100 starts with random doors.
  - RM_0110's door to RM_0120 (area 11): the nop range ended one opcode past the `else`, on the line that makes the area a door. Range end fixed; `verify-data` now reports nops that remove every door setup of an area the room file does not make a door.
- How the script reads flags (event.c): `ck`/`set` value byte 0 means set (`ck ... 01` tests "clear"); room flags (group 4) reset on every room change; `ck 0A 17 xx` tests "the trigger area in use is xx". A trigger area is only checked while its `flg` bit 0x1 is set; `RECVX_TEST_DOOR` skips that check and copies the area's floor, so the sweep can pass doors a player cannot use.
- RM_0100 entered from RM_1120 is a pocket (the crates and fire), as the data already says: its door to RM_1120 is `unblock`, so the logic never routes through RM_1120 -> RM_0100.
- Seed 79634's layout is unchanged by these fixes (only room scripts), so the owner's save on it carries on.
- The full door sweep under door mode (318 doors) found no other door that works in the normal game but not in door mode, once flaky timeouts (several game copies loading at once) were rerun. It did show that with random doors RM_5000 (the plane) starts the flight to Antarctica when it loads (its door mode story flags): seed 7 had a random door into it, which would skip the rest of Rockfort. RM_5000's door back to the hangar is `doorRando: false` now, so only the plane door (kept, Army/Navy/Air Force Proofs) leads in. This one changes layouts (seed 79634's folder keeps the earlier build, where nothing leads into the plane).
- Seed 79634 regenerated by the owner with that build dead-ended on RM_2040's bed canopy: RM_0050's door was paired with the attic ladder, so the player arrived where the ladder comes down, and only the King Ant / Music Box Plate puzzle on the floor connects the canopy to the room. The ladder is `unblock` now (kept with the attic, closed from the attic for the logic, both modes).
- Same seed, later: the Gold Key in RM_3210 could be taken again on every visit. `set_ck_item`/`ck_item`'s last byte says whether the game switches a taken pickup's area off (0) or only hides the model (1, event.c `bhItmSetCk`/`bhItmCk`); RM_3210 uses 1 and switches area 9 off in procedure 1, which its door mode nop removes whole. With random doors both bytes are 0 now (`RoomTweaks`). An audit of every door mode nop found no other range that touches pickup switching. The owner's session log had no errors, and every room kept at least 8.7 MB free.

### Sixth playtest fix and the side pack (2026-10-16)
- Door seed 306305: RM_1070 -> RM_4050, and back out of it the player came to RM_4030 instead of RM_1070. RM_4050 and RM_1140 are one lift (same trigger areas and entrances): area 3 rides it, area 0's exit goes to RM_4030 at entrance 0 and RM_1130 at entrance 1 whichever file is loaded (checked in the unmodified game). The door table matches by room, area and original target, so the lift's exits only matched by chance. Both rooms are `exclude` now: the lift and its two exits keep their doors.
- New setting `StartSidePack` (app: "Start with the side pack", CLI `--side-pack`, spoiler line): event flags 6 (Claire) and 7 (Chris) set when RM_0000 and RM_0010 start; the status screen turns them into the bigger inventory (player.c `bhCheckSubPack`). Works without random items too. Checked in a test game: the item list is the side pack's size from the first room.
- Door seed 996813: RM_0021's door to RM_0010 was redirected (logged) and then went to RM_0010 anyway. Area 1 is a door area and also starts a script door call, and the script runs `room_case_no 0` (which sets `sys->rcase`) before its `set_door_call`, so the second lookup asked for RM_0020's doors and found none. recvx-pc now matches redirects on the case of the room file that is loaded (`pcRandoRoomLoaded` from system.c). Checked with a walk test at that door: the old exe went to RM_0010, the new one to RM_0110 (both lookups logged). This covers every script door that sets the next room's case first.

### Item mode playtest fix (2026-10-16)
- Seed 522131 (items only) softlocked in RM_0150: taking the statue's pickup springs its trap (the door locks, "The door won't open!") and only using a Rusted Sword on the statue releases it (`ck_use_item` Rusted Sword). In the game that pickup is the Rusted Sword itself; the seed had made it a Blue Herb and put the sword in RM_1110. The pickup is `fixed` now. An audit of every room with randomized pickups found no other room whose script asks to use the item one of its own pickups gives.

- **Saves (owner's request):** random runs share one save folder, `saves_rando` (`saves=saves_rando`), instead of one per seed. Generating a seed empties it (`GameFolder.ClearRandomSaves`, which also removes the per-seed folders of older versions); the app refuses while the game runs and asks first when the folder holds saves. The player's own saves (`saves_normal=`) are never touched. The owner's old random saves went to the Recycle Bin.
- **One seed at a time (owner's request):** generating deletes every other seed folder in `rando\` and the random saves once the new seed is written (`GameFolder.DeleteOtherSeeds`), after asking. "Delete old seeds..." is gone. The **Random** button only picks a new number (for a while it also generated the seed; the owner wanted it back to just the number). Deleting an old seed folder is tried again for about two seconds: OneDrive held the emptied folder for a moment, and the app reported it could not be deleted.

### Starting items: knife as a weapon, less healing (2026-10-19, owner's request)
- The Combat Knife is a sidearm now (`items.json` `weaponKind`): it is no longer always given; a Sidearm pick (or Random rolling Sidearm) can be the knife instead of a handgun. Both picks set to None means no weapon at all.
- Custom mode places the knife once like the other weapons, unless Claire starts with it (her normal inventory has it, so without a random inventory it is not placed). It is also in the app's custom weapon list.
- One healing item: Green Herb 60%, Red + Green mix 25%, First Aid Spray 15%. No ink ribbons.
- Not verified in play: picking up a placed knife (the knife's `getbulletmax` amount is whatever the game has; no pickup amount is written for it).

### Door segments, segment size, one character (2026-10-19, owner's request)
- Door mode works like BioRand's RE2 door rando now (`DoorRandomizer`, after `CreateRandomGraph`): settings `DoorSegments` (1-4, default 4), `DoorSegmentSize` (1-8 eighths, default 8) and `DoorCharacter` (Claire/Chris). App: "Play as", "Segments", "Segment size" on the Doors tab; CLI `--segments`, `--segment-size`, `--chris`; spoiler lists the segments.
- **One character, one inventory:** the three `player_change_set` opcodes are removed with random doors (RM_6100 to Chris, RM_9280 to Chris, RM_9320 to Claire). The item logic keeps one inventory (`ItemGraph.OnlyCharacter`): no handover, no keys handed over by the other character (a key whose pickups are all outside the run gets one placed). Only that character's starting items are made.
- **Starts:** Claire in RM_0010 (as before). Chris in RM_7240 (`8180`, entrance 0), where the game sends him after the switch in RM_6100; RM_6100 is left out of his runs. recvx-pc: `start=` takes a fifth value, the player (0 Claire, 1 Chris; default 0).
- **Segments:** the data's `segment` rooms are bridges (RM_5000 plane `6000` -> `7000`, `7060` -> RM_6100 `70A0` -> Chris's arrival, the handover `8080` -> `A090`). A run uses `segments - 1` of them in a random order (Chris has two: his start is behind the third, so at most 3 segments); the last segment ends in `A1E0`. Unused bridges are left out with their rooms.
- **Rooms:** rooms joined by `randomize: false` doors or room requirements form groups that are used whole (BioRand's sticky groups). Excluded rooms are only used inside such a group. The size keeps a share of the other groups (rooms to save in first, at least one per segment; BioRand's formula), dealt out over the segments. At size 8 a run uses about 55-70 of the 165 rooms (excluded rooms are never picked); at size 1 about 11-23.
- **Inside a segment:** a tree from the entry (its first door to a room to save in with another door, required in the first segment), the exit (the bridge room, or the end) joined last to the deepest room, its other doors dead ends; the rest paired (loops). Kept doors (locks, conditions, one-way, doors into excluded rooms, and the way back through any of those) stay inside the segment; when their other side is not in the segment they lead back into their own room, like doors into rooms the run does not use. Groups the tree cannot reach are dropped and the segment is tried again.
- **Items:** only the rooms of the run are in the item graph, so only their pickups change and hold keys.
- Sweep (20 seeds each, Claire and Chris, 1-4 segments, sizes 1/2/4/8): every layout finished, in 1-2 attempts on average; the shortest path from start to end is about 6 doors (1 segment, size 1) up to 37 (4 segments, size 8).
- **In game** (test folder, Chris seed 5, 3 segments, size 4): a new game started in RM_7240 as Chris (`rando: new game starts in stage 7 room 24 entrance 0 case 0 as player 1`) and his arrival led on into RM_7220.
- **Not verified in play:** a whole run; Claire in Chris's rooms and Chris in Claire's (room scripts that check the player), the RM_9280/RM_9320 removals, and the bridges taken out of order (each one's story flags).

## Risks / open items for the executing agent
- **Don't copy BioRand's CV item writes as-is**: it writes `type` and relies on its executable patch; we write `id` (see Key facts). Treat every BioRand CV write as unverified until it is checked against the game's C code.
- **Room memory budget** when copying enemy assets: measure it, don't guess.
- **Script length table:** CV's interpreter has no opcode-length table (each handler advances `bhScePtr`). Rely on BioRand's `ScdReader.ReadScriptCv` and cross-check lengths against `event.c` handlers if new script patches are needed.
- **Chris's half:** BioRand's `rdt.json` covers it (stage `A` = file stage 9), but CV-specific inventory transfers between halves need checking in `sub1.c`.
- **Possible gap in BioRand's CV logic:** because it was never released, expect bugs. Treat its data as a strong starting point, not as verified.

### Window redesign (2026-10-20, owner's request)
- The owner's design handoff (layout "1b": command bar + flow footer, dark, Geist type) replaces the WinForms window. Presentation only: every setting, rule and message of the old `MainForm` stays; the share code format and defaults are unchanged.
- **Avalonia 11.3.22** (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` in Dark, restyled in `Theme.axaml`). `MainViewModel` holds the state and the old form's logic (`Generate`, `PlayNormal`, `OpenFolder`, `OpenSpoiler`, `LoadShareCode`, `FindGame`, `ChooseGame`, the enable rules); `MainWindow` is the layout and the in-window dialogs (`IAppHost`: confirm, message, folder picker, clipboard).
- Layout: title bar 36 (custom, the system's min/max/close restyled as plain glyphs), command bar 60 (seed + Random, share code, game status pill = choose folder), sidebar 256 (each category with its detail line and master switch; the path and About at the bottom), settings pane, footer 68 (OPTIONS -> SEED FOLDER -> GAME, status line with a thin progress bar, Folder, Spoiler log, Play normal game when a seed is active, Generate, Generate and play).
- Controls: switches, segmented controls, steppers (`Stepper`), accent sliders, weapon chips, the item mix bar (`MixBar`). Dialogs are in the window (dim overlay, 400 px card); errors have a warn-coloured title. Enter = Generate; Esc closes a dialog.
- New in `cvx-rando.json` (optional; old files load): `LastGeneratedCode` (the share code of the last seed generated: the footer says whether the options still match the seed in the game) and `Window` (size, position, maximized).
- Chris's door runs clamp Segments to 3 in the window (the door randomizer already used at most 3 for him).
- Fonts: Geist and Geist Mono v1.7.2 (OFL), embedded. Icon: the Umbrella logo from Wikimedia Commons (public domain), `cvx-rando.ico` at 16-256. Notices: Avalonia, SkiaSharp/HarfBuzzSharp (and `THIRD_PARTY_NOTICES_SKIASHARP.txt` for their native libraries), ANGLE, MicroCom, Tmds.DBus, Geist, the icon.
- Published exe: 45.7 MB (was 47 MB).

### Music from other games (2026-10-20, owner's request)
- With BioRand's folder on the computer (its `datapacks/*.zip`, or loose `data/bgm`), the music shuffle can draw on other games: `data/bgm/<game>/<tag>/*.ogg`, 17 games and 710 tracks in the owner's copy (re0, re0_n64, re1, re1_dc, re1.5, re1r, re2, re2r, re3, re4, re5, re7, re7_jack, redc, reuc, resurv, sh1; BioRand's own `recv` folder is left out: the game's own tracks are used). `Music/MusicLibrary` scans the zips' directories (0.03 s) and decodes OGG with NVorbis 0.10.5 (MIT).
- Setting `MusicGames` (default `["recv"]`, so old share codes and seeds are unchanged). Kinds: `data/music.json` gives each category BioRand's tags (owner's choice): Area <- calm, creepy, basement; Save room <- safe; Tension <- danger; Boss <- boss. Stingers, countdowns, results and instrument tracks stay out.
- `MusicRandomizer.Mix`: each category's tracks become ones from a pool of the chosen games' tracks of that kind (each used once before any repeats); without other games it is the old `Shuffle` (same RNG, a test checks it). An imported track goes to an archive entry that is free (a placeholder or past the end, below the BGM partition's 128 entries) on every disc the replaced track plays on; `bgm_map` points the track there. The owner's discs have 26 entries free on both and 63/66 on each, for 16 tracks on both discs and 17/11 on one.
- `MusicWriter` writes `BGM1.AFS`/`BGM2.AFS` into the seed folder (`AfsWriter` can now add entries past the end, filled with the archive's 72-byte placeholder ADX). recvx-pc needed no change: a mod folder already replaces any data file, and the audio layer resamples by each track's rate.
- **ADX encoder** (`Cvx.Formats/Adx`): type 3, version 3 header, 18-byte blocks, cutoff 500, written against the game's own decoder (libadxe `ADX_DecodeInfo`, `ADX_DecodeInfoExLoop`, `ADX_DecodeMonoFloat`: gain = scale + 1, unrounded float history); loops over the whole track from a sector-aligned start (2048), like the game's tracks. A few gains are tried per block and the least error kept. A decoder too (`dump-music --levels`).
- **Loudness:** each imported track is scaled to `targetRms` 5038 (the average RMS of the 44 shuffled tracks, `dump-music --levels`), never past its peak.
- **Verified in the game:** a test mod put a 440 Hz / 660 Hz tone at BGM1 entry 18 with `bgm_map=4:18`; warped to RM_2010, the mix dump (`RECVX_MIXDUMP`) had 440 Hz on the left and 660 Hz on the right about 300 times above other frequencies, still after the 20 s loop. A seed with RE2 and Silent Hill music (seed 4242): `rando: music 4 plays as 18`, `adx: play 2 ch, 32000 Hz` (Silent Hill's rate).
- Cost: converting is about 0.4 s per 30 s of music; 30 tracks in about 6 s on the owner's PC. The two archives add about 450 MB to the seed folder.
- App: the Music pane has a chip per game (Code: Veronica plus each game found), the BioRand folder with **Choose...** (`AppSettings.BioRandDirectory`; otherwise a `biorand` folder next to or above the program or the game), a warning when a share code asks for games the folder does not have, and each kind with its BioRand tags. CLI: `generate --music --music-games re2,sh1 [--biorand <dir>]`. The spoiler lists the imported tracks.
- Not verified: a whole run with imported music, and how each game's tracks sound looping in place of the game's.
- **Title call** (owner's request): setting `RandomTitleCall`. BioRand's `base.zip` has `data/title/*.ogg`, 25 recordings of the name by its community (about 4.7 s each, 22-48 kHz); `template.ogg` is the music without a voice and is left out. The game's own call is ADV.AFS entry 3 (4.3 s stereo, played once: adv.c `TitleCall`, `PlayAdx(0, PatId[3], 3)`); the seed picks one (`Rng "title"`), and `MusicWriter.WriteTitleCall` writes ADV.AFS with it as a one-shot ADX (`Adx.Encode(..., loop: false)`), matched to the game's call's RMS but never past its peak. In the game (seed 77, "irons", 22050 Hz): the first ADX played at the title is it (`adx: play 2 ch, 22050 Hz`, the game's own is 48000 Hz), and the mix dump has it whole (about 5 s) before the title music, like the game's. It is about 4 dB quieter than the game's call: the recordings peak near full scale, so the peak limit holds the gain. App: "Title call" switch in the Music pane (works without the music shuffle); CLI `--title-call`; spoiler "Title call: BioRand's <name>".

### NPC looks and voices (2026-10-20, owner's request)
- BioRand has no new models for this game: `data/recv/pld0/{claire,chris}.recv/PL00.PLD` are byte for byte SYSTEM.AFS entries 10 and 11 (the game's player models, `SKIN` format; the game loads entry `ply_id + 10 + costume * 4`, so 12 and 13 are Steve and Wesker of the Battle Game and 14 Claire's other costume). Its other characters are PS1 models of RE1-3 (`PLD`/`EMD`); converting them was put aside (owner's choice).
- **The cast in the rooms:** NPCs are enemy entries of types 56-99 (all but 71 run `bhSubpl`: the room script moves them). The human cast are 91 Claire, 92 Chris (their cutscene models), 93 Steve, 94 and 96 Alfred, 95 Alexia (also Alfred in disguise in the palace), 97 Wesker, 98 Rodrigo; identified in the game by putting each model in Chris's place in RM_7220's opening scene. Their motions are not in the rooms (no motion block for these types), and they share one skeleton (the same collision joints in eneset.c): a test mod gave Chris's cutscene model Steve's model and textures, and the scene played with Steve, cleanly.
- `data/npcs.json`: the five NPCs (owner's choice: Claire and Chris stay), their types and the entry that gives their look. `Npcs/NpcRandomizer`: a derangement of the five (one new look each for the seed); in every room their entries keep type, position, motions and scripts and get the new look's model page and textures (`RdxEnemyRewriter`; `ItemPatcher.SetRebuild` now chains rebuilds). 40 rooms change; none is skipped.
- **Voices** (owner's request: the new look's voice): `voice_on` (event.c) plays VOICE?.AFS entry `u16 at +2` at the player (+1 = 0), an enemy entry (+1 = 1, index at +6) or an object. 562 lines are played by the scripts (of 1040). Who speaks each: an audio match (10 ms loudness envelopes, correlation > 0.85, a clear winner) with BioRand's clips of this game (`data/voice/<name>.recv`, 1,213 OGG) for 318 lines, else the entry the script plays it at (244); the labels for the five NPCs (311 lines: Steve 168, Wesker 59, Alfred 29, Alexia 28, Rodrigo 27) are in `data/npcs.json`, so generating needs no BioRand. Position alone would be wrong for about a quarter (Claire's entry plays some of Steve's lines in their scenes). Each of a character's lines becomes a line of the new character, one of the four closest in length, the least used first; the ADX is copied from the game's own archives into the mod folder's VOICE1/VOICE2.AFS. The subtitles keep the original words.
- App: an NPCs category (pane and sidebar switch); CLI `--npcs`; spoiler "NPCs (look and voice)" with the rooms changed.
- Checked in the game: every changed room of seed 31 loads and runs (warp sweep). Not verified: the cutscenes themselves with the new looks and voices (warps do not set their story flags), and lines the scripts do not play (movies and the rest) keep their voices.

### Doors that lead nowhere are locked (2026-10-20, owner's report)
- Door seed 365484 (1 segment, size 1/8: 13 rooms): RM_3160 had two doors that "loop back to themselves": its doors to RM_3180 are kept doors (`Special`) whose room is not in the run, so they were dead ends leading back into RM_3160 (through the door, arriving at the same door). The layout was as designed, but a door that walks you back to where you stood reads as broken.
- Now a dead end the player opens by hand (it has a trigger area) is **locked**: `DoorLink.Locked`, an eleventh column `1` in rando_doors.txt; recvx-pc's `pcRandoDoor` returns 2 for it and hitchk.c (the door areas, `TARGET_PC`) cancels the door and shows "It's locked." the way the game's message areas do (`bhSetMessage`'s steps, with the text from `pcRandoLockedText` in SYSMES.ALD's codes: characters - 0x20, 0xFF01 space, 0xFFFF end; system message 63 is "It's locked. There must be a keyhole somewhere.", which would promise a key). Script doors (no area) still lead back into their room. The spoiler says "locked (leads nowhere)". The item logic is unchanged (dead ends were never a way anywhere).
- In the game (seed 365484): RM_0010's door to the cell (area 4) shows "It's locked." (`rando: door 0 1 0 area 4 ... (locked)`), the message closes on a press, Claire stays and plays on.

### Cutscene softlock: a script door with the wrong id (2026-10-20, owner's report)
- Seed 117323 (doors, 1 segment, size 1/8): from RM_7120 the run went on into RM_7230 (no `rando: door` line in recvx.log), a room of no run, and its scene stuck (the same state every frame until the window closed). RM_7120's door to RM_7230 (8170) is a script door: `set_door_call` runs when trigger area 4 is used (`ck 0A 17 00 04`), but the data gave it id 3 (area 3 is the locked door to RM_7170). recvx-pc matches script doors on `sys->etc_idx`, so the redirect for area 3 never applied and the game's own door played.
- Fixed in the data (id 4). `DataValidator` now checks every script door: the area the script's open `if` blocks test before its `set_door_call` (a forward walk with a block stack; an `else` drops its conditions) must be the door's id. That found one more, 9010 (RM_8010), excluded from random doors (left as an open item).
- The same seed had RM_7120's doors to RM_7000 and RM_7030 paired with each other (each led to the other door of the same room). The leftover doors now pair with a door of another room; one without such a partner is a dead end (locked when it is a door area). Script doors are never marked locked (only door areas can show the lock).
- Not verified in the game: RM_7230's scene itself (it may need story flags a run never sets: other rooms outside a run could do the same if a door leaks into them; the validator check closes that way for script doors).

### BioRand's characters as NPC looks (2026-10-21, docs/handoff-character-models.md)
Owner's answers to the handoff's questions: the RE2 cast first (after Leon); BioRand's voices for an NPC that looks like a BioRand character (later); the player's look may follow later (the converter takes any target skeleton).

**The model page** (`Cvx.Formats/Models`). `bhSetEneMdl` (room.c) and the player loader (dread.c) read blocks `[u32 size][data]` until -1 (0 = an empty entry):
- `SKIN` (`SkinData`): one entry per object in tree order (objects with eval flag 0x20000000 have none). Flag 0: nothing. Flag 3 (bit 2): this object holds skinned mesh `mno`, then a count and one word per vertex whose top byte is the object (tree order) that moves it (`npSkinConvSub` uses only the top byte; the low 24 bits are probably blend data for `npCalcSkin`). Flag 1: this object moves mesh `mno`'s vertices. The holder gets eval bit 0x80000000 and is drawn at the enemy's position only (`EasyMultiDrawTreeCnk`), its vertices skinned each frame.
- `MASK`: the face animation (`bhInitMask`, face.c). Without one the face code does nothing (`bhControlMask`, `bhSetMask` and `bhSetLip` all check).
- MLB model (`NjModel`, `bhMlbBinRealize`): "MDL" + status (0x80 = chunk models), header size 16, object count, texture list and object array offsets (relative to the end of the header). The objects are an array of `NJS_CNK_OBJECT` (0x34 bytes, links relocated in place). Chunk models (`NJS_CNK_MODEL`, 0x18 bytes): the radius is stored negative (`bhCnkBinRealize` relocates the lists only then). Layout of the game's files: texture list, `NJS_TEXNAME` array, names, pad to 4; objects; per object in index order: model (4-aligned), zeros to a 64-byte boundary of the page, vertex list, polygon list; then zeros to a multiple of 64 and 64 more (the rooms') or none (SYSTEM.AFS's player pages).
- Vertex list: type 51 (`njCnkCvVnPs2`, the PS2's own): index offset, count, 56 bytes of padding (a 64-byte `HDR_PS`), then per vertex the position and normal as float4 (w = 1); end `FF 00 00 00`. Polygon list: standard Ninja chunks (`pCnkFuncTbl`): material 19 (diffuse + ambient, `7F7F7FFF`), texture 8 (id in the low 12 bits, head bits 0x34, filter 1), strips 66 (UVs 0-1023 across the texture). Strip flags seen: 0x02 (opaque), 0x0A (alpha), 0x1A (alpha, double-sided: hair). Winding: a strip's even triangles (a, b, c) face along cross(b - a, c - a), odd ones the other way, and a negative length flips them all (Steve: 2,789 of 2,798 agree with the vertex normals).
- **Round trip**: every enemy model page of the 205 rooms (759 pages, 1,489 models, every strip chunk re-encoded) and SYSTEM.AFS 10-14 write back byte for byte (`ModelTests`). Player pages also hold basic models (status 0x13, kept as bytes).

**Textures** (`TextureEntry`, `bhSetMemPvpTexture` in ps2_texture.c): records `[code][size][24 bytes "CDCD..."][data]` until -1 (some entries stop without one; RM_5680's last is cut off by the end of the room). A `TIM2` record is one texture, in the order of the model's texture names: `TIM2_PICTUREHEADER_EX` (0xD0 bytes, padded to 0x100; the global index at +8), the image, the palette. The characters' textures are all 8-bit (image type 5), with 256 RGBA colours in CSM1 order (entries 8-15 and 16-23 of each 32 swapped) and alpha 0-0x80; sizes 32-256, square. **The global index identifies a texture in the game's cache** (`SearchNumber`, ps2_NaTextureFunction.c): textures with the same index are shared, so converted textures use indexes from 0x3A00000 (every room texture is below: `ConvertedTexturesHaveGlobalIndexesOfTheirOwn`).

**The NPC skeleton**: types 91-98 all have it, 27 objects in the same tree; the bone lengths differ a little per character. Y is up, the characters face -Z with their right side toward +X, are about 17-18 units tall, and are bound in a T-pose (arms along ±X, every angle 0). Steve's (RM_0030 entry 2) bind positions:

| Object | Parent | Bind position | What |
|---|---|---|---|
| 0 | - | 0, 0, 0 | root |
| 1 | 0 | 0, 11.04, -0.16 | hips (holds the skinned body: 1,470 vertices) |
| 2 | 1 | 0, 11.04, -0.16 | waist (spine bend) |
| 3 | 2 | 0, 12.90, 0.00 | chest |
| 4 | 3 | 0, 15.00, 0.40 | neck |
| 5 | 4 | 0, 15.45, 0.42 | head (975 of the body's vertices: the face) |
| 6-10 | 5 | about 0, 15.9-16.2, -0.4 | face parts (rigid models, no SKIN): mouth and jaw pieces, the eyes (8 left, 10 right) |
| 11 / 15 | 3 | ±0.21, 12.90, 0.00 | right / left collarbone |
| 12 / 16 | 11 / 15 | ±1.70, 14.05, 0.30 | shoulder (upper arm) |
| 13 / 17 | 12 / 16 | ±3.98, 14.00, 0.30 | elbow (forearm; it also moves the hand's vertices) |
| 14 / 18 | 13 / 17 | ±6.40, 13.98, 0.30 | wrist (no SKIN entry) |
| 19 / 23 | 1 | ±0.80, 9.55, -0.10 | hip (thigh) |
| 20 / 24 | 19 / 23 | ±0.70, 6.00, 0.25 | knee (shin) |
| 21 / 25 | 20 / 24 | ±0.60, 1.40, 0.80 | ankle (foot) |
| 22 / 26 | 21 / 25 | ±0.66, 0.21, -0.26 | toes |

`dump-model RM_0030:2` prints it (any page: `RM_xxxx:<enemy>`, `SYSTEM.AFS:<entry>` or a file; `--vertices`, and `--png <dir>` for the textures). Claire's page in the rooms (91) has a second model: 31 objects, vertex type 34, no polygons (not drawn as a mesh).

**RE2's PLD** (`Pld`, ported from biohazard-utils): a directory of 4 chunks (EDD animations, EMR skeleton, MD1 mesh, TIM texture). EMR: 15 joints, offsets from the parent (int16), lists of children. MD1: 19 parts (15 + 4 one-vertex dummies), each triangles + quads with a normal index and UV per corner, a texture page and a CLUT. Y is down, the characters face +X with their right side toward -Z, arms hanging down, about 3,000 units tall. Joints: 0 torso (the root, at the waist), 1 pelvis, 2-4 right thigh/shin/foot, 5-7 left, 8 head (from the neck), 9-11 right upper arm/forearm/hand, 12-14 left. Leon, Ada and Hunk: an 8-bit TIM of 512x256 with 4 CLUTs of 256; the parts use page 0 (pixels 0-127) with CLUT 0 and page 1 (128-255) with CLUT 1 (UVs 0-127 within a page). The TIM's image block length is wrong in BioRand's files (the size is used instead). `dump-pld <file> [--vertices] [--png <dir>]`. `classic.zip` has 36 RE2 characters (26 pld0, 10 pld1).

**The converter** (`Npcs/CharacterConverter`): the target's 27 objects are kept (positions, tree, eval flags, minus the hide flag where a part goes); no SKIN and no MASK. Each RE2 part becomes one rigid chunk model (type 51 vertices, one strip per triangle or quad, grouped by texture) on the object it rides: torso -> chest (3, placed from the waist 2), pelvis -> 1, head -> 5 (placed from the neck 4), arms -> 12-14 / 16-18, legs -> 19-21 / 23-25. Axes: (x, y, z) -> (-z, -y, -x) (a rotation, not a mirror), scaled by the ratio of the leg lengths (hip to ankle). Upper arms, forearms, thighs and shins are turned (the shortest turn) so each bone points where CV's does (arms down -> T-pose); hands take their forearm's turn. Textures: one 256x256 8-bit picture per (page, CLUT) used, with the page on the left (its last column repeated) and PS1 colour 0 transparent; the TIM2 head comes from a 256x256 texture of the target. Leon: model 27.6 KB and textures 134 KB (Chris's: 171 KB and 345 KB). Rendered from the converted data (`render-model <RM_xxxx>:<enemy> | <file> --out <png>`: the bind pose from the front, side and back, back faces culled) next to Chris's own model: Leon, Ada and Hunk stand on the ground at Chris's height, face the right way (R.P.D. reads on the front) and have every part in place.

**Test mod**: `make-look-mod --pld <PLD> [--slot RM_7220:1] [--skeleton <RM_xxxx>:<enemy>] [--dump <dir>] --out <dir>` puts a converted character into an NPC entry (default: Chris in RM_7220's opening scene) and writes a mod folder. Not for seeds: Chris stays Chris. `extract-afs <archive> <entry> --out <f>` and `dump-enemies --out <dir>` (each enemy's page, motion and texture entries as files) were added for samples.

- In the game (owner, 2026-10-21): the Leon test mod (`RECVX_MOD=rando\look-leon`, `RECVX_WARP=7,22`, new game) loads and Leon plays RM_7220's opening scene in Chris's place: the engine takes a page with no SKIN and no MASK and the converted textures.
- Owner: "looked great" in motion. The only thing they saw was that the test mod changes Chris's cutscene model while a warped new game plays Claire; their rule for the player look (later): **a character's cutscene model (91 Claire, 92 Chris) always matches their in-game model (SYSTEM.AFS)**, never one without the other.

**Textures, corrected after a survey of all 95 characters** (the 36 of `classic.zip` and the RE2-form characters of `dinocrisis`, `modern`, `reboot`, `remakes`, `silenthill` and `spinoffs`): all convert (15 joints; 15 or 19 parts; 8-bit 512x256 TIMs with 4 CLUTs), stand 16-19 units tall, and render with clean textures. The CLUT ids in the parts are not to be trusted: some TIMs name CLUT y 0 instead of 480, and Ben, Brad, Marvin, Kendo and others give page 0 CLUT 1 and the like. Measured (mean colour step between neighbouring pixels, every character, every page with every CLUT), **page N looks right only with CLUT N**, which is also how BioRand's converter writes the ids; so the converter picks the CLUT by page (one texture per page used: 2, or 3 for a few).

**In seeds** (2026-10-21, owner's request: "move on"):
- `CharacterLibrary` (Npcs) finds the characters in the BioRand folder (`data/re2/pld0|pld1/<id>/PL0x.PLD` in the datapacks or loose; 95 in the owner's copy), like `MusicLibrary`. Ids are the folders (`leon.re2`); names "Leon (RE2)" (`MusicLibrary.DisplayName` knows the games: RE3 Remake, RE6, Village, Outbreak, Revelations, Dino Crisis...).
- Setting `NpcCharacters` (ids; default empty: seeds and share codes are unchanged, and with none picked the shuffle is the old derangement on the same RNG, a test checks it). With some picked, every NPC takes a different look from the pool (the other NPCs + the picked characters, the pool shuffled, each NPC the first look left that is not their own). A BioRand look is converted onto the skeleton of the NPC who takes it (their own look entry) with texture global indexes 0x3A00000 + 16 per NPC (two NPCs share some rooms); that NPC keeps their own voice for now (BioRand's voices are the owner's choice, next). Characters the folder lacks or that do not read are left out and listed (`MissingCharacters`).
- Spoiler: "Alfred looks like Leon (RE2) from BioRand, voice unchanged", and the ones left out. CLI: `generate --npcs --npc-characters leon.re2,ada.re2 [--biorand <dir>]`. App: the NPCs pane has a "BioRand characters" row (a chip per character, buttons RE2 cast / All / None, the left-out ones in a warning, the BioRand folder row); the sidebar says "Shuffled + N BioRand characters".
- Seed 31 with five picked: Alfred as Leon, Wesker as Hunk, 40 rooms changed, none kept (generation 26 s, writing 58 s).
- Owner's seed (2026-10-21): "working", but Irons (as Rodrigo, RM_0000) had the tops of his sleeves standing up above his vest like shoulder pads once the scene lowered his arms.

**Skeleton fit** (the fix): Irons is wide, and his parts were hung from the target's narrower shoulders, so lowered arms swung through his torso. The player motions in SYSTEM.AFS (60) have position keys on object 0 only (type 0x20003, the rest turn), so bone lengths come from the model: the converter now moves the target's joints to the character's proportions (`CharacterConverter.Fit`): neck, shoulders and hips where the character's are from the pelvis, each arm and leg bone the character's length along the target's direction, then the body moved so the ankles stay at the target's height; collarbones, toes and the face objects keep their offsets; all object scales 1. Scale: the neck as high above the ankles as the target's (`Stature`), so a child (Lott, Sherry) is as tall as the NPC rather than blown up by short legs (by leg length, Lott stood 20.6). The torso's joint is RE2's root placed from the pelvis: RE3's Nemesis has his root 539 units below his pelvis (floating torso until this). All 95: 17.2-18.7 tall, feet at -0.3-1.2. `render-model --arms-down` (the arms turned 75 degrees down) shows Irons before (pads) and after (clean), and Leon unchanged.
- Not verified in the game: the fitted skeletons in scenes (the hands reach where the motions put them for the target's proportions, so things held or touched may be a little off for very different builds).

### Biorand++: BioRand's window, with CV:X (2026-10-21, owner's request)
Owner's decisions: a 1:1 clone of BioRand's app, called **Biorand++**; RE 1-3 fully working on BioRand's own engine; fork BioRand's WPF window (not a rebuild); the CV:X tile in BioRand's layout with this project's extras. It replaces the Avalonia window (design 1b), which is removed.
- **BioRand's source** is a submodule, `external/biorand` (biorand/classic v3.3.2, `79229a24`, with `biohazard-utils` and `Ps2IsoTools`). `external/Directory.Build.props` keeps this repository's build settings (warnings as errors, implicit usings) away from it; `external/Directory.Build.targets` turns off what the engine's project does at build time for BioRand's own release: building `biorandmod` (C++, `biorand.dll`) into its data folder and copying the data folder to the output. The engine reads its data at run time from the player's BioRand folder through `BIORAND_DATA` (`BaseRandomiser.CreateDataManager`): custom content (`%AppData%\biorand`) first when enabled, then `<BioRand>\datapacks\*`, then `<BioRand>\data`. Nothing of BioRand's data is in the repository or the program. The `.gitignore` rule for the BioRand release folder is now `/biorand/` (it also matched `external/biorand`).
- **The window** (`src/Biorand.App`, WPF, `net10.0-windows`, `cvx-rando.exe`): BioRand's `biorand/` files, each with a note of where it came from; its pictures and icon are linked from the submodule. Changes marked "Biorand++": title and version line; links (this project's GitHub, "Based on BioRand", its documentation; BioRand's site, Discord, GitHub and X are credited on the Info page with its CONTRIBUTORS.md, read from BioRand's folder); What's New is Biorand++'s own changelog (embedded); no update check; settings in `%AppData%\biorandpp` (`BIORANDPP_SETTINGS` for tests), so a BioRand install keeps its own; on .NET a URL or log opens through the shell (`OpenShell`). First start: BioRand's settings (`%AppData%\biorand\settings.json`: the RE 1-3 folders, executables, volumes, tweaks) and the earlier window's `cvx-rando.json` (game and BioRand folders, the CV:X settings as a share code) are taken over; recvx-pc next to the program is found.
- **Settings page**: RE 1-3 as BioRand; CV:X is recvx-pc's folder (choose recvx.exe; checked with `GameFolder`: exe and data), not an ISO; a BioRand folder box (data and datapacks; found by itself next to or above the program or a game). RE 1-3 need its data: without it their tiles say so and open the settings.
- **The CV:X tile** (`MainWindow.Cvx.cs`, the seam: where BioRand's code would use `BaseRandomiser`/`RandoConfig` for game 3 it calls in here; `GetRandomizer(3)` is null, BioRand's `ReCvRandomiser` is not used). The seed box holds the share code (Paste finds a `CVX1-` code; QR code drawn at BioRand's size; the box keeps BioRand's width and scrolls). Mapping: Randomize Doors = `RandomDoors`, Number of Segments 1-4, Segment Size 1-8, + Character (Claire/Chris); Randomize Items = `RandomItems`, Hidden Key Items, + Include Chris's half; Custom item pool = `ItemMode.Custom`, the four probability sliders (0-31) = the ratios, Ammo average quantity with 16 steps on this tile = `AmmoQuantity` 1-16, the weapon list = `Weapons` (all ticked = all); Random inventory = `RandomInventory`, Weapon 1/2 (the same five choices), + Side pack from the start; Randomize Enemies = `RandomEnemies`, Difficulty, + No Nosferatu poison; Randomize Music = `RandomMusic`, the list = Code: Veronica and the BioRand folder's games (`MusicGames`); Randomize NPCs = `RandomNpcs`, the list = BioRand's characters (`NpcCharacters`; right-click RE2 cast/All/None); settings' Randomize title voice = `RandomTitleCall`, Door Skip (RE CVX) = recvx.ini `skip_doors=1` when a seed is made. Hidden on this tile: player and cutscene boxes, enemy skins, enemy placements and per-enemy sliders, documents, alternative routes, segmented placement, prioritise cutscenes. Picks a share code has that this computer's BioRand folder lacks are kept (the code stays the same). Generate = the earlier window's (`Generator.Generate` + `WriteMod`, `DeleteOtherSeeds`, `ClearRandomSaves`, `GameFolder.Play`, the game-running and replace checks); Start CV:X runs recvx.exe; Spoiler Log; Play normal game.
- **Licences**: AFSLib and Reloaded.Memory (GPLv3) are used only by BioRand's CV path, so they are kept out of the program (`ExcludeAssets="runtime"`); OpenSoftware.DgmlBuilder (Apache-2.0, BioRand's play graphs) is in, its licence in `THIRD_PARTY_NOTICES_DGMLBUILDER.txt`; QRCoder, NVorbis, Ps2IsoTools are MIT. The Avalonia, Skia, ANGLE, Geist and icon notices are gone. Published exe: 63.2 MB.
- **Checked** (`BIORANDPP_SHOTS`, pictures of every tile from the published exe): RE 1-3 list BioRand's characters, enemies, enemy skins, music and NPCs from the BioRand folder; the owner's settings came over (RE 1-3 from BioRand, CV:X from `cvx-rando.json`); the CV:X tile gives back exactly the share code it shows (`cvx_roundtrip.txt`: same). One loss: the earlier window allowed item ratios up to 32, BioRand's sliders stop at 31.
- **Not verified**: generating from the window (RE 1-3 write into the games' folders; CV:X into recvx-pc's), and the look beside BioRand 3.3.2.
- **Owner's test (2026-10-21)**: Biorand++ generated and played; two issues. (1) The reskinned NPCs' hands were turned wrong in scenes: the game's NPCs move their hand vertices with the forearm (objects 13/17; the wrists 14/18 have no SKIN entry and no vertices in every NPC: Steve, Rodrigo, Wesker, Chris checked with `dump-model`), so the motions never pose the wrists, and a hand hung on one turned at random. The converter now puts each hand on its forearm object, placed at the wrist (a forearm's model holds both parts; `Map` groups parts by object). (2) The CV:X tile's character list had a horizontal scroll bar: BioRand's three list columns are equal thirds and the hidden Enemy Skins column kept its third; on CV:X it now has no width (Music and NPCs share the room). The "Select RE2 Cast" item is now in BioRand's own list menu (Unselect All, Select All, Random): a menu on the control was never reached, the list's own wins.

### CV:X enemies like BioRand's: the per-enemy sliders (2026-10-21, owner's request)
Owner's choices: the sliders and difficulty first, random placements next (spawn points derived from the room data, then tested with a warp sweep); the seven tested kinds plus whichever others work.
- Setting `EnemyRatios` (kind name -> 0-7, BioRand's slider range). Empty (the default, and what the window stores when every slider sits at its default): the difficulty's own mix as before, so old seeds and share codes are unchanged. With ratios, as in BioRand: a kind's weight is its ratio (0 = never) and the difficulty only sets how many of a kind a room may hold (`max`). `EnemyKind.RatioIn`: a kind left out has 4, an untested one 0.
- `enemies.json`: each kind has a `colour` (the window's bar) and may be `untested`: never placed unless the player raises its slider. Added untested (donors copy, and alone at 7 each fills about 35 rooms that rebuild and fit, Tyrant 17; `UntestedKindsFitTheirRooms`): Moth (6, RM_6030#3), Spotter (11, RM_7081#0, RM_7110#0), Albinoid infant (21, RM_3120#0), Anatomist zombie (26, RM_0110#4), Tyrant T-078 (19, RM_1122#1, RM_3230#1; BioRand's own CV list had it). Left out: bosses (Gulp Worm, Alexia, Nosferatu, Steve, the giant Black Widow), water creatures (fish, adult Albinoid), event-only enemies (ants, tentacles, the ceiling trap).
- Biorand++'s CV:X tile shows BioRand's enemy list (a slider per kind, its menu: all none, all max, random) and its bar; Random enemy placements stays hidden until the next step. Spoiler: "mix Zombie 4/7, ..."; CLI `--enemy-ratios Zombie=4,Hunter=7`.
- Not verified in the game: the five untested kinds (raise one alone, generate, and warp into one of its rooms).
- Owner's request: Cerberus is now "Zombie Dog" (the name the later games use); `enemies.json` `aliases` keeps "Cerberus" so ratios saved under the old name still count.

### The courtyard gate and the Sp Alloy Emblem (2026-10-21, owner's report)
- Seed 300378: the Sp Alloy Emblem was in RM_0000 (the first room); at the courtyard gate, using it said "There's no need to use it now". The gate (a script door in RM_0031 to RM_0100) opens when flag 1:106 is clear, the player is in its area and the item used is 0x56; but the courtyard is RM_0031 only once item flag 4 is set (RM_0010 and RM_0050 run `room_case_no` by `ck 07:4`; a `ck` with value 1 is true while the flag is clear, and a false check ends the `if`). Flag 4 is the pickup in RM_0080 area 2 (the Padlock Key's spot). Before that pickup the courtyard is RM_0030, which has no gate handler. The game's own order always gives that pickup before the emblem (RM_0090); the seed did not.
- Fix (owner's choice, `RoomTweaks.TieCourtyardToEmblem`): the pickup holding the emblem takes flag 4 (its `ck_item`/`set_ck_item`, `[op][x][flag u16][area][x]`), RM_0080's takes the emblem spot's flag, and other rooms' checks of that flag follow it to 4 (`Items/PickupFlags`: every `ck_item`/`set_ck_item` and group-7 `ck`/`set` of the original rooms, read once per archive). Holding the emblem is what switches the courtyard (and RM_0010's graveyard, RM_0020/RM_0021) in every seed. Seed 300378 regenerated: RM_0000 area 7 flag 120 -> 4, RM_0080 area 2 flag 4 -> 120, same placements. Test `TheCourtyardGateFollowsTheEmblem` (6 seeds). The old note in RoomTweaks had the direction of RM_0030/RM_0031 backwards; corrected.
- Wider finding: many rooms read other rooms' pickup flags as story progress (e.g. RM_0080 reads flags 12 and 21, RM_0090 flag 1, RM_1100 flags 10 and 13); the item logic does not model them, so other places may strand a player the same way. Not audited yet.
- Not verified in the game: a new seed's gate (the owner's current run keeps its files: picking up the item at the Padlock Key's spot in RM_0080, then coming back, switches the courtyard).

### Random enemy placements (2026-10-22, owner's request)
BioRand's "Random enemy placements" box on the CV:X tile: Allow enemies in save rooms, Average number of enemies per room (0-15: average 1-16), Rooms with enemies (0-7 sevenths). Settings `RandomEnemyPlacements` (off by default: old seeds and share codes unchanged; placements draw from their own random stream, "placements"), `EnemyQuantity` 2, `EnemyRooms` 5, `EnemiesInSaveRooms`. CLI `--enemy-placements --enemy-quantity --enemy-rooms --enemies-in-save-rooms`.
- Where enemies can start (`Enemies/EnemySpots`, CLI `enemy-spots --png` draws it): BioRand only recorded positions for 7 CV rooms (`data/recv/enemy.json`), so the places come from the room. Walls are `walp` as hitchk.c `bhCheckWall` reads them: types 0-1 boxes, 2-3 circles, 4-5 right triangles (x,z), (x+w,z), (x,z+d) (`bhCheckInnerTriangle`), 6 stairs and slopes, 7 pits (they only stop what is below their top: ignored); height 0 is the room's (`ROM_WORK.h`), a negative height never meets anything. The floor: a flood fill on a 2-unit grid at the height of each entrance and of the room's enemies in every version (and BioRand's recorded places), through gaps of 2 units; a fill that leaves the walls keeps only the footstep areas (`flrp` type 1). Kept: fills holding an entrance, and fills stairs or a slope join to one (a fill only an enemy stands in may be a pit). Places: 10 units from doors, 30 from entrances (20 when the room has none that far), each with the room it has to walls (4.5, 5.5 or 6.5); an enemy starts where its `radius` (`enemies.json`, the game's `ar`: zombie 3.8, spider 6...) plus 0.5 fits. 1 unit is about 10 cm. Of 205 rooms, 22 have no place (small rooms, boss arenas). Floors only stairs lead to are found only when an enemy (in any version) or BioRand's list stands on them.
- Which rooms: those the enemy swap reads, not `keepPositions`, no boss or other special enemy (types 1-40 that are not replaceable, bats aside), save rooms only when allowed, and a room with cutscene characters (types 91+) only when it had enemies to change. 88 of the 165 mapped rooms. Shuffled; the first `EnemyRooms`/7 of those with places get enemies, the rest none (as BioRand).
- How (`EnemyRandomizer.Populate`): one kind per room by the sliders; 1 to 2 x average - 1 of it, at most the kind's `max` at the difficulty and 16, spread 10 units apart (then 5, then any). Entries are never removed or reordered (the scripts name enemies by their index, which is their work number `wrk_no`; checked for every room): the room's own changeable enemies move and take the kind; new ones are added at the end (`RdxEnemyRewriter.Rebuild`, which `Replace` now wraps: byte-identical to the old `Replace` over 1297 room and donor cases). Enemies a room loses are counted as killed: `set 03:flag` first in its start procedure, so its own `set_ck_enemy` hides them; only those whose death flag nothing else reads (`ck`/`set` group 3 anywhere is story). Those that cannot go stay and move. Ones that go take the kind too, so the room's enemy sounds can follow.
- Death flags (`Enemies/EnemyFlags`): `ed_flg`, 1024 bits in the save; `set_ck_enemy [22][enemy][flag]` at the room's start hides an enemy whose flag is set (it reads 9 bits: flags below 512), `ck_death [0D][enemy][flag]` every frame sets it when the enemy dies. The game uses 320 flags (0-357, 873); 193 below 512 are free. Each new enemy gets its own, shared by the versions of its room (the k-th added enemy of RM_003x has the same flag in RM_0030 and RM_0031); when they run out they are reused round the given ones, as BioRand does (the spoiler says so). Added: `set_ck_enemy` first in procedure 0 and `ck_death` first in procedure 1 (procedures are found through the script's offset table, which is rebuilt, as the start opcodes already were).
- Memory: the rebuilt room must leave 64 KB plus 32 KB per added enemy of the room pool (the loader then takes 80 bytes per model object and 12 KB per enemy type's motions); the writer still checks every room expands in place. Added enemies are large (each has its own model page): a room that does not fit loses added enemies one by one.
- Faster generation for every seed: the rooms are read in parallel (the enemy step, the pickup flags, the enemy scan), and the scans run once (`Lazy`: several threads asking at once each ran a full scan). Seed 12345: 32 s -> 12 s without placements, 21 s with.
- Tests: `RandomPlacementsStandOnTheFloor` (places on the derived floor with room for the kind, indexes kept, flags new, unused by the game and one room number each, gone ones marked first, the biggest rooms expand in place, share codes unchanged when off), `SpotsMatchBioRandsRecordedPlaces` (BioRand's played positions come within 4 units of a place), `SameSeedSameEnemies` with placements.
- Not verified in the game: all of it. To test: generate with placements (e.g. average 5, rooms 6/7) and walk rooms from the spoiler ("new places, n more", "n gone", "no enemies"): enemies stand on the floor and not in walls, none is on top of the player at a door, killed ones stay dead after leaving and coming back, gone ones never appear, rooms with many enemies load.

### Cross-room pickup flags (2026-10-22, audit after the courtyard gate)
Every room script's use of an item flag (`it_flg`, group 7) that a pickup sets, in the original rooms (`Items/PickupFlags`): 980 uses, of which 519 are in a room other than the pickup's; leaving out flags only scripts use and pickups the map does not randomize, about 95 remain. The game marks a pickup taken with its own flag, and other rooms use that flag to mean "the player has the item that lay there". Once items move, that is no longer true. The courtyard gate was one case; the others:

| Pickup (flag) | Item there | Read by | What it gates | Risk with random items | Now |
|---|---|---|---|---|---|
| RM_0020/21 #2 (1) | Briefcase | RM_0090 start | a cutscene (events 5-7) once taken, with story flags 120 and 126 clear; the map's Sp Alloy Emblem there needs Hawk Emblem + Briefcase | the emblem's event waits for that spot, not the Briefcase | follows the Briefcase |
| RM_0080 #2 (4) | Extinguisher | RM_0010, RM_0050 | the courtyard's version (the gate) | gate unusable | tied to the Sp Alloy Emblem (earlier fix) |
| RM_1000 #5 (9), RM_3120 #5 (24), RM_2070 area 26 (18) | Navy, Army, Air Force Proof | RM_1001, RM_2040 | events once all three are taken; they set 1:181, which many rooms' music and sounds read, and RM_1001 changes its area 1 | the events wait for the three spots | follow the proofs (the Air Force Proof is RM_2070's scripted pickup, map entry 254, whose area 26 sets 18) |
| RM_1040 #9 (10), RM_1110 #5 (13) | Earthenware Vase, King Ant Object | RM_1100; 13 alone: RM_2000, RM_2020, RM_2030, RM_2031 | an event with five enemies in RM_1100; the versions of the stage 2 rooms those doors load (and RM_2000's door to RM_2010) | versions out of step with the King Ant Object | follow the items |
| RM_1050 #5 (11) | Steering Wheel | RM_1020 | until taken, area 10 is the door to RM_1000; once taken, a cutscene and area 10 becomes an event (RM_1000 is also reached from RM_1120) | the change comes with that spot | follows the Steering Wheel |
| RM_1060 #3 (12), RM_3040 #4 (21) | Eagle Plate (two of three) | RM_0080 | which enemies appear | none | follow the plates (the three Eagle Plates are one item type: the n-th original plate's checks follow the n-th placed plate, by global id) |
| RM_3140 #11 (25) | Skeleton Picture | RM_3030 (its area 0 event sets 25; its event 7 reads it: enemies come back), RM_3130 (a model) | not clear; the map's RM_3040 pickups need the picture | not clear | follows the picture (the set too) |
| RM_4020 #12 (27) | Biohazard Card | RM_1120, RM_3020, RM_3080 (versions), RM_3091 (event) | the card reader rooms' versions; the map's RM_3080 doors need the card | the readers' version comes with that spot | follows the card |
| RM_7020 #2 (33) | Turn Table Key | RM_7070 (event), RM_7081, RM_7110 (models), RM_7181 | the turntable event | none found: the placer never places the key (below) | unchanged |
| RM_7040 #3 (34), RM_7050 #3 (35), RM_7100 #18 (40) | Clement Alpha, Clement Sigma, Eagle Plate | RM_7081 (all three: two enemies gone, models), RM_7140 (areas 8-10), RM_7191 | the map's RM_7080 door to RM_9090 needs the three | out of step | follow the items |
| RM_7120 #1 (107) | Chem Storage Key | RM_7040 | areas 1 and 3 swap once taken | out of step | follows the key |
| RM_0090 #4 (5), RM_0110 #12 (6), RM_9080 #7 (43) | Sp Alloy Emblem, Glass Eye, Serum | RM_5510 (checks, clears); RM_6080, RM_9190 | stage resets and events | out of step | follow the items |
| RM_3040 #1, #10 (204, 205); RM_3070 #7, #5, #3 (212-214) | First Aid Spray, Ink Ribbon; arrows, bullets, Green Herb | RM_7020, RM_7150 (Claire's later visit): once she took the earlier pickup, the later one (300, 303; 339-341) is marked taken | the later pickups are separate spots for the logic | a key item in RM_7150 #8 (normal priority), or any of them with Hidden key items, vanishes | the `set`s are blanked: each keeps its own item |
| RM_8020 #8 (252), RM_8000 #4 (247) | Shotgun Shells | RM_4020 marks 252 taken when it starts; RM_4000 marks 247 while you are in it (cleared on leaving) | | a key item at RM_8020 #8 vanishes when Claire passes RM_4020 | the `set`s are blanked |
| RM_2060 #12, #13 (196, 197); RM_3130 #2 (232); RM_3130 #5 (103); RM_6060 #13 (276) | herbs, rounds; Bow Gun; Memo; Sniper Rifle | RM_2070 (models); RM_7180/7181 (the same Bow Gun: one location, shared global id); RM_7180 (a document, never moved); RM_9150 (an object link) | looks only | none | unchanged |
| flags 7, 59-61, 110, 240 | pickups the map does not randomize | | | none | unchanged |

- `Items/StoryFlags.Apply` (after the courtyard swap, which now only swaps the two pickups): a check or set of a key item spot's flag in another room points at the flag of the spot that holds the item now; uses in the spot's own room follow the spot (so they follow the courtyard swap); the courtyard switch itself (flag 4 in RM_0010/RM_0050) keeps meaning the emblem. Pickups that are taken away because of another room are blanked (a `set` in a room that does not list the pickup, or one inside an `if` on another room's pickup flag). The spoiler lists it all under "Story checks".
- 64 of the map's 401 pickups set no item flag at all (the game hands them over in an event: the Padlock Key's examine spot in RM_0080, Chris's Wing Objects, ...), so a check cannot follow an item put there. The placer now puts these 14 keys (and the Sp Alloy Emblem, for the courtyard) only where a pickup flag is, when one is free (`StoryFlags.Rule`, `ItemPlacer.PickLocation`). **This changes the items of most seeds**: a share code made before gives other placements. Over 20 seeds no check is left on an old spot.
- `ItemPatcher.MirrorVariants` no longer counts an unmapped room version as changed when none of its pickups changed (RM_7030 was written unchanged).
- Tests: `StoryChecksFollowTheirItems` (4 seeds, with and without random doors: each moved check reads a flag the item's pickup sets; the 14 keys sit where a flag is), `LaterRoomsKeepTheirPickups`.
- For the owner:
  - The Turn Table Key is never placed: the logic reaches the end without RM_7110's door to RM_7160, so its pickup gets something else and RM_7070's turntable event comes with that pickup, as before. If the turntable is needed to finish, the map lacks that requirement.
  - RM_3030's area 0 event (it marks the Skeleton Picture's pickup taken) and RM_2070's area 26 (assumed to be the Air Force Proof's event) were read from the scripts, not seen in the game.
  - Rooms that mark their own pickups taken when they start (RM_1070 #7, RM_2060 #13, RM_9290 #9, under their own conditions) were not audited.
  - Not verified in the game: any of it. To check: a new seed's spoiler "Story checks" lists which rooms follow which item; get one of those items from its new place and see the room change (for example the stage 2 rooms after the King Ant Object, RM_0090's cutscene after the Briefcase).

### Doors out of the run, and bats at None (2026-10-22, owner's report, seed 428946)
- Random doors, 1 segment, size 1/8 (9 rooms). recvx.log: after RM_0110 -> RM_2060, the player went through RM_2060's door to RM_2050 and no redirect was logged; RM_2050, RM_2030, RM_2040 and RM_2010 followed as in the normal game until a run room came back. RM_2060's door to RM_2050 is a script door (`set_door_call` when area 9 is used); BioRand's door mode data lists it for item mode only (`doors: false`) and blanks the script in door mode, but this randomizer keeps scripts with doors in them (recvx-pc redirects doors in the engine), so the door kept its target and led out of the run.
- Fix (`DoorTable.Entries`): every door of the run's room files that no entry covers and that leads to a room outside the run gets a locked entry: door trigger areas (in the room, or made by `set_etc_atari2` type 0) and script door calls a used trigger area starts (`ck 0A:17 == area` around them). Doors into run rooms are kept doors (the plan leaves those out of its links), and door calls without a trigger area are cutscenes' (the ending's RM_9302 to RM_9360) and stay. Seed 428946 gets two: RM_2060 area 9 to RM_2050, RM_7120 area 3 to RM_7170. A locked script door leads back into its room. Room versions the run does not go through itself now get the run version's entries (before: only versions the data does not list). Test `NoDoorLeadsOutOfTheRun` (the owner's seed and 10 layouts).
- Bats with their slider at None: bats (7) were not a replaceable type, so the game's own bats stayed (RM_0130 in the owner's run). They are now; what takes a bat's place starts at the floor place nearest below it (`EnemySpots`; bats hang at 26-44). Two more causes kept RM_0130's: it allows only difficulty 0 kinds (Bat), so a room whose own kinds are all at None now takes the easiest kinds left; and no kind may hold five at Easy, so a room with more enemies than any kind may hold at the difficulty still changes (it keeps its count). Bats an event moves keep their type (RM_1030 one, RM_4010 five).
- `EnemySpots`: bats, moths and spiders no longer seed floors (they hang, fly or climb: RM_2010's bats at 44 made a floor there), footstep areas seed fills too (a floor only stairs reach: RM_1030's main floor below its landing), stairs join floors only at heights they reach (give or take a step of 3), and over a lower floor a cell is floor only where a footstep area says so (walls do not tell a balcony from the air over a hall: RM_9321). 20 of 205 rooms have no place.
- Old seeds with random enemies change in bat rooms (the enemy step draws differently from the first bat room on).

### Seed 152118: one Wing Object of four (2026-10-22, owner's report)
- Random doors, 1 segment, size 1/8 (7 rooms). recvx.log: the player reached RM_9300 (the end room) twice and stopped there. RM_9300's door to RM_9310 runs on `ck_use_item` 0x42, the Gold Dragonfly: the wingless one combined with four Wing Objects (1, 2, 3 wings, then the Gold Dragonfly). The data lists the door's needs as Gold Dragonfly No Wings, Security Card, Wing Object; the four Wing Object pickups (RM_9130 two, RM_9170, RM_9220) were all in rooms the run did not use, and such a key got one copy.
- Fix: in a run with random doors a key gets as many copies as the whole game has pickups of it (`ItemGraph.GameCopies`, less the ones kept in place), at least one. Test `RunsGetEveryCopyOfAKey` (12 layouts: each placed key in as many pickups as the game has; four Wing Objects). Seed 152118 regenerated: the same doors, four Wing Objects (RM_6080 three, RM_3090 one).
- The owner's save on 152118 cannot be finished; a new seed (or 152118 generated again) is needed.

### Seed 202385: the Gold Key door in its own room (2026-10-22, owner's report)
- Random doors, 1 segment (11 rooms). The Gold Key lay in RM_1070, which holds the Gold Key door (door 3, to RM_1040; this run sends it to RM_0120). RM_1070's start procedure arms that door only if Claire carries the Gold Key as she comes in (`ck_player_item` 0x44, then trigger area 3 becomes the key event and room flag 8 is set; the use check in procedure 1 needs that flag). Picked up inside, the key did nothing until she left and came back. In the normal game the key is never in that room.
- Many rooms arm a lock this way at their start: RM_1020/1021 and RM_1070 (Silver, Gold Key), RM_3080 and RM_3150 (Biohazard Card), RM_3120, RM_3160, RM_3170, RM_7200, RM_7210 (Emblem Card), RM_4020 (Airport Key), RM_6050 (Mining, Machine Room Key, Gas Mask), RM_6060 (Valve Handle), RM_7110 (Turn Table Key), RM_7170 (Door Knob), RM_9050 (Crane Key), RM_9150 (Serum), RM_9320 (Sterile Room Key), and others. Checks in procedure 1 (every frame) or in events are not affected.
- Fix: the placer puts a key elsewhere than in a room (any version) whose start procedure checks that the player carries it, when another pickup is free (`StoryFlags.FlagRule.Avoid`, `ItemPlacer.PickLocation`). Seed 202385 regenerated: the Gold Key in RM_1080, the Silver Key in RM_0010. Test `KeysAvoidRoomsThatArmTheirLockAtTheStart` (8 seeds, with and without random doors). Placements change in seeds where a key would have gone to such a room.
- The owner's current run: leave RM_1070 and come back; the Gold Key then opens the door.


### Seed 985463: into RM_9320 by the open side of its jewel door (2026-10-22, owner's report)
- Random doors, 1 segment, size 1/8 (6 rooms: RM_0010, RM_1080, RM_3150, RM_9320, RM_9300 the end, RM_9310). recvx.log: RM_0010 -> RM_1080 -> RM_3150 -> RM_9320 (entrance 1) -> RM_9300 -> RM_9320 -> RM_9300, then the window closed. RM_9320's door 7 (to RM_9200 in the game) needs Alexander's Pierce, Alfred's Ring and Alexia's Choker from RM_9320's side; the run sent it to RM_3150, whose door 0 (to RM_3110 in the game) led into RM_9320 with no lock. Back out needs the three jewels, Alfred's Ring lies in RM_9310 behind RM_9300's Dragonfly door (Gold Dragonfly No Wings, Security Card, four Wing Objects), and the Pierce, the Dragonfly, the Card and three Wing Objects were in RM_0010, RM_1080 and RM_3150: anything not yet picked up there was out of reach.
- The item logic counts on going back to every room reached, which holds when the planner enters each new block through a door without keys. It took the block's door at random, keyed ones too.
- Fix (`DoorRandomizer`, the tree): the door the player comes into a new block through has no keys; a block whose free doors all need keys is stranded (the segment is tried again without it). Keyed doors still lead out of a block the run reached, where the logic checks their keys. Test `RoomsReachedLeadBackWithTheSameKeys` (the owner's seed and 20 one-segment layouts: with the keys of each step of the key order, every room reached leads back to the start; the closed side of a one-side door counts as open once its open side was reached). It fails on the owner's seed without the fix. Layouts change in seeds where the tree had entered a block by a keyed door.
- The test also found seed 18 (1 segment, size 3/8): RM_9170's kept door into RM_9160 needs the Octa Valve Handle (copied from RM_9160's side), but RM_9160's door back also needs RM_9140 (`requiresRoom`), which `SameKeysBothWays` did not copy, so the logic could go in and not out. Fix: kept doors need their rooms from both sides too. Not checked in the game: whether RM_9170's side is open without the handle (then the logic is right only because it is stricter).
- The owner's save on 985463 cannot be finished unless everything had been picked up before RM_9320; a new seed is needed.

### Door runs start as a new game does (2026-10-23, owner's request)
- Claire's door runs started in RM_0010 (`start=0,1,0,0,0`), the cell left out: a shortcut for testing. Now a door run is a new game as the game starts it (the opening, Claire waking in the cell, Rodrigo letting her out) and the randomized doors begin at RM_0000's one door, the first of the game (owner's choice). RM_0000 is one room, the cell and Rodrigo's guard post (dump-room: one door area, to RM_001x). Chris's runs keep their start (`start=7,24,0,0,1`, where he arrives).
- `DoorRandomizer.StartRoom` is `1000`; the cell is skipped for Chris only. `rando.ini` gets no `start=` for Claire. RM_0000's pickups (Rodrigo's gift for the Hemostatic among them) are in the run. The side pack's flags are set in RM_0000 only (RM_0010's copies were for the old start).
- The wider key test (`RoomsReachedLeadBackWithTheSameKeys`, now 60 layouts) found another one-way trap in the new layouts: seed 7's tree left RM_1070 by its keyed door to RM_3160, and RM_3160 was later reached without that key from the other side (loops and kept doors join the tree's branches), into RM_1070, whose other ways back needed keys (RM_9160's valve door). Any link keyed on one side is one-way from the other. Using no keyed door in random links at all left most seeds without a layout, so instead every door layout is checked for traps (`Playthrough.FindTrap`: with the start keys and each step of the key order, every room reached from the run's start or a one-way door's far side leads back to it) and one with a trap is not used (`Generator.MakeDoors` tries the next; its "no layout" message now says why the last attempt failed). Doors the game itself makes one-way (story doors, and doors whose far side is locked for good, as around RM_6100, where the normal game switches to Chris) are not walked through by the check; their far sides are starts of their own. Tests: `RoomsReachedLeadBackWithTheSameKeys` checks the generated layouts with `FindTrap`; `FindTrapSeesADoorKeyedOnOneSide` builds the owner's seed in small (an open door in, a keyed door back) and expects the trap, and none once the way back is open.
- Every seed with random doors gets a new layout. Tests tied to the old start or to one seed's old layout now check the new start (`TheStartRoomLeadsToASaveRoom`: the cell's door leads to a save room; no `start=` for Claire) or take the first layout from that seed on that has the rooms they need.

### Biorand++ holding 5-6 GB while idle (2026-10-23, owner's report)
- After a CV:X seed the window sat at about 5 GB doing nothing. Measured with the app's own generate path (Generate then WriteMod, seed 796072 with music and NPCs): 73 MB before, 3.6 GB after Generate, 6.0 GB after WriteMod, almost all of it large byte arrays (the archives: RDX_LNK.AFS 590 MB, BGM1/2.AFS about 480 MB, the sound and voice packs, and their copies while written). A heap dump of the idle process found 2.8 MB live: nothing keeps the seed, it is garbage. An idle window allocates too little for the GC to run; a full compacting collection frees the objects but keeps the memory committed for reuse (6.0 GB); an aggressive one returns it (62 MB).
- Fix: `MainWindow.ReleaseSeedMemory` runs one aggressive, LOH-compacting collection when a seed is done (or failed). The peak while generating is unchanged; writing the archives to disk as they are built would lower it.

### NPCs with BioRand looks speak with that character's voice (2026-10-23, owner's report)
- An NPC that took a BioRand character's look kept their own CV:X voice (deferred on 2026-10-21: "BioRand's voices ... later"). Owner's choices: BioRand's way, and characters with few clips repeat them.
- BioRand's voices: `data/voice/<id>/*.ogg` in the datapacks (classic.zip: leon.re2 149 clips, ada.re2 118, claire.re2 170, sherry.re2 64, misty.re2 5...), the same ids as the looks. File names carry BioRand's tags (`VoiceRandomiser.ProcessSample`): `_kind` (radio, doom...) and `-name` conditions (said with that character there, `-noname` without).
- `CharacterLibrary.VoiceClips(id)` lists a character's clips with their length (loose `data/voice/<id>` first, then the datapacks; a `$variant` id uses the base folder). `NpcRandomizer.Voices`: an NPC with a BioRand look gets, for each line they speak, one of the four clips closest in length, the least used first, from that character's clips of no kind, the plain ones (no conditions) when there are at least 10. `ClipAsAdx` writes it as one-shot mono ADX at the clip's rate (the game's lines are all mono 48 kHz; the ADX header gives the rate, as the title call showed), as loud as the line it replaces unless the clip's peaks stop the gain (then quieter, never clipped). The subtitles keep the game's words.
- Spoiler: "Rodrigo looks and sounds like Sherry (RE2) (BioRand's model and voice clips)"; all 311 NPC lines change when every NPC has a new look. Tests: `VoiceClipsBecomeTheGamesVoiceLines`; the NPC test now checks every line of a BioRand-look NPC is that character's clip. The BioRand tests also find `build_pc\biorand` (they were skipped here before).

### Claire and Chris as BioRand characters, stage 1: cutscene models and voices (2026-10-23, owner's request)
- Owner's choices: BioRand's characters only; BioRand's own "Change Player Character" box on the CV:X tile ("Claire becomes:", "Chris becomes:", Random or a character); each their own pick (Random never gives both the same); voice lines first (pain and death sounds stay CV:X's, they are in the sound banks).
- Settings `RandomPlayers`, `ClaireLook`, `ChrisLook` (empty = random; share codes unchanged when off). `Generator.PlayerLooks` resolves them (`Rng "players"`, from the BioRand folder's characters). CLI `--players [--claire-look <id>] [--chris-look <id>]`.
- `data/npcs.json` `players`: Claire (type 91, look RM_0000:0) and Chris (type 92, look RM_7220:1), entries of the same kind as the NPCs' (one human skeleton). `NpcRandomizer` takes them as two more characters with fixed looks (`players`; `shuffle: false` keeps the NPCs when only the players change), so their cutscene entries are converted like the NPCs' and their lines become clips.
- Their lines: the data had none (only the five NPCs'). A one-off match of every VOICE1/2 entry's loudness envelope (10 ms) against BioRand's clips of the CV:X cast (claire.recv 261, chris.recv 137 and the five NPCs', 1,226 clips, lengths within 20%, lag up to 0.3 s) agreed with the existing NPC labels for 161 of 164 lines at correlation >= 0.9 and 0.1 ahead of any other speaker; at that bar 116 lines are Claire's (79) or Chris's (37) and were added. Their other lines keep CV:X's voice.
- Seed 5, Claire as Leon (RE2), Chris as Ada (RE2): 56 rooms changed, 116 lines; in the game (warp to RM_7220) the opening close-up of Chris shows Ada. Tests `ClaireAndChrisTakeBioRandLooksAndVoices`, `PlayerLooksLeaveShareCodesAlone`.
- Next (stage 2): the gameplay model. SYSTEM.AFS entries 10 (Claire), 14 (her other costume), 11 (Chris): `dread.c` reads [size][model pages (the body: 22 objects with a SKIN mesh; a 22-object collision model; small pieces: the ponytail chain, ...)][size][motions][size][...] then each textured page's TIM2 data. The converter makes 27-object NPC pages; the player needs the 22-object tree, its textures in the file, and the extra pieces hidden.

### Claire and Chris as BioRand characters, stage 2: the gameplay model (2026-10-23)
- The player files (`SYSTEM.AFS` 10 Claire, 14 her other costume, 11 Chris), as `bhReadPlayerData` reads them: `[size][model page][size][motions][size][a third block]`, then one texture entry per model with a texture list, `[size | 0x80000000: starts at the next 32 bytes][entry]`, then the end word. Claire's page: a SKIN block, the body (22 objects, one skinned mesh on object 1, textures cl01-cl1_02), two basic models, a 22-object model with no polygons (16-34 vertices per object, status 0xAD), and her extra pieces (a skinned 4-object chain, a 1-object piece, two `egg` pieces); Chris has only the first four. Each textured model has its own copy of the textures (Claire: 7 entries, Chris 3, the other costume 4; the basic models' are among them, which is why the entries are taken while they look like ones).
- The player skeleton (`CharacterRig.Player`): 1 pelvis, 2 waist, 3 chest, 4 neck, 5 head; 6-9 right collar, upper arm, forearm, wrist; 10-13 left; 14-17 right hip, knee, ankle, toes; 18-21 left. `CharacterConverter` takes a rig (the NPCs' is `CharacterRig.Npc`, unchanged); the hands ride the forearms as on the NPCs.
- `PlayerModel.Replace`: the body converted (rigid parts, no SKIN: its SKIN block is dropped), its texture entry the converted pictures (global indexes `0x3A00200 + 16 * entry`), every other block, the motions and the other texture entries byte for byte, each entry re-aligned where it lands. Leon (RE2): page 53,872 -> 40,048 bytes, body textures 235,940 -> 133,728. The seed's folder gets `SYSTEM.AFS` with the changed entries (`NpcRandomizer.WritePlayers`).
- recvx-pc: `bhReadPlayerData` set a model's skin pointer only when a SKIN block comes before it and never cleared it, so a body without one would keep the last player file's; under `TARGET_PC` it now clears them first (dread.c).
- In the game (seed 5, Claire as Leon, warp to RM_0010, a scripted walk and aim): Leon stands, walks into the next room and raises his arm with his parts together; no crash in 150 s. Not checked: weapons in his hands, every motion (stairs, pushing, hurt, death, cutscenes that use the gameplay model), Chris's and the other costume's files in the game, and whether Claire's extra pieces (the chain, `egg`) show on someone else (none showed in the screenshots).
- Test `PlayerFilesGetTheBodyAndKeepTheRest`.
- Owner's play-test (seed 394207, Claire as Ark Thompson (Survivor)): Ark had a ponytail, and his body kept flashing to Claire's look. Both from pieces of Claire's file left as they were:
  - The flashing is the blink: player.c (`PlyEyeTab`) gives the body model 1's and model 2's texture lists (`txp[1]`, `txp[2]`) for a few frames at random; models 1 and 2 are the two basic models, and their texture entries are full copies of Claire's textures with the eyes half and fully closed. Their entries now get the converted pictures too (the same global indexes: a blink changes nothing).
  - The ponytail is model 4, which the game hangs on her head as a linked object with its own motion (player.c `lkmtab`, `mdlver` 4, `obwp[2]`; objitm.c `bhCalcHair`). Its objects now have the hide flag; the object still runs. Models 6 and 7 (`egg`) stay as they are: enemy 27 uses them (en27.c, `plp->mdl[6]`, `plp->skp[7]`).
  - Checked in the game with the owner's seed: no frame with Claire's colours in the save room (the run before had two in twelve), no ponytail from behind.

### Claire and Chris: more of their lines, and their pain and death sounds (2026-10-25, owner's request)
- More lines: a scan of every room's `voice_on` (event.c `bhVoiceOn`: `[op][target: 0 player, 1 enemy entry +6, 2 object][entry u16]...`) found 240 entries played on Claire's or Chris's cutscene models (types 91, 92) or at the player. The scripts' target is where a line is heard, not who says it (Steve's lines often play on Claire's model), so a line is theirs when it is played there and its audio's best match is theirs: correlation >= 0.7, not below any NPC's or the other player's, of VOICE1/2 the one matching them best (the disc of a room is not in the data). Checked against the known lines: 94 agree, 3 do not (Steve's; the known label stays). 35 new: Claire 97, Chris 54 (were 79 and 37). Of the 113 left, nearly all are NPC lines by their audio; most of what Claire and Chris say is in the movies.
- Pain and death: `CallPlayerVoice` (sdfunc.c) plays sounds 1024-1027 from bank 4, which is the player's own sound pack, CORE_00n.SPQ in MULTSPQ1/2.AFS (key 0xFFF0 | player; `RequestPlayerVoiceSoundBank`). Its one shot bank (`[hd off, hd size, bd off, bd size]`, ps2_sg_sd.c `sdBankDownload`) has 4 programs, one 32 kHz sample each: program 1 (sound 1025, `bhCPM0_die`) the death (Claire 1.43 s), the others hurt (`bhCPM0_damage`, and enemies' grabs). BioRand's `data/hurt/<id>/0-3.ogg|wav` (0-2 hurt, 3 death; 16-bit PCM WAVs for some) go to programs 0, 2, 3 and 1, mono, resampled to 22,050 Hz, a hurt at most 1.2 s and the death 3 s, as loud as the sample each replaces (the peak limit as for voices), encoded as SPU ADPCM (`Vag.Encode`: per block the filter and shift with the least error, decoded as pc_iop_tsnddrv.c does; a silent first block and the end flag on the last, as the game's samples). `PlayerSounds.Replace` rewrites the BD and the VAG entries' offsets and rates; `EnemySounds.Write` puts the packs into MULTSPQ1/2.AFS with the rooms'.
- In the game (Claire as Leon, RM_0060, standing among zombies, `RECVX_SEDUMP`): at each bite the mix holds Leon's hurt sample (waveform correlation 0.81-0.95, 12 times) and once his death sample (0.94); Claire's own samples nowhere above 0.27.
- Tests `VagEncodesWhatTheSpuDecodes` (a falling noisy tone: > 20 dB), `PlayerPainSoundsBecomeTheCharactersHurtClips`.

### The title screen of a randomized game (2026-10-26, owner's request)
Like BioRand's RE 1-3 (its title says BIORAND, with its version and the seed's QR code), a seed's CV:X title screen is BioRand's CV:X art with Biorand++'s version.
- Where: the title is ADV.AFS entry 2 (adv.c: `RequestAdvInsideFileEx(2, 0)`, then `DisplayTitleBg` draws resource 0 as one 1024x512 quad, of which the screen shows the top left 640x448, and two sprites of resource 2). Resource 0 is a PVPL palette (a Dreamcast leftover: `TransPvpData` only reads its bank number) and a TIM2 at 0x460-0x80960: 1024x512, 8-bit (type 5), 256 colours, full alpha. The game's logo is part of the picture.
- BioRand's unreleased CV:X code did this already (`ReCvRandomiser.ReplaceBackground`, `BiorandBgCreator`): its art `data/recv/bg.png` (1024x512, 256 colours: the game's title art with BIORAND under the logo), the version at (16, 16) and a QR code of the seed (error correction M, 2 pixels a module, white modules over the art) at the screen's top right, 16 pixels in.
- `Title/TitleScreen`: the same with Biorand++'s caption ("Biorand++ 0.9.94 (b6da6e9)", the program's version and commit, in a 5x7 dot font at 3 pixels a dot, white with a black shadow). The QR code is left out (owner, after seeing it in the game): a share code holds every setting, so its QR code had about 90 modules a side, 180 of the screen's 640 pixels, over the art and into the caption. The art's palette is kept (white and black added when missing, the least used colours giving way to the nearest kept one); the TIM2 keeps its header and global index. `MusicWriter.WriteTitleCall` writes ADV.AFS with the title screen and the title call when there is either. The art comes from the BioRand folder the music library found (data first, then the datapacks); without it the title stays the game's. Spoiler: "Title screen: BioRand's, with ...".
- Tests `TheTitleScreenCarriesTheVersion` (the entry outside the picture unchanged, the art kept, the caption, the top right untouched), `SeedsGetTheTitleScreenWithBioRandsArt`.
- Seen in the game by the owner with the QR code (too big), then without it ("looks way better"). HD texture packs replace textures by their content, so a pack's title picture does not apply to a seed's.

### Seed 86426: RM_3150's gate cut the way back to the start (2026-10-26, owner's report)
- Random doors, Claire, 1 segment, size 1/8 (11 rooms). The Gold Dragonfly needs four Wing Objects: RM_0000, RM_1080, RM_3090, RM_9200. A replay of the owner's recording (`RECVX_REPLAY`, with a temporary log of `sys->itm` at each room load, since removed) stayed in sync: the Security Card was taken in RM_0000 but not its Wing Object, the other three were combined (Gold Dragonfly 3 Wings), and RM_0000 was never reached again.
- The only way back to RM_1080 and RM_0000 is RM_3150's door 1. RM_3150 is two halves joined through a gate (wall 27, x 42-45, and the object the start procedure places with `common_ctr`): door 1 and entrance 0 on one side, doors 0 and 2 and entrances 1 and 2 on the other. Its procedure 1 event on area 2 (while flags 1:0x20 and 1:0xE5 are clear) sets both flags, closes the gate (`set_wall_atari 27`) and makes areas 0 and 2 doors; it opens again only with flag 1:0xD8 (set in RM_3100, outside this run) and the panel (areas 3 and 4). The owner's first visit crossed from door 1 and set off the event; the second visit (from RM_3160) could not reach door 1.
- BioRand's door mode data blanks this event (`doorRando` nop 0x723E2-0x724F6), but the range also holds area 1's door call, so `RoomTweaks` skips it (recvx-pc needs script door calls for its redirects). An audit of the 35 skipped ranges (top-level statements by the `if`/`else` block lengths) found no other one with a standalone event that changes walls or floors.
- Fix: with random doors, RM_3150 sets flag 1:0xE5 when it starts (`RoomTweaks`, like BioRand's story flags). The event needs it clear, so it never runs; flag 0x20 stays clear, so the gate stays open (the start procedure's E5-set branch) and areas 0 and 2 are doors (while 0xD8 is clear). No other room reads or sets 0xE5 or 0x20. Tests `RandomDoorsKeepRm3150sGateOpen`, `Rm3150sGateEventNeedsFlagE5Clear`; `DoorSeedRoomsAreValid` checks the flag opcode lands at the start of procedure 0.
- Layouts and items do not change; only RM_3150's script does. The owner's run on 86426 cannot be finished (the gate stays closed in that game: 0x20 is already set); a new game on the regenerated seed has the same rooms and items.
- Not verified in the game: crossing RM_3150 both ways after the fix, and the gate's look (the start procedure places its object by the same flags).

### Claire's and Chris's unlabelled lines take the new voice too (2026-10-30, owner's report)
- Seed 992583 (Claire as Brad (RE3), Chris as Wesker (RE1)): in the cell, between Rodrigo's lines (Daniela's clips, 32 kHz in recvx.log) and Claire's one labelled line (Brad's, 22 kHz), some lines played in Claire's own CV:X voice (48 kHz). A scan of every room's `voice_on` (target 0 the player, 1 a room enemy entry, 2 an object): RM_0000 plays VOICE1 entries 19, 22, 230 and 232 on Claire's model (type 91), and none had a speaker in `npcs.json`, so they kept her voice. 673 `voice_on` in all; 0.2 s entries are silent placeholders (the room's line is on the other disc's VOICE file).
- Owner's choices: in-room scenes only (the movies keep their voices: there they are mixed with the music and effects); label by audio and context, with a page of the changed lines to check by ear.
- A pitch and spectrum speaker model trained on the labelled lines did not work (Steve's and Alfred's voices are as high as Claire's: leave-one-out put 40 of Claire's 97 lines on Steve). An exact match does: BioRand's `<name>.recv` clips are cut from this game's own lines. Each unlabelled line the rooms play (176 of more than 0.3 s) against the 1,226 clips of the seven: log spectrogram (16 kHz, 24 bands 100-7000 Hz, 10 ms, silence trimmed, each band centred), the best mean correlation over lags with at least 60% of the shorter overlapping, clips 0.4-2.5 times the line's length. On a random quarter of the labelled lines (132), a best match of 0.9 or more agreed with the label 85 times in 88; at 0.85 and 0.1 ahead of any other speaker, 89 in 93.
- Added at that bar: 45 lines, Claire 17 (among them the four in the cell: clairecv1, 2, 75, 76), Chris 4, Steve 13, Wesker 8, Alexia 2, Rodrigo 1. Lines over about 8 s match badly (several speakers in one entry) and stay unlabelled, as do lines with no clear match.
- The owner listened to all of them on a local page (each line as 16 kHz WAV; the game's audio, so not published) and reported: VOICE2 417 (13 s, matched Alexia) has Claire speaking first, then Alexia; owner's call: Claire's. The four old labels the match disagreed with were wrong, as the match said: VOICE1 39 is Steve's (was Rodrigo), 127 Claire's (was Alfred); VOICE2 445 (was Steve) and 503 (was Wesker) are Chris's. Claire's known lines 97 -> 116, Chris's 54 -> 60.
- Test: `ClaireAndChrisTakeBioRandLooksAndVoices` also checks that the cell's lines (VOICE1 16, 19, 22, 230, 232) and the owner's corrections are theirs.
- Not verified in the game yet: the cell scene with a new seed.

### Seed 992583: RM_9290's dead pickup and its gated back corner (2026-10-30, owner's report)
- Random doors, Claire, 1 segment, size 1/8 (13 rooms). The run reached RM_0000, RM_1080, RM_3170, RM_9290, RM_9320 and RM_4010; the ways on were RM_4010's door to RM_3160 (needs the Control Lever) and RM_9320's jewel door (Alexander's Pierce, Alfred's Ring, Alexia's Choker). A replay of the owner's recording with a temporary log of `sys->itm` at each room load (removed after) stayed in sync: in RM_9290 Claire swapped the Crystal for the Choker (item 12) and took the Bow Gun (item 13), and never had the Control Lever, the Gold Dragonfly No Wings or the Security Card, which the seed put in RM_9290's items 9, 10 and 11.
- Item 9 (the Control Lever's spot) never shows: RM_9290's start procedure sets its pickup flag (item flag 431) before `set_ck_item`, outside any `if`. An audit of every room's start procedure (`set` of group 7 on a flag a `set_ck_item` there uses) found about twenty such pickups; BioRand's map leaves all of them out except RM_9290 #9 and RM_1070 #7 (item flag 169, the same pattern). RM_6100's area 4 holds two pickups and the room takes only the one the map does not list. Both removed from `rdt.json`; `verify-data` now reports such a pickup (`DataValidator.TakenAtStart`).
- Items 9, 10 and 11 lie upstairs behind a gate (walls 18 and 19, x 41-50 at z 110-112, in the fence around that corner). The room's pickup handler (`cmpb` on `sys->sb_id`, which `hitchk.c` sets to the item of a pickup being taken, and `ItemUse` to an item used) opens it when item 13's pickup is taken: `set_wall_atari` 18 and 19 with 1, and `bhWalAtariSet` clears a wall's enable bit for a nonzero value (so `set_wall_atari id, 1` removes a wall, as read for RM_3150). The map lists that `cmpb` (0x1D27E6) as item 13's offset, so whatever item 13 holds opens the gate; item 13 needs the Crystal. Items 10 and 11 had no needs in the map; they now need the Crystal too.
- Not a softlock in this seed by itself: taking the Bow Gun from item 13 opened the gate, and the owner did not go back up. The lever was the softlock.
- Tests `PickupsTheirRoomTakesAtTheStartAreNotInTheMap` (both rooms found, RM_6100 not, no map pickup taken at the start, RM_9290's 10 and 11 need what 13 needs); `RoomDataMatchesTheGameExceptKnownDoorIssues` counts two items fewer.
- Every seed whose items would have used those two spots changes. The owner's run on 992583 cannot be finished (the lever never shows). Regenerated: the same rooms and doors, the key items placed anew (the Control Lever in RM_1080; RM_9290's 10 and 11 hold Eagle Plates, after the Crystal in RM_3200).
- Not verified in the game: the gate opening when item 13 is taken.

### Seed 461621: the Wing Object in Claire's cell (2026-10-30, owner's report)
- Random doors, Claire, 1 segment (9 rooms). The Gold Dragonfly needs four Wing Objects: RM_0000, RM_0010, RM_0031, RM_0070. A replay of the owner's recording with the temporary inventory log (removed after) stayed in sync: Claire took the Security Card in RM_0000 but not its Wing Object, then the wings in RM_0010 and RM_0070 (combined: Gold Dragonfly 2 Wings), and not RM_0031's (two short visits; that pickup is there while item flag 127 is clear).
- RM_0000's Wing Object was at item 8 (Green Herb, area 8, x 49-55, z 64-70): inside Claire's cell, behind the bars at x 35 (walls 2 and 24) and the cell door (wall 18). The opening scene lets her out; the room's start procedure switches walls 18, 19 and 23 off only while map flag 1:0x62 is set, which only RM_0050 and RM_0100 set. So once she leaves RM_0000, the cell stays shut, and its pickup is lost in a run without those rooms. Seed 86426 had a Wing Object there too, also missed (and RM_3150's gate cut the way back anyway).
- Fix: the map gives item 8 `priority: low`, so keys go elsewhere when another spot is free (as BioRand's other low spots; non-key items may still go there). Test `KeysStayOutOfClairesCell` (12 seeds, with and without random doors, half with hidden key items: no key there).
- Found on the way, not changed: RM_0031's pickup handler marks area 4's pickup taken only when the item taken is Handgun Bullets (`cmpb` at 0x1C2D0E, not in the map's offsets), so whatever replaces them is there again on every visit.
- The owner's run on 461621 cannot be finished (the fourth wing is in the shut cell). Not verified in the game: the cell staying shut on a later visit (read from the script).
