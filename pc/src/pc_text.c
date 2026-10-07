/* PC port: replacement wording for the save/load messages.
 *
 * On PC the game's "memory card" is the saves folder (pc_mc.c), so the messages that talk about
 * memory cards, slots and the MAIN POWER switch are replaced by plain save-data wording. Two
 * tables are covered: the system messages loaded from the disc (sys->mes_sp, message type 1)
 * and SaveLoadMessage in adv.c (the boot / system setup screens). Messages are stored in the
 * game's font encoding: a character is its ASCII code - 0x20, 0xFF01 a space, 0xFF00 a new
 * line and 0xFFFF the end.
 */
#include <string.h>

#define SYS_MESSAGES 494      /* entries in the US system message table */
#define MAX_CODES    512      /* AutoSaveLoadEasyDispMessage copies 1024 bytes */

typedef struct {
    int table;
    int idx;
    const char* text;
} Override;

#define SETUP_START  "\n\nPress START button to begin game with \ndefault system setup. "
#define SETUP_CANCEL "\n\nPress START button to cancel saving \nsystem setup data. "

static const Override overrides[] = {
    /* system messages (load / save screens) */
    { 1, 451, "Checking save data. " },
    { 1, 454, "Save data cannot be accessed. " },
    { 1, 455, "Save data cannot be accessed. " },
    { 1, 456, "Save data cannot be accessed. " },
    { 1, 460, "Now loading. " },
    { 1, 463, "" },
    { 1, 464, "" },
    { 1, 465, "" },
    { 1, 471, "Checking save data. " },
    { 1, 474, "Save data cannot be accessed. " },
    { 1, 475, "Save data cannot be accessed. " },
    { 1, 476, "There is not enough disk space to save. " },
    { 1, 477, "Save data has not been created yet. \n\nCreate it now? " },
    { 1, 478, "Creating save data. " },
    { 1, 479, "Save data created. " },
    { 1, 480, "Failed to create save data. " },
    { 1, 484, "Now saving. " },
    { 1, 487, "Save data cannot be accessed. " },
    { 1, 488, "Creating save data. " },
    { 1, 489, "Save data created. " },
    { 1, 490, "Failed to create save data. " },

    /* SaveLoadMessage (boot / system setup screens) */
    { 2, 0,  "\nChecking save data. " },
    { 2, 10, "System setup data cannot be accessed. " SETUP_START },
    { 2, 11, "System setup data cannot be accessed. " SETUP_START },
    { 2, 12, "There is not enough disk space to save \nsystem setup data. " SETUP_START },
    { 2, 13, "There is not enough disk space to save \nsystem setup data. " SETUP_START },
    { 2, 14, "There is not enough disk space to save \nsystem setup data. " SETUP_START },
    { 2, 15, "System setup data cannot be accessed. " SETUP_START },
    { 2, 16, "There is no saved system setup data. " SETUP_START },
    { 2, 17, "\nLoading system setup data. " },
    { 2, 18, "Failed to load system setup data. " SETUP_START },
    { 2, 19, "System setup data is corrupted. " SETUP_START },
    { 2, 30, "System setup data cannot be saved. " SETUP_CANCEL },
    { 2, 31, "There is not enough disk space to save \nsystem setup data. " SETUP_CANCEL },
    /* the Yes / No lines must stay where the cursor is drawn */
    { 2, 32, "Save data has not been created yet. \n\n\nCreate it now? \n\nYes \nNo  " },
    { 2, 33, "\nCreating save data. " },
    { 2, 34, "Save data created. " },
    { 2, 35, "Failed to create save data. " SETUP_CANCEL },
    { 2, 36, "Save data has not been created yet. \n\n\n\n\nCreate it now? \n\nYes \nNo  " },
    { 2, 37, "\nCreating save data. " },
    { 2, 38, "Save data created. " },
    { 2, 39, "Failed to create save data. " SETUP_CANCEL },
    { 2, 40, "\nSaving system setup data. " },
    { 2, 42, "Failed to save system setup data. " SETUP_CANCEL },
    { 2, 43, "System setup data is corrupted. " SETUP_CANCEL },
};

#define NUM_OVERRIDES (int)(sizeof(overrides) / sizeof(overrides[0]))

static unsigned short encoded[NUM_OVERRIDES][MAX_CODES];
static int encoded_done[NUM_OVERRIDES];

static void encode(const char* s, unsigned short* out)
{
    int n = 0;

    for ( ; (*s != 0) && (n < MAX_CODES - 1); s++)
    {
        if (*s == '\n')
        {
            out[n++] = 0xFF00;
        }
        else if (*s == ' ')
        {
            out[n++] = 0xFF01;
        }
        else if ((unsigned char)*s > 0x20)
        {
            out[n++] = (unsigned short)((unsigned char)*s - 0x20);
        }
    }

    out[n] = 0xFFFF;
}

/* table 1 = system messages (base = sys->mes_sp), 2 = SaveLoadMessage. Returns the replacement
 * text (font codes) or NULL to keep the original. */
const unsigned short* pcMessageOverride(int table, int idx, const void* base)
{
    int i;

    /* only the table this was written for (the US release's system messages) */
    if ((table == 1) && ((base == NULL) || (*(const unsigned int*)base != SYS_MESSAGES)))
    {
        return NULL;
    }

    for (i = 0; i < NUM_OVERRIDES; i++)
    {
        if ((overrides[i].table == table) && (overrides[i].idx == idx))
        {
            if (!encoded_done[i])
            {
                encode(overrides[i].text, encoded[i]);
                encoded_done[i] = 1;
            }

            return encoded[i];
        }
    }

    return NULL;
}
