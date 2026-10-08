/* PC port: replacement for TSNDDRV.IRX, Tamsoft's IOP sound driver (sound effects and the
 * MIDI-sequenced sounds; music and voices are ADX streams handled elsewhere).
 *
 * Speaks the module's protocol so the EE sound code (ps2_snddrv.c, ps2_sg_sd.c) runs unchanged:
 *   RPC id 0 - request stream: [cmd byte, port, ...] entries terminated by 0xFF
 *   RPC id 1 - "get state": fno is the query, the reply goes to receive[0]
 *   SND_STATUS block in IOP memory, read by the EE every frame through sceSifGetOtherData
 *
 * Banks use Sony's standard hardware-synth format (MODHSYN): an HD header ("IECSsreV",
 * "IECSdaeH", "IECSigaV" VAG list, "IECSlpmS" samples, "IECStesS" sample sets, "IECSgorP"
 * programs with split blocks) and a BD body of SPU ADPCM data; sequences ("IECSuqeS"/"IECSidiM")
 * are Sony-compressed MIDI. All uploads arrive through the IOP staging buffer.
 *
 * Sound effects (cmd 0x00-0x10): request = port (SE bank slot) << 16 | program << 8 | channel;
 * the program is played at its sample's base note on that channel (one sound per channel).
 * MIDI (cmd 0x20/0x21/0x27): [0x20, port, bank, volume, sequence] plays a sequence of a MIDI bank
 * on a player port. Voices use the SPU's ADPCM decoding, loop flags and ADSR envelopes and are
 * mixed at 48 kHz a few frames ahead into one mixer voice. The EE marks channels / ports as
 * playing in se_info / midi_info; the driver clears the bits when they end.
 *
 * Each sample's SPU attribute byte routes its voice dry and/or into the reverb of SPU core 0 or 1;
 * reverb (cmd 0x44) emulates the SPU reverberator with the Hall preset, the only one the game uses.
 *
 * Debug: RECVX_SNDLOG=1 logs sequence starts and reverb changes, RECVX_SEDUMP=<file> dumps the mix.
 */
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "sce_pc.h"
#include "pc_platform.h"
#include "pc_iop.h"
#include "pc_audio.h"

/* IOP memory layout used by this driver */
#define STATUS_ADDR 0x1000    /* SND_STATUS */
#define TABLE_ADDR  0x1100    /* iop_data_adr[16] read by SdrInit() */
#define HD_ADDR     0x2000    /* bank headers (IOP_hd_size[] consecutive) */
#define SQ_ADDR     0x10000   /* sequences (IOP_tq_size[] consecutive) */
#define DATA_ADDR   0x20000   /* staging buffer for HD / BD / SQ uploads (BD double buffered) */
#define DATA_ALT    204800    /* second BD buffer, relative to DATA_ADDR */

#define OUT_RATE     48000
#define MAX_BANKS    8
#define SE_PORTS     5
#define SE_CHANNELS  16
#define MIDI_BANKS   4
#define MIDI_PORTS   8
#define MAX_VOICES   48

typedef struct {
    unsigned short se_info[6];
    unsigned short midi_info;
    short port_info[8];
    short midi_sum[4];
    short se_sum[5];
    unsigned short dummy[9];
} SndStatus;

static SndStatus* status(void)
{
    return (SndStatus*)pcIopPtr(STATUS_ADDR, sizeof(SndStatus));
}

/* SE port -> SPU bank (SE_BANK[] in ps2_sg_sd.c); MIDI bank n uses SPU bank n (MIDI_BANK[]) */
static const int se_bank_of_port[SE_PORTS] = { 0, 4, 5, 6, 7 };

typedef struct {
    unsigned char* data;
    unsigned int size;
} Blob;

static Blob se_hd[SE_PORTS];          /* SE headers by port */
static Blob midi_hd[MIDI_BANKS];      /* MIDI headers by bank */
static Blob midi_sq[MIDI_BANKS];      /* MIDI sequences by bank */
static Blob bodies[MAX_BANKS];        /* sample data by SPU bank */

static unsigned int rd32(const unsigned char* p) { return p[0] | (p[1] << 8) | (p[2] << 16) | ((unsigned int)p[3] << 24); }
static unsigned int rd16(const unsigned char* p) { return p[0] | (p[1] << 8); }

/* entry i of the offset table of the chunk at off (0xFFFFFFFF = none) */
static unsigned int chunk_entry(const Blob* b, unsigned int off, unsigned int i)
{
    unsigned int e;

    if ((off == 0xFFFFFFFF) || (off + 16 > b->size) || (i > rd32(b->data + off + 12)) || (off + 20 + 4 * i > b->size))
    {
        return 0xFFFFFFFF;
    }

    e = rd32(b->data + off + 16 + 4 * i);

    return (e == 0xFFFFFFFF) ? e : off + e;
}

/* ------------------------------------------------------------------------------------------- */
/* voices */

enum { OWNER_SE, OWNER_MIDI };

/* sample SPU attributes (last byte of a sample record): dry / reverb sends and the SPU core */
#define SPU_ATTR_DRY_L  0x01
#define SPU_ATTR_DRY_R  0x02
#define SPU_ATTR_WET_L  0x04
#define SPU_ATTR_WET_R  0x08
#define SPU_ATTR_CORE1  0x20

typedef struct {
    int active;
    int owner, port, channel, note;   /* SE: port/channel; MIDI: player port/MIDI channel/note */
    unsigned int serial;
    const unsigned char* bd;
    unsigned int bd_size;
    unsigned int addr;                /* next ADPCM block (byte offset), 0xFFFFFFFF = end */
    unsigned int loop_addr;
    int has_loop;
    short block[28];
    int block_pos;
    int hist1, hist2;
    unsigned int frac;                /* 16.16 position within the decoded block */
    unsigned int step;                /* 16.16 source samples per output sample */
    double base_step;
    int bend_up, bend_down;           /* bend range in 1/128 semitone */
    float bank_vol;                   /* program * split * sample volume */
    float vol;                        /* request / velocity * channel volume */
    float scale;                      /* MIDI port volume */
    int pan;
    int spu_attr;                     /* sample's SPU routing: SPU_ATTR_* */
    float vol_l, vol_r;
    unsigned short adsr1, adsr2;
    int phase;                        /* 0 attack 1 decay 2 sustain 3 release 4 off */
    int env;
    int env_counter;
} Voice;

