/* PC port: input bindings shared by the game (pc_platform.c) and the settings app
 * (pc/config). In recvx.ini each action can have "key_<name>=<SDL scancode name>" and
 * "pad_<name>=<SDL game controller button or trigger name>"; "none" unbinds it. */
#pragma once

typedef struct {
    const char* name;       /* ini suffix */
    const char* label;      /* shown in the settings app (the button's job in the game) */
    const char* key;        /* default keyboard key (SDL_GetScancodeName), "" = none */
    const char* pad;        /* default controller button/trigger (SDL names), "" = none */
    int hidden;             /* not shown in the settings app (unused by the game) */
} PcBindingDef;

static const PcBindingDef pc_binding_defs[] = {
    { "cross",       "OK / Attack",         "Z",         "a",             0 },
    { "square",      "OK / Attack",         "X",         "x",             0 },
    { "circle",      "Run / Cancel",        "C",         "b",             0 },
    { "triangle",    "Map",                 "V",         "y",             0 },
    { "l1",          "Change aim",          "A",         "leftshoulder",  0 },
    { "r1",          "Aim",                 "S",         "rightshoulder", 0 },
    { "l2",          "L2",                  "Q",         "lefttrigger",   1 },
    { "r2",          "R2",                  "W",         "righttrigger",  1 },
    { "l3",          "L3",                  "",          "leftstick",     1 },
    { "r3",          "R3",                  "",          "rightstick",    1 },
    { "start",       "Status",              "Return",    "start",         0 },
    { "select",      "Options",             "Backspace", "back",          0 },
    { "up",          "Forward",             "Up",        "dpup",          0 },
    { "down",        "Backward",            "Down",      "dpdown",        0 },
    { "left",        "Turn left",           "Left",      "dpleft",        0 },
    { "right",       "Turn right",          "Right",     "dpright",       0 },
    { "fastforward", "Fast-forward (hold)", "Keypad 0",  "",              0 },
    { "fullscreen",  "Fullscreen on/off",   "F11",       "",              0 },
};

#define PC_BINDING_COUNT ((int)(sizeof(pc_binding_defs) / sizeof(pc_binding_defs[0])))
#define PC_BINDING_PS2_BUTTONS 16   /* the first 16 entries are DualShock 2 buttons */
