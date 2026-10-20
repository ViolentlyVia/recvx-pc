/* PC port: randomizer support.
 *
 * The randomizer (cvx-rando.exe) writes the game files it changes (RDX_LNK.AFS, the rooms) to a
 * mod folder, which pc_cdvd.c reads in place of the data folder's copies (mod= in recvx.ini).
 * Changes that live in the executable on the PS2 (BioRand patched SLUS_201.84 for these) are
 * described by rando.ini in the same folder:
 *
 *   version=1                          format version; a newer one is refused
 *   seed=<text>                        only logged
 *   pickup_amounts=<id>:<n>,...        what a pickup of item <id> gives (getbulletmax, all modes)
 *   fix_rifle_stacking=1               rifle and assault rifle bullets add to ammo already carried
 *   no_nosferatu_poison=1              Nosferatu's attack never poisons
 *   start_special_claire=<id>          Claire's special slot at the start (default: lighter)
 *   start_items_claire=<id>:<n>,...    Claire's other starting items (replace the defaults)
 *   start_special_chris=<id>, start_items_chris=...   the same for Chris
 *   bgm_map=<from>:<to>,...            music tracks swapped in the room scripts' music requests
 *   start=<stage>,<room>,<entrance>,<case>[,<player>]   where a new game starts, and as whom
 *                                      (random doors; player 0 Claire, 1 Chris; default 0)
 *
 * Random doors are listed in rando_doors.txt next to it (see read_doors).
 *
 * Item ids are the game's (itemdata index); <n> is the count, or the loaded ammo for a weapon.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../../include/ps2/veronica/prog/item.h"
#include "pc_platform.h"
#include "pc_rando.h"

#define RANDO_VERSION 1
#define ITEM_COUNT 156
#define START_SLOTS 8   /* pip[2] .. pip[9] */

extern short standard[3][2];

typedef struct {
    int special;                /* -1: keep the game's */
    int count;                  /* -1: keep the game's */
    int id[START_SLOTS], n[START_SLOTS];
} StartItems;

static int active;
static int no_poison;
static short bgm_map[256];
static StartItems start[2] = { { -1, -1 }, { -1, -1 } };
static int start_room[5];   /* start=: stage, room, entrance, case, player */
static int has_start;

static void read_doors(const char* mod_dir);

/* "a:b,c:d" -> calls fn(a, b) for each pair; returns the number of pairs */
static int parse_pairs(const char* text, void (*fn)(int a, int b, void* ctx), void* ctx)
{
    const char* p = text;
    int n = 0;

    while (*p != 0)
    {
        char* end;
        long a, b;

        a = strtol(p, &end, 0);

        if ((end == p) || (*end != ':'))
        {
            pcLog("rando: bad list entry near \"%s\"", p);
            break;
        }

        p = end + 1;
        b = strtol(p, &end, 0);

        if (end == p)
        {
            pcLog("rando: bad list entry near \"%s\"", p);
            break;
        }

        fn((int)a, (int)b, ctx);
        n++;

        p = end;

        while ((*p == ',') || (*p == ' '))
        {
            p++;
        }
    }

    return n;
}

static void set_pickup_amount(int id, int amount, void* ctx)
{
    int mode;

    (void)ctx;

    if ((id <= 0) || (id >= ITEM_COUNT) || (amount <= 0))
    {
        pcLog("rando: pickup amount for item %d ignored", id);
        return;
    }

    for (mode = 0; mode < 4; mode++)
    {
        getbulletmax[id][mode] = amount;
    }
}

static void set_bgm(int from, int to, void* ctx)
{
    (void)ctx;

    if ((from >= 0) && (from < 256) && (to >= 0) && (to < 256))
    {
        bgm_map[from] = (short)to;
    }
}

static void add_start_item(int id, int n, void* ctx)
{
    StartItems* s = (StartItems*)ctx;

    if ((id <= 0) || (id >= ITEM_COUNT) || (n <= 0) || (s->count >= START_SLOTS))
    {
        pcLog("rando: starting item %d x%d ignored", id, n);
        return;
    }

    s->id[s->count] = id;
    s->n[s->count] = n;
    s->count++;
}

static void read_start(const char* ini, const char* who, StartItems* s)
{
    char key[64], v[512];

    snprintf(key, sizeof(key), "start_special_%s", who);

    if (pcIniFileGet(ini, key, v, sizeof(v)))
    {
        s->special = atoi(v);
    }

    snprintf(key, sizeof(key), "start_items_%s", who);

    if (pcIniFileGet(ini, key, v, sizeof(v)))
    {
        s->count = 0;
        parse_pairs(v, add_start_item, s);
    }
}

