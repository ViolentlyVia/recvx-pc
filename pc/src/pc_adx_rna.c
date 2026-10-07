/* PC port: ADXRNA, the output stage of CRI ADX (replaces adx_rnap.c + ps2_rna.c).
 *
 * The ADX decoder writes 16-bit PCM for each channel into a stream joint (SJ): lane 1 holds
 * decoded data, lane 0 free space. On the PS2 the RNA layer shipped that data to the IOP's PCM
 * mixer. Here ADXRNA_ExecServer (ADX server thread, every vsync) keeps a short queue filled in
 * pcAudio, which plays it through SDL.
 *
 * Playback position: ADXT_GetTimeSfreq2() computes decoded samples - ADXRNA_GetNumData(), so
 * GetNumData() must count everything decoded but not yet played.
 */
#include <string.h>

#include "../../include/cri/mwlib/ee/lib/libadxe/adx_rnap.h"

#include "pc_platform.h"
#include "pc_audio.h"

#define MAX_RNA 8

typedef struct {
    int used;
    int maxnch;
    int nch;
    SJ sj[2];
    int plysw;
    int trnsw;
    int sfreq;
    int vol;          /* 0 .. -960 (0.1 dB units) */
    int pan[2];       /* -15 .. 15 */
    int bps;
    int voice;        /* pcAudio voice */
} PcRna;

static PcRna rnas[MAX_RNA];
static int initialized;

void ADXRNA_Init(void)
{
    if (!initialized)
    {
        memset(rnas, 0, sizeof(rnas));
        initialized = 1;
    }
}

void ADXRNA_Finish(void)
{
}

ADXRNA ADXRNA_Create(SJ* sj, Sint32 maxnch)
{
    int i, ch;

    for (i = 0; i < MAX_RNA; i++)
    {
        if (!rnas[i].used)
        {
            PcRna* r = &rnas[i];

            memset(r, 0, sizeof(*r));

            r->used = 1;
            r->maxnch = maxnch;
            r->nch = maxnch;
            r->sfreq = 44100;
            r->bps = 16;

            for (ch = 0; ch < maxnch; ch++)
            {
                r->sj[ch] = sj[ch];
            }

            r->voice = pcAudioOpenVoice();

            return r;
        }
    }

    return NULL;
}

void ADXRNA_Destroy(ADXRNA rna)
{
    PcRna* r = (PcRna*)rna;

    r->plysw = 0;
    pcAudioCloseVoice(r->voice);
    r->used = 0;
}

void ADXRNA_Start(ADXRNA rna)
{
    (void)rna;
}

void ADXRNA_Stop(ADXRNA rna)
{
    ADXRNA_SetTransSw(rna, 0);
    ADXRNA_SetPlaySw(rna, 0);
}

void ADXRNA_SetTransSw(ADXRNA rna, Sint32 sw)
{
    ((PcRna*)rna)->trnsw = sw;
}

void ADXRNA_SetPlaySw(ADXRNA rna, Sint32 sw)
{
    PcRna* r = (PcRna*)rna;

    if (sw && !r->plysw)
    {
        pcLog("adx: play %d ch, %d Hz", r->nch, r->sfreq);
    }

    if (!sw)
    {
        pcAudioFlushVoice(r->voice);
    }

    r->plysw = sw;
}

/* Called by ADXT_StopWithoutLsc() (adx_tlk.c) where the PS2 version reset its IOP buffers. */
void pcAdxRnaReset(ADXRNA rna)
{
    PcRna* r = (PcRna*)rna;

    r->trnsw = 0;
    pcAudioFlushVoice(r->voice);
}

void ADXRNA_GetTime(ADXRNA rna, Sint32* ncount, Sint32* tscale)
{
    (void)rna;

    /* same as PS2RNA_GetTime: the ADXT layer derives time from decoded - queued samples */
    *ncount = 0;
    *tscale = 48000;
}

static int sj_samples(SJ sj, int lane)
{
    return SJ_GetNumData(sj, lane) / 2;
}

Sint32 ADXRNA_GetNumData(ADXRNA rna)
{
    PcRna* r = (PcRna*)rna;

    return sj_samples(r->sj[0], 1) + pcAudioQueuedSamples(r->voice);
}

