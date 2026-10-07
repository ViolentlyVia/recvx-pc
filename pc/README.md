# PC port (work in progress)

Goal: build Resident Evil Code: Veronica X as a native 32-bit Windows program from the decompiled
sources, replacing the PS2 platform layer (`src/ps2/veronica/prog/ps2_*.c`, CRI middleware, VU
microcode) with a PC one.

## Approach

- **32-bit x86 first.** Asset loaders patch file offsets into pointer fields in place
  (`binfunc.c`, `door.c`, ...), so pointers must stay 4 bytes.
- **`TARGET_PC` switch.** Every change to upstream sources keeps the original code for the PS2
  build:

  ```c
  #ifdef TARGET_PC
  /* C replacement */
  #else
  /* original MWCC/PS2 code */
  #endif
  ```

- **Sony SDK stand-ins** live in `pc/include` (`sce_pc.h` and one stub per SDK header). They only
  declare what the game needs; nothing from the real SDK is required.
- Dreamcast Katana/Ninja headers come from the `include/recvx-decomp-katana` submodule (copied to
  `build_pc/katana_inc` without its libc headers).

## Status

| Step | State |
|---|---|
| 1. Game logic compiles with MSVC x86 | done: all non-`ps2_*` files in `src/ps2/veronica/prog` |
| 2. Stub platform layer, link, run main loop | next |
| 3. Files / pad / 2D / 3D / audio / movies / saves | later |

VU0 assembly inside game logic was translated by hand (see the `pc/tools/port_*.py` scripts,
which document what each block computes): facial animation (`face_bh.c`), morphing, UV
scrolling and skinning (`njplus.c`), water surface (`objitm.c`) and the LZ decompressor
(`expand.c`).

## Tools

```bash
python pc/tools/check.py            # syntax/type check all game sources, summary of errors
python pc/tools/check.py <files>    # check specific files; logs in build_pc/logs
```

Requires Visual Studio 2022 Build Tools (MSVC x86); the path is in `pc/tools/msvc.py`.