void pcRandoInit(const char* mod_dir)
{
    char ini[1100], v[4096];
    int i, version;

    for (i = 0; i < 256; i++)
    {
        bgm_map[i] = (short)i;
    }

    snprintf(ini, sizeof(ini), "%s/rando.ini", mod_dir);

    if (!pcIniFileGet(ini, "version", v, sizeof(v)))
    {
        return;
    }

    version = atoi(v);

    if ((version < 1) || (version > RANDO_VERSION))
    {
        pcPlatformFatal("This randomized game was made by a newer version of the randomizer\n"
                        "(rando.ini version %d, this recvx.exe reads version %d).\n\n"
                        "Update recvx.exe, or choose \"Play normal game\" in the randomizer.",
                        version, RANDO_VERSION);
        return;
    }

    active = 1;

    if (!pcIniFileGet(ini, "seed", v, sizeof(v)))
    {
        strcpy(v, "?");
    }

    pcLog("rando: %s (seed %s)", ini, v);

    if (pcIniFileGet(ini, "pickup_amounts", v, sizeof(v)))
    {
        pcLog("rando: %d pickup amounts", parse_pairs(v, set_pickup_amount, NULL));
    }

    /* BioRand's FixRifleStacking: type bit 0x200 lets a pickup add to the ammo carried (sub1.c) */
    if (pcIniFileGetInt(ini, "fix_rifle_stacking", 0))
    {
        itemdata[40].type |= 0x200;   /* rifle bullets */
        itemdata[42].type |= 0x200;   /* assault rifle bullets */
    }

    no_poison = pcIniFileGetInt(ini, "no_nosferatu_poison", 0) != 0;

    if (pcIniFileGet(ini, "bgm_map", v, sizeof(v)))
    {
        pcLog("rando: %d music tracks swapped", parse_pairs(v, set_bgm, NULL));
    }

    read_start(ini, "claire", &start[0]);
    read_start(ini, "chris", &start[1]);

    start_room[4] = 0;   /* Claire, when start= has no player */
    if (pcIniFileGet(ini, "start", v, sizeof(v)) &&
        (sscanf(v, "%d,%d,%d,%d,%d", &start_room[0], &start_room[1], &start_room[2], &start_room[3], &start_room[4]) >= 4))
    {
        if ((start_room[4] < 0) || (start_room[4] > 1))
        {
            start_room[4] = 0;
        }

        has_start = 1;
    }

    read_doors(mod_dir);
}

int pcRandoActive(void)
{
    return active;
}

int pcRandoNoNosferatuPoison(void)
{
    return active && no_poison;
}

int pcRandoBgm(int bgm_no)
{
    if (!active || (bgm_no < 0) || (bgm_no >= 256) || (bgm_map[bgm_no] == bgm_no))
    {
        return bgm_no;
    }

    pcLog("rando: music %d plays as %d", bgm_no, bgm_map[bgm_no]);

    return bgm_map[bgm_no];
}

void pcRandoStartItems(int player, unsigned int* pip)
{
    StartItems* s;
    int i;

    if (!active || (player < 0) || (player > 1))
    {
        return;
    }

    s = &start[player];

    if (s->special >= 0)
    {
        /* the special slot only takes the player's "standard" items (sub1.c) */
        pip[1] = (s->special > 0) ? (((unsigned int)s->special << 16) | 1) : 0;
        standard[player][0] = (short)((s->special > 0) ? s->special : -1);
        standard[player][1] = -1;
    }

    if (s->count >= 0)
    {
        for (i = 0; i < START_SLOTS; i++)
        {
            pip[2 + i] = (i < s->count) ? (((unsigned int)s->id[i] << 16) | (unsigned int)s->n[i]) : 0;
        }
    }
}

/* ---- doors: rando_doors.txt in the mod folder ----
 *
 * One door per line: "<stage> <room> <case> <area> <old stage> <old room>
 * <new stage> <new room> <new case> <entrance>". A door of room (stage, room, case) whose
 * trigger area is <area> (255: any, for script doors) and that leads to (old stage, old room)
 * leads to the new room instead, arriving at <entrance>, with the room case (variant) set on
 * the way. Doors are matched where the game sets sys->door (hitchk.c for door areas,
 * bhSetDoorDemo for script doors), so both kinds work and no room script has to change. */

typedef struct {
    short stage, room, rcase, area, ostage, oroom;
    short tstage, troom, tcase, tpos;
} DoorMap;

static DoorMap* doors;
static int door_count;
static int pending_case = -1;
static int loaded_room[3] = { -1, -1, -1 }; /* stage, room, case of the room file in memory */

void pcRandoRoomLoaded(int stage, int room, int rcase)
{
    loaded_room[0] = stage;
    loaded_room[1] = room;
    loaded_room[2] = rcase;
}

