# Biorand++

## 0.9.94
- CV:X: BioRand's "Change Player Character": Claire and Chris become BioRand characters (Random,
  or one you pick for each): their cutscene and gameplay models, their lines and their pain
  and death sounds. Converted players no longer blink into Claire or keep her ponytail.
- CV:X: NPCs that take a BioRand character's look speak with that character's voice (BioRand's
  clips; the subtitles keep the game's words).

## 0.9.93
- CV:X random doors with Claire start as a new game does: the opening, Claire waking in the
  cell and Rodrigo letting her out. The cell's door, the first of the game, is the first random
  door. Chris's runs keep their start. Every random-door seed gets a new layout.
- CV:X random doors: no layout where a door lets you into rooms you cannot get back out of with
  the keys you have (a door locked on one side only).
- Biorand++ gives back the memory a seed used once it is written (it stayed at about 5 GB).

## 0.9.92
- Biorand++ now ships with recvx-pc and has its version (0.9.92); it lives in recvx-pc's
  rando folder.
- CV:X random doors: the rooms the run reaches always lead back with the keys you have (seed
  985463 could walk into RM_9320 by the open side of its jewel door and not get out).
- CV:X: doors of the run's rooms that would lead out of the run are locked; bats count for the
  enemy sliders; a key is not placed in a room that only arms its lock when you enter.

## 0.2.0
- BioRand's window (3.3.2), with Resident Evil 1, 2 and 3 on BioRand's own randomizer, and
  Resident Evil Code: Veronica X on the CVX Randomizer for recvx-pc.
- CV:X: items (shuffle or a custom mix), starting inventory, enemies, random doors (one
  character, 1-4 segments), music from BioRand's other games, BioRand's title calls, NPC looks
  and voices, and BioRand's characters as NPC looks. Seeds are shared as CVX1- codes.
- CV:X enemies as in BioRand: a slider per kind, and random enemy placements (how many rooms
  have enemies, how many on average, save rooms or not).
- BioRand's data (for RE 1-3) and its music and characters (for CV:X) are read from BioRand's
  folder, set in the settings.
