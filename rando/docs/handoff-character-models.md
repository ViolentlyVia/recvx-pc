# Handoff: other games' characters as NPC looks in Code: Veronica

You are picking up one part of the CVX Randomizer: making the characters BioRand brings from other
Resident Evil games (Leon, Jill, Ada, Hunk, Sherry, Barry...) usable as looks for Code: Veronica's
NPCs. This file is everything the owner and the previous sessions know. Read it all before you
start, then read `PLAN.md` (the project's running log) for anything this file points to.

## 1. The project and its rules

- **CVX Randomizer** (this repo, `ViolentlyVia/CVXRandomizer`, branch `main`): a BioRand-style
  randomizer for **recvx-pc**, the owner's PC port of Resident Evil Code: Veronica X (sibling repo
  `ViolentlyVia/recvx-pc`, branch `pc-port`, built from the recvx-decomp decompilation of the US
  PS2 disc SLUS-20184). C# / .NET 10: `src/Cvx.Formats` (file formats), `src/Cvx.Rando`
  (randomizer), `src/Cvx.Rando.Cli` (`cvx-rando-cli`), `src/Cvx.Rando.App` (the window, Avalonia),
  `tests/Cvx.Tests` (xUnit).
- The randomizer never edits the disc: it writes a **mod folder** (`rando\<seed>` in the game
  folder) whose files replace the game's by name (`RDX_LNK.AFS` for rooms, `BGM?.AFS`,
  `VOICE?.AFS`...), plus `rando.ini` and `rando_doors.txt` that recvx-pc reads (`pc/src/pc_rando.c`).

Rules from the owner (these are not negotiable):

- **Never mention Claude, AI or any assistant** anywhere: commit messages (no `Co-Authored-By`,
  no "Generated with" lines), code comments, docs. This overrides any default you have.
- **Never commit game data or BioRand's files**: no ISO, no `data/` folder of the game, no
  `*.AFS`, `*.RDX`, `*.RDT`, no extracted rooms or models, nothing from BioRand's datapacks
  (PLD, PLW, EMD, TIM, OGG, PNG...). They are copyrighted. Samples the owner gives you go in
  `samples/` (git-ignored) and stay there. Check `git status` before every commit.
- **Commit only when the owner asks; push only when asked.** Commit style: `Rando: <what>` /
  `Docs: <what>` here, `PC: <what>` / `Docs: <what>` in recvx-pc; a short subject and a body that
  explains. Identity: `git -c user.name=ViolentlyVia -c user.email=53280007+ViolentlyVia@users.noreply.github.com commit`.
- In recvx-pc, game code changes go inside `#ifdef TARGET_PC` (or in `pc/` files); no upstream
  line changes. `pickup.md` and `.claude/` there are untracked: never commit them.
- Write code like the code around it (comment density, naming, idiom). Tests for every format
  you parse or write.
- The owner is the only one who can run the game. Say plainly what you verified and what needs
  their check.

## 2. The goal

Phase 1 (this handoff): BioRand's **characters** become extra looks for CV's **human NPCs**, the
ones the NPC shuffle already swaps: Steve, Rodrigo, Alfred, Alexia, Wesker. An NPC keeps its
animations, scenes, voice logic and position; only its model and textures change. Claire and
Chris (the player characters, and their cutscene models 91 and 92) always stay themselves.

Phase 2 (later, not now): BioRand's **enemies** (EMD: Lickers, Hunters, Tyrants...) as enemy
skins. Leave this alone unless the owner asks.

## 3. What already works (build on it)

**The NPC shuffle** (`src/Cvx.Rando/Npcs/NpcRandomizer.cs`, `data/npcs.json`, setting
`RandomNpcs`, app pane "NPCs", CLI `--npcs`): the five NPCs get each other's looks (one new look
each for the seed) and voices. Read PLAN.md, section "NPC looks and voices", for how it was found
out. The facts that matter for you:

| Enemy type | Character | A room with its model (look) |
|---|---|---|
| 91 | Claire (cutscene model) | never swapped |
| 92 | Chris (cutscene model) | never swapped |
| 93 | Steve | RM_0030 entry 2 |
| 94, 96 | Alfred (two model variants) | RM_1020 entry 1 |
| 95 | Alexia (also Alfred in disguise) | RM_2030 entry 1 |
| 97 | Wesker | RM_1002 entry 1 |
| 98 | Rodrigo | RM_0000 entry 1 |

- NPCs are **enemy entries** of the room files, types 56-99, run by `bhSubpl` (eneset.c); the
  room script moves them. Their **motions are not in the rooms** (no motion block for these
  types), and the human cast **shares one skeleton** (eneset.c gives 91-98 the same collision
  joints: 0, 3, 5, 17, 21, 24, 28). Proof: a test mod gave Chris's cutscene model (RM_7220 entry
  1) Steve's model page and textures, and the scene played cleanly with Steve doing Chris's moves.
  So a new look = a **model page and its textures** built for that skeleton.
- `src/Cvx.Formats/Rdx/RdxEnemyLayout.cs` reads how a room's enemies map to its model pages
  (`hed01`), motion entries (`hed02`) and texture groups (`hed04`), following the game's loader
  (room.c `bhSetRDT`, `bhSetEneMdl`, `bhFinishRoom`). Textures are keyed by
  `(type, model version, chunk)` (`TextureKey`): one texture entry per textured model chunk.
- `src/Cvx.Formats/Rdx/RdxEnemyRewriter.cs` replaces enemies' model pages and textures and
  rebuilds the room (used by the enemy and NPC randomizers). `NpcRandomizer` builds an
  `RdxEnemyDonor` with the NPC's own type, version, effect and (empty) motion and a donor's
  model + textures. Your converter only has to produce that model page and those texture
  entries; the rest is done.
- Rooms have a **memory limit**: an expanded room must fit `Expand.RoomPool` minus
  `EnemyRandomizer.MemoryMargin` (64 KB). CV's NPC model pages are about 166-182 KB.
- Rooms are PRS-compressed in `RDX_LNK.AFS` (`RoomArchive`, `Prs`); `RoomArchive.WriteModified`
  writes a mod archive.

**BioRand's music** (`src/Cvx.Rando/Music/MusicLibrary.cs`) is the pattern for using BioRand's
files: the player points the app at their BioRand folder (`AppSettings.BioRandDirectory`, or a
`biorand` folder found next to or above the program); the randomizer reads the datapack zips at
generate time and writes converted data into the seed's folder. Nothing of BioRand ships with
the program. Do the same for characters.

## 4. The target format: CV's NPC model page

Everything below is from the decompiled engine (recvx-pc). Confirm each point in the code before
relying on it.

- A model page is loaded by `bhSetEneMdl` (`src/ps2/veronica/prog/room.c`, around line 559): it
  may start with a **SKIN block** (magic `SKIN`, `SKIN_MAGIC` 0x4E494B53 in `dread.h`), then an
  **MLB** model realized by `bhMlbBinRealize` (`binfunc.c`): a header (status byte at +3, object
  count at +6, header size at +4, texture-list offset at +8, object-list offset at +12), then a
  tree of **`NJS_CNK_OBJECT`** (Sega Ninja, from the Dreamcast version) whose child, sibling and
  model pointers are offsets the loader relocates in place. Models are **chunk models**
  (`bhCnkBinRealize`, status bit 0x80) or basic models (`bhBscBinRealize`). With a SKIN block,
  `npSkinConvert` (`njplus.c`, line 1255) applies vertex weights.
