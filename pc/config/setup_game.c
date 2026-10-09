/* recvx-config.exe: setting the game up from the player's own disc image, without Python.
 *
 * The same steps as pc/tools/setup_game.py: every file of the ISO9660 image is copied into the
 * "data" folder next to recvx.exe, files.idx lists each file's start sector, size and path (the
 * game serves its sector reads from those files), and the movies (MOVIE/MV_*.PSS) are converted
 * for the game's MPEG-1 player. PSS audio is Sony "SShd"/"SSbd" PCM that FFmpeg cannot read, so
 * it is pulled out here into a WAV and FFmpeg (downloaded once into tools\ffmpeg, only used here)
 * writes MV_*.MPG: MPEG-1 video and MP2 audio. The work runs on a thread behind a progress window.
 *
 * "Upscale movies" (UpscaleMovies) makes MOVIE_HD/MV_*.MPG for the game to use instead: the disc
 * movie's frames are enlarged 4x by Real-ESRGAN (its realesr-animevideov3 model, run on the GPU
 * through Vulkan; downloaded once into tools\realesrgan) in batches, each batch is encoded as
 * MPEG-1 (a complete MPEG file each, so they keep their timing), and the pieces are joined by FFmpeg's
 * concat demuxer and given the converted movie's sound.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commctrl.h>
#include <wininet.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "setup_game.h"

#pragma comment(lib, "wininet.lib")

#define SECTOR 2048
#define EXPECTED_ID "SLUS_201.84"
#define FFMPEG_URL "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
#define REALESRGAN_URL "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip"
#define UPSCALE_BATCH 500   /* frames enlarged at a time (keeps temporary files to a few GB) */

#define WM_SETUP_PROGRESS (WM_APP + 1)   /* wparam: percent (or -1), lparam: unused */
#define WM_SETUP_DONE     (WM_APP + 2)   /* wparam: 1 ok, 0 failed, -1 cancelled */

typedef struct {
    char path[260];
    unsigned int lsn, size;
} IsoFile;

static struct {
    HWND wnd, bar, text;
    HANDLE thread;
    volatile LONG cancel;
    char iso[MAX_PATH];
    char exe_dir[MAX_PATH];
    char status[512];
    char error[1024];
    CRITICAL_SECTION lock;
    IsoFile* files;
    int nfiles, cap;
    int result;
    int job;            /* 0: set up from the disc image, 1: upscale the movies */
} su;

static void set_status(const char* fmt, ...)
{
    va_list ap;

    EnterCriticalSection(&su.lock);
    va_start(ap, fmt);
    vsnprintf(su.status, sizeof(su.status), fmt, ap);
    va_end(ap);
    LeaveCriticalSection(&su.lock);
}

static void progress(int percent)
{
    PostMessageA(su.wnd, WM_SETUP_PROGRESS, (WPARAM)percent, 0);
}

static int fail(const char* fmt, ...)
{
    va_list ap;

    va_start(ap, fmt);
    vsnprintf(su.error, sizeof(su.error), fmt, ap);
    va_end(ap);
    return 0;
}

/* ------------------------------------------------------------------------------------------- */
/* disc image */

static HANDLE iso_h;

static int iso_read(unsigned int lsn, void* buf, unsigned int bytes)
{
    LARGE_INTEGER pos;
    DWORD got = 0;

    pos.QuadPart = (LONGLONG)lsn * SECTOR;
    return SetFilePointerEx(iso_h, pos, NULL, FILE_BEGIN) && ReadFile(iso_h, buf, bytes, &got, NULL) && (got == bytes);
}

static void add_file(const char* path, unsigned int lsn, unsigned int size)
{
    if (su.nfiles == su.cap)
    {
        su.cap = su.cap ? su.cap * 2 : 256;
        su.files = (IsoFile*)realloc(su.files, su.cap * sizeof(IsoFile));
    }

    snprintf(su.files[su.nfiles].path, sizeof(su.files[0].path), "%s", path);
    su.files[su.nfiles].lsn = lsn;
    su.files[su.nfiles].size = size;
    su.nfiles++;
}

/* every file below the directory at lsn; records never cross sectors */
static int walk(unsigned int lsn, unsigned int size, const char* prefix, int depth)
{
    unsigned int sectors = (size + SECTOR - 1) / SECTOR, s;
    unsigned char* dir;

    if ((depth > 16) || (sectors == 0) || (sectors > 4096))
    {
        return 1;
    }

    dir = (unsigned char*)malloc(sectors * SECTOR);

    if (!iso_read(lsn, dir, sectors * SECTOR))
    {
        free(dir);
        return fail("Could not read the disc image's directories.");
    }

    for (s = 0; s < sectors; s++)
    {
        unsigned int off = 0;

        while (off < SECTOR)
        {
            unsigned char* rec = dir + s * SECTOR + off;
            unsigned int rec_len = rec[0], name_len;
            char name[256], path[260];
            char* semi;

            if (rec_len == 0)
            {
                break;
            }

            off += rec_len;
            name_len = rec[32];

            if ((name_len == 1) && (rec[33] <= 1))
            {
                continue;   /* "." and ".." */
            }

            memcpy(name, rec + 33, name_len);
            name[name_len] = 0;

            if ((semi = strchr(name, ';')) != NULL)
            {
                *semi = 0;
            }

            snprintf(path, sizeof(path), "%s%s", prefix, name);

            if (rec[25] & 2)
            {
                char sub[262];

                snprintf(sub, sizeof(sub), "%s/", path);

                if (!walk(*(unsigned int*)(rec + 2), *(unsigned int*)(rec + 10), sub, depth + 1))
                {
                    free(dir);
                    return 0;
                }
            }
            else
            {
                add_file(path, *(unsigned int*)(rec + 2), *(unsigned int*)(rec + 10));
            }
        }
    }

    free(dir);
    return 1;
}

