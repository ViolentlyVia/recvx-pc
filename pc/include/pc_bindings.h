/* PC port: input bindings shared by the game (pc_platform.c) and the settings app
 * (pc/config). In recvx.ini each action can have "key_<name>=<SDL scancode name>" and
 * "pad_<name>=<SDL game controller button or trigger name>"; "none" unbinds it. */
#pragma once

typedef struct {
    const char* name;       /* ini suffix */
    const char* label;      /* shown in the settings app */
    const char* key;        /* default keyboard key (SDL_GetScancodeName), "" = none */
    const char* pad;        /* default controller button/trigger (SDL names), "" = none */
} PcBindingDef;

static const PcBindingDef pc_binding_defs[] = {
    { "cross",       "Cross",              "Z",         "a" },
    { "square",      "Square",             "X",         "x" },
    { "circle",      "Circle",             "C",         "b" },
    { "triangle",    "Triangle",           "V",         "y" },
    { "l1",          "L1",                 "A",         "leftshoulder" },
    { "r1",          "R1",                 "S",         "rightshoulder" },
    { "l2",          "L2",                 "Q",         "lefttrigger" },
    { "r2",          "R2",                 "W",         "righttrigger" },
    { "l3",          "L3",                 "",          "leftstick" },
    { "r3",          "R3",                 "",          "rightstick" },
    { "start",       "Start",              "Return",    "start" },
    { "select",      "Select",             "Backspace", "back" },
    { "up",          "Up",                 "Up",        "dpup" },
    { "down",        "Down",               "Down",      "dpdown" },
    { "left",        "Left",               "Left",      "dpleft" },
    { "right",       "Right",              "Right",     "dpright" },
    { "fastforward", "Fast-forward (hold)", "Keypad 0", "" },
    { "fullscreen",  "Fullscreen on/off",  "F11",       "" },
};

#define PC_BINDING_COUNT ((int)(sizeof(pc_binding_defs) / sizeof(pc_binding_defs[0])))
#define PC_BINDING_PS2_BUTTONS 16   /* the first 16 entries are DualShock 2 buttons */
