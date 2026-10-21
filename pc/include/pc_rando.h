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

/* Random doors: a door of room (stage, room, rcase) through trigger area `area` (the static
 * area index, or sys->etc_idx for script doors) going to (*tstage, *troom) is redirected when
 * rando_doors.txt lists it. Returns 1 and changes the target, arrival entrance and pending room
 * case when it is. */
int pcRandoDoor(int stage, int room, int rcase, int area, int* tstage, int* troom, int* tpos);
/* pcRandoDoor returned 2: the door leads nowhere and is locked; the message to show */
const unsigned short* pcRandoLockedText(void);

/* The room file just loaded (system.c): pcRandoDoor matches doors on its case, which room scripts
 * may change (room_case_no) before their own door call. */
void pcRandoRoomLoaded(int stage, int room, int rcase);

/* The room case a redirected door needs, or -1; cleared by the call. Applied when the room
 * changes (bhStartDoorDemo). */
int pcRandoTakePendingCase(void);

/* Testing aid: RECVX_TEST_DOOR=<area>[,<presses>]; see pc_rando.c. */
int pcTestDoorDue(void);

/* Testing aid: RECVX_WARP=<stage>,<room>[,<entrance>[,<room case>[,<player>]]] starts a new game
 * in that room instead of the first one. Without it, the randomizer's start= room (if any) is
 * used. Returns 1 and fills the values when either is set. */
int pcWarpGet(int* stage, int* room, int* pos, int* rcase, int* player);
