/* PC port: libmc (memory card) on top of a folder.
 *
 * The game's memory card code (ps2_MemoryCard..c and the save/load screens) runs unchanged; it
 * just finds one always-present, formatted card in slot 1 whose root is the "saves" folder
 * next to recvx.exe (or "saves=" in recvx.ini, or RECVX_SAVES). Slot 2 is empty. Card paths
 * map to files below that folder, e.g. "/BASLUS-20184/SAVEDATA-00" ->
 * saves/BASLUS-20184/SAVEDATA-00. The save data itself is the same bytes the PS2 writes.
 *
 * RECVX_MCLOG=1 logs every call.
 *
 * Every call completes immediately; sceMcSync() then reports it once (1) and afterwards that
 * nothing is pending (-1), which the game's idle check relies on. Files are written to a
 * temporary name and renamed on close, so a crash while saving never damages an old save.
 */
#ifdef _WIN32
#include <direct.h>
#include <io.h>
#endif
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>

#include "sce_pc.h"
#include "pc_platform.h"

#define MC_FREE_KB    8000              /* an empty 8 MB card */

#define MC_RDONLY     0x0001
#define MC_WRONLY     0x0002
#define MC_RDWR       0x0003
#define MC_CREAT      0x0200

#define ATTR_FILE     0x8497            /* exists | closed | file | read/write/execute */
#define ATTR_DIR      0x8427            /* exists | subdir | read/write/execute */

static char root[1024];
static char last_path[1200];        /* for RECVX_MCLOG */
static int pending, pending_cmd, pending_result;

static struct {
    int open;
    int mode;
    int dirty;
    unsigned char* data;
    unsigned int size, pos;
    char path[1200];
} file;

static void mc_root(void)
{
    const char* env = getenv("RECVX_SAVES");
    char dir[1024];

    if (root[0] != 0)
    {
        return;
    }

    pcPlatformExeDir(dir, sizeof(dir));

    if ((env != NULL) && (env[0] != 0))
    {
        snprintf(root, sizeof(root), "%s", env);
    }
    else if (!pcIniGet("saves", root, sizeof(root)) || (root[0] == 0))
    {
        snprintf(root, sizeof(root), "%s/saves", dir);
    }
    else if (!((root[0] == '\\') || (root[0] == '/') || (root[1] == ':')))
    {
        /* relative to the exe, like mod= (not to the folder the game was started from) */
        char rel[1024];

        snprintf(rel, sizeof(rel), "%s", root);
        snprintf(root, sizeof(root), "%s/%s", dir, rel);
    }

    /* every level, so saves= can name a folder inside a new one (saves_rando/<seed>) */
    {
        char* p;

        for (p = root + 1; *p != 0; p++)
        {
            if ((*p == '/') || (*p == '\\'))
            {
                char c = *p;

                *p = 0;
                _mkdir(root);
                *p = c;
            }
        }
    }

    _mkdir(root);
    pcLog("saves: %s", root);
}

/* card path -> host path (absolute card paths only; the game always uses those) */
static void host_path(const char* name, char* out, int size)
{
    char* p;

    mc_root();

    while (*name == '/')
    {
        name++;
    }

    snprintf(out, size, "%s/%s", root, name);
    snprintf(last_path, sizeof(last_path), "%s", name);

    for (p = out + strlen(root); *p != 0; p++)
    {
        if (*p == '\\')
        {
            *p = '/';
        }
    }

    /* no trailing slash, and never above the root */
    if ((strlen(out) > 1) && (out[strlen(out) - 1] == '/'))
    {
        out[strlen(out) - 1] = 0;
    }

    if (strstr(out + strlen(root), "..") != NULL)
    {
        snprintf(out, size, "%s/invalid", root);
    }
}

static int finish(int cmd, int result)
{
    static int log = -1;

    if (log < 0)
    {
        log = (getenv("RECVX_MCLOG") != NULL);
    }

    if (log && (cmd != 1))
    {
        pcLog("mc: cmd %d -> %d (%s)", cmd, result, last_path);
    }

    pending = 1;
    pending_cmd = cmd;
    pending_result = result;
    return 0;
}

int sceMcInit(void)
{
    mc_root();
    return 0;
}

