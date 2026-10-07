/* PC port: movie playback (replaces ps2_MovieFunc.c).
 *
 * Placeholder: every movie reports "finished" at once so the game moves on. The real player
 * (MPEG-2 video from the .PSS files + audio) comes with the movie step.
 */
#include "../../include/ps2/veronica/prog/ps2_MovieFunc.h"
#include "../../include/ps2/veronica/prog/main.h"

#include "pc_platform.h"

RMI_WORK rmi;
MDSIZE_WORK mdSize;
int movie_draw;
StrFile infile;
u_long128 test_tag[1400];
VoBuf voBuf;

#define MOVIE_STATE_END 3

void initAll()
{
    pcLog("movie: playback not implemented yet, skipping");
    rmi.iMovieState = 0;
}

void readMpeg()
{
    rmi.iMovieState = MOVIE_STATE_END;
    movie_draw = 1;
}

void termAll()
{
}

void setImageTag(unsigned int* tags, void* image)
{
}

void vbrank_draw()
{
}

int sendToIOP(int dst, u_char* src, int size)
{
    return size;
}

void changeInputVolume(u_int val)
{
}
