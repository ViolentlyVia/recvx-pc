/* PC port: libcdvd replacement reading straight from the game's ISO image.
 *
 * The game locates files with sceCdSearchFile() (ISO9660 path -> start sector and size) and
 * reads whole 2048-byte sectors with sceCdRead(). Reads complete immediately on PC.
 *
 * The disc comes from either
 *  - an extracted data folder (pc/tools/extract_iso.py): RECVX_DATA, a "data=" line in recvx.ini
 *    or a "data" folder next to the executable, holding the disc's files and files.idx (start
 *    sector, size, path of each file); sector reads are served from those files, or
 *  - the ISO image: RECVX_ISO, an "iso=" line in recvx.ini or the first *.iso next to the exe.
 *
 * With the data folder, a mod folder (RECVX_MOD or "mod=" in recvx.ini, relative to the exe) can
 * replace any of its files: a file there with the same relative path is read instead (the
 * randomizer writes RDX_LNK.AFS there). See mod_open().
 */
#include <ctype.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>

#include "sce_pc.h"
#include "pc_platform.h"
#include "pc_rando.h"

#define SECTOR 2048

static FILE* iso;
static char iso_path[1024];
static int last_error;

/* streaming (sceCdSt*) state */
static u_int st_lsn;
static int st_active;

/* ---- extracted data folder ---- */

typedef struct {
    u_int lsn, size, sectors;
    char path[200];
    char* mod_path;   /* full path of the mod folder's replacement, or NULL */
    FILE* f;
} DataFile;

static DataFile* data_files;
static int data_count;
static char data_dir[1024];
static char mod_dir[1024];

static DataFile* data_file_at(u_int lsn)
{
    int lo = 0, hi = data_count - 1;

    while (lo <= hi)
    {
        int mid = (lo + hi) / 2;
        DataFile* d = &data_files[mid];

        if (lsn < d->lsn)
        {
            hi = mid - 1;
        }
        else if (lsn >= d->lsn + d->sectors)
        {
            lo = mid + 1;
        }
        else
        {
            return d;
        }
    }

    return NULL;
}

static int data_read_sectors(u_int lsn, u_int count, unsigned char* buf)
{
    while (count > 0)
    {
        DataFile* d = data_file_at(lsn);
        u_int n, off, avail;

        if (d == NULL)
        {
            /* outside every file (directory records, padding): zeros */
            memset(buf, 0, SECTOR);
            lsn++;
            count--;
            buf += SECTOR;
            continue;
        }

        if (d->f == NULL)
        {
            char path[1300];

            if (d->mod_path != NULL)
            {
                snprintf(path, sizeof(path), "%s", d->mod_path);
            }
            else
            {
                snprintf(path, sizeof(path), "%s/%s", data_dir, d->path);
            }

            if ((d->f = fopen(path, "rb")) == NULL)
            {
                pcLog("disc: cannot open %s", path);
                return 0;
            }
        }

        n = d->lsn + d->sectors - lsn;

        if (n > count)
        {
            n = count;
        }

        off = (lsn - d->lsn) * SECTOR;
        avail = (off < d->size) ? d->size - off : 0;

        if (avail > n * SECTOR)
        {
            avail = n * SECTOR;
        }

        memset(buf + avail, 0, n * SECTOR - avail);

        if ((avail > 0) && ((_fseeki64(d->f, off, SEEK_SET) != 0) || (fread(buf, 1, avail, d->f) != avail)))
        {
            return 0;
        }

        lsn += n;
        count -= n;
        buf += n * SECTOR;
    }

    return 1;
}

static int by_lsn(const void* a, const void* b)
{
    u_int x = ((const DataFile*)a)->lsn, y = ((const DataFile*)b)->lsn;

    return (x > y) - (x < y);
}

/* Point data_files at the mod folder's replacements. The game finds files by name and reads
 * them by sector, so a replacement keeps its sector range when it fits and otherwise moves past
 * the last file (the ranges are only keys for data_file_at). Then loads the folder's rando.ini. */