- The player models are the same format: `SYSTEM.AFS` entries 10-14 (entry =
  `ply_id + 10 + costume * 4`: 10 Claire, 11 Chris, 12 Steve and 13 Wesker of the Battle Game,
  14 Claire's other costume), loaded in `dread.c` (line 40 on).
- Chunk drawing: `ps2_NinjaCnk.c` (and the Katana SDK headers under `include/recvx-decomp-katana`)
  define the chunk types: vertex chunks, strip chunks with UVs, material and texture chunks.
- Textures are **TIM2** (PS2): `ps2_loadtim2.c`, `ps2_NaTextureFunction.c`, `ps2_pxlconv.c`.
  Room texture groups are what the room's texture list (`hed04`) points to; check their exact
  layout in room.c (`texhed`, lines 160-320).
- recvx-pc can swap in HD textures by hash (`hd_textures=`, `pc/src/gs/gs_replace.c`); new
  textures will simply not be in that pack, which is fine.

Your first deliverable is a **parser and writer** for this in `Cvx.Formats` (MLB, the object
tree, chunk models, the SKIN block, TIM2 texture entries) that **round-trips** the owner's
sample byte for byte, and a dump that prints the skeleton: each object's position, rotation,
scale, parent and the vertices it holds. From that, write down the skeleton (which object is
the head, torso, upper arm...) in PLAN.md.

## 5. The source format: BioRand's characters

The owner's BioRand folder has `datapacks/*.zip` (classic, modern, reboot, remakes, spinoffs,
silenthill, dinocrisis). Characters are converted by BioRand's community for each classic game:

```
data/<target game>/<slot>/<name>.<source game>/
    PLxx.PLD     the model (skeleton, mesh, texture, animations of the target game)
    PLxxWyy.PLW  weapon variants (ignore)
    face.png     a portrait (could be the app's chip image)
data/<target>/emd/<name>.<game>/  enemies (EMxxx.EMD, .TIM): phase 2
```

- Target games: `re1`, `re2`, `re3`; slots `pld0` (male player slot) and `pld1` (female).
  **Use the RE2 versions** (`data/re2/pld0`, `data/re2/pld1`): they cover the most characters
  (58 + 38 in the owner's copy): Leon, Ada, Hunk, Sherry, Annette, Barry, Jill, Rebecca, Carlos,
  Billy, Krauser, Ethan, Harry (Silent Hill), Regina (Dino Crisis), Wesker of RE1/RE5... and
  CV's own Claire, Chris, Steve, Alfred, Alexia, Rodrigo, Wesker in RE2 form.
- RE2's PLD: a directory of sections: skeleton (EMR: joint offsets, parent tree), mesh (MD1:
  one rigid part per joint, triangles and quads with UVs), texture (TIM: PS1, 4 or 8 bit with
  palettes), animations (EDD/EMR). The parts are **rigid** (one per joint, about 15 joints), at
  PS1 resolution.
- Format references (open source): BioRand's own code, github.com/biorand/classic, and its
  format library, github.com/biorand/biohazard-utils (`IntelOrca.Biohazard`: `PldFile`, `Md1`,
  `Emr`, `Tim`...). Read them; port what you need into `Cvx.Formats` (MIT: keep a note of where
  each ported file came from at the top, as the repo already does, and add it to
  `THIRD_PARTY_NOTICES.md`).
- BioRand also has the classic games' **voices** per character (`data/voice/<name>.<game>/`) and
  hurt sounds (`data/hurt/`). Out of scope for now (the NPC shuffle's voices are CV's own lines).

## 6. The conversion (the hard part)

The converter maps a PS1 character onto CV's NPC skeleton:

1. **Joint map**: each RE2 joint (head, torso, hips, upper/lower arms, hands, upper/lower legs,
   feet) to the CV object it rides on. CV has many more objects (about 29+: fingers, face...);
   unused ones get no geometry.
2. **Placement**: RE2 parts are modelled around their own joint origins in RE2's units and rest
   pose; CV's objects have their own bind pose. Scale (RE2 units to CV units: measure both
   characters' heights), and fit each part to its CV bone (position, and rotation where the rest
   poses differ, e.g. arms down vs a T-pose). Expect to tune this by eye from the owner's
   screenshots.
3. **Mesh**: the parts' triangles and quads become CV chunk-model strips (or triangles), with
   UVs. Rigid parts: either one object per part with no SKIN weights, or a SKIN block giving
   every vertex weight 1 on its bone. Find out which the engine accepts (try the simplest first).
4. **Texture**: the PS1 TIM (4/8 bit, palette, 128x256 or similar) becomes TIM2 texture entries
   in the layout CV's texture groups use, one per textured chunk, with UVs remapped if needed.
5. **Size**: keep the page and textures well under the room memory budget (the NPCs' own pages
   are about 170 KB).