static int cmp_lsn(const void* a, const void* b)
{
    unsigned int x = ((const IsoFile*)a)->lsn, y = ((const IsoFile*)b)->lsn;

    return (x < y) ? -1 : (x > y);
}

static void make_dirs(char* path)   /* every folder of path (a file path) */
{
    char* p;

    for (p = path + 3; *p; p++)
    {
        if ((*p == '\\') || (*p == '/'))
        {
            char c = *p;

            *p = 0;
            CreateDirectoryA(path, NULL);
            *p = c;
        }
    }
}

static int extract(const char* data_dir)
{
    unsigned char pvd[SECTOR];
    unsigned long long total = 0, done = 0;
    unsigned char* buf;
    char idx_path[MAX_PATH + 16];
    FILE* idx;
    int i, found_id = 0;

    iso_h = CreateFileA(su.iso, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, NULL);

    if (iso_h == INVALID_HANDLE_VALUE)
    {
        return fail("Could not open the disc image:\n%s", su.iso);
    }

    if (!iso_read(16, pvd, SECTOR) || (memcmp(pvd + 1, "CD001", 5) != 0))
    {
        CloseHandle(iso_h);
        return fail("This file is not a disc image (ISO9660):\n%s", su.iso);
    }

    su.nfiles = 0;

    if (!walk(*(unsigned int*)(pvd + 156 + 2), *(unsigned int*)(pvd + 156 + 10), "", 0))
    {
        CloseHandle(iso_h);
        return 0;
    }

    for (i = 0; i < su.nfiles; i++)
    {
        total += su.files[i].size;
        found_id |= (strcmp(su.files[i].path, EXPECTED_ID) == 0);
    }

    if (!found_id)
    {
        CloseHandle(iso_h);
        return fail("This disc image is not Resident Evil Code: Veronica X for the US PlayStation 2 "
                    "(SLUS-20184), which this port needs.");
    }

    {
        ULARGE_INTEGER avail;
        char root[MAX_PATH];

        snprintf(root, sizeof(root), "%s\\", su.exe_dir);

        if (GetDiskFreeSpaceExA(root, &avail, NULL, NULL) && (avail.QuadPart < total + (512ull << 20)))
        {
            CloseHandle(iso_h);
            return fail("Not enough free disk space: setting up needs about %.1f GB next to the game.",
                        (total + (512ull << 20)) / 1e9);
        }
    }

    CreateDirectoryA(data_dir, NULL);
    buf = (unsigned char*)malloc(8 << 20);

    for (i = 0; (i < su.nfiles) && !su.cancel; i++)
    {
        IsoFile* f = &su.files[i];
        char dest[MAX_PATH + 300];
        WIN32_FILE_ATTRIBUTE_DATA attr;
        char* p;

        snprintf(dest, sizeof(dest), "%s\\%s", data_dir, f->path);

        for (p = dest; *p; p++)
        {
            if (*p == '/')
            {
                *p = '\\';
            }
        }

        set_status("Copying game files: %s", f->path);

        /* already there from an earlier, interrupted setup */
        if (!(GetFileAttributesExA(dest, GetFileExInfoStandard, &attr) && (attr.nFileSizeHigh == 0) &&
              (attr.nFileSizeLow == f->size)))
        {
            HANDLE out;
            unsigned int left = f->size, lsn = f->lsn;

            make_dirs(dest);
            out = CreateFileA(dest, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);

            if (out == INVALID_HANDLE_VALUE)
            {
                free(buf);
                CloseHandle(iso_h);
                return fail("Could not write %s", dest);
            }

            while (left && !su.cancel)
            {
                unsigned int n = (left < (8u << 20)) ? left : (8u << 20);
                DWORD wrote = 0;

                if (!iso_read(lsn, buf, (n + SECTOR - 1) / SECTOR * SECTOR) && !iso_read(lsn, buf, n))
                {
                    CloseHandle(out);
                    free(buf);
                    CloseHandle(iso_h);
                    return fail("Could not read %s from the disc image.", f->path);
                }

                if (!WriteFile(out, buf, n, &wrote, NULL) || (wrote != n))
                {
                    CloseHandle(out);
                    free(buf);
                    CloseHandle(iso_h);
                    return fail("Could not write %s (is the disk full?)", dest);
                }

                left -= n;
                lsn += n / SECTOR;
                done += n;
                progress((int)(done * 100 / total));
            }

            CloseHandle(out);

            if (su.cancel)
            {
                DeleteFileA(dest);   /* not a complete copy */
            }
        }
        else
        {
            done += f->size;
            progress((int)(done * 100 / total));
        }
    }

    free(buf);
    CloseHandle(iso_h);

    if (su.cancel)
    {
        return 0;
    }

    /* the game's disc layout */
    qsort(su.files, su.nfiles, sizeof(IsoFile), cmp_lsn);
    snprintf(idx_path, sizeof(idx_path), "%s\\files.idx", data_dir);

    if ((idx = fopen(idx_path, "w")) == NULL)
    {
        return fail("Could not write %s", idx_path);
    }

    fprintf(idx, "# start sector, size, path (recvx.exe disc layout)\n");

    for (i = 0; i < su.nfiles; i++)
    {
        fprintf(idx, "%u %u %s\n", su.files[i].lsn, su.files[i].size, su.files[i].path);
    }

    fclose(idx);
    return 1;
}