static void mod_open(const char* exe_dir)
{
    const char* env = getenv("RECVX_MOD");
    u_int next = 0;
    int i, replaced = 0, moved = 0;
    struct _stat64 st;

    mod_dir[0] = 0;

    if ((env != NULL) && (env[0] != 0))
    {
        snprintf(mod_dir, sizeof(mod_dir), "%s", env);
    }
    else if (!pcIniGet("mod", mod_dir, sizeof(mod_dir)) || (mod_dir[0] == 0))
    {
        return;
    }

    /* relative to the exe, not the working directory */
    if (!((mod_dir[0] == '\\') || (mod_dir[0] == '/') || ((mod_dir[0] != 0) && (mod_dir[1] == ':'))))
    {
        char rel[1024];

        snprintf(rel, sizeof(rel), "%s", mod_dir);
        snprintf(mod_dir, sizeof(mod_dir), "%s/%s", exe_dir, rel);
    }

    if ((_stat64(mod_dir, &st) != 0) || !(st.st_mode & _S_IFDIR))
    {
        pcLog("mod: folder %s not found, playing without it", mod_dir);
        mod_dir[0] = 0;
        return;
    }

    for (i = 0; i < data_count; i++)
    {
        if (data_files[i].lsn + data_files[i].sectors > next)
        {
            next = data_files[i].lsn + data_files[i].sectors;
        }
    }

    for (i = 0; i < data_count; i++)
    {
        DataFile* d = &data_files[i];
        char path[1300];
        u_int sectors;

        snprintf(path, sizeof(path), "%s/%s", mod_dir, d->path);

        if ((_stat64(path, &st) != 0) || (st.st_mode & _S_IFDIR) || (st.st_size > 0xFFFFFFFFll))
        {
            continue;
        }

        sectors = (u_int)((st.st_size + SECTOR - 1) / SECTOR);

        if (sectors == 0)
        {
            sectors = 1;
        }

        if (sectors > d->sectors)
        {
            d->lsn = next;
            next += sectors;
            moved = 1;
        }

        pcLog("mod: %s replaces %s (%lld bytes, was %u)", path, d->path, st.st_size, d->size);

        d->size = (u_int)st.st_size;
        d->sectors = sectors;
        d->mod_path = _strdup(path);
        replaced++;
    }

    if (moved)
    {
        qsort(data_files, data_count, sizeof(*data_files), by_lsn);
    }

    pcLog("mod: using %s (%d file(s) replaced)", mod_dir, replaced);

    pcRandoInit(mod_dir);
}

static int data_open(void)
{
    const char* env = getenv("RECVX_DATA");
    char dir[1024], line[1300], idx[1100];
    FILE* f;
    int cap = 0;

    data_dir[0] = 0;
    pcPlatformExeDir(dir, sizeof(dir));

    if ((env != NULL) && (env[0] != 0))
    {
        snprintf(data_dir, sizeof(data_dir), "%s", env);
    }
    else if (!pcIniGet("data", data_dir, sizeof(data_dir)) || (data_dir[0] == 0))
    {
        snprintf(data_dir, sizeof(data_dir), "%s/data", dir);
    }
    else if (!((data_dir[0] == '\\') || (data_dir[0] == '/') || (data_dir[1] == ':')))
    {
        /* relative to the exe, like mod= */
        char rel[1024];

        snprintf(rel, sizeof(rel), "%s", data_dir);
        snprintf(data_dir, sizeof(data_dir), "%s/%s", dir, rel);
    }

    snprintf(idx, sizeof(idx), "%s/files.idx", data_dir);

    if ((f = fopen(idx, "r")) == NULL)
    {
        return 0;
    }

    while (fgets(line, sizeof(line), f) != NULL)
    {
        DataFile d;

        memset(&d, 0, sizeof(d));

        if ((line[0] == '#') || (sscanf(line, "%u %u %199[^\r\n]", &d.lsn, &d.size, d.path) != 3))
        {
            continue;
        }

        d.sectors = (d.size + SECTOR - 1) / SECTOR;

        if (d.sectors == 0)
        {
            d.sectors = 1;
        }

        if (data_count == cap)
        {
            cap = cap ? cap * 2 : 64;
            data_files = realloc(data_files, cap * sizeof(*data_files));
        }

        data_files[data_count++] = d;
    }

    fclose(f);

    pcLog("disc: using extracted files in %s (%d files)", data_dir, data_count);

    if (data_count > 0)
    {
        mod_open(dir);
    }

    return data_count > 0;
}

/* path as the game passes it ("cdrom0:\MOVIE\MV_000.PSS;1") -> file index entry */
static DataFile* data_find(const char* path)
{
    char norm[200];
    size_t n = 0;
    int i;

    if (strchr(path, ':') != NULL)
    {
        path = strchr(path, ':') + 1;
    }

    while ((*path == '\\') || (*path == '/'))
    {
        path++;
    }

    for (; (*path != 0) && (*path != ';') && (n < sizeof(norm) - 1); path++)
    {
        norm[n++] = (*path == '\\') ? '/' : *path;
    }

    norm[n] = 0;

    for (i = 0; i < data_count; i++)
    {
        if (_stricmp(data_files[i].path, norm) == 0)
        {
            return &data_files[i];
        }
    }

    return NULL;
}

/* ---- ISO image ---- */

