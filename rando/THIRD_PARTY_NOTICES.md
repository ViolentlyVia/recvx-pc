# Third-party notices

## BioRand

Biorand++ is BioRand, the classic Resident Evil randomizer, with this project's Code:
Veronica X randomizer added:

- the window (`src/Biorand.App`) is BioRand's (`biorand/`), changed where marked "Biorand++";
- Resident Evil 1, 2 and 3 are randomized by BioRand's engine, built from its source
  (`external/biorand`, a git submodule: `IntelOrca.Biohazard.BioRand`, `biohazard-utils`,
  `Ps2IsoTools`); its data is read from the player's BioRand folder, not shipped;
- parts of the CV:X randomizer are ported from BioRand: the room file format (`RdtCv` and
  its model, motion and texture lists), the RE2 player model format (`PldFile`, `Emr`,
  `Md1`, `TimFile`: reading BioRand's characters), the PRS compressor, the randomization
  logic and the Code: Veronica room data in `data/`.

- Source: https://github.com/biorand/classic (tag `v3.3.2`, commit `79229a240edbf1abc620f30da342af37291e0781`)
- Format library: https://github.com/biorand/biohazard-utils (commit `b88e24c977ca5392daffcb6489391ff45fc97430`)
- Code: Veronica X information in BioRand by **kapdap**; RDT information by **re_duke**.

Ported files keep a note of where they came from at the top. BioRand's pictures (the game
tiles and icons) and its program icon come from the submodule.

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

## Libraries

`cvx-rando.exe` (Biorand++) is built with these (MIT licence: the text above applies to each,
with its own copyright line), on .NET and WPF (MIT, Microsoft):

- QRCoder 1.6.0 (https://github.com/codebude/QRCoder), the seed's QR code: Copyright (c)
  2013-2018 Raffael Herrmann
- NVorbis 0.10.5 (https://github.com/NVorbis/NVorbis), which reads the OGG music of BioRand's
  datapacks: Copyright (c) 2020 Andrew Ward
- Ps2IsoTools (https://github.com/IntelOrca/Ps2IsoTools, branch netstandard2), part of
  BioRand's engine: by Vi Ness, MIT

OpenSoftware.DgmlBuilder 2.1.0 (https://github.com/merijndejonge/DgmlBuilder), which BioRand's
engine uses for its play graphs, is by Merijn de Jonge under the Apache License 2.0; its text is
in `THIRD_PARTY_NOTICES_DGMLBUILDER.txt` (appended to `cvx-rando-notices.txt`).

BioRand's engine also refers to AFSLib and Reloaded.Memory (GPLv3) for its own Code: Veronica
path; Biorand++ does not use that path, and neither library is part of the program.

## Music from other games

The program has no music of its own. With BioRand installed, it reads the OGG tracks of
BioRand's datapacks from the player's BioRand folder and converts the chosen ones into the
seed's folder on the player's computer; nothing of them is part of this program.