Sint32 ADXRNA_GetNumRoom(ADXRNA rna)
{
    return sj_samples(((PcRna*)rna)->sj[0], 0);
}

Sint32 ADXRNA_GetStat(ADXRNA rna)
{
    (void)rna;
    return 0;
}

/* Move up to n samples per channel out of the stream joints (into pcAudio). */
static int consume(PcRna* r, int n)
{
    static short buf[2][4096];
    int ch, done = 0;

    for (ch = 0; ch < r->nch; ch++)
    {
        int avail = sj_samples(r->sj[ch], 1);

        if (avail < n)
        {
            n = avail;
        }
    }

    while (done < n)
    {
        int chunk = n - done;

        if (chunk > 4096)
        {
            chunk = 4096;
        }

        for (ch = 0; ch < r->nch; ch++)
        {
            int got = 0;

            while (got < chunk)
            {
                SJCK ck;

                SJ_GetChunk(r->sj[ch], 1, (chunk - got) * 2, &ck);

                if (ck.len <= 0)
                {
                    break;
                }

                memcpy(&buf[ch][got], ck.data, ck.len);
                got += ck.len / 2;

                SJ_PutChunk(r->sj[ch], 0, &ck);
            }
        }

        pcAudioQueue(r->voice, buf[0], (r->nch > 1) ? buf[1] : NULL, chunk, r->sfreq, r->vol,
                     r->pan[0], (r->nch > 1) ? r->pan[1] : r->pan[0]);

        done += chunk;
    }

    return n;
}

void ADXRNA_ExecServer(void)
{
    int i;

    for (i = 0; i < MAX_RNA; i++)
    {
        PcRna* r = &rnas[i];
        int n;

        if (!r->used || !r->plysw)
        {
            continue;
        }

        /* keep ~100 ms queued ahead of the audio device; the device paces playback */
        n = r->sfreq / 10 - pcAudioQueuedSamples(r->voice);

        if (n > 0)
        {
            consume(r, n);
        }
    }
}

void ADXRNA_SetStartSmpl(ADXRNA rna, Sint32 nsmpl)
{
    (void)rna; (void)nsmpl;
}

void ADXRNA_SetNumChan(ADXRNA rna, Sint32 nch)
{
    ((PcRna*)rna)->nch = nch;
}

void ADXRNA_SetSfreq(ADXRNA rna, Sint32 sfreq)
{
    ((PcRna*)rna)->sfreq = sfreq;
}

void ADXRNA_SetOutVol(ADXRNA rna, Sint32 vol)
{
    ((PcRna*)rna)->vol = vol;
}

void ADXRNA_SetOutPan(ADXRNA rna, Sint32 chno, Sint32 pan)
{
    if ((chno >= 0) && (chno < 2))
    {
        ((PcRna*)rna)->pan[chno] = pan;
    }
}

void ADXRNA_SetBitPerSmpl(ADXRNA rna, Sint32 bps)
{
    ((PcRna*)rna)->bps = bps;
}

Sint32 ADXRNA_DiscardData(ADXRNA rna, Sint32 nsmpl)
{
    PcRna* r = (PcRna*)rna;
    int ch, done = nsmpl;

    for (ch = 0; ch < r->nch; ch++)
    {
        int left = nsmpl;

        while (left > 0)
        {
            SJCK ck;

            SJ_GetChunk(r->sj[ch], 1, left * 2, &ck);

            if (ck.len <= 0)
            {
                break;
            }

            SJ_PutChunk(r->sj[ch], 0, &ck);
            left -= ck.len / 2;
        }

        if (nsmpl - left < done)
        {
            done = nsmpl - left;
        }
    }

    return done;
}

void ADXRNA_SetTotalNumSmpl(ADXRNA rna, Sint32 nsmpl)
{
    (void)rna; (void)nsmpl;
}

Sint32 ADXRNA_SetStmHdInfo(ADXRNA rna, void* snddat)
{
    (void)rna; (void)snddat;
    return 0;
}

void ADXRNA_ClearBuf(ADXRNA rna)
{
    pcAudioFlushVoice(((PcRna*)rna)->voice);
}