static int read_sectors(u_int lsn, u_int count, void* buf)
{
    if (data_count > 0)
    {
        return data_read_sectors(lsn, count, (unsigned char*)buf);
    }

    if ((iso == NULL) || (_fseeki64(iso, (long long)lsn * SECTOR, SEEK_SET) != 0))
    {
        return 0;
    }

    return fread(buf, SECTOR, count, iso) == count;
}

static int find_iso(void)
{
    const char* env = getenv("RECVX_ISO");
    char dir[1024];

    if ((env != NULL) && (env[0] != '\0'))
    {
        strncpy(iso_path, env, sizeof(iso_path) - 1);
        return 1;
    }

    if (pcIniGet("iso", iso_path, sizeof(iso_path)) && (iso_path[0] != '\0'))
    {
        return 1;
    }

    pcPlatformExeDir(dir, sizeof(dir));

    return pcPlatformFindFile(dir, ".iso", iso_path, sizeof(iso_path));
}

int pcCdOpen(void)
{
    unsigned char pvd[SECTOR];

    if ((iso != NULL) || (data_count > 0))
    {
        return 1;
    }

    if (data_open())
    {
        return 1;
    }

    if (!find_iso())
    {
        pcPlatformNotSetUp();
        return 0;
    }

    iso = fopen(iso_path, "rb");

    if ((iso == NULL) || !read_sectors(16, 1, pvd) || (memcmp(pvd + 1, "CD001", 5) != 0))
    {
        pcPlatformFatal("Could not read the disc image:\n%s", iso_path);
        return 0;
    }

    pcLog("disc image: %s", iso_path);

    return 1;
}

/* Find one path component in the directory at (lsn, size). */
static int find_in_dir(u_int lsn, u_int size, const char* name, size_t name_len,
                       u_int* out_lsn, u_int* out_size, int* out_is_dir)
{
    unsigned char sec[SECTOR];
    u_int off, s;

    for (s = 0; s < (size + SECTOR - 1) / SECTOR; s++)
    {
        if (!read_sectors(lsn + s, 1, sec))
        {
            return 0;
        }

        for (off = 0; off < SECTOR; )
        {
            unsigned char len = sec[off];
            unsigned char nlen;
            const char* rname;
            size_t cmp_len;

            if (len == 0)
            {
                break;   /* records never cross sectors; rest of this sector is padding */
            }

            nlen = sec[off + 32];
            rname = (const char*)&sec[off + 33];

            /* compare ignoring case and the ";1" version suffix on either side */
            cmp_len = nlen;

            if ((cmp_len > 2) && (rname[cmp_len - 2] == ';'))
            {
                cmp_len -= 2;
            }

            if ((cmp_len == name_len) && (_strnicmp(rname, name, name_len) == 0))
            {
                *out_lsn = sec[off + 2] | (sec[off + 3] << 8) | (sec[off + 4] << 16) | (sec[off + 5] << 24);
                *out_size = sec[off + 10] | (sec[off + 11] << 8) | (sec[off + 12] << 16) | (sec[off + 13] << 24);
                *out_is_dir = (sec[off + 25] & 2) != 0;

                return 1;
            }

            off += len;
        }
    }

    return 0;
}

static int search_file(const char* path, u_int* lsn, u_int* size)
{
    unsigned char pvd[SECTOR];
    u_int cur_lsn, cur_size;
    int is_dir = 1;
    const char* p = path;

    if (!pcCdOpen())
    {
        return 0;
    }

    if (data_count > 0)
    {
        DataFile* d = data_find(path);

        if (d == NULL)
        {
            return 0;
        }

        *lsn = d->lsn;
        *size = d->size;
        return 1;
    }

    if (!read_sectors(16, 1, pvd))
    {
        return 0;
    }

    /* root directory record lives at offset 156 of the primary volume descriptor */
    cur_lsn = pvd[156 + 2] | (pvd[156 + 3] << 8) | (pvd[156 + 4] << 16) | (pvd[156 + 5] << 24);
    cur_size = pvd[156 + 10] | (pvd[156 + 11] << 8) | (pvd[156 + 12] << 16) | (pvd[156 + 13] << 24);

    /* skip a device prefix such as "cdrom0:" */
    if (strchr(p, ':') != NULL)
    {
        p = strchr(p, ':') + 1;
    }

    for (;;)
    {
        size_t len;

        while ((*p == '\\') || (*p == '/'))
        {
            p++;
        }

        if (*p == '\0')
        {
            break;
        }

        len = strcspn(p, "\\/;");

        if (!is_dir || !find_in_dir(cur_lsn, cur_size, p, len, &cur_lsn, &cur_size, &is_dir))
        {
            return 0;
        }

        p += len;

        if (*p == ';')
        {
            break;
        }
    }

    *lsn = cur_lsn;
    *size = cur_size;

    return !is_dir;
}