Fallback if mapping onto CV's skeleton is impossible: say so with evidence and propose an
alternative before building one.

## 7. Integration (after one character works)

- Data: extra looks for the NPC shuffle, e.g. `data/npcs.json` gains a list of BioRand characters
  usable as looks (by datapack path), or the app picks them from the folder like the music
  chips do. No new defaults: off unless the player turns it on. Keep share codes compatible
  (new settings with defaults are left out of them; see `Setup/ShareCode.cs`).
- Generate: read the PLD from the player's BioRand folder, convert, hand the result to
  `NpcRandomizer` as donors. Cache nothing in the repo.
- App: an option in the NPCs pane (chips per BioRand character, like the music pane's games).
- Spoiler: "Steve looks like Leon (RE2)".
- Voices: an NPC that looks like a BioRand character keeps the voice the shuffle gives it now
  (ask the owner whether they want BioRand's voices instead).

## 8. Samples to ask the owner for

You cannot extract anything yourself. Ask the owner for these (they run the commands on their
PC); put them in `samples/` (git-ignored) and **never commit them**:

1. A CV room with an NPC: `cvx-rando-cli extract-room RM_0030 --out samples/RM_0030.rdx`
   (Steve, entry 2; expanded room file, about 9 MB). Also RM_1002 (Wesker).
2. CV's player model: `SYSTEM.AFS` entry 10 (Claire, 1,294,440 bytes). There is no CLI command
   for raw AFS entries yet: add one (e.g. `extract-afs <archive> <entry> --out <file>`) and ask
   the owner to run it.
3. BioRand characters from `datapacks/classic.zip`: `data/re2/pld0/leon.re2/` and
   `data/re2/pld1/ada.re2/` (the PLD and face.png), and one from another game, e.g.
   `data/re2/pld0/hunk.re2/`.

Write small CLI commands that dump what you need (`dump-model`, `dump-pld`), so the owner can run
them on other files and paste you the output.

## 9. How the owner tests in the game

The owner runs recvx-pc from `..\recvx-pc\build_pc`. Testing aids (environment variables):

- `RECVX_MOD=<folder>`: use a mod folder (else `mod=` in recvx.ini).
- `RECVX_WARP=<stage>,<room>[,<entrance>[,<case>]]`: a new game starts in that room.
- `RECVX_INPUT=<file>`: scripted button presses (`title+<n> start`, `title+<n> cross`...).
- `RECVX_SHOTS=<seconds>`: screenshots into `build_pc/shots`.
- `recvx.log`: room loads with memory use, `rando:` lines.

**The look test that already worked:** RM_7220 (`RECVX_WARP=7,22`) opens with a scene of Chris
(entry 1, type 92). A mod that gives that entry another model shows the new look in close-up
in the first seconds. For your test mods, write a CLI command that puts a converted BioRand
character into that slot (like `make-mod` does for items), and ask the owner to run the game
with it and send screenshots. Do not ship that slot change: Chris stays Chris in real seeds.

Tests: `dotnet test` from the repo root. Tests marked `[GameDataFact]` need the game's data and
skip without it (they will skip in your session); write plain `[Fact]` tests on synthetic data
and on samples under `samples/` that skip when the sample is missing.

## 10. Deliverables, in order

1. An `extract-afs` command (`samples/` is already git-ignored).
2. CV model page parser/writer with a byte-identical round-trip test and `dump-model`; the
   skeleton written down in PLAN.md.
3. RE2 PLD reader (skeleton, parts, TIM) with `dump-pld`.
4. Converter for one character (Leon onto Steve's slot), a test-mod command, and the owner's
   screenshots of it in RM_7220.
5. Then integration (section 7), and the other characters.

Keep PLAN.md up to date as you go (a section per finding, with evidence: the code you read, the
bytes you saw, what the owner saw in the game). When something does not work, say so and show
what you tried.

## 11. Open questions to ask the owner when you get there

- Which characters matter most (first ones to make look right)?
- Should a BioRand character be able to replace the player's look too (later)?
- BioRand's voices for those characters, or CV's own as now?
