"""Convert the game's movies (MOVIE/MV_*.PSS) for the PC movie player.

usage: python pc/tools/convert_movies.py <data folder from extract_iso.py>

PSS files are MPEG-2 program streams whose audio sits in private stream 1 (substream 0xFF) as
Sony "SShd"/"SSbd" PCM: 16-bit little endian, channels interleaved in blocks. FFmpeg does not
read that audio, so it is extracted here into a WAV, then FFmpeg writes MV_*.MPG next to each
PSS: MPEG-1 video (same 320x352 frames) and MP2 audio, which the game decodes with pl_mpeg.
FFmpeg is downloaded once into build_pc/deps (it is only used here, not by the game).
"""
import os
import struct
import subprocess
import sys
import tempfile
import wave

sys.path.insert(0, os.path.dirname(__file__))
import fetch_deps


def extract_audio(pss):
    """Returns (rate, channels, interleaved 16-bit PCM bytes) or None."""
    d = open(pss, "rb").read()
    aud = bytearray()
    i = 0
    while True:
        i = d.find(b"\x00\x00\x01", i)
        if i < 0 or i + 6 > len(d):
            break
        sid = d[i + 3]
        if sid == 0xBA:                       # pack header
            i += 14 + (d[i + 13] & 7)
            continue
        if sid >= 0xBB:                       # PES packet
            length = int.from_bytes(d[i + 4:i + 6], "big")
            if sid == 0xBD:
                p = i + 9 + d[i + 8]
                if d[p] == 0xFF:              # substream id, then 3 bytes, then audio
                    aud += d[p + 4:i + 6 + length]
            i += 6 + length
            continue
        i += 3
    if aud[:4] != b"SShd":
        return None
    _, kind, rate, channels, interleave = struct.unpack_from("<IIIII", aud, 4)
    if kind != 1:
        print(f"  {os.path.basename(pss)}: audio type {kind} not supported, no sound")
        return None
    data = bytes(aud[40:])                    # after SShd (32) + SSbd (8)
    block = interleave * channels
    data = data[:len(data) // block * block]
    out = bytearray(len(data))
    frames = interleave // 2
    for b in range(0, len(data), block):
        for c in range(channels):
            src = data[b + c * interleave:b + (c + 1) * interleave]
            # interleave channels sample by sample
            for s in range(frames):
                o = b + (s * channels + c) * 2
                out[o:o + 2] = src[s * 2:s * 2 + 2]
    return rate, channels, bytes(out)


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 1
    movie_dir = os.path.join(sys.argv[1], "MOVIE")
    ffmpeg = fetch_deps.ffmpeg_exe()
    names = sorted(n for n in os.listdir(movie_dir) if n.upper().endswith(".PSS"))
    for n in names:
        pss = os.path.join(movie_dir, n)
        mpg = os.path.join(movie_dir, os.path.splitext(n)[0] + ".MPG")
        if os.path.exists(mpg) and os.path.getmtime(mpg) >= os.path.getmtime(pss):
            print(f"{n}: up to date")
            continue
        print(f"{n}: converting")
        audio = extract_audio(pss)
        with tempfile.TemporaryDirectory() as tmp:
            cmd = [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", pss]
            if audio:
                wav = os.path.join(tmp, "audio.wav")
                with wave.open(wav, "wb") as w:
                    w.setnchannels(audio[1])
                    w.setsampwidth(2)
                    w.setframerate(audio[0])
                    w.writeframes(audio[2])
                cmd += ["-i", wav, "-map", "0:v", "-map", "1:a", "-c:a", "mp2", "-b:a", "256k", "-shortest"]
            else:
                cmd += ["-map", "0:v"]
            cmd += ["-c:v", "mpeg1video", "-q:v", "2", "-f", "mpeg", mpg]
            subprocess.run(cmd, check=True)
    print("done")
    return 0


if __name__ == "__main__":
    sys.exit(main())