static void read_doors(const char* mod_dir)
{
    char path[1100], line[256];
    FILE* f;
    int cap = 0;

    snprintf(path, sizeof(path), "%s/rando_doors.txt", mod_dir);

    if ((f = fopen(path, "r")) == NULL)
    {
        return;
    }

    while (fgets(line, sizeof(line), f) != NULL)
    {
        DoorMap d;

        if ((line[0] == '#') || (sscanf(line, "%hd %hd %hd %hd %hd %hd %hd %hd %hd %hd",
                                        &d.stage, &d.room, &d.rcase, &d.area, &d.ostage, &d.oroom,
                                        &d.tstage, &d.troom, &d.tcase, &d.tpos) != 10))
        {
            continue;
        }

        if (door_count == cap)
        {
            cap = cap ? cap * 2 : 128;
            doors = realloc(doors, cap * sizeof(*doors));
        }

        doors[door_count++] = d;
    }

    fclose(f);
    pcLog("rando: %d doors changed", door_count);
}

int pcRandoDoor(int stage, int room, int rcase, int area, int* tstage, int* troom, int* tpos)
{
    const DoorMap* hit = NULL;
    int i;

    pending_case = -1;

    if (!active || (door_count == 0))
    {
        return 0;
    }

    /* match the room file that is loaded: scripts set sys->rcase (room_case_no) for the next
       room before their door call, e.g. RM_0021's door to RM_0010 sets case 0 first */
    if ((stage == loaded_room[0]) && (room == loaded_room[1]))
    {
        rcase = loaded_room[2];
    }

    for (i = 0; i < door_count; i++)
    {
        const DoorMap* d = &doors[i];

        if ((d->stage != stage) || (d->room != room) || (d->rcase != rcase) ||
            (d->ostage != *tstage) || (d->oroom != *troom))
        {
            continue;
        }

        if (d->area == area)
        {
            hit = d;
            break;
        }

        if ((d->area == 255) && (hit == NULL))
        {
            hit = d;
        }
    }

    if (hit == NULL)
    {
        return 0;
    }

    pcLog("rando: door %d %d %d area %d to %d %d -> %d %d case %d entrance %d", stage, room, rcase, area,
          *tstage, *troom, hit->tstage, hit->troom, hit->tcase, hit->tpos);

    *tstage = hit->tstage;
    *troom = hit->troom;
    *tpos = hit->tpos;
    pending_case = hit->tcase;

    return 1;
}

int pcRandoTakePendingCase(void)
{
    int c = pending_case;

    pending_case = -1;
    return c;
}

/* Testing aid: RECVX_TEST_DOOR=<area>[,<presses>] makes action press number <presses> (default
 * 1) in play use trigger area <area> of the current room (bhCheckExmAtari). Returns the area
 * once, else -1. */
int pcTestDoorDue(void)
{
    static int calls, done;
    const char* env;
    int area = -1, delay = 1;

    if (done || ((env = getenv("RECVX_TEST_DOOR")) == NULL) || (sscanf(env, "%d,%d", &area, &delay) < 1))
    {
        return -1;
    }

    if (++calls < delay)
    {
        return -1;
    }

    done = 1;
    pcLog("RECVX_TEST_DOOR: using area %d", area);
    return area;
}

int pcWarpGet(int* stage, int* room, int* pos, int* rcase, int* player)
{
    const char* env = getenv("RECVX_WARP");
    int v[5] = { 0, 0, 0, 0, -1 };
    int n;

    if ((env == NULL) || (env[0] == 0))
    {
        /* the randomizer's start room (start= in rando.ini), e.g. with random doors */
        if (active && has_start)
        {
            *stage = start_room[0];
            *room = start_room[1];
            *pos = start_room[2];
            *rcase = start_room[3];
            *player = start_room[4];
            pcLog("rando: new game starts in stage %d room %d entrance %d case %d as player %d",
                  *stage, *room, *pos, *rcase, *player);
            return 1;
        }

        return 0;
    }

    n = sscanf(env, "%d,%d,%d,%d,%d", &v[0], &v[1], &v[2], &v[3], &v[4]);

    if (n < 2)
    {
        pcLog("RECVX_WARP: expected <stage>,<room>[,<entrance>[,<room case>[,<player>]]], got \"%s\"", env);
        return 0;
    }

    *stage = v[0];
    *room = v[1];
    *pos = v[2];
    *rcase = v[3];

    if (v[4] >= 0)
    {
        *player = v[4];
    }

    pcLog("RECVX_WARP: new game starts in stage %d room %d entrance %d case %d", v[0], v[1], v[2], v[3]);

    return 1;
}
