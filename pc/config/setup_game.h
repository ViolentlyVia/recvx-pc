/* recvx-config.exe: game setup from the player's disc image (setup_game.c). */
#pragma once

#include <windows.h>

/* Copies the disc into <exe_dir>\data and converts the movies, behind a progress window.
 * Returns 1 when done, 0 on failure (message in error), -1 when cancelled. */
int SetupGameFromIso(HWND owner, HFONT font, float dpi, const char* exe_dir, const char* iso_path, char* error, int error_size);

/* 1 when <exe_dir>\data holds a set up game (its path in data_dir either way) */
int SetupGameIsReady(const char* exe_dir, char* data_dir, int size);