static Voice voices[MAX_VOICES];
static unsigned int voice_serial;
static int audio_voice = -1;
static float master = 1.0f;
static int mono;

static const int adpcm_f0[5] = { 0, 60, 115, 98, 122 };
static const int adpcm_f1[5] = { 0, 0, -52, -55, -60 };

static void decode_block(Voice* v)
{
    const unsigned char* b;
    int shift, filter, flags, i;

    if (v->addr + 16 > v->bd_size)
    {
        v->addr = 0xFFFFFFFF;
        memset(v->block, 0, sizeof(v->block));
        return;
    }

    b = v->bd + v->addr;
    shift = b[0] & 15;
    filter = (b[0] >> 4) & 7;
    flags = b[1];

    if (filter > 4) filter = 4;
    if (shift > 12) shift = 9;

    if (flags & 4)
    {
        v->loop_addr = v->addr;
        v->has_loop = 1;
    }

    for (i = 0; i < 28; i++)
    {
        int nib = (b[2 + i / 2] >> ((i & 1) * 4)) & 15;
        int s = (short)(nib << 12) >> shift;

        s += (v->hist1 * adpcm_f0[filter] + v->hist2 * adpcm_f1[filter] + 32) >> 6;

        if (s > 32767) s = 32767;
        if (s < -32768) s = -32768;

        v->hist2 = v->hist1;
        v->hist1 = s;
        v->block[i] = (short)s;
    }

    /* loop end flag 1: jump to the loop start with repeat flag 2, otherwise the voice ends */
    if (flags & 1)
    {
        v->addr = ((flags & 2) && v->has_loop) ? v->loop_addr : 0xFFFFFFFF;
    }
    else
    {
        v->addr += 16;
    }
}

/* SPU envelope step (rate 0..127, exponential/decreasing as on the hardware) */
static int env_tick(Voice* v, int rate, int decreasing, int exponential)
{
    int step = 7 - (rate & 3);
    int inc = 0x8000;
    int level = v->env;

    if (decreasing) step = ~step;

    if (rate < 44)
    {
        step <<= (11 - (rate >> 2));
    }
    else if (rate >= 48)
    {
        inc >>= ((rate >> 2) - 11);
    }

    if (exponential)
    {
        if (decreasing)
        {
            step = (step * level) >> 15;
        }
        else if (level >= 0x6000)
        {
            if (rate < 40) step >>= 2;
            else if (rate >= 44) inc >>= 2;
            else { step >>= 1; inc >>= 1; }
        }
    }

    v->env_counter += inc;

    if (!(v->env_counter & 0x8000))
    {
        return level;
    }

    v->env_counter = 0;
    level += step;

    return (level < 0) ? 0 : (level > 0x7FFF) ? 0x7FFF : level;
}

static void env_update(Voice* v)
{
    switch (v->phase)
    {
    case 0:
    {
        int rate = (v->adsr1 >> 8) & 0x7F;

        if (rate != 0x7F)
        {
            v->env = env_tick(v, rate, 0, v->adsr1 >> 15);
        }

        if (v->env >= 0x7FFF)
        {
            v->phase = 1;
            v->env_counter = 0;
        }
        break;
    }
    case 1:
        v->env = env_tick(v, ((v->adsr1 >> 4) & 15) << 2, 1, 1);

        if (v->env <= (((v->adsr1 & 15) + 1) * 0x800))
        {
            v->phase = 2;
            v->env_counter = 0;
        }
        break;
    case 2:
    {
        int rate = (v->adsr2 >> 6) & 0x7F;

        if (rate != 0x7F)
        {
            v->env = env_tick(v, rate, (v->adsr2 >> 14) & 1, v->adsr2 >> 15);
        }
        break;
    }
    case 3:
        v->env = env_tick(v, (v->adsr2 & 0x1F) << 2, 1, (v->adsr2 >> 5) & 1);

        if (v->env <= 0)
        {
            v->phase = 4;
        }
        break;
    }
}

static void voice_free(Voice* v)
{
    if (!v->active)
    {
        return;
    }

    v->active = 0;

    if (v->owner == OWNER_SE)
    {
        status()->se_info[v->port] &= (unsigned short)~(1 << v->channel);
    }
}

static void voice_release(Voice* v)
{
    if (v->active && (v->phase < 3))
    {
        v->phase = 3;
        v->env_counter = 0;
    }
}

static void voice_update_volume(Voice* v)
{
    int pan = mono ? 64 : v->pan;
    float l, r, amp = v->bank_vol * v->vol * v->scale;

    if (pan < 0) pan = 0;
    if (pan > 127) pan = 127;

    /* 0 = left, 64 = centre, 127 = right */
    l = (pan <= 64) ? 1.0f : (127 - pan) / 63.0f;
    r = (pan >= 64) ? 1.0f : pan / 64.0f;

    v->vol_l = amp * l;
    v->vol_r = amp * r;
}

static void voice_set_bend(Voice* v, int bend)
{
    double semis = 0;

    if (bend > 8192)
    {
        semis = (bend - 8192) / 8192.0 * (v->bend_up / 128.0);
    }
    else if (bend < 8192)
    {
        semis = (bend - 8192) / 8192.0 * (v->bend_down / 128.0);
    }

    v->step = (unsigned int)(v->base_step * pow(2.0, semis / 12.0) * 65536.0);
}

