# recvx-pc

A native Windows port of **Resident Evil Code: Veronica X**, recompiled from the
[recvx-decomp](https://github.com/AshfordFamily/recvx-decomp) decompilation. The game's own C
code is built with MSVC as a regular 32-bit Windows program; the PS2 hardware and SDK it was
written for (Graphics Synthesizer, VU0, IOP sound driver, memory cards, disc drive, ...) are
replaced by a PC platform layer. No emulator is involved.

This repository contains **no game data**. You need your own copy of the US PlayStation 2 disc
(**SLUS-20184**) as an `.iso` image.

## Status

The game boots and plays; testing so far covers the opening section (prison, explosion,
graveyard), saving and loading. Report problems with a `last_run_input.txt` recording (see
[pc/README.md](pc/README.md)).

| Feature | State |
|---|---|
| Graphics (OpenGL 3.3, 3x internal resolution by default, exact PS2 blending) | done |
| Widescreen (wider 3D view, 2D kept at 4:3), window size, fullscreen | done |
| Settings app (`recvx-config.exe`): display, folders, key and controller bindings | done |
| PCSX2-format HD texture packs (`.dds`) | done |
| Movies (converted once from the disc with FFmpeg) | done |
| AI-upscaled movies (optional, 4x with Real-ESRGAN on the player's GPU) | done |
| Music and voices (CRI ADX) | done |
| Sound effects with room reverb (Tamsoft sound driver replacement) | done |
| Saving and loading (to a `saves` folder, no memory card screens) | done |
| Keyboard and game controllers (SDL2), with vibration | done |
| Fast-forward (hold Numpad 0) | done |
| Randomizer support (mod folder, `rando.ini`) for the separate `cvx-rando.exe` | done |

## Building

Requirements:

- Windows with **Visual Studio 2022 Build Tools** (the "Desktop development with C++" workload,
  which includes the x86 compiler). The build expects it at
  `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools`; if Visual Studio is
  installed elsewhere (e.g. Community edition), change `VCVARS` in `pc/tools/msvc.py`.
- **Python 3**.

```bash
git clone --recursive https://github.com/ViolentlyVia/recvx-pc.git
cd recvx-pc
python pc/tools/build.py
```

`build.py` downloads SDL2 into `build_pc/deps` on first run and writes `build_pc/recvx.exe` and
`build_pc/recvx-config.exe` (add `--debug` for an unoptimised build, `--clean` to rebuild
everything). If you cloned without `--recursive`, run `git submodule update --init` first.
Python is only needed to build; players need nothing but the programs.

## Setting up the game data

Run `recvx-config.exe` and choose "Set up from disc image..." (it offers this by itself on first
start; dropping the `.iso` on `recvx-config.exe` works too). It copies the disc into a `data`
folder next to the exe and converts the movies, downloading FFmpeg into `tools\ffmpeg` next to
the exe once (about 100 MB). After that the ISO is no longer needed.

`python pc/tools/setup_game.py "C:\path\to\RECVX.iso"` does the same from the command line.
Alternatives:

- Put the `.iso` next to `recvx.exe` (or set `iso=` in `recvx.ini`) to run straight from the
  image; movies are skipped in that mode.
- Keep the extracted data elsewhere and point to it with `data=` in `recvx.ini`.

### Upscaled movies (optional)

"Upscale movies (AI)..." in `recvx-config.exe` enlarges the movies 4x (to 1280x1408) with
Real-ESRGAN's `realesr-animevideov3` model, which runs on the graphics card through Vulkan. It
downloads the upscaler once (about 45 MB, into `tools\realesrgan`), takes about 45 minutes on a
recent GPU (several hours on a slow one), and writes `data\MOVIE_HD`, about 1.6 GB. It works in
batches of 500 frames, so it needs about 5 GB of temporary space at most; it can be stopped and
continued, and finished movies are kept. The game plays the upscaled movies while "Use upscaled
movies" is ticked (`movies_hd=0` in `recvx.ini` turns them off) and the original ones otherwise.

## Running

Start `build_pc/recvx.exe` (or "Save and play" in `recvx-config.exe`). It is a normal Windows
program: no console window (set `RECVX_CONSOLE=1` for one with the log), the window comes back
at its last size and position, the mouse cursor hides while playing, and the game pauses while
its window is not active (can be turned off in the settings app).

Keyboard: arrows = move, Z = Cross, X = Square, C = Circle, V = Triangle, A / S = L1 / R1,
Enter = Start, Backspace = Select, hold Numpad 0 = fast-forward, F11 or Alt+Enter =
fullscreen on/off, F9 = back to the title screen (pauses and asks; F9 again to confirm). The
window title shows the frame rate. Game controllers are picked up automatically; a controller
with motors vibrates like a DualShock 2 (VIBRATION in the title OPTION menu turns it on or off).

Saves are written to `build_pc/saves` (copy the folder to back them up).

### Settings

Run `recvx-config.exe` (next to `recvx.exe`) to change the display settings, folders and
key / controller bindings; it writes `recvx.ini`. "Save and play" starts the game.

### recvx.ini

Optional, next to `recvx.exe`, one `key=value` per line (the settings app writes it too):

| Key | Meaning |
|---|---|
| `data=` | extracted game data folder (default: `data` next to the exe; a relative path is relative to the exe) |
| `iso=` | disc image to run from instead of extracted data |
| `saves=` | save folder (default: `saves` next to the exe; a relative path is relative to the exe, and missing folders are created) |
| `hd_textures=` | folder of a PCSX2 texture replacement pack (`.dds` files) |
| `debug_save=1` | debug: the "Save anywhere" key (F5) opens the typewriter's save screen anywhere in normal play, without an ink ribbon |
| `scale=` | internal resolution multiplier, 1 to 8 (default 3: 1920x1440 at 4:3) |
| `aspect=` | display aspect, e.g. `16:9` or `21:9` (default `4:3`) |
| `window=` | window size, e.g. `1920x1080` (default: 960 pixels high at the chosen aspect) |
| `fullscreen=` | `1` for exclusive fullscreen at the window size, `desktop` for borderless (default `0`) |
| `pause_unfocused=` | `0` keeps the game running while its window is not active (default `1`) |
| `movies_hd=` | `0` plays the original movies even when upscaled ones exist (default `1`) |
| `skip_intro=` | `1` starts at the title screen, without the warning screens, Capcom logo and opening movie (default `0`) |
| `skip_doors=` | `1` lets OK / Attack (Cross or Square) skip the door animations (default `0`) |
| `mod=` | folder whose files replace the data folder's files of the same name (relative to the exe; see [Randomizer](#randomizer)) |
| `saves_normal=` | written by the randomizer: your own `saves=` while a randomized game is active (`*` = none) |
| `window_pos=`, `window_max=` | written by the game: where the window was left, and if it was maximized |
| `key_<action>=` | keyboard key, by its SDL name (`Z`, `Return`, `Left Shift`, `Keypad 0`, `none`) |
| `pad_<action>=` | controller button, by its SDL name (`a`, `b`, `leftshoulder`, `lefttrigger`, `dpup`, `none`) |

Actions: `cross square circle triangle l1 r1 l2 r2 l3 r3 start select up down left right
fastforward fullscreen` (defaults in `pc/include/pc_bindings.h`).

With a widescreen `aspect=`, the 3D view is widened rather than stretched, and menus, text and
movies stay at 4:3 in the middle of the screen. The internal resolution rises horizontally to
match, so `scale=3` at 16:9 renders at 2560x1440.

### Randomizer

The randomizer, `cvx-rando.exe`, is a separate program (the CVXRandomizer project).
"Randomizer..." in `recvx-config.exe` starts it when it is next to the exe. It writes the files
it changes (the room archive `RDX_LNK.AFS`, and the room sound packs `MULTSPQ1.AFS` and
`MULTSPQ2.AFS` when enemies change) to a folder such as `rando\<seed>` and points `mod=`
at it. A file in the `mod=` folder is read instead of the data folder's file with the same path,
so the game data itself is never changed. The randomizer also sets `saves=` to a save folder for
that seed. "Play normal game" (in either program) removes `mod=` and puts your own `saves=` back.

Changes that need code rather than files are read from `rando.ini` in the mod folder (format
in `pc/src/pc_rando.c`):

| Key | Meaning |
|---|---|
| `version=` | format version (1); the game refuses a newer one |
| `seed=` | shown in `recvx.log` and the settings app |
| `pickup_amounts=<id>:<n>,...` | how much a pickup of item `<id>` gives |
| `fix_rifle_stacking=1` | rifle and assault rifle bullets add to ammo you already carry |
| `no_nosferatu_poison=1` | Nosferatu's attack never poisons |
| `start_special_claire=<id>`, `start_items_claire=<id>:<n>,...` | Claire's starting items (the same keys with `chris` for Chris) |
| `bgm_map=<from>:<to>,...` | music tracks swapped when room scripts ask for music |
| `start=<stage>,<room>,<entrance>,<case>` | where a new game starts (random doors start in `RM_0010`) |

With random doors, `rando_doors.txt` in the mod folder lists the doors that lead somewhere new,
one per line: `<stage> <room> <case> <area> <old stage> <old room>  <new stage> <new room>
<new case> <entrance> [1]`; a trailing `1` marks a door that leads nowhere, which the game then
shows as locked ("It's locked.") instead of opening. The game looks a door up where it decides where the door goes (door
areas and script doors), so the room files keep their own door data. `<case>` is the case of the
room file that is loaded, even when the room's script has already set the next room's case
(`room_case_no`) before its door call. When a room's variant
file does not exist, the game loads variant 0 instead of waiting forever.

`recvx.log` lists each room's memory use when it loads (`room rm_SRRC.rdx: ... pool ...`): the
room is expanded in place inside a fixed pool, and the randomizer checks every room it changes
against that pool.

## Sharing with friends

```bash
python pc/tools/make_share.py [--rando <path to cvx-rando.exe>]
```

builds `build_pc/share/recvx-cvx-pc.zip` with just `recvx.exe`, `recvx-config.exe`, `SDL2.dll`
and a README (plus `cvx-rando.exe` and its `cvx-rando-notices.txt` when given or found in
`build_pc`; CVXRandomizer's `tools/publish.ps1` builds and copies them there). It refuses to
include game files; each recipient unzips it, runs `recvx-config.exe` and sets the game up from
their own disc image (no Python needed).

## How it works

The upstream decompiled sources are kept as they are; every PC change is wrapped in
`#ifdef TARGET_PC` so the original PS2 build still works. The PC platform layer lives in
`pc/`:

| Part | Files |
|---|---|
| GS emulation on OpenGL (render targets, CLUTs, texture cache, HD replacement) | `pc/src/gs/` |
| VU0 microcode translated to C | `pc/src/vu/` |
| EE kernel (threads as fibers, semaphores, VBLANK) | `pc_kernel.c` |
| Disc access (extracted folder or ISO, mod folder) | `pc_cdvd.c` |
| Randomizer options (`rando.ini`), `RECVX_WARP` | `pc_rando.c` |
| IOP / SIF RPC, Tamsoft sound driver (SPU ADPCM synth, MIDI sequencer) | `pc_iop.c`, `pc_iop_tsnddrv.c` |
| CRI ADX output, SDL audio mixer | `pc_adx_rna.c`, `pc_audio.c` |
| Movies (pl_mpeg) | `pc_movie.c`, `pc_mpeg.c` |
| Memory cards as a folder, save/load wording | `pc_mc.c`, `pc_text.c` |
| Window, input, logging, testing aids | `pc_platform.c` |

More detail (porting approach, tools, debug environment variables) is in
[pc/README.md](pc/README.md). `recvx.log` next to the exe records warnings and prints a stack
trace on crashes.

## Credits and license

- The decompilation this port is built on: [recvx-decomp](https://github.com/AshfordFamily/recvx-decomp)
  by AshfordFamily and contributors, including the CRI and Katana header submodules.
- [SDL2](https://www.libsdl.org/) (zlib), [pl_mpeg](https://github.com/phoboslab/pl_mpeg) (MIT),
  [xxHash](https://github.com/Cyan4973/xxHash) (BSD-2).

The code is MIT licensed, like upstream. Resident Evil and Code: Veronica are trademarks of
Capcom; this project is not affiliated with or endorsed by Capcom, and it contains none of the
game's assets.
