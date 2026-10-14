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
| Stack | C# **.NET 10** WinForms, published as a **self-contained single-file exe** (players install nothing) |
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
  src/Cvx.Rando.App/    WinForms UI (exe name: cvx-rando.exe)
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
  - No trimming (WinForms doesn't support it). The size is about 50–70 MB.
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
- **Contract for the randomizer app (Phase 9):** write `mod=rando\<seed>` and `saves=saves_rando\<seed>`; before the first change keep the player's own value as `saves_normal=<value>` (`*` when there was no `saves=`), and don't overwrite an existing `saves_normal=`. "Play normal game" = remove `mod=`, restore `saves=` from `saves_normal=` (remove it for `*`), remove `saves_normal=`.
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

### Phase 7: done (2026-10-14), not committed yet
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

## Risks / open items for the executing agent
- **Don't copy BioRand's CV item writes as-is**: it writes `type` and relies on its executable patch; we write `id` (see Key facts). Treat every BioRand CV write as unverified until it is checked against the game's C code.
- **Room memory budget** when copying enemy assets: measure it, don't guess.
- **Script length table:** CV's interpreter has no opcode-length table (each handler advances `bhScePtr`). Rely on BioRand's `ScdReader.ReadScriptCv` and cross-check lengths against `event.c` handlers if new script patches are needed.
- **Chris's half:** BioRand's `rdt.json` covers it (stage `A` = file stage 9), but CV-specific inventory transfers between halves need checking in `sub1.c`.
- **Possible gap in BioRand's CV logic:** because it was never released, expect bugs. Treat its data as a strong starting point, not as verified.