/* ------------------------------------------------------------------------------------------- */
/* FFmpeg: found in tools\ffmpeg next to the program, or downloaded there once */

static int find_file(const char* dir, const char* name, char* out, int size, int depth)
{
    char pattern[MAX_PATH + 4];
    WIN32_FIND_DATAA fd;
    HANDLE h;
    int found = 0;

    snprintf(pattern, sizeof(pattern), "%s\\*", dir);

    if ((h = FindFirstFileA(pattern, &fd)) == INVALID_HANDLE_VALUE)
    {
        return 0;
    }

    do
    {
        char sub[MAX_PATH];

        if (fd.cFileName[0] == '.')
        {
            continue;
        }

        snprintf(sub, sizeof(sub), "%s\\%s", dir, fd.cFileName);

        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
        {
            found = (depth < 4) && find_file(sub, name, out, size, depth + 1);
        }
        else if (_stricmp(fd.cFileName, name) == 0)
        {
            snprintf(out, size, "%s", sub);
            found = 1;
        }
    } while (!found && FindNextFileA(h, &fd));

    FindClose(h);
    return found;
}

/* run a program without a window; returns its exit code (-1 if it could not start) */
static int run_hidden(char* cmdline, const char* cwd)
{
    STARTUPINFOA si;
    PROCESS_INFORMATION pi;
    DWORD code = (DWORD)-1;

    memset(&si, 0, sizeof(si));
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;

    if (!CreateProcessA(NULL, cmdline, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, cwd, &si, &pi))
    {
        return -1;
    }

    while (WaitForSingleObject(pi.hProcess, 200) == WAIT_TIMEOUT)
    {
        if (su.cancel)
        {
            TerminateProcess(pi.hProcess, 1);
        }
    }

    GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return (int)code;
}

/* <exe>\tools\<folder> holds a downloaded tool: download the zip from url and unpack it there
 * unless <name> is already in it; its full path goes to exe */