int sceMcGetInfo(int port, int slot, int* type, int* free, int* format)
{
    (void)slot;

    if (port != 0)
    {
        if (type != NULL)   *type = 0;
        if (free != NULL)   *free = 0;
        if (format != NULL) *format = 0;

        return finish(1, -10);   /* no card */
    }

    if (type != NULL)   *type = 2;   /* PS2 memory card */
    if (free != NULL)   *free = MC_FREE_KB;
    if (format != NULL) *format = 1;

    return finish(1, 0);             /* same card as last time */
}

static void file_close(int keep)
{
    if (file.open && file.dirty && keep)
    {
        char tmp[1300];
        FILE* f;

        snprintf(tmp, sizeof(tmp), "%s.tmp", file.path);

        if ((f = fopen(tmp, "wb")) != NULL)
        {
            int ok = (fwrite(file.data, 1, file.size, f) == file.size);

            ok &= (fclose(f) == 0);

            if (ok)
            {
                remove(file.path);
                ok = (rename(tmp, file.path) == 0);
            }

            if (!ok)
            {
                pcLog("saves: could not write %s", file.path);
                remove(tmp);
            }
        }
    }

    free(file.data);
    memset(&file, 0, sizeof(file));
}

int sceMcOpen(int port, int slot, const char* name, int mode)
{
    struct _stat st;
    FILE* f;

    (void)slot;

    if (port != 0)
    {
        return finish(2, -10);
    }

    if (file.open)
    {
        file_close(1);
    }

    host_path(name, file.path, sizeof(file.path));

    if ((_stat(file.path, &st) == 0) && (st.st_mode & _S_IFDIR))
    {
        return finish(2, -4);
    }

    if ((f = fopen(file.path, "rb")) != NULL)
    {
        fseek(f, 0, SEEK_END);
        file.size = (unsigned int)ftell(f);
        fseek(f, 0, SEEK_SET);
        file.data = malloc(file.size ? file.size : 1);
        file.size = (unsigned int)fread(file.data, 1, file.size, f);
        fclose(f);
    }
    else if (mode & MC_CREAT)
    {
        file.data = malloc(1);
        file.size = 0;
        file.dirty = 1;   /* an opened-and-closed new file still exists afterwards */
    }
    else
    {
        return finish(2, -4);   /* not found */
    }

    file.open = 1;
    file.mode = mode;
    file.pos = 0;

    /* the game never keeps the returned descriptor (it always uses 0): one file at a time */
    return finish(2, 0);
}

int sceMcClose(int fd)
{
    (void)fd;

    if (!file.open)
    {
        return finish(3, -4);
    }

    file_close(1);
    return finish(3, 0);
}

int sceMcRead(int fd, void* buf, int size)
{
    int n;

    (void)fd;

    if (!file.open || (size < 0))
    {
        return finish(5, -4);
    }

    n = ((unsigned int)size > file.size - file.pos) ? (int)(file.size - file.pos) : size;
    memcpy(buf, file.data + file.pos, n);
    file.pos += n;

    return finish(5, n);
}

int sceMcWrite(int fd, const void* buf, int size)
{
    (void)fd;

    if (!file.open || (size < 0) || ((file.mode & MC_RDWR) == MC_RDONLY))
    {
        return finish(6, -4);
    }

    if (file.pos + size > file.size)
    {
        file.data = realloc(file.data, file.pos + size);
        file.size = file.pos + size;
    }

    memcpy(file.data + file.pos, buf, size);
    file.pos += size;
    file.dirty = 1;

    return finish(6, size);
}

int sceMcMkdir(int port, int slot, const char* name)
{
    char path[1200];

    (void)slot;

    if (port != 0)
    {
        return finish(11, -10);
    }

    host_path(name, path, sizeof(path));

    return finish(11, (_mkdir(path) == 0) ? 0 : -4);
}

int sceMcChdir(int port, int slot, const char* newdir, char* olddir)
{
    struct _stat st;
    char path[1200];

    (void)slot;

    if (olddir != NULL)
    {
        strcpy(olddir, "/");
    }

    if (port != 0)
    {
        return finish(12, -10);
    }

    host_path(newdir, path, sizeof(path));

    return finish(12, ((_stat(path, &st) == 0) && (st.st_mode & _S_IFDIR)) ? 0 : -4);
}