static Voice* voice_alloc(void)
{
    Voice* best = NULL;
    int i;

    for (i = 0; i < MAX_VOICES; i++)
    {
        if (!voices[i].active)
        {
            return &voices[i];
        }
    }

    /* steal the oldest releasing voice, else the oldest one */
    for (i = 0; i < MAX_VOICES; i++)
    {
        Voice* v = &voices[i];

        if ((best == NULL) || ((v->phase >= 3) && (best->phase < 3)) ||
            (((v->phase >= 3) == (best->phase >= 3)) && (v->serial < best->serial)))
        {
            best = v;
        }
    }

    voice_free(best);
    return best;
}

/* Start a note of a program from bank (hd, bd). Returns the voice or NULL. */
static Voice* note_on(const Blob* hd, const Blob* bd, int prog, int note, int velocity, int owner, int port,
                      int channel)
{
    unsigned int prog_chunk, sset_chunk, smp_chunk, vag_chunk, p, split = 0xFFFFFFFF, ss, smp = 0xFFFFFFFF, vag;
    int nsplit, split_size, k, base_note, transpose, detune, prog_vol, split_vol, smp_vol, pan;
    unsigned int rate;
    Voice* v;

    if ((hd->data == NULL) || (bd->data == NULL) || (hd->size < 0x40))
    {
        return NULL;
    }

    prog_chunk = rd32(hd->data + 0x10 + 0x14);
    sset_chunk = rd32(hd->data + 0x10 + 0x18);
    smp_chunk = rd32(hd->data + 0x10 + 0x1C);
    vag_chunk = rd32(hd->data + 0x10 + 0x20);

    p = chunk_entry(hd, prog_chunk, prog);

    if ((p == 0xFFFFFFFF) || (p + 0x24 > hd->size))
    {
        return NULL;
    }

    nsplit = hd->data[p + 4];
    split_size = hd->data[p + 5];
    prog_vol = hd->data[p + 6];
    pan = (signed char)hd->data[p + 7];
    transpose = (signed char)hd->data[p + 8];
    detune = (signed char)hd->data[p + 9];

    /* split block by key range */
    for (k = 0; k < nsplit; k++)
    {
        unsigned int b = p + rd32(hd->data + p) + k * split_size;

        if (b + 20 > hd->size) break;

        if ((note >= hd->data[b + 2]) && (note <= hd->data[b + 4]))
        {
            split = b;
            break;
        }
    }

    if (split == 0xFFFFFFFF)
    {
        return NULL;
    }

    split_vol = hd->data[split + 16];
    pan += (signed char)hd->data[split + 17] - 64;
    transpose += (signed char)hd->data[split + 18];
    detune += (signed char)hd->data[split + 19];

    /* sample set -> sample by velocity */
    ss = chunk_entry(hd, sset_chunk, rd16(hd->data + split));

    if ((ss == 0xFFFFFFFF) || (ss + 4 > hd->size))
    {
        return NULL;
    }

    for (k = 0; k < hd->data[ss + 3]; k++)
    {
        unsigned int s = chunk_entry(hd, smp_chunk, rd16(hd->data + ss + 4 + 2 * k));

        if ((s == 0xFFFFFFFF) || (s + 42 > hd->size)) continue;

        if ((velocity >= hd->data[s + 2]) && (velocity <= hd->data[s + 4]))
        {
            smp = s;
            break;
        }

        if (smp == 0xFFFFFFFF) smp = s;
    }

    if (smp == 0xFFFFFFFF)
    {
        return NULL;
    }

    base_note = hd->data[smp + 11];
    detune += (signed char)hd->data[smp + 12];
    pan += (signed char)hd->data[smp + 13] - 64;
    smp_vol = hd->data[smp + 16];

    vag = chunk_entry(hd, vag_chunk, rd16(hd->data + smp));

    if ((vag == 0xFFFFFFFF) || (vag + 8 > hd->size) || (rd32(hd->data + vag) >= bd->size))
    {
        return NULL;
    }

    rate = rd16(hd->data + vag + 4);

    v = voice_alloc();
    memset(v, 0, sizeof(*v));
    v->active = 1;
    v->owner = owner;
    v->port = port;
    v->channel = channel;
    v->note = note;
    v->serial = ++voice_serial;
    v->bd = bd->data;
    v->bd_size = bd->size;
    v->addr = rd32(hd->data + vag);
    v->adsr1 = (unsigned short)rd16(hd->data + smp + 18);
    v->adsr2 = (unsigned short)rd16(hd->data + smp + 20);
    v->bend_down = rd16(hd->data + split + 6);
    v->bend_up = rd16(hd->data + split + 8);
    v->base_step = (double)rate / OUT_RATE *
                   pow(2.0, (note - base_note + transpose + detune / 128.0) / 12.0);
    v->bank_vol = (prog_vol / 127.0f) * (split_vol / 127.0f) * (smp_vol / 127.0f);
    v->vol = velocity / 127.0f;
    v->scale = 1.0f;
    v->pan = pan;
    v->spu_attr = hd->data[smp + 41];
    v->block_pos = 28;

    voice_update_volume(v);
    voice_set_bend(v, 8192);

    return v;
}

/* ------------------------------------------------------------------------------------------- */
/* sound effects */

static Voice* se_voice(int port, int channel)
{
    int i;

    for (i = 0; i < MAX_VOICES; i++)
    {
        Voice* v = &voices[i];

        if (v->active && (v->owner == OWNER_SE) && (v->port == port) && (v->channel == channel))
        {
            return v;
        }
    }

    return NULL;
}

