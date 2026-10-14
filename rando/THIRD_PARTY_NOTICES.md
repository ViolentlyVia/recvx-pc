# Third-party notices

## BioRand

Parts of this program are ported from BioRand, the classic Resident Evil randomizer:
the room file format (`RdtCv` and its model, motion and texture lists), the PRS
compressor, and (in later versions) the randomization logic and the Code: Veronica
room data in `data/`.

- Source: https://github.com/biorand/classic (tag `v3.3.2`, commit `79229a240edbf1abc620f30da342af37291e0781`)
- Format library: https://github.com/biorand/biohazard-utils (commit `b88e24c977ca5392daffcb6489391ff45fc97430`)
- Code: Veronica X information in BioRand by **kapdap**; RDT information by **re_duke**.

Ported files keep a note of where they came from at the top.

```
MIT License

Copyright (c) 2022-2026 Ted John

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## PRS compression

BioRand's PRS code is itself based on the PRS compressor by Sewer56
(https://github.com/Sewer56/dlang-prs).

## Expand()

`Cvx.Formats/Expand.cs` is a C# version of the game's own decompressor as written for
recvx-pc (C version by Clownacy), used to check that compressed rooms load in the game.
