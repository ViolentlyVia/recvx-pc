/* PC port: minimal PNG writer (stored deflate blocks) for debug screenshots. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static unsigned int crc_table[256];

static unsigned int crc32_update(unsigned int c, const unsigned char* p, size_t n)
{
    size_t i;

    if (crc_table[1] == 0)
    {
        unsigned int k, b;

        for (k = 0; k < 256; k++)
        {
            unsigned int v = k;

            for (b = 0; b < 8; b++)
            {
                v = (v & 1) ? 0xEDB88320u ^ (v >> 1) : v >> 1;
            }

            crc_table[k] = v;
        }
    }

    for (i = 0; i < n; i++)
    {
        c = crc_table[(c ^ p[i]) & 0xFF] ^ (c >> 8);
    }

    return c;
}

static void put32(FILE* f, unsigned int v)
{
    unsigned char b[4] = { (unsigned char)(v >> 24), (unsigned char)(v >> 16), (unsigned char)(v >> 8), (unsigned char)v };

    fwrite(b, 1, 4, f);
}

static void chunk(FILE* f, const char* type, const unsigned char* data, unsigned int len)
{
    unsigned int c;

    put32(f, len);
    fwrite(type, 1, 4, f);
    fwrite(data, 1, len, f);
    c = crc32_update(0xFFFFFFFFu, (const unsigned char*)type, 4);
    c = crc32_update(c, data, len);
    put32(f, c ^ 0xFFFFFFFFu);
}

/* rgb: w*h*3 bytes, top row first */
int pcWritePng(const char* path, const unsigned char* rgb, int w, int h)
{
    FILE* f = fopen(path, "wb");
    size_t raw_len = (size_t)(w * 3 + 1) * h;
    unsigned char* raw = malloc(raw_len);
    size_t blocks = (raw_len + 65534) / 65535;
    unsigned char* z = malloc(raw_len + blocks * 5 + 6);
    unsigned char ihdr[13];
    unsigned int a = 1, b = 0;
    size_t i, zp = 0, off;
    int y;

    if (f == NULL)
    {
        free(raw);
        free(z);
        return 0;
    }

    for (y = 0; y < h; y++)
    {
        raw[y * (w * 3 + 1)] = 0;
        memcpy(&raw[y * (w * 3 + 1) + 1], &rgb[y * w * 3], w * 3);
    }

    z[zp++] = 0x78;
    z[zp++] = 0x01;

    for (off = 0; off < raw_len; off += 65535)
    {
        size_t n = raw_len - off < 65535 ? raw_len - off : 65535;

        z[zp++] = (off + n >= raw_len) ? 1 : 0;
        z[zp++] = (unsigned char)n;
        z[zp++] = (unsigned char)(n >> 8);
        z[zp++] = (unsigned char)~n;
        z[zp++] = (unsigned char)(~n >> 8);
        memcpy(&z[zp], &raw[off], n);
        zp += n;
    }

    for (i = 0; i < raw_len; i++)
    {
        a = (a + raw[i]) % 65521;
        b = (b + a) % 65521;
    }

    z[zp++] = (unsigned char)(b >> 8);
    z[zp++] = (unsigned char)b;
    z[zp++] = (unsigned char)(a >> 8);
    z[zp++] = (unsigned char)a;

    fwrite("\x89PNG\r\n\x1a\n", 1, 8, f);
    ihdr[0] = (unsigned char)(w >> 24); ihdr[1] = (unsigned char)(w >> 16); ihdr[2] = (unsigned char)(w >> 8); ihdr[3] = (unsigned char)w;
    ihdr[4] = (unsigned char)(h >> 24); ihdr[5] = (unsigned char)(h >> 16); ihdr[6] = (unsigned char)(h >> 8); ihdr[7] = (unsigned char)h;
    ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
    chunk(f, "IHDR", ihdr, 13);
    chunk(f, "IDAT", z, (unsigned int)zp);
    chunk(f, "IEND", NULL, 0);
    fclose(f);

    free(raw);
    free(z);

    return 1;
}
