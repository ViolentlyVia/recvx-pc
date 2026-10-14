/* PC port: randomizer support (cvx-rando.exe), see pc_rando.c. */
#pragma once

/* Called by pc_cdvd.c once the mod folder (mod= in recvx.ini) is known: reads its rando.ini and
 * patches the item tables. Does nothing when the folder has no rando.ini. */
void pcRandoInit(const char* mod_dir);

/* rando.ini was found and loaded */
int pcRandoActive(void);

/* no_nosferatu_poison=1 */
int pcRandoNoNosferatuPoison(void);

/* bgm_map: the track to play instead of bgm_no (bgm_no itself when not remapped) */
int pcRandoBgm(int bgm_no);

/* New game: replace player's (0 Claire, 1 Chris) starting special slot and items in pip
 * (sys->itm + 16 * player), when rando.ini says so. */
void pcRandoStartItems(int player, unsigned int* pip);

/* Testing aid: RECVX_WARP=<stage>,<room>[,<entrance>[,<room case>[,<player>]]] starts a new game
 * in that room instead of the first one. Returns 1 and fills the values when it is set. */
int pcWarpGet(int* stage, int* room, int* pos, int* rcase, int* player);