static void se_play(int port, int prog, int channel, int vol, int pan, int pitch)
{
    const Blob* hd = &se_hd[port];
    unsigned int prog_chunk, p, split, ss, smp;
    Voice* v;
    int note = 60;

    channel &= SE_CHANNELS - 1;

    if ((v = se_voice(port, channel)) != NULL)
    {
        voice_free(v);
    }

    /* sound effects play at their sample's own pitch: use the sample's base note */
    if ((hd->data != NULL) && (hd->size >= 0x40))
    {
        prog_chunk = rd32(hd->data + 0x10 + 0x14);
        p = chunk_entry(hd, prog_chunk, prog);

        if ((p != 0xFFFFFFFF) && (p + 0x24 <= hd->size))
        {
            split = p + rd32(hd->data + p);

            if (split + 20 <= hd->size)
            {
                ss = chunk_entry(hd, rd32(hd->data + 0x10 + 0x18), rd16(hd->data + split));

                if ((ss != 0xFFFFFFFF) && (ss + 6 <= hd->size))
                {
                    smp = chunk_entry(hd, rd32(hd->data + 0x10 + 0x1C), rd16(hd->data + ss + 4));

                    if ((smp != 0xFFFFFFFF) && (smp + 42 <= hd->size))
                    {
                        note = hd->data[smp + 11];
                    }
                }
            }
        }
    }

    v = note_on(hd, &bodies[se_bank_of_port[port]], prog, note, 127, OWNER_SE, port, channel);

    if (v == NULL)
    {
        return;
    }

    if (vol >= 0) v->vol = vol / 127.0f;
    if (pan >= 0) v->pan = pan;

    voice_update_volume(v);
    voice_set_bend(v, (pitch >= 0) ? pitch : 8192);

    status()->se_info[port] |= (unsigned short)(1 << channel);
}

static void se_change(int port, int channel, int vol, int pan, int pitch)
{
    Voice* v = se_voice(port, channel & (SE_CHANNELS - 1));

    if (v == NULL)
    {
        return;
    }

    if (vol >= 0) v->vol = vol / 127.0f;
    if (pan >= 0) v->pan = pan;

    voice_update_volume(v);

    if (pitch >= 0)
    {
        voice_set_bend(v, pitch);
    }
}

/* ------------------------------------------------------------------------------------------- */
/* MIDI sequences */

typedef struct {
    int active;
    int bank;
    const unsigned char* data;
    unsigned int len, pos;
    int running;
    double wait;                  /* output samples until the next event */
    double samples_per_tick;
    int division;
    int program[16], volume[16], pan[16], bend[16];
    float port_vol;
    int port_pan, port_pitch;
} MidiPort;

static MidiPort midi[MIDI_PORTS];

static unsigned int read_vlq(MidiPort* m)
{
    unsigned int v = 0;

    while (m->pos < m->len)
    {
        unsigned char b = m->data[m->pos++];

        v = (v << 7) | (b & 0x7F);

        if (!(b & 0x80))
        {
            break;
        }
    }

    return v;
}

static void midi_voices_update(int port, int channel)
{
    MidiPort* m = &midi[port];
    int i;

    for (i = 0; i < MAX_VOICES; i++)
    {
        Voice* v = &voices[i];

        if (v->active && (v->owner == OWNER_MIDI) && (v->port == port) && ((channel < 0) || (v->channel == channel)))
        {
            int ch = v->channel;

            v->pan = m->pan[ch] + m->port_pan - 64;
            voice_update_volume(v);
            voice_set_bend(v, m->bend[ch] + (m->port_pitch - 8192));
        }
    }
}

static void midi_stop(int port)
{
    int i;

    for (i = 0; i < MAX_VOICES; i++)
    {
        if (voices[i].active && (voices[i].owner == OWNER_MIDI) && (voices[i].port == port))
        {
            voice_release(&voices[i]);
        }
    }

    midi[port].active = 0;
    status()->midi_info &= (unsigned short)~(1 << port);
}

static void midi_play(int port, int bank, int vol, int seq)
{
    MidiPort* m = &midi[port];
    const Blob* sq = &midi_sq[bank];
    unsigned int chunk, block, end;
    int i;

    midi_stop(port);

    if ((bank >= MIDI_BANKS) || (sq->data == NULL) || (sq->size < 0x40))
    {
        return;
    }

    /* "IECSuqeS" header at 0x10: the MIDI chunk offset is at +0x14 */
    chunk = rd32(sq->data + 0x10 + 0x14);
    block = chunk_entry(sq, chunk, seq);

    if ((block == 0xFFFFFFFF) || (block + 8 > sq->size))
    {
        return;
    }

    end = (chunk + rd32(sq->data + chunk + 8) <= sq->size) ? chunk + rd32(sq->data + chunk + 8) : sq->size;

    memset(m, 0, sizeof(*m));
    m->active = 1;
    m->bank = bank;
    m->data = sq->data + block + rd32(sq->data + block);
    m->len = (end > block + rd32(sq->data + block)) ? end - (block + rd32(sq->data + block)) : 0;
    m->division = rd16(sq->data + block + 4);
    m->samples_per_tick = 500000.0 * OUT_RATE / (1e6 * (m->division ? m->division : 480));
    m->port_vol = vol / 127.0f;
    m->port_pan = 64;
    m->port_pitch = 8192;

    for (i = 0; i < 16; i++)
    {
        m->volume[i] = 127;
        m->pan[i] = 64;
        m->bend[i] = 8192;
    }

    m->wait = read_vlq(m) * m->samples_per_tick;

    if (getenv("RECVX_SNDLOG")) pcLog("snd: midi port %d bank %d seq %d vol %d len %u", port, bank, seq, vol, m->len);

    status()->midi_info |= (unsigned short)(1 << port);
}

/* Sony's compressed MIDI: note-off has no velocity; a last data byte with bit 7 set means the
 * next event follows with no delta time. */
