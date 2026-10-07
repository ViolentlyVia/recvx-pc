/* PC port: movie playback (replaces ps2_MovieFunc.c).
 *
 * Placeholder: every movie goes PREP -> PLAYING -> PLAYEND over two server calls so the game
 * moves on (WaitPrePlayMovie waits for PLAYING, PlayMovieMain then for PLAYEND). The real
 * player (MPEG-2 video from the .PSS files + audio) comes with the movie step.
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

/* MWE_PLY_STAT */
#define MOVIE_STATE_PREP    1
#define MOVIE_STATE_PLAYING 2
#define MOVIE_STATE_END     3

void initAll()
{
    pcLog("movie: playback not implemented yet, skipping");
    rmi.iMovieState = MOVIE_STATE_PREP;
}

void readMpeg()
{
    rmi.iMovieState = (rmi.iMovieState == MOVIE_STATE_PREP) ? MOVIE_STATE_PLAYING : MOVIE_STATE_END;
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
