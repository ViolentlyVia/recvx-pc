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
| Music and voices (CRI ADX) | done |
| Sound effects with room reverb (Tamsoft sound driver replacement) | done |
| Saving and loading (to a `saves` folder, no memory card screens) | done |
| Keyboard and game controllers (SDL2) | done |
| Fast-forward (hold Numpad 0) | done |

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

## Running

Start `build_pc/recvx.exe`.

Keyboard: arrows = move, Z = Cross, X = Square, C = Circle, V = Triangle, A / S = L1 / R1,
Q / W = L2 / R2, Enter = Start, Backspace = Select, hold Numpad 0 = fast-forward, F11 =
fullscreen on/off. Game
controllers are picked up automatically.

Saves are written to `build_pc/saves` (copy the folder to back them up).

### Settings

Run `recvx-config.exe` (next to `recvx.exe`) to change the display settings, folders and
key / controller bindings; it writes `recvx.ini`. "Save and play" starts the game.

### recvx.ini

Optional, next to `recvx.exe`, one `key=value` per line (the settings app writes it too):

| Key | Meaning |
|---|---|
| `data=` | extracted game data folder (default: `data` next to the exe) |
| `iso=` | disc image to run from instead of extracted data |
| `saves=` | save folder (default: `saves` next to the exe) |
| `hd_textures=` | folder of a PCSX2 texture replacement pack (`.dds` files) |
| `scale=` | internal resolution multiplier, 1 to 8 (default 3: 1920x1440 at 4:3) |
| `aspect=` | display aspect, e.g. `16:9` or `21:9` (default `4:3`) |
| `window=` | window size, e.g. `1920x1080` (default: 960 pixels high at the chosen aspect) |
| `fullscreen=` | `1` for exclusive fullscreen at the window size, `desktop` for borderless (default `0`) |
| `key_<action>=` | keyboard key, by its SDL name (`Z`, `Return`, `Left Shift`, `Keypad 0`, `none`) |
| `pad_<action>=` | controller button, by its SDL name (`a`, `b`, `leftshoulder`, `lefttrigger`, `dpup`, `none`) |

Actions: `cross square circle triangle l1 r1 l2 r2 l3 r3 start select up down left right
fastforward fullscreen` (defaults in `pc/include/pc_bindings.h`).

With a widescreen `aspect=`, the 3D view is widened rather than stretched, and menus, text and
movies stay at 4:3 in the middle of the screen. The internal resolution rises horizontally to
match, so `scale=3` at 16:9 renders at 2560x1440.

## Sharing with friends

```bash
python pc/tools/make_share.py
```

builds `build_pc/share/recvx-cvx-pc.zip` with just the program, the setup scripts and a README.
It refuses to include game files; each recipient runs the setup with their own disc image.

## How it works

The upstream decompiled sources are kept as they are; every PC change is wrapped in
`#ifdef TARGET_PC` so the original PS2 build still works. The PC platform layer lives in
`pc/`:

| Part | Files |
|---|---|
| GS emulation on OpenGL (render targets, CLUTs, texture cache, HD replacement) | `pc/src/gs/` |
| VU0 microcode translated to C | `pc/src/vu/` |
| EE kernel (threads as fibers, semaphores, VBLANK) | `pc_kernel.c` |
| Disc access (extracted folder or ISO) | `pc_cdvd.c` |
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
