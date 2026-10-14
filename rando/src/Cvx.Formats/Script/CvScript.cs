// Opcode sizes and names ported from BioRand's biohazard-utils (BioCvConstantTable.cs, ScdReader.cs),
// MIT, Copyright (c) 2022-2026 Ted John. The game's interpreter is bhScenarioJmpT in event.c; it
// has no size table of its own (each handler advances bhScePtr).

using Cvx.Formats.Rdx;

namespace Cvx.Formats.Script;

/// <summary>One script instruction at its absolute offset in the expanded room file.</summary>
public readonly record struct CvOpcode(int Offset, int Procedure, ReadOnlyMemory<byte> Bytes)
{
    public byte Opcode => Bytes.Span[0];
    public int Length => Bytes.Length;
    public string Name => CvOpcodes.GetName(Opcode);
    public override string ToString() => $"0x{Offset:X} {Name} [{Convert.ToHexString(Bytes.Span)}]";
}

/// <summary>Reads the event scripts of a room (<c>hed03</c>).</summary>
public static class CvScriptReader
{
    /// <summary>Every opcode in every procedure, in file order.</summary>
    public static IReadOnlyList<CvOpcode> ReadOpcodes(RdxFile room)
    {
        var result = new List<CvOpcode>();
        var script = room.Script;
        var data = script.Data;
        var baseOffset = room.ScriptOffset;
        var count = script.Count;
        for (var p = 0; p < count; p++)
        {
            var start = script.GetOffset(p);
            var end = p == count - 1 ? data.Length : script.GetOffset(p + 1);
            var pos = start;
            while (pos < end)
            {
                var remaining = end - pos;
                var size = CvOpcodes.GetSize(data.Span[pos..end]);
                if (size <= 0 || size > remaining)
                    size = Math.Min(16, remaining);   // as BioRand: unknown sizes skip ahead
                result.Add(new CvOpcode(baseOffset + pos, p, data.Slice(pos, size)));
                pos += size;
            }
        }
        return result;
    }
}

public static class CvOpcodes
{
    // Instructions BioRand's room data points at. Byte positions count the opcode byte as 0.
    public const byte Cmpb = 0x06;              // cmpb var, 0, value: item in byte 3 when var is 8
    public const byte Setb = 0x08;              // setb var, value: item in byte 2
    public const byte SetEtcAtari = 0x0B;       // set_etc_atari id, off: switches a trigger area on/off
    public const byte CkUseItem = 0x10;         // ck_use_item item
    public const byte CkPlayerItem = 0x11;      // ck_player_item item
    /// <summary>
    /// set_etc_atari2 id, attr (2 bytes), prm0, prm1, prm2, prm3, type, pad: rewrites a trigger
    /// area (bhEtcAtariSet2 in event.c). Room scripts use it to make doors and items.
    /// </summary>
    public const byte SetEtcAtari2 = 0x25;
    public const byte PlayerItemLost = 0x31;    // player_item_lost item
    public const byte SetDoorCall = 0x33;       // set_door_call: stage, room, entrance in bytes 4-6
    public const byte RoomCaseNo = 0x37;        // room_case_no: room variant in byte 1
    public const byte FlrAtariSet2 = 0x7C;      // flr_atari_set2: BioRand puts an item in byte 4
    public const byte RoomSoundCase = 0xB6;     // room_sound_case: variant in byte 1 for doors
    public const byte ItemBoxToPlayer = 0xB8;   // item_box_to_player item
    public const byte PlayerItemChangeM = 0xC7; // player_item_change_m from, to, ?: BioRand patches byte 2
    public const byte PlayerItemTama = 0xC9;    // player_item_tama item, amount

    /// <summary>The bytes of <paramref name="op"/> that hold an item id (byte 0 is the opcode).</summary>
    public static int[] ItemPositions(CvOpcode op) => op.Opcode switch
    {
        Setb => [2],
        Cmpb when op.Length >= 4 && op.Bytes.Span[1] == 8 && op.Bytes.Span[2] == 0 => [3],
        CkUseItem or CkPlayerItem or PlayerItemLost or ItemBoxToPlayer or PlayerItemTama => [1],
        FlrAtariSet2 => [4],
        PlayerItemChangeM => [1, 2],
        _ => [],
    };

