/* PC port: global variables of ps2_dummy.c (that file is replaced by the PC renderer, but the
 * rest of the game references its data). */
#include "../../include/ps2/veronica/prog/ps2_dummy.h"
#include "../../include/ps2/veronica/prog/ps2_loadtim2.h"
#include "../../include/ps2/veronica/prog/ps2_NaDraw2D.h"
#include "../../include/ps2/veronica/prog/ps2_NaTextureFunction.h"
#include "../../include/ps2/veronica/prog/main.h"

sceGsDBuffDc Db;
void (*EorFunc)();
void (*VsyncFunc)();
unsigned int Ps2_vcount;
unsigned int Ps2_dbuff;
unsigned int Ps2_njControl3D_flag;
unsigned int Ps2_sys_cnt;
unsigned char Ps2_DRAW_TMP[16384];
NJS_SCREEN _nj_screen_;
unsigned int _nj_control_3d_flag_;
NJS_VERTEX_BUF* _nj_vertex_buf_;
Uint8* _BSG_END;
unsigned int Ps2_use_pt_flag;
NJS_MATRIX crmat;
float cmmat[2][16]; 
NJS_MATRIX lcmat[12];
float mbuf[128][16];
unsigned int palbuf[4096];
unsigned char Ps2_tex_mem[10485760];
PS2_OT* Ps2_OT[4096][2];
unsigned char Ps2_PBUFF[1835008];
void* Ps2_PP;
PS2_OT Ps2_ot_list[8192];
unsigned int Ps2_ot_list_no;
PS2_TP_TAG Ps2_tp_tag[64];
PS2_TP_CACHE ps2_tp_cache[64];
float Ps2_shadow_vec[4];
float Ps2_shadow_fog;
int Ps2_shadow_z;
void* Ps2_tex_cache_buff[4];
unsigned int Ps2_tex_cache_beflag[4];
unsigned int Ps2_tex_cache_num;
PS2_GS_SAVE Ps2_gs_save;
NJS_TEXMEMLIST* Ps2_now_tex;
unsigned int Ps2_now_bank;
float Ps2AddPrimPrio;
int ViewType;
unsigned int Ps2_albinoid_flag;
unsigned int Ps2_ice_flag;
unsigned int PS2_Render_tex_sub_flag;
unsigned int Ps2_tex_load_tp_cancel;

unsigned char* Ps2_MOVIE = &Ps2_PBUFF[1179648];
