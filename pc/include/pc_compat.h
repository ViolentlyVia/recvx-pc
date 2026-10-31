/* PC port: the MSVC C runtime names the port uses, for other systems (Linux). Included by
 * pc_prefix.h before game code changes "long". */
#pragma once

#ifndef _WIN32

#include <ctype.h>
#include <dirent.h>
#include <fnmatch.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <sys/stat.h>
#include <sys/types.h>

#define _stricmp strcasecmp
#define _strnicmp strncasecmp
#define _strdup strdup
#define _fseeki64 fseeko
#define _ftelli64 ftello
#define _stat64 stat
#define _stat stat
#define _S_IFDIR S_IFDIR
#define _mkdir(path) mkdir((path), 0777)
#define _rmdir rmdir
#define _access access

/* from unistd.h, which would bring names that game and port code use (link) */
int access(const char* path, int mode);
int rmdir(const char* path);

/* _findfirst/_findnext/_findclose over opendir: "dir/pattern", names only */
struct _finddata_t
{
    char name[256];
};

typedef struct
{
    DIR* dir;
    char pattern[256];
} PcFind;

static inline void pc_find_lower(char* out, const char* in, size_t size)
{
    size_t i;

    for (i = 0; (i + 1 < size) && (in[i] != 0); i++)
    {
        out[i] = (char)tolower((unsigned char)in[i]);
    }

    out[i] = 0;
}

/* names matched without regard to case, as on Windows */
static inline int pc_find_next(PcFind* f, struct _finddata_t* fd)
{
    struct dirent* e;

    while ((e = readdir(f->dir)) != NULL)
    {
        char name[256];

        pc_find_lower(name, e->d_name, sizeof(name));

        if (fnmatch(f->pattern, name, 0) == 0)
        {
            snprintf(fd->name, sizeof(fd->name), "%s", e->d_name);
            return 0;
        }
    }

    return -1;
}

static inline intptr_t _findfirst(const char* spec, struct _finddata_t* fd)
{
    char dir[1024];
    const char* slash = strrchr(spec, '/');
    PcFind* f;

    if (slash != NULL)
    {
        snprintf(dir, sizeof(dir), "%.*s", (int)(slash - spec), spec);
    }
    else
    {
        snprintf(dir, sizeof(dir), ".");
    }

    if ((f = calloc(1, sizeof(PcFind))) == NULL)
    {
        return -1;
    }

    pc_find_lower(f->pattern, (slash != NULL) ? slash + 1 : spec, sizeof(f->pattern));

    if (((f->dir = opendir((dir[0] != 0) ? dir : "/")) == NULL) || (pc_find_next(f, fd) != 0))
    {
        if (f->dir != NULL)
        {
            closedir(f->dir);
        }

        free(f);
        return -1;
    }

    return (intptr_t)f;
}

static inline int _findnext(intptr_t h, struct _finddata_t* fd)
{
    return pc_find_next((PcFind*)h, fd);
}

static inline int _findclose(intptr_t h)
{
    closedir(((PcFind*)h)->dir);
    free((PcFind*)h);
    return 0;
}

#endif