static int get_tool(const char* url, const char* folder, const char* name, const char* what, char* exe, int size)
{
    char tools[MAX_PATH], zip[MAX_PATH + 32], cmd[3 * MAX_PATH];
    HINTERNET net, inet;
    HANDLE out;
    unsigned char* buf;
    unsigned long long got = 0, total = 0;
    DWORD n, len = sizeof(DWORD), clen = 0;

    snprintf(tools, sizeof(tools), "%s\\tools\\%s", su.exe_dir, folder);

    if (find_file(tools, name, exe, size, 0))
    {
        return 1;
    }

    set_status("Downloading %s...", what);
    progress(0);
    snprintf(cmd, sizeof(cmd), "%s\\tools", su.exe_dir);
    CreateDirectoryA(cmd, NULL);
    CreateDirectoryA(tools, NULL);
    snprintf(zip, sizeof(zip), "%s\\download.zip", tools);

    net = InternetOpenA("recvx-config", INTERNET_OPEN_TYPE_PRECONFIG, NULL, NULL, 0);
    inet = net ? InternetOpenUrlA(net, url, NULL, 0, INTERNET_FLAG_RELOAD | INTERNET_FLAG_SECURE, 0) : NULL;

    if (inet == NULL)
    {
        if (net) InternetCloseHandle(net);
        return fail("Could not download %s. Check the internet connection and try again.", what);
    }

    if (HttpQueryInfoA(inet, HTTP_QUERY_CONTENT_LENGTH | HTTP_QUERY_FLAG_NUMBER, &clen, &len, NULL))
    {
        total = clen;
    }

    out = CreateFileA(zip, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    buf = (unsigned char*)malloc(1 << 20);

    while (!su.cancel && InternetReadFile(inet, buf, 1 << 20, &n) && (n > 0))
    {
        DWORD wrote;

        WriteFile(out, buf, n, &wrote, NULL);
        got += n;
        set_status("Downloading %s: %.0f MB", what, got / 1e6);
        progress(total ? (int)(got * 100 / total) : -1);
    }

    free(buf);
    CloseHandle(out);
    InternetCloseHandle(inet);
    InternetCloseHandle(net);

    if (su.cancel || (got < (1 << 20)))
    {
        DeleteFileA(zip);
        return su.cancel ? 0 : fail("The %s download failed. Try again later.", what);
    }

    /* tar.exe ships with Windows 10 and 11 and unpacks zip files. Its exit code is not trusted:
     * it can complain about a zip it unpacked completely. */
    set_status("Unpacking %s...", what);
    progress(-1);
    snprintf(cmd, sizeof(cmd), "tar.exe -xf \"%s\" -C \"%s\"", zip, tools);
    run_hidden(cmd, tools);
    DeleteFileA(zip);

    if (!find_file(tools, name, exe, size, 0))
    {
        return fail("Could not unpack %s (%s was not found in the download).", what, name);
    }

    return 1;
}

static int get_ffmpeg(char* exe, int size)
{
    return get_tool(FFMPEG_URL, "ffmpeg", "ffmpeg.exe", "FFmpeg (used once to convert the movies)", exe, size);
}

/* ------------------------------------------------------------------------------------------- */
/* movies */

static unsigned char* read_all(const char* path, unsigned int* size)
{
    FILE* f = fopen(path, "rb");
    unsigned char* d;
    long n;

    if (f == NULL)
    {
        return NULL;
    }

    fseek(f, 0, SEEK_END);
    n = ftell(f);
    fseek(f, 0, SEEK_SET);
    d = (unsigned char*)malloc(n + 16);

    if (fread(d, 1, n, f) != (size_t)n)
    {
        free(d);
        fclose(f);
        return NULL;
    }

    fclose(f);
    *size = (unsigned int)n;
    return d;
}

/* the PSS's Sony PCM audio (private stream 1, substream 0xFF) as a 16-bit WAV; 0 = no audio */
static int write_audio(const char* pss, const char* wav)
{
    unsigned int size, i = 0, alen = 0, rate, channels, interleave, kind, block, frames, b, c, s;
    unsigned char* d = read_all(pss, &size);
    unsigned char* aud;
    unsigned char* pcm;
    unsigned int pcm_len;
    FILE* f;

    if (d == NULL)
    {
        return 0;
    }

    aud = (unsigned char*)malloc(size);

    while (i + 6 <= size)
    {
        unsigned int sid;

        if (!((d[i] == 0) && (d[i + 1] == 0) && (d[i + 2] == 1)))
        {
            i++;
            continue;
        }

        sid = d[i + 3];

        if (sid == 0xBA)   /* pack header */
        {
            i += 14 + (d[i + 13] & 7);
        }
        else if (sid >= 0xBB)   /* PES packet */
        {
            unsigned int length = (d[i + 4] << 8) | d[i + 5];

            if (sid == 0xBD)
            {
                unsigned int p = i + 9 + d[i + 8];

                if ((p < size) && (d[p] == 0xFF) && (i + 6 + length <= size) && (p + 4 <= i + 6 + length))
                {
                    memcpy(aud + alen, d + p + 4, i + 6 + length - (p + 4));
                    alen += i + 6 + length - (p + 4);
                }
            }

            i += 6 + length;
        }
        else
        {
            i += 3;
        }
    }

    free(d);

    if ((alen < 40) || (memcmp(aud, "SShd", 4) != 0))
    {
        free(aud);
        return 0;
    }

    kind = *(unsigned int*)(aud + 8);
    rate = *(unsigned int*)(aud + 12);
    channels = *(unsigned int*)(aud + 16);
    interleave = *(unsigned int*)(aud + 20);

    if ((kind != 1) || (channels == 0) || (channels > 8) || (interleave == 0))
    {
        free(aud);
        return 0;
    }

    /* after SShd (32 bytes) and SSbd (8): blocks of <interleave> bytes per channel */
    block = interleave * channels;
    pcm_len = (alen - 40) / block * block;
    pcm = (unsigned char*)malloc(pcm_len + 1);
    frames = interleave / 2;

    for (b = 0; b < pcm_len; b += block)
    {
        for (c = 0; c < channels; c++)
        {
            const unsigned char* src = aud + 40 + b + c * interleave;

            for (s = 0; s < frames; s++)
            {
                unsigned int o = b + (s * channels + c) * 2;

                pcm[o] = src[s * 2];
                pcm[o + 1] = src[s * 2 + 1];
            }
        }
    }

    free(aud);

    if ((f = fopen(wav, "wb")) == NULL)
    {
        free(pcm);
        return 0;
    }

    {
        unsigned int byte_rate = rate * channels * 2, riff = 36 + pcm_len;
        unsigned short fmt_tag = 1, ch = (unsigned short)channels, align = (unsigned short)(channels * 2), bits = 16;
        unsigned int fmt_len = 16;

        fwrite("RIFF", 1, 4, f); fwrite(&riff, 4, 1, f); fwrite("WAVEfmt ", 1, 8, f);
        fwrite(&fmt_len, 4, 1, f); fwrite(&fmt_tag, 2, 1, f); fwrite(&ch, 2, 1, f);
        fwrite(&rate, 4, 1, f); fwrite(&byte_rate, 4, 1, f); fwrite(&align, 2, 1, f); fwrite(&bits, 2, 1, f);
        fwrite("data", 1, 4, f); fwrite(&pcm_len, 4, 1, f);
        fwrite(pcm, 1, pcm_len, f);
    }

    fclose(f);
    free(pcm);
    return 1;
}

static int convert_movies(const char* data_dir)
{
    char ffmpeg[MAX_PATH], pattern[MAX_PATH + 16], movie_dir[MAX_PATH + 8], tmp[MAX_PATH], wav[MAX_PATH + 16];
    char names[128][64];
    int count = 0, i;
    WIN32_FIND_DATAA fd;
    HANDLE h;

    snprintf(movie_dir, sizeof(movie_dir), "%s\\MOVIE", data_dir);
    snprintf(pattern, sizeof(pattern), "%s\\*.PSS", movie_dir);

    if ((h = FindFirstFileA(pattern, &fd)) != INVALID_HANDLE_VALUE)
    {
        do
        {
            if (count < 128)
            {
                snprintf(names[count++], sizeof(names[0]), "%s", fd.cFileName);
            }
        } while (FindNextFileA(h, &fd));

        FindClose(h);
    }

    if (count == 0)
    {
        return 1;
    }

    if (!get_ffmpeg(ffmpeg, sizeof(ffmpeg)))
    {
        return 0;
    }

    GetTempPathA(sizeof(tmp), tmp);
    snprintf(wav, sizeof(wav), "%srecvx_movie_audio.wav", tmp);

    for (i = 0; (i < count) && !su.cancel; i++)
    {
        char pss[MAX_PATH + 80], mpg[MAX_PATH + 80], cmd[4 * MAX_PATH + 512];
        WIN32_FILE_ATTRIBUTE_DATA a_pss, a_mpg;
        char* dot;
        int audio;

        snprintf(pss, sizeof(pss), "%s\\%s", movie_dir, names[i]);
        snprintf(mpg, sizeof(mpg), "%s", pss);

        if ((dot = strrchr(mpg, '.')) != NULL)
        {
            strcpy(dot, ".MPG");
        }

        set_status("Converting movies: %s (%d of %d)", names[i], i + 1, count);
        progress(i * 100 / count);

        if (GetFileAttributesExA(mpg, GetFileExInfoStandard, &a_mpg) && GetFileAttributesExA(pss, GetFileExInfoStandard, &a_pss) &&
            (CompareFileTime(&a_mpg.ftLastWriteTime, &a_pss.ftLastWriteTime) >= 0) && (a_mpg.nFileSizeLow > 0))
        {
            continue;   /* converted earlier */
        }

        audio = write_audio(pss, wav);

        if (audio)
        {
            snprintf(cmd, sizeof(cmd), "\"%s\" -hide_banner -loglevel error -y -i \"%s\" -i \"%s\" -map 0:v -map 1:a "
                     "-c:a mp2 -b:a 256k -shortest -c:v mpeg1video -q:v 2 -f mpeg \"%s\"", ffmpeg, pss, wav, mpg);
        }
        else
        {
            snprintf(cmd, sizeof(cmd), "\"%s\" -hide_banner -loglevel error -y -i \"%s\" -map 0:v "
                     "-c:v mpeg1video -q:v 2 -f mpeg \"%s\"", ffmpeg, pss, mpg);
        }

        if (run_hidden(cmd, movie_dir) != 0)
        {
            DeleteFileA(mpg);
            DeleteFileA(wav);

            if (su.cancel)
            {
                return 0;
            }

            return fail("Converting %s failed.", names[i]);
        }

        DeleteFileA(wav);
    }

    progress(100);
    return !su.cancel;
}

/* ------------------------------------------------------------------------------------------- */

/* deletes the files in dir that match pattern (e.g. "*.png") */
static void clear_dir(const char* dir, const char* pattern)
{
    char p[MAX_PATH + 16], f[2 * MAX_PATH];
    WIN32_FIND_DATAA fd;
    HANDLE h;

    snprintf(p, sizeof(p), "%s\\%s", dir, pattern);

    if ((h = FindFirstFileA(p, &fd)) == INVALID_HANDLE_VALUE)
    {
        return;
    }

    do
    {
        snprintf(f, sizeof(f), "%s\\%s", dir, fd.cFileName);
        DeleteFileA(f);
    } while (FindNextFileA(h, &fd));

    FindClose(h);
}

static int count_files(const char* dir, const char* pattern)
{
    char p[MAX_PATH + 16];
    WIN32_FIND_DATAA fd;
    HANDLE h;
    int n = 0;

    snprintf(p, sizeof(p), "%s\\%s", dir, pattern);

    if ((h = FindFirstFileA(p, &fd)) == INVALID_HANDLE_VALUE)
    {
        return 0;
    }

    do n++; while (FindNextFileA(h, &fd));

    FindClose(h);
    return n;
}

/* one movie: disc frames -> 4x frames (in batches) -> MPEG-1 pieces -> joined, with the sound of
 * the converted movie. Progress runs from done_frames to done_frames + its frames of all_frames. */
static int upscale_one(const char* ffmpeg, const char* esrgan, const char* movie_dir, const char* hd_dir,
                       const char* name, const char* work, int index, int count)
{
    char base[64], pss[MAX_PATH + 80], mpg[MAX_PATH + 80], out[MAX_PATH + 80], tmp_out[MAX_PATH + 96];
    char src[MAX_PATH + 16], up[MAX_PATH + 16], esr_dir[MAX_PATH], list[MAX_PATH + 16], cmd[6 * MAX_PATH];
    char* dot;
    FILE* lf;
    int frames, first, batch = 0;

    snprintf(base, sizeof(base), "%s", name);

    if ((dot = strrchr(base, '.')) != NULL)
    {
        *dot = 0;
    }

    snprintf(pss, sizeof(pss), "%s\\%s.PSS", movie_dir, base);
    snprintf(mpg, sizeof(mpg), "%s\\%s.MPG", movie_dir, base);
    snprintf(out, sizeof(out), "%s\\%s.MPG", hd_dir, base);
    snprintf(tmp_out, sizeof(tmp_out), "%s\\%s.MPG.part", hd_dir, base);
    snprintf(src, sizeof(src), "%s\\src", work);
    snprintf(up, sizeof(up), "%s\\up", work);
    snprintf(list, sizeof(list), "%s\\pieces.txt", work);
    snprintf(esr_dir, sizeof(esr_dir), "%s", esrgan);

    if ((dot = strrchr(esr_dir, '\\')) != NULL)
    {
        *dot = 0;   /* Real-ESRGAN finds its models next to it */
    }

    if (GetFileAttributesA(mpg) == INVALID_FILE_ATTRIBUTES)
    {
        return fail("%s has not been converted yet: set the game up first.", mpg);
    }

    CreateDirectoryA(src, NULL);
    CreateDirectoryA(up, NULL);
    clear_dir(src, "*.png");
    clear_dir(up, "*.png");
    clear_dir(work, "p*.mpg");

    /* the disc movie's frames */
    set_status("Upscaling movie %d of %d (%s): reading frames", index + 1, count, base);
    progress(index * 100 / count);
    snprintf(cmd, sizeof(cmd), "\"%s\" -hide_banner -loglevel error -y -i \"%s\" -map 0:v \"%s\\f%%06d.png\"", ffmpeg, pss, src);

    if (run_hidden(cmd, work) != 0)
    {
        return su.cancel ? 0 : fail("Reading the frames of %s failed.", base);
    }

    frames = count_files(src, "*.png");

    if (frames == 0)
    {
        return fail("%s has no frames.", base);
    }

    /* in batches: move a batch aside, enlarge it, encode it, throw the images away */
    for (first = 1; (first <= frames) && !su.cancel; first += UPSCALE_BATCH, batch++)
    {
        char bdir[MAX_PATH + 16], piece[MAX_PATH + 32];
        int k, last = first + UPSCALE_BATCH - 1;

        if (last > frames)
        {
            last = frames;
        }

        snprintf(bdir, sizeof(bdir), "%s\\batch", work);
        CreateDirectoryA(bdir, NULL);
        clear_dir(bdir, "*.png");

        for (k = first; k <= last; k++)
        {
            char a[MAX_PATH + 32], b[MAX_PATH + 32];

            snprintf(a, sizeof(a), "%s\\f%06d.png", src, k);
            snprintf(b, sizeof(b), "%s\\f%06d.png", bdir, k);
            MoveFileA(a, b);
        }

        set_status("Upscaling movie %d of %d (%s): frames %d-%d of %d", index + 1, count, base, first, last, frames);
        progress((index * 100 + (first - 1) * 100 / frames) / count);

        snprintf(cmd, sizeof(cmd), "\"%s\" -i \"%s\" -o \"%s\" -n realesr-animevideov3 -s 4 -f png", esrgan, bdir, up);

        if ((run_hidden(cmd, esr_dir) != 0) || (count_files(up, "*.png") != last - first + 1))
        {
            return su.cancel ? 0 : fail("Upscaling the frames of %s failed. Real-ESRGAN needs a graphics card "
                                        "with Vulkan support and an up-to-date driver.", base);
        }

        snprintf(piece, sizeof(piece), "%s\\p%04d.mpg", work, batch);
        snprintf(cmd, sizeof(cmd), "\"%s\" -hide_banner -loglevel error -y -framerate 30000/1001 -start_number %d "
                 "-i \"%s\\f%%06d.png\" -c:v mpeg1video -q:v 3 -g 15 -f mpeg \"%s\"", ffmpeg, first, up, piece);

        if (run_hidden(cmd, work) != 0)
        {
            return su.cancel ? 0 : fail("Encoding the upscaled frames of %s failed.", base);
        }

        clear_dir(bdir, "*.png");
        clear_dir(up, "*.png");
    }

    if (su.cancel)
    {
        return 0;
    }

    /* the pieces in order, for FFmpeg's concat demuxer (it keeps the timestamps running on) */
    if ((lf = fopen(list, "w")) == NULL)
    {
        return fail("Could not write %s.", list);
    }

    {
        int b;

        for (b = 0; b < batch; b++)
        {
            fprintf(lf, "file 'p%04d.mpg'\n", b);
        }
    }

    fclose(lf);

    /* with the converted movie's sound; written under another name until complete */
    set_status("Upscaling movie %d of %d (%s): finishing", index + 1, count, base);
    snprintf(cmd, sizeof(cmd), "\"%s\" -hide_banner -loglevel error -y -f concat -safe 0 -i \"%s\" "
             "-i \"%s\" -map 0:v -map 1:a -c copy -f mpeg \"%s\"", ffmpeg, list, mpg, tmp_out);

    if (run_hidden(cmd, work) != 0)
    {
        DeleteFileA(tmp_out);
        return su.cancel ? 0 : fail("Writing the upscaled %s failed.", base);
    }

    DeleteFileA(out);

    if (!MoveFileA(tmp_out, out))
    {
        return fail("Could not write %s.", out);
    }

    clear_dir(work, "p*.mpg");
    DeleteFileA(list);
    return 1;
}

static int upscale_movies(const char* data_dir)
{
    char ffmpeg[MAX_PATH], esrgan[MAX_PATH], movie_dir[MAX_PATH + 8], hd_dir[MAX_PATH + 16], work[MAX_PATH];
    char pattern[MAX_PATH + 16], names[128][64];
    int count = 0, i, ok = 1;
    WIN32_FIND_DATAA fd;
    HANDLE h;

    snprintf(movie_dir, sizeof(movie_dir), "%s\\MOVIE", data_dir);
    snprintf(hd_dir, sizeof(hd_dir), "%s\\MOVIE_HD", data_dir);
    snprintf(pattern, sizeof(pattern), "%s\\*.PSS", movie_dir);

    if ((h = FindFirstFileA(pattern, &fd)) != INVALID_HANDLE_VALUE)
    {
        do
        {
            if (count < 128)
            {
                snprintf(names[count++], sizeof(names[0]), "%s", fd.cFileName);
            }
        } while (FindNextFileA(h, &fd));

        FindClose(h);
    }

    if (count == 0)
    {
        return fail("No movies were found. Set the game up from the disc image first.");
    }

    {
        ULARGE_INTEGER avail;
        char root[MAX_PATH], t[MAX_PATH];

        snprintf(root, sizeof(root), "%s\\", data_dir);
        GetTempPathA(sizeof(t), t);

        if ((GetDiskFreeSpaceExA(root, &avail, NULL, NULL) && (avail.QuadPart < (3ull << 30))) ||
            (GetDiskFreeSpaceExA(t, &avail, NULL, NULL) && (avail.QuadPart < (5ull << 30))))
        {
            return fail("Not enough free disk space: upscaling needs about 2 GB next to the game and "
                        "5 GB of temporary space while it works.");
        }
    }

    if (!get_ffmpeg(ffmpeg, sizeof(ffmpeg)) ||
        !get_tool(REALESRGAN_URL, "realesrgan", "realesrgan-ncnn-vulkan.exe", "Real-ESRGAN (the AI upscaler)", esrgan, sizeof(esrgan)))
    {
        return 0;
    }

    CreateDirectoryA(hd_dir, NULL);
    GetTempPathA(sizeof(work), work);
    strncat(work, "recvx_upscale", sizeof(work) - strlen(work) - 1);
    CreateDirectoryA(work, NULL);

    for (i = 0; (i < count) && ok && !su.cancel; i++)
    {
        char pss[MAX_PATH + 80], out[MAX_PATH + 80], base[64];
        WIN32_FILE_ATTRIBUTE_DATA a_pss, a_out;
        char* dot;

        snprintf(base, sizeof(base), "%s", names[i]);

        if ((dot = strrchr(base, '.')) != NULL)
        {
            *dot = 0;
        }

        snprintf(pss, sizeof(pss), "%s\\%s", movie_dir, names[i]);
        snprintf(out, sizeof(out), "%s\\%s.MPG", hd_dir, base);

        /* done in an earlier run (upscaling can be stopped and continued) */
        if (GetFileAttributesExA(out, GetFileExInfoStandard, &a_out) && GetFileAttributesExA(pss, GetFileExInfoStandard, &a_pss) &&
            (CompareFileTime(&a_out.ftLastWriteTime, &a_pss.ftLastWriteTime) >= 0) && (a_out.nFileSizeLow > 0))
        {
            continue;
        }

        ok = upscale_one(ffmpeg, esrgan, movie_dir, hd_dir, names[i], work, i, count);
    }

    {
        char d[MAX_PATH + 16];

        snprintf(d, sizeof(d), "%s\\src", work); clear_dir(d, "*.png"); RemoveDirectoryA(d);
        snprintf(d, sizeof(d), "%s\\up", work); clear_dir(d, "*.png"); RemoveDirectoryA(d);
        snprintf(d, sizeof(d), "%s\\batch", work); clear_dir(d, "*.png"); RemoveDirectoryA(d);
        clear_dir(work, "p*.mpg");
        clear_dir(work, "pieces.txt");
        RemoveDirectoryA(work);
    }

    progress(100);
    return ok && !su.cancel;
}

static DWORD WINAPI worker(LPVOID param)
{
    char data_dir[MAX_PATH + 8];
    int ok;

    (void)param;
    snprintf(data_dir, sizeof(data_dir), "%s\\data", su.exe_dir);

    ok = (su.job == 1) ? upscale_movies(data_dir) : (extract(data_dir) && convert_movies(data_dir));

    PostMessageA(su.wnd, WM_SETUP_DONE, su.cancel ? (WPARAM)-1 : (WPARAM)ok, 0);
    return 0;
}

static LRESULT CALLBACK wnd_proc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg)
    {
    case WM_SETUP_PROGRESS:
    {
        char text[512];
        LONG_PTR style = GetWindowLongPtrA(su.bar, GWL_STYLE);

        if ((int)wp < 0)
        {
            if (!(style & PBS_MARQUEE))
            {
                SetWindowLongPtrA(su.bar, GWL_STYLE, style | PBS_MARQUEE);
                SendMessageA(su.bar, PBM_SETMARQUEE, TRUE, 30);
            }
        }
        else
        {
            if (style & PBS_MARQUEE)
            {
                SendMessageA(su.bar, PBM_SETMARQUEE, FALSE, 0);
                SetWindowLongPtrA(su.bar, GWL_STYLE, style & ~PBS_MARQUEE);
            }

            SendMessageA(su.bar, PBM_SETPOS, wp, 0);
        }

        EnterCriticalSection(&su.lock);
        snprintf(text, sizeof(text), "%s", su.status);
        LeaveCriticalSection(&su.lock);
        SetWindowTextA(su.text, text);
        return 0;
    }

    case WM_SETUP_DONE:
        su.result = (int)wp;
        DestroyWindow(hwnd);
        return 0;

    case WM_COMMAND:
        if (LOWORD(wp) == IDCANCEL)
        {
            InterlockedExchange(&su.cancel, 1);
            EnableWindow((HWND)lp, FALSE);
            SetWindowTextA(su.text, "Cancelling...");
        }
        return 0;

    case WM_CLOSE:
        InterlockedExchange(&su.cancel, 1);
        return 0;
    }

    return DefWindowProcA(hwnd, msg, wp, lp);
}