    /// <summary>Size of the instruction at the start of <paramref name="code"/>.</summary>
    public static int GetSize(ReadOnlySpan<byte> code)
    {
        if (code.Length == 0)
            return 0;
        var opcode = code[0];
        if (opcode is 0x64 or 0x66 or 0x67 or 0x69)
        {
            if (code.Length < 2)
                return 1;
            return ((opcode << 8) | code[1]) switch
            {
                0x6407 => 8, 0x640D => 6, 0x6434 => 2, 0x6480 => 2, 0x6481 => 12, 0x6482 => 2,
                0x6483 => 4, 0x6489 => 4, 0x648B => 2, 0x648E => 2, 0x6496 => 4,
                0x6601 => 2, 0x6608 => 2, 0x660C => 6, 0x6614 => 4,
                0x6780 => 2, 0x678F => 6, 0x6793 => 8, 0x6794 => 4,
                0x6900 => 5, 0x6902 => 3, 0x6903 => 3, 0x6904 => 3, 0x6905 => 6, 0x6906 => 6,
                0x6907 => 8, 0x6908 => 8, 0x6909 => 6, 0x690A => 6, 0x690B => 6, 0x690C => 6,
                0x690D => 6, 0x690F => 6, 0x6910 => 6, 0x6911 => 4, 0x6912 => 4, 0x6913 => 4,
                0x6917 => 4, 0x6918 => 8, 0x6919 => 8, 0x691A => 10, 0x691B => 8, 0x691C => 6,
                0x691D => 3, 0x691E => 3, 0x691F => 3, 0x6920 => 3, 0x6921 => 3, 0x6922 => 10,
                0x6924 => 2, 0x6926 => 10, 0x6928 => 4, 0x6929 => 4, 0x692A => 4, 0x692C => 5,
                0x692E => 4, 0x692F => 2, 0x6930 => 4, 0x6931 => 4, 0x6932 => 4, 0x6933 => 6,
                0x6934 => 4, 0x6935 => 4,
                _ => 1,
            };
        }
        return Sizes[opcode];
    }

    public static string GetName(byte opcode) => Names[opcode];

    private static readonly byte[] Sizes =
    [
        2, 2, 2, 2, 6, 6, 4, 6, 4, 4, 4, 4, 4, 4, 6, 2, 2,
        2, 2, 4, 4, 4, 2, 8, 2, 8, 2, 6, 6, 4,
        6, 4, 4, 6, 4, 6, 4, 10, 2, 2, 2, 2, 4,
        2, 4, 2, 2, 4, 4, 2, 12, 8, 12, 4, 6, 2,
        6, 4, 2, 6, 4, 4, 4, 4, 8, 2, 4, 4,
        6, 2, 8, 10, 6, 10, 8, 4, 6, 4, 14, 6, 2, 12,
        12, 12, 12, 2, 6, 2, 14, 12, 4, 2, 2, 6, 4, 2,
        6, 2, 6, 2, 0, 4, 0, 0, 6, 0, 2, 2, 8, 22, 22, 12,
        2, 2, 14, 22, 4, 4, 4, 6, 14, 6, 2, 10, 10,
        4, 4, 6, 4, 2, 4, 2, 8, 4, 16, 2, 10, 2,
        2, 12, 6, 2, 4, 4, 2, 4, 2, 2, 4, 4, 2, 8, 12,
        4, 4, 6, 2, 4, 2, 2, 6, 2, 4, 12, 4, 4,
        4, 4, 10, 2, 10, 6, 6, 4, 2, 4, 22, 6, 4, 8, 4,
        4, 2, 2, 2, 10, 16, 2, 2, 2, 6, 4, 28, 2, 6, 4, 8,
        2, 2, 4, 8, 4, 2, 2, 4, 2, 2, 2, 4, 10, 10,
        2, 4, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 4, 3, 4, 2, 2, 1, 1, 2,
    ];