/* Used by the PC layer to read files from the disc by name. Returns malloc'd data. */
void* pcCdLoadFile(const char* path, u_int* size_out)
{
    u_int lsn, size;
    void* buf;

    if (!search_file(path, &lsn, &size))
    {
        return NULL;
    }

    buf = malloc(((size + SECTOR - 1) / SECTOR) * SECTOR);

    if (!read_sectors(lsn, (size + SECTOR - 1) / SECTOR, buf))
    {
        free(buf);
        return NULL;
    }

    if (size_out != NULL)
    {
        *size_out = size;
    }

    return buf;
}

int sceCdInit(int mode)
{
    if (mode == SCECdEXIT)
    {
        return 1;
    }

    return pcCdOpen();
}

int sceCdMmode(int media)
{
    (void)media;
    return 1;
}

int sceCdSearchFile(sceCdlFILE* fp, const char* name)
{
    u_int lsn, size;
    const char* base;

    if (!search_file(name, &lsn, &size))
    {
        pcLog("sceCdSearchFile: not found: %s", name);
        last_error = SCECdErREAD;
        return 0;
    }

    fp->lsn = lsn;
    fp->size = size;

    pcLog("disc: %s (%u bytes)", name, size);

    base = strrchr(name, '\\');
    base = (base != NULL) ? base + 1 : name;
    strncpy(fp->name, base, sizeof(fp->name) - 1);
    fp->name[sizeof(fp->name) - 1] = '\0';
    memset(fp->date, 0, sizeof(fp->date));

    return 1;
}

int sceCdRead(u_int lsn, u_int sectors, void* buf, sceCdRMode* mode)
{
    (void)mode;

    if (!read_sectors(lsn, sectors, buf))
    {
        pcLog("sceCdRead failed: lsn %u, %u sectors", lsn, sectors);
        last_error = SCECdErREAD;
        return 0;
    }

    last_error = SCECdErNO;

    return 1;
}

/* All commands complete immediately: 0 = done (blocking or not). */
int sceCdSync(int mode)
{
    (void)mode;
    pcKernelPoll();
    return 0;
}

int sceCdGetError(void)
{
    return last_error;
}

int sceCdDiskReady(int mode)
{
    (void)mode;
    return 2;   /* SCECdComplete */
}

int sceCdBreak(void)
{
    return 1;
}

int sceCdPause(void)
{
    return 1;
}

int sceCdStandby(void)
{
    return 1;
}

int sceCdGetDiskType(void)
{
    return SCECdPS2DVD;
}

static unsigned char to_bcd(int v)
{
    return (unsigned char)(((v / 10) << 4) | (v % 10));
}

int sceCdReadClock(sceCdCLOCK* rtc)
{
    time_t t = time(NULL);
    struct tm* lt = localtime(&t);

    rtc->stat = 0;
    rtc->second = to_bcd(lt->tm_sec);
    rtc->minute = to_bcd(lt->tm_min);
    rtc->hour = to_bcd(lt->tm_hour);
    rtc->pad = 0;
    rtc->day = to_bcd(lt->tm_mday);
    rtc->month = to_bcd(lt->tm_mon + 1);
    rtc->year = to_bcd(lt->tm_year % 100);

    return 1;
}

/* ---- streaming (movies) ---- */

int sceCdStInit(u_int bufmax, u_int bankmax, u_int iop_bufaddr)
{
    (void)bufmax; (void)bankmax; (void)iop_bufaddr;
    return 1;
}

int sceCdStStart(u_int lsn, sceCdRMode* mode)
{
    (void)mode;
    st_lsn = lsn;
    st_active = 1;
    return 1;
}

int sceCdStRead(u_int sectors, u_int* buf, u_int mode, u_int* err)
{
    (void)mode;

    if (!st_active || !read_sectors(st_lsn, sectors, buf))
    {
        if (err != NULL)
        {
            *err = SCECdErREAD;
        }

        return 0;
    }

    st_lsn += sectors;

    if (err != NULL)
    {
        *err = SCECdErNO;
    }

    return sectors;
}

int sceCdStStop(void)
{
    st_active = 0;
    return 1;
}

int sceCdStSeek(u_int lsn)
{
    st_lsn = lsn;
    return 1;
}

int sceCdStPause(void)
{
    return 1;
}

int sceCdStResume(void)
{
    return 1;
}

/* extracted data folder in use, or NULL when reading the ISO */
const char* pcCdDataDir(void)
{
    return (data_count > 0) ? data_dir : NULL;
}
