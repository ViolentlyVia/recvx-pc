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

int pcWarpGet(int* stage, int* room, int* pos, int* rcase, int* player)
{
    const char* env = getenv("RECVX_WARP");
    int v[5] = { 0, 0, 0, 0, -1 };
    int n;

    if ((env == NULL) || (env[0] == 0))
    {
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