    private static readonly string[] Names =
    [
        "end", "if", "else", "endif", "ck", "set", "cmpb", "cmpw", "setb", "setw",
        "set_wall_atari", "set_etc_atari", "set_floor_atari", "ck_death", "ck_item", "clr_use_item",
        "ck_use_item", "ck_player_item", "set_cinematic", "set_camera", "event_on", "bgm_on", "bgm_off",
        "se_on", "se_off", "voice_on", "voice_off", "ck_adx", "bg_se_on", "bg_se_off", "ck_adx_time",
        "set_message", "set_display_object", "ck_death_event", "set_ck_enemy", "set_ck_item",
        "set_init_model", "set_etc_atari2", "ck_arms_item", "change_arms_item", "sub_status",
        "set_camera_pause", "set_camera_2", "set_motion_pause", "set_effect", "init_motion_pause",
        "set_player_motion_pause", "init_set_kage", "init_motion_pause_ex", "player_item_lost",
        "set_object_link", "set_door_call", "set_player_object_link", "set_light", "set_fade",
        "room_case_no", "ck_frame", "set_camera_info", "set_player_muteki", "set_default_model",
        "set_mask", "set_lip", "start_mask", "start_lip", "set_player_start_look_g",
        "set_player_stop_look_g", "set_item_aspd", "set_effect_display", "set_effect_amb",
        "delete_object_se", "set_next_room_bgm", "set_next_room_bg_se", "call_foot_se",
        "call_weapon_se", "set_yakkyou", "set_light_type", "set_fog_color", "ck_player_item_block",
        "set_effect_blood", "set_cyouten_henkei", "set_object_motion", "set_object_enemy_link",
        "set_object_item_link", "set_enemy_item_link", "set_enemy_enemy_link", "start_cyouten_henkei",
        "set_effect_blood_pool", "fix_event_camera_player", "set_effect_blood_pool_2",
        "set_object_object_link", "set_camera_yure", "set_init_camera", "set_message_display_end",
        "check_pad", "start_movie", "stop_movie", "check_t_frame", "check_event_timer", "check_camera",
        "set_random", "player_ctr", "load_work", "object_ctr", "sub_ctr", "load_work_2", "common_ctr",
        "event_skip_set", "delete_yakkyou", "object_alpha_set", "cyodan_set", "h_effect_set",
        "object_link_set_object_ply", "effect_push", "effect_pop", "area_search_object",
        "light_parameter_c_set", "light_parameter_start", "init_midi_slot_set", "d_sound_flag_set",
        "sound_volume_set", "light_parameter_set", "enemy_se_on", "enemy_se_off", "wal_atari_set_2",
        "flr_atari_set2", "motion_pos_set_enemy_ply", "kage_sw_set", "sound_pan_set", "init_pony_set",
        "sub_map_busy_check", "set_debug_loop_ex", "sound_fade_out", "cyouten_henkei_set_ex",
        "cyouten_henkei_start_ex", "easy_s_e_set", "sound_flag_re_set", "effect_uv_set",
        "player_change_set", "player_poison_check", "add_object_se", "rand_test", "event_com_set",
        "zombie_up_death_check", "face_pause_set", "face_re_set", "effect_mode_set", "bg_se_off_2",
        "bgm_off_2", "bg_se_on_2", "bgm_on_2", "effect_sensya_set", "effect_kokuen_set",
        "effect_sand_set", "enemy_hp_up", "face_rep", "movie_check", "set_item_motion",
        "object_aspd_set", "puru_puru_flag_set", "puru_puru_start", "map_system_on", "set_trap_damage",
        "event_lighter_fire_set", "object_link_set_ply_item", "player_kaidan_motion", "enemy_render_set",
        "bgm_on_ex", "bgm_on_2_ex", "fog_parameter_c_set", "fog_parameter_start", "effect_uv_set_2",
        "bg_color_set", "movie_time_check", "effect_type_set", "player_poison_2_cr", "ply_hand_change",
        "h_effect_set_2", "object_dpos_check", "item_get_get", "etc_atari_enemy_pos_set",
        "etc_atari_event_pos_set", "load_work_ex", "room_sound_case", "item_player_to_box",
        "item_box_to_player", "grd_pos_set", "grd_pos_move_c_set", "grd_pos_move_start", "event_kill",
        "re_try_point_set", "ply_dpos_check", "player_item_lost_ex", "cyodan_set_ex", "arms_item_set",
        "item_get_get_ex", "effect_sand_set_matsumoto", "voice_wait", "voice_start", "game_over_set",
        "player_item_change_m", "effect_baku_drm_set", "player_item_tama", "effect_clear_event",
        "event_timer_set", "enemy_look_flag_set", "return_title_event", "syukan_mode_set",
        "ex_game_item_init", "set_enemy_life_m", "set_effect_ssize", "set_effect_link_offset",
        "ranking_call", "call_sys_se",
        "nothing", "nothing", "nothing", "nothing", "nothing", "nothing", "nothing", "nothing",
        "nothing", "nothing", "nothing", "nothing", "nothing", "nothing", "nothing", "nothing",
        "nothing", "nothing", "nothing", "nothing", "nothing", "nothing", "nothing", "nothing",
        "nothing", "nothing", "nothing", "nothing", "nothing", "nothing",
        "e_while_2", "com_next", "nothing", "nothing", "nothing", "sleep", "sleeping", "for", "next",
        "while", "e_while", "event_next", "event_end",
    ];

    static CvOpcodes()
    {
        if (Sizes.Length != 256 || Names.Length != 256)
            throw new InvalidOperationException($"Opcode tables have {Sizes.Length} sizes and {Names.Length} names.");
    }
}