int sceMcFormat(int port, int slot)
{
    (void)slot;

    /* never wipe the saves folder: the card is always formatted */
    return finish(16, (port == 0) ? 0 : -10);
}

int sceMcDelete(int port, int slot, const char* name)
{
    char path[1200];

    (void)slot;

    if (port != 0)
    {
        return finish(15, -10);
    }

    host_path(name, path, sizeof(path));

    return finish(15, ((remove(path) == 0) || (_rmdir(path) == 0)) ? 0 : -4);
}

static void set_time(sceMcTblGetDir* e, time_t t)
{
    struct tm* tm = localtime(&t);

    if (tm == NULL)
    {
        return;
    }

    e->_Create.Sec = (u_char)tm->tm_sec;
    e->_Create.Min = (u_char)tm->tm_min;
    e->_Create.Hour = (u_char)tm->tm_hour;
    e->_Create.Day = (u_char)tm->tm_mday;
    e->_Create.Month = (u_char)(tm->tm_mon + 1);
    e->_Create.Year = (u_short)(tm->tm_year + 1900);
    e->_Modify = e->_Create;
}

static void fill_entry(sceMcTblGetDir* e, const char* name, const char* path)
{
    struct _stat st;

    memset(e, 0, sizeof(*e));
    strncpy((char*)e->EntryName, name, sizeof(e->EntryName) - 1);

    if (_stat(path, &st) == 0)
    {
        e->AttrFile = (st.st_mode & _S_IFDIR) ? ATTR_DIR : ATTR_FILE;
        e->FileSizeByte = (st.st_mode & _S_IFDIR) ? 0 : (u_int)st.st_size;
        set_time(e, st.st_mtime);
    }
}

/* name is a path whose last part may be "*" (list the directory, with "." and "..") */
int sceMcGetDir(int port, int slot, const char* name, unsigned int mode, int maxent, sceMcTblGetDir* table)
{
    char path[1200], dir[1200], pattern[1300], full[1500];
    const char* last;
    struct _finddata_t fd;
    intptr_t h;
    int n = 0;

    (void)slot; (void)mode;

    if (port != 0)
    {
        return finish(13, -10);
    }

    host_path(name, path, sizeof(path));
    last = strrchr(path, '/');
    last = (last != NULL) ? last + 1 : path;

    if (strchr(last, '*') == NULL)
    {
        struct _stat st;

        if ((maxent > 0) && (_stat(path, &st) == 0))
        {
            fill_entry(&table[0], last, path);
            n = 1;
        }

        return finish(13, n);
    }

    snprintf(dir, sizeof(dir), "%.*s", (int)(last - path - 1), path);

    if ((n < maxent) && (_access(dir, 0) == 0))
    {
        fill_entry(&table[n++], ".", dir);
    }

    if ((n < maxent) && (n == 1))
    {
        fill_entry(&table[n++], "..", dir);
    }

    snprintf(pattern, sizeof(pattern), "%s/%s", dir, last);

    if ((h = _findfirst(pattern, &fd)) != -1)
    {
        do
        {
            if ((strcmp(fd.name, ".") == 0) || (strcmp(fd.name, "..") == 0))
            {
                continue;
            }

            /* leftovers of an interrupted save are not part of the card */
            if ((strlen(fd.name) > 4) && (_stricmp(fd.name + strlen(fd.name) - 4, ".tmp") == 0))
            {
                continue;
            }

            if (n >= maxent)
            {
                break;
            }

            snprintf(full, sizeof(full), "%s/%s", dir, fd.name);
            fill_entry(&table[n++], fd.name, full);
        } while (_findnext(h, &fd) == 0);

        _findclose(h);
    }

    return finish(13, n);
}

/* 1 = the last call finished (its result in *result), -1 = nothing pending */
int sceMcSync(int mode, int* cmd, int* result)
{
    (void)mode;

    if (!pending)
    {
        return -1;
    }

    pending = 0;

    if (cmd != NULL)    *cmd = pending_cmd;
    if (result != NULL) *result = pending_result;

    return 1;
}
