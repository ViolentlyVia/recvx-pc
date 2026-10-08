/* PC port: software mixer on top of SDL audio.
 *
 * Voices hold stereo frames at their source rate; the SDL callback resamples (linear) and mixes
 * them into a 48 kHz stereo device. Without an audio device, playback is simulated with the
 * clock so the game (which waits for streams to finish) keeps running.
 */
#include <math.h>
#include <string.h>

#include <SDL.h>

#include "pc_platform.h"
#include "pc_audio.h"
#include "../src/pc_os.h"

#define MAX_VOICES  16
#define RING_FRAMES 65536          /* power of two */
#define OUT_RATE    48000

typedef struct {
    int used;
    float ring[RING_FRAMES][2];
    unsigned int head;             /* write position (frames) */
    unsigned int tail;             /* read position (frames) */
    double frac;                   /* resampler phase */
    int sfreq;
    double sim_clock;              /* simulated playback without a device */
} Voice;

static Voice voices[MAX_VOICES];
static SDL_AudioDeviceID device;
static int init_done;

static void lock(void)
{
    if (device != 0)
    {
        SDL_LockAudioDevice(device);
    }
}

static void unlock(void)
{
    if (device != 0)
    {
        SDL_UnlockAudioDevice(device);
    }
}

static void SDLCALL callback(void* userdata, Uint8* stream, int len)
{
    float* out = (float*)stream;
    int frames = len / (int)(2 * sizeof(float));
    int v, i;

    (void)userdata;

    memset(stream, 0, len);

    for (v = 0; v < MAX_VOICES; v++)
    {
        Voice* vc = &voices[v];
        double step;

        if (!vc->used || (vc->sfreq <= 0))
        {
            continue;
        }

        step = (double)vc->sfreq / OUT_RATE;

        for (i = 0; i < frames; i++)
        {
            float* a;
            float* b;
            float t;

            if (vc->head - vc->tail < 2)
            {
                break;
            }

            a = vc->ring[vc->tail & (RING_FRAMES - 1)];
            b = vc->ring[(vc->tail + 1) & (RING_FRAMES - 1)];
            t = (float)vc->frac;

            out[i * 2 + 0] += a[0] + (b[0] - a[0]) * t;
            out[i * 2 + 1] += a[1] + (b[1] - a[1]) * t;

            vc->frac += step;

            while (vc->frac >= 1.0)
            {
                vc->frac -= 1.0;
                vc->tail++;
            }
        }
    }

    for (i = 0; i < frames * 2; i++)
    {
        if (out[i] > 1.0f)  out[i] = 1.0f;
        if (out[i] < -1.0f) out[i] = -1.0f;
    }
}

void pcAudioInit(void)
{
    SDL_AudioSpec want, have;

    if (init_done)
    {
        return;
    }

    init_done = 1;

    SDL_zero(want);
    want.freq = OUT_RATE;
    want.format = AUDIO_F32SYS;
    want.channels = 2;
    want.samples = 1024;
    want.callback = callback;

    device = SDL_OpenAudioDevice(NULL, 0, &want, &have, 0);

    if (device == 0)
    {
        pcLog("audio: no device (%s), sound disabled", SDL_GetError());
        return;
    }

    SDL_PauseAudioDevice(device, 0);
    pcLog("audio: %d Hz stereo", have.freq);
}

void pcAudioPause(int pause)
{
    if (device != 0)
    {
        SDL_PauseAudioDevice(device, pause);
    }
}

int pcAudioOpenVoice(void)
{
    int v;

    pcAudioInit();

    lock();

    for (v = 0; v < MAX_VOICES; v++)
    {
        if (!voices[v].used)
        {
            voices[v].head = voices[v].tail = 0;
            voices[v].frac = 0;
            voices[v].sfreq = 0;
            voices[v].sim_clock = pcOsTime();
            voices[v].used = 1;
            break;
        }
    }

    unlock();

    return (v < MAX_VOICES) ? v : -1;
}

void pcAudioCloseVoice(int voice)
{
    if (voice < 0)
    {
        return;
    }

    lock();
    voices[voice].used = 0;
    unlock();
}

void pcAudioFlushVoice(int voice)
{
    if (voice < 0)
    {
        return;
    }

    lock();
    voices[voice].tail = voices[voice].head;
    voices[voice].frac = 0;
    voices[voice].sim_clock = pcOsTime();
    unlock();
}

/* CRI pan: -15 = left, 0 = center, 15 = right (constant power) */
static void pan_gains(int pan, float gain, float* l, float* r)
{
    float a = (float)((pan + 15) / 30.0 * (3.14159265 / 2));

    *l = cosf(a) * gain;
    *r = sinf(a) * gain;
}

void pcAudioQueue(int voice, const short* left, const short* right, int n, int sfreq, int vol,
                  int pan_left, int pan_right)
{
    Voice* vc;
    float gain = powf(10.0f, vol / 200.0f) / 32768.0f;
    float ll, lr, rl = 0, rr = 0;
    int i;

    if (voice < 0)
    {
        return;
    }

    vc = &voices[voice];

    pan_gains(pan_left, gain, &ll, &lr);

    if (right != NULL)
    {
        pan_gains(pan_right, gain, &rl, &rr);
    }

    lock();

    vc->sfreq = sfreq;

    for (i = 0; i < n; i++)
    {
        float* f;

        if (vc->head - vc->tail >= RING_FRAMES)
        {
            break;
        }

        f = vc->ring[vc->head & (RING_FRAMES - 1)];

        if (right != NULL)
        {
            f[0] = left[i] * ll + right[i] * rl;
            f[1] = left[i] * lr + right[i] * rr;
        }
        else
        {
            f[0] = left[i] * ll;
            f[1] = left[i] * lr;
        }

        vc->head++;
    }

    unlock();
}

int pcAudioQueuedSamples(int voice)
{
    Voice* vc;
    int n;

    if (voice < 0)
    {
        return 0;
    }

    vc = &voices[voice];

    lock();

    if ((device == 0) && (vc->sfreq > 0))
    {
        /* no device: "play" in real time */
        double now = pcOsTime();
        unsigned int played = (unsigned int)((now - vc->sim_clock) * vc->sfreq);
        unsigned int avail = vc->head - vc->tail;

        if (played > 0)
        {
            vc->tail += (played < avail) ? played : avail;
            vc->sim_clock = now;
        }
    }

    n = (int)(vc->head - vc->tail);

    unlock();

    return n;
}