static void midi_event(int port)
{
    MidiPort* m = &midi[port];
    int status_byte, ch, implied = 0;
    unsigned char a = 0, b = 0;

    if (m->pos >= m->len)
    {
        midi_stop(port);
        return;
    }

    if (m->data[m->pos] & 0x80)
    {
        status_byte = m->data[m->pos++];

        if (status_byte != 0xFF)
        {
            m->running = status_byte;
        }
    }
    else
    {
        status_byte = m->running;
    }

    if (status_byte == 0xFF)
    {
        int type = m->data[m->pos], len;

        m->pos++;
        len = m->data[m->pos++];

        if ((type == 0x51) && (len == 3))
        {
            unsigned int tempo = (m->data[m->pos] << 16) | (m->data[m->pos + 1] << 8) | m->data[m->pos + 2];

            m->samples_per_tick = tempo * (double)OUT_RATE / (1e6 * (m->division ? m->division : 480));
        }

        m->pos += len;

        if (type == 0x2F)
        {
            midi_stop(port);
            return;
        }
    }
    else
    {
        ch = status_byte & 15;

        switch (status_byte & 0xF0)
        {
        case 0x80:
        {
            int i;

            a = m->data[m->pos++];
            implied = a & 0x80;
            a &= 0x7F;

            for (i = 0; i < MAX_VOICES; i++)
            {
                Voice* v = &voices[i];

                if (v->active && (v->owner == OWNER_MIDI) && (v->port == port) && (v->channel == ch) && (v->note == a))
                {
                    voice_release(v);
                }
            }
            break;
        }
        case 0x90:
            a = m->data[m->pos++];
            b = m->data[m->pos++];
            implied = b & 0x80;
            b &= 0x7F;

            if (b == 0)
            {
                int i;

                for (i = 0; i < MAX_VOICES; i++)
                {
                    Voice* v = &voices[i];

                    if (v->active && (v->owner == OWNER_MIDI) && (v->port == port) && (v->channel == ch) && (v->note == a))
                    {
                        voice_release(v);
                    }
                }
            }
            else
            {
                Voice* v = note_on(&midi_hd[m->bank], &bodies[m->bank], m->program[ch], a, b, OWNER_MIDI, port, ch);

                if (v != NULL)
                {
                    v->vol = (b / 127.0f) * (m->volume[ch] / 127.0f);
                    v->scale = m->port_vol;
                    v->pan = m->pan[ch] + m->port_pan - 64 + (v->pan - 64);
                    voice_update_volume(v);
                    voice_set_bend(v, m->bend[ch] + (m->port_pitch - 8192));
                }
            }
            break;
        case 0xA0:
            m->pos++;
            b = m->data[m->pos++];
            implied = b & 0x80;
            break;
        case 0xB0:
            a = m->data[m->pos++];
            b = m->data[m->pos++];
            implied = b & 0x80;
            b &= 0x7F;

            if (a == 7) m->volume[ch] = b;
            if (a == 10) m->pan[ch] = b;
            break;
        case 0xC0:
            a = m->data[m->pos++];
            implied = a & 0x80;
            m->program[ch] = a & 0x7F;
            break;
        case 0xD0:
            a = m->data[m->pos++];
            implied = a & 0x80;
            break;
        case 0xE0:
            a = m->data[m->pos++];
            b = m->data[m->pos++];
            implied = b & 0x80;
            m->bend[ch] = (a & 0x7F) | ((b & 0x7F) << 7);
            midi_voices_update(port, ch);
            break;
        default:
            midi_stop(port);
            return;
        }
    }

    m->wait += implied ? 0 : read_vlq(m) * m->samples_per_tick;
}

static void midi_advance(int samples)
{
    int port;

    for (port = 0; port < MIDI_PORTS; port++)
    {
        MidiPort* m = &midi[port];
        int guard = 0;

        if (!m->active)
        {
            continue;
        }

        m->wait -= samples;

        while (m->active && (m->wait <= 0) && (guard++ < 256))
        {
            midi_event(port);
        }
    }
}

/* ------------------------------------------------------------------------------------------- */
/* reverb */

/* The SPU's Schroeder reverberator, one per core, running at half the output rate. Registers in
 * hardware order; offsets are in 8-byte units of the reverb work area. The game only uses the
 * Hall preset (libsd mode 5, values from psx-spx), so the other libsd modes fall back to it. */
enum {
    dAPF1, dAPF2, vIIR, vCOMB1, vCOMB2, vCOMB3, vCOMB4, vWALL,
    vAPF1, vAPF2, mLSAME, mRSAME, mLCOMB1, mRCOMB1, mLCOMB2, mRCOMB2,
    dLSAME, dRSAME, mLDIFF, mRDIFF, mLCOMB3, mRCOMB3, mLCOMB4, mRCOMB4,
    dLDIFF, dRDIFF, mLAPF1, mRAPF1, mLAPF2, mRAPF2, vLIN, vRIN
};

#define REV_MODE_OFF  0
#define REV_MODE_HALL 5

static const unsigned short rev_hall[32] = {
    0x01A5, 0x0139, 0x6000, 0x5000, 0x4C00, 0xB800, 0xBC00, 0xC000,
    0x6000, 0x5C00, 0x15BA, 0x11BB, 0x14C2, 0x10BD, 0x11BC, 0x0DC1,
    0x11C0, 0x0DC3, 0x0DC0, 0x09C1, 0x0BC4, 0x07C1, 0x0A00, 0x06CD,
    0x09C2, 0x05C1, 0x05C0, 0x041A, 0x0274, 0x013A, 0x8000, 0x8000
};
#define REV_HALL_SIZE (0xADE0 / 2)   /* work area in 16-bit samples */

typedef struct {
    int mode;
    float depth;                      /* output volume (EVOL) */
    short buf[REV_HALL_SIZE];
    int pos;
    float in_l, in_r;                 /* input of the current tick */
    float out_l, out_r, prev_l, prev_r;
    int phase;                        /* output sample within the tick (0/1) */
} Reverb;

static Reverb reverb[2];

static float rev_vol(int reg)
{
    return (short)rev_hall[reg] / 32768.0f;
}

/* work area sample at register offset reg (in 8-byte units) plus delta samples */
static short* rev_at(Reverb* rv, int reg, int delta)
{
    int i = (rv->pos + rev_hall[reg] * 4 + delta) % REV_HALL_SIZE;

    return &rv->buf[(i < 0) ? i + REV_HALL_SIZE : i];
}

static void rev_write(Reverb* rv, int reg, float x)
{
    *rev_at(rv, reg, 0) = (short)((x > 32767) ? 32767 : (x < -32768) ? -32768 : x);
}

