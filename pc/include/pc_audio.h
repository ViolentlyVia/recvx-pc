/* PC port: software mixer on top of SDL audio (pc/src/pc_audio.c). */
#pragma once

void pcAudioInit(void);
void pcAudioPause(int pause);   /* stop / resume the sound output (window not active) */

/* A voice is one output stream (e.g. one ADX player). Returns a handle >= 0. */
int  pcAudioOpenVoice(void);
void pcAudioCloseVoice(int voice);
void pcAudioFlushVoice(int voice);

/* Queue n sample frames at rate sfreq. right may be NULL for mono. vol: 0..-960 (0.1 dB),
 * pans: -15 (left) .. 15 (right) for each source channel. */
void pcAudioQueue(int voice, const short* left, const short* right, int n, int sfreq, int vol,
                  int pan_left, int pan_right);

/* Frames queued but not yet played (in the voice's source rate). */
int  pcAudioQueuedSamples(int voice);