static int run_job(HWND owner, HFONT font, float dpi, const char* exe_dir, const char* iso_path, int job,
                   const char* title, char* error, int error_size);

int SetupGameFromIso(HWND owner, HFONT font, float dpi, const char* exe_dir, const char* iso_path, char* error, int error_size)
{
    return run_job(owner, font, dpi, exe_dir, iso_path, 0, "Setting up the game", error, error_size);
}

int UpscaleMovies(HWND owner, HFONT font, float dpi, const char* exe_dir, char* error, int error_size)
{
    return run_job(owner, font, dpi, exe_dir, "", 1, "Upscaling the movies", error, error_size);
}

static int run_job(HWND owner, HFONT font, float dpi, const char* exe_dir, const char* iso_path, int job,
                   const char* title, char* error, int error_size)
{
    WNDCLASSA wc;
    RECT r;
    MSG m;
    HWND btn;
    int w = (int)(460 * dpi), h = (int)(150 * dpi);

    memset(&su, 0, sizeof(su));
    InitializeCriticalSection(&su.lock);
    snprintf(su.iso, sizeof(su.iso), "%s", iso_path);
    snprintf(su.exe_dir, sizeof(su.exe_dir), "%s", exe_dir);
    snprintf(su.status, sizeof(su.status), job ? "Getting ready..." : "Reading the disc image...");
    su.job = job;

    memset(&wc, 0, sizeof(wc));
    wc.lpfnWndProc = wnd_proc;
    wc.hInstance = GetModuleHandle(NULL);
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
    wc.lpszClassName = "recvx_setup";
    RegisterClassA(&wc);

    GetWindowRect(owner, &r);
    su.wnd = CreateWindowExA(WS_EX_DLGMODALFRAME, "recvx_setup", title,
                             WS_POPUP | WS_CAPTION | WS_VISIBLE, (r.left + r.right - w) / 2, (r.top + r.bottom - h) / 2,
                             w, h, owner, NULL, wc.hInstance, NULL);
    su.text = CreateWindowExA(0, "STATIC", su.status, WS_CHILD | WS_VISIBLE | SS_LEFT | SS_ENDELLIPSIS,
                              (int)(16 * dpi), (int)(14 * dpi), w - (int)(40 * dpi), (int)(20 * dpi), su.wnd, NULL, wc.hInstance, NULL);
    su.bar = CreateWindowExA(0, PROGRESS_CLASSA, "", WS_CHILD | WS_VISIBLE, (int)(16 * dpi), (int)(42 * dpi),
                             w - (int)(40 * dpi), (int)(20 * dpi), su.wnd, NULL, wc.hInstance, NULL);
    btn = CreateWindowExA(0, "BUTTON", "Cancel", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON, w - (int)(124 * dpi),
                          (int)(74 * dpi), (int)(100 * dpi), (int)(28 * dpi), su.wnd, (HMENU)IDCANCEL, wc.hInstance, NULL);
    SendMessageA(su.text, WM_SETFONT, (WPARAM)font, TRUE);
    SendMessageA(btn, WM_SETFONT, (WPARAM)font, TRUE);
    SendMessageA(su.bar, PBM_SETRANGE, 0, MAKELPARAM(0, 100));

    EnableWindow(owner, FALSE);
    su.result = -2;
    su.thread = CreateThread(NULL, 0, worker, NULL, 0, NULL);

    while ((su.result == -2) && GetMessageA(&m, NULL, 0, 0) > 0)
    {
        if (!IsDialogMessageA(su.wnd, &m))
        {
            TranslateMessage(&m);
            DispatchMessageA(&m);
        }
    }

    WaitForSingleObject(su.thread, INFINITE);
    CloseHandle(su.thread);
    EnableWindow(owner, TRUE);
    SetForegroundWindow(owner);

    snprintf(error, error_size, "%s", su.error);
    free(su.files);
    DeleteCriticalSection(&su.lock);
    return su.result;
}

int SetupGameIsReady(const char* exe_dir, char* data_dir, int size)
{
    char idx[MAX_PATH + 32];

    snprintf(data_dir, size, "%s\\data", exe_dir);
    snprintf(idx, sizeof(idx), "%s\\files.idx", data_dir);
    return GetFileAttributesA(idx) != INVALID_FILE_ATTRIBUTES;
}