static float rev_read(Reverb* rv, int reg)
{
    return *rev_at(rv, reg, 0);
}

static float rev_read_diff(Reverb* rv, int reg, int dreg)
{
    return *rev_at(rv, reg, -(int)rev_hall[dreg] * 4);
}

static void reverb_tick(Reverb* rv)
{
    float lin = rv->in_l * rev_vol(vLIN), rin = rv->in_r * rev_vol(vRIN);
    float iir = rev_vol(vIIR), wall = rev_vol(vWALL), l, r, prev;

    prev = *rev_at(rv, mLSAME, -1);
    rev_write(rv, mLSAME, (lin + rev_read(rv, dLSAME) * wall - prev) * iir + prev);
    prev = *rev_at(rv, mRSAME, -1);
    rev_write(rv, mRSAME, (rin + rev_read(rv, dRSAME) * wall - prev) * iir + prev);
    prev = *rev_at(rv, mLDIFF, -1);
    rev_write(rv, mLDIFF, (lin + rev_read(rv, dRDIFF) * wall - prev) * iir + prev);
    prev = *rev_at(rv, mRDIFF, -1);
    rev_write(rv, mRDIFF, (rin + rev_read(rv, dLDIFF) * wall - prev) * iir + prev);

    l = rev_vol(vCOMB1) * rev_read(rv, mLCOMB1) + rev_vol(vCOMB2) * rev_read(rv, mLCOMB2) +
        rev_vol(vCOMB3) * rev_read(rv, mLCOMB3) + rev_vol(vCOMB4) * rev_read(rv, mLCOMB4);
    r = rev_vol(vCOMB1) * rev_read(rv, mRCOMB1) + rev_vol(vCOMB2) * rev_read(rv, mRCOMB2) +
        rev_vol(vCOMB3) * rev_read(rv, mRCOMB3) + rev_vol(vCOMB4) * rev_read(rv, mRCOMB4);

    l -= rev_vol(vAPF1) * rev_read_diff(rv, mLAPF1, dAPF1);
    rev_write(rv, mLAPF1, l);
    l = l * rev_vol(vAPF1) + rev_read_diff(rv, mLAPF1, dAPF1);
    r -= rev_vol(vAPF1) * rev_read_diff(rv, mRAPF1, dAPF1);
    rev_write(rv, mRAPF1, r);
    r = r * rev_vol(vAPF1) + rev_read_diff(rv, mRAPF1, dAPF1);

    l -= rev_vol(vAPF2) * rev_read_diff(rv, mLAPF2, dAPF2);
    rev_write(rv, mLAPF2, l);
    l = l * rev_vol(vAPF2) + rev_read_diff(rv, mLAPF2, dAPF2);
    r -= rev_vol(vAPF2) * rev_read_diff(rv, mRAPF2, dAPF2);
    rev_write(rv, mRAPF2, r);
    r = r * rev_vol(vAPF2) + rev_read_diff(rv, mRAPF2, dAPF2);

    rv->prev_l = rv->out_l;
    rv->prev_r = rv->out_r;
    rv->out_l = l * rv->depth;
    rv->out_r = r * rv->depth;

    rv->pos = (rv->pos + 1) % REV_HALL_SIZE;
}

/* one output sample: feed (wet_l, wet_r) in and add the reverb output to *l, *r */
static void reverb_sample(Reverb* rv, float wet_l, float wet_r, float* l, float* r)
{
    float t;

    if (rv->mode == REV_MODE_OFF)
    {
        return;
    }

    /* input averaged over the two output samples of a tick, output interpolated across them */
    rv->in_l += wet_l * 0.5f;
    rv->in_r += wet_r * 0.5f;

    if (rv->phase)
    {
        reverb_tick(rv);
        rv->in_l = rv->in_r = 0;
    }

    t = rv->phase ? 1.0f : 0.5f;
    *l += rv->prev_l + (rv->out_l - rv->prev_l) * t;
    *r += rv->prev_r + (rv->out_r - rv->prev_r) * t;

    rv->phase ^= 1;
}

/* SdrSetRev: [0x44, mode | (core + 1) << 6, depth hi, depth lo, delay, feedback] */
static void reverb_set(const unsigned char* p)
{
    int core = (p[1] >> 6) - 1, mode = p[1] & 0x3F;
    Reverb* rv;

    if ((core < 0) || (core > 1))
    {
        return;
    }

    rv = &reverb[core];

    if (getenv("RECVX_SNDLOG")) pcLog("snd: reverb core %d mode %d depth %04x", core, mode, (p[2] << 8) | p[3]);

    if (mode != REV_MODE_OFF)
    {
        mode = REV_MODE_HALL;
    }

    /* a new mode starts from a cleared work area, as sceSdSetEffectAttr does */
    if (mode != rv->mode)
    {
        memset(rv, 0, sizeof(*rv));
        rv->mode = mode;
    }

    rv->depth = (short)((p[2] << 8) | p[3]) / 32768.0f;
}

/* ------------------------------------------------------------------------------------------- */
/* mixing */

/* many loud voices at once: compress the top of the range instead of hard clipping */
static float soft_clip(float x)
{
    const float knee = 24576.0f, room = 32767.0f - 24576.0f;
    float m = (x < 0) ? -x : x;

    if (m > knee)
    {
        m = knee + room * (float)tanh((m - knee) / room);
    }

    return (x < 0) ? -m : m;
}

static void render(void)
{
    static short left[4096], right[4096];
    int want, n, i, k;

    if (audio_voice < 0)
    {
        audio_voice = pcAudioOpenVoice();
    }

    want = OUT_RATE * 60 / 1000 - pcAudioQueuedSamples(audio_voice);

    if (want <= 0)
    {
        return;
    }

    n = (want > 4096) ? 4096 : want;

    for (i = 0; i < n; i++)
    {
        float l = 0, r = 0, wet[2][2] = { { 0, 0 }, { 0, 0 } };

        if ((i & 63) == 0)
        {
            midi_advance(64);
        }

        for (k = 0; k < MAX_VOICES; k++)
        {
            Voice* v = &voices[k];
            float s;

            if (!v->active)
            {
                continue;
            }

            while ((v->block_pos >= 28) && v->active)
            {
                if (v->addr == 0xFFFFFFFF)
                {
                    voice_free(v);
                    break;
                }

                decode_block(v);
                v->block_pos -= 28;
            }

            if (!v->active)
            {
                continue;
            }

            env_update(v);

            if (v->phase == 4)
            {
                voice_free(v);
                continue;
            }

            s = v->block[v->block_pos] * (v->env / 32767.0f);

            {
                int attr = v->spu_attr, core = (attr & SPU_ATTR_CORE1) ? 1 : 0;

                /* no routing at all: play it dry rather than not at all */
                if (!(attr & (SPU_ATTR_DRY_L | SPU_ATTR_DRY_R | SPU_ATTR_WET_L | SPU_ATTR_WET_R)))
                {
                    attr |= SPU_ATTR_DRY_L | SPU_ATTR_DRY_R;
                }

                if (attr & SPU_ATTR_DRY_L) l += s * v->vol_l;
                if (attr & SPU_ATTR_DRY_R) r += s * v->vol_r;
                if (attr & SPU_ATTR_WET_L) wet[core][0] += s * v->vol_l;
                if (attr & SPU_ATTR_WET_R) wet[core][1] += s * v->vol_r;
            }

            v->frac += v->step;
            v->block_pos += v->frac >> 16;
            v->frac &= 0xFFFF;
        }

        reverb_sample(&reverb[0], wet[0][0], wet[0][1], &l, &r);
        reverb_sample(&reverb[1], wet[1][0], wet[1][1], &l, &r);

        l = soft_clip(l * master);
        r = soft_clip(r * master);
        left[i] = (short)((l > 32767) ? 32767 : (l < -32768) ? -32768 : l);
        right[i] = (short)((r > 32767) ? 32767 : (r < -32768) ? -32768 : r);
    }

    pcAudioQueue(audio_voice, left, right, n, OUT_RATE, 0, -15, 15);

    /* debug: RECVX_SEDUMP=<file> appends the mix (16-bit stereo, 48 kHz) */
    {
        static FILE* dump;
        static int checked;

        if (!checked)
        {
            const char* e = getenv("RECVX_SEDUMP");

            checked = 1;
            dump = (e != NULL) ? fopen(e, "wb") : NULL;
        }

        if (dump != NULL)
        {
            for (i = 0; i < n; i++)
            {
                fwrite(&left[i], 2, 1, dump);
                fwrite(&right[i], 2, 1, dump);
            }
        }
    }
}

/* ------------------------------------------------------------------------------------------- */
/* protocol */

/* signed-char sum of the bank header that was just uploaded, as computed by the EE */
static short last_upload_sum(u_int size)
{
    u_int addr, dma_size, i;
    const signed char* p;
    short sum = 0;

    pcIopLastDma(&addr, &dma_size);

    if (size > dma_size)
    {
        size = dma_size;
    }

    p = (const signed char*)pcIopPtr(addr, size);

    for (i = 0; i < size; i++)
    {
        sum += p[i];
    }

    return sum;
}

/* Length in bytes of one request, mirroring sending_req() in ps2_snddrv.c */
static int request_length(const unsigned char* p)
{
    int cd = p[0];

    switch (cd & 0xF0)
    {
    case 0x00:
        return 4 + ((cd & 1) ? 1 : 0) + ((cd & 2) ? 1 : 0) + ((cd & 4) ? 2 : 0);
    case 0x10:
        return (cd == 17) ? 3 : 1;
    case 0x20:
        if ((cd >= 34) && (cd <= 37)) return 3;
        if (cd == 38) return 4;
        if (cd == 32) return 5;
        if ((cd == 39) || (cd == 40) || (cd == 41) || (cd == 44) || (cd == 45)) return 8;
        return 2;
    case 0x40:
        if ((cd == 71) || (cd == 72) || (cd == 73) || (cd == 74) || (cd == 65) || (cd == 66)) return 2;
        if (cd == 75) return 3;
        if ((cd == 69) || (cd == 76)) return 4;
        if ((cd == 68) || (cd == 79)) return 6;
        if ((cd == 77) || (cd == 78)) return 21;
        return 1;
    case 0x50:
    case 0x60:
        return ((cd >= 81) && (cd <= 84)) ? 8 : 2;
    }

    return 1;
}

static void blob_store(Blob* b, u_int src, u_int offset, u_int size)
{
    if (offset + size > b->size)
    {
        b->data = realloc(b->data, offset + size);
        b->size = offset + size;
    }

    memcpy(b->data + offset, pcIopPtr(src, size), size);
}

/* stop voices that read a bank's samples or header before it is replaced */
static void stop_bank_users(const unsigned char* data)
{
    int i;

    for (i = 0; i < MAX_VOICES; i++)
    {
        if (voices[i].active && (voices[i].bd == data))
        {
            voice_free(&voices[i]);
        }
    }
}

static void request_rpc(unsigned int fno, void* send, int ssize, void* receive, int rsize)
{
    const unsigned char* p = (const unsigned char*)send;
    const unsigned char* end = p + ssize;

    (void)fno; (void)receive; (void)rsize;

    while ((p < end) && (p[0] != 0xFF))
    {
        int cd = p[0];
        int port = p[1];

        if (cd < 0x10)
        {
            /* sound effect: [cd, port, program, channel, (vol), (pan), (pitch hi, lo)] */
            const unsigned char* q = p + 4;
            int vol = -1, pan = -1, pitch = -1;

            if (cd & 1) vol = *q++;
            if (cd & 2) pan = *q++;
            if (cd & 4) { pitch = (q[0] << 8) | q[1]; q += 2; }

            if (port < SE_PORTS)
            {
                if (cd == 0x08)
                {
                    Voice* v = se_voice(port, p[3] & (SE_CHANNELS - 1));

                    if (v != NULL) voice_release(v);
                }
                else if (cd & 8)
                {
                    se_change(port, p[3], vol, pan, pitch);
                }
                else
                {
                    se_play(port, p[2], p[3], vol, pan, pitch);
                }
            }
        }
        else if (cd == 0x10)
        {
            int i;

            for (i = 0; i < MAX_VOICES; i++)
            {
                if (voices[i].active && (voices[i].owner == OWNER_SE))
                {
                    voice_release(&voices[i]);
                }
            }
        }
        else if (cd == 0x20)
        {
            /* [0x20, port, bank, volume, sequence] */
            if (port < MIDI_PORTS)
            {
                midi_play(port, p[2], p[3], p[4]);
            }
        }
        else if (cd == 0x21)
        {
            if (port < MIDI_PORTS)
            {
                midi_stop(port);
            }
        }
        else if (cd == 0x27)
        {
            /* [0x27, port, 0, 0, volume, pan, pitch lo 7 bits, pitch hi 7 bits] */
            if ((port < MIDI_PORTS) && midi[port].active)
            {
                MidiPort* m = &midi[port];

                if ((signed char)p[4] >= 0) m->port_vol = p[4] / 127.0f;
                if ((signed char)p[5] >= 0) m->port_pan = p[5];
                m->port_pitch = (p[6] & 0x7F) | ((p[7] & 0x7F) << 7);

                {
                    int i;

                    for (i = 0; i < MAX_VOICES; i++)
                    {
                        Voice* v = &voices[i];

                        if (v->active && (v->owner == OWNER_MIDI) && (v->port == port))
                        {
                            v->scale = m->port_vol;
                        }
                    }
                }

                midi_voices_update(port, -1);
            }
        }
        else if ((cd == 0x28) || (cd == 0x29))
        {
            u_int size = p[4] | (p[5] << 8) | (p[6] << 16) | (p[7] << 24);
            short sum = last_upload_sum(size);

            if ((cd == 0x28) && (port < MIDI_BANKS))
            {
                int i;

                for (i = 0; i < MIDI_PORTS; i++)
                {
                    if (midi[i].active && (midi[i].bank == port)) midi_stop(i);
                }

                midi_hd[port].size = 0;
                blob_store(&midi_hd[port], DATA_ADDR, 0, size);
                status()->midi_sum[port] = sum;
            }
            else if ((cd == 0x29) && (port < SE_PORTS))
            {
                int i;

                for (i = 0; i < MAX_VOICES; i++)
                {
                    if (voices[i].active && (voices[i].owner == OWNER_SE) && (voices[i].port == port))
                    {
                        voice_free(&voices[i]);
                    }
                }

                se_hd[port].size = 0;
                blob_store(&se_hd[port], DATA_ADDR, 0, size);
                status()->se_sum[port] = sum;
            }
        }
        else if (cd == 0x2C)
        {
            /* sample data: [cd, bank | flag << 7, adrs (3 bytes), size (3 bytes)] */
            int bank = p[1] & 0x7F, flag = p[1] >> 7;
            u_int adrs = p[2] | (p[3] << 8) | (p[4] << 16);
            u_int size = p[5] | (p[6] << 8) | (p[7] << 16);

            if (bank < MAX_BANKS)
            {
                if (adrs == 0)
                {
                    stop_bank_users(bodies[bank].data);
                    bodies[bank].size = 0;
                }
                else if (adrs + size > bodies[bank].size)
                {
                    stop_bank_users(bodies[bank].data);   /* realloc may move the data */
                }

                blob_store(&bodies[bank], DATA_ADDR + (flag ? DATA_ALT : 0), adrs, size);
            }
        }
        else if (cd == 0x2D)
        {
            /* sequences: [cd, bank, 0, 0, size (4 bytes)] */
            u_int size = p[4] | (p[5] << 8) | (p[6] << 16) | (p[7] << 24);

            if (port < MIDI_BANKS)
            {
                int i;

                for (i = 0; i < MIDI_PORTS; i++)
                {
                    if (midi[i].active && (midi[i].bank == port)) midi_stop(i);
                }

                midi_sq[port].size = 0;
                blob_store(&midi_sq[port], DATA_ADDR, 0, size);
            }
        }
        else if (cd == 0x42)
        {
            int i;

            mono = (port == 0);   /* SdrSetOutputMode: 1 = stereo */

            for (i = 0; i < MAX_VOICES; i++)
            {
                if (voices[i].active) voice_update_volume(&voices[i]);
            }
        }
        else if (cd == 0x44)
        {
            reverb_set(p);
        }
        else if (cd == 0x4B)
        {
            /* master volume: [cd, hi, lo] -> 0..0x3FFF */
            int mv = (p[1] << 8) | p[2];

            master = (mv >= 0x3FFF) ? 1.0f : mv / 16383.0f;
        }

        p += request_length(p);
    }

    render();
}

static void get_state_rpc(unsigned int fno, void* send, int ssize, void* receive, int rsize)
{
    int* reply = (int*)receive;

    (void)send; (void)ssize;

    if ((reply == NULL) || (rsize < 4))
    {
        return;
    }

    switch (fno)
    {
    case 18:
        reply[0] = STATUS_ADDR;
        break;
    case 19:
        reply[0] = TABLE_ADDR;
        break;
    default:
        reply[0] = 0;
        break;
    }
}

static void tsnddrv_init(void)
{
    int* table = (int*)pcIopPtr(TABLE_ADDR, 64);

    memset(status(), 0, sizeof(SndStatus));
    memset(table, 0, 64);

    table[0] = HD_ADDR;
    table[1] = SQ_ADDR;
    table[2] = DATA_ADDR;

    pcIopRegisterService(0, request_rpc);
    pcIopRegisterService(1, get_state_rpc);
}

void pcIopInitServices(void)
{
    static int done;

    if (done)
    {
        return;
    }

    done = 1;

    tsnddrv_init();
}
