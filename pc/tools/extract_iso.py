"""Extract the game disc so recvx.exe runs without the ISO.

usage: python pc/tools/extract_iso.py <game.iso> <output folder>

Copies every file of the ISO9660 file system into the output folder and writes files.idx, which
lists each file's start sector, size and path. recvx.exe serves the game's sector reads from
these files (set "data=<output folder>" in recvx.ini). Movies are converted for the PC player
separately by pc/tools/convert_movies.py.
"""
import os
import struct
import sys

SECTOR = 2048


def read_sector(f, lsn, count=1):
    f.seek(lsn * SECTOR)
    return f.read(count * SECTOR)


def walk(f, lsn, size, path, out):
    """Yield (path, lsn, size) for every file below the directory at lsn."""
    data = read_sector(f, lsn, (size + SECTOR - 1) // SECTOR)
    for s in range(0, len(data), SECTOR):
        off = 0
        while off < SECTOR:
            rec_len = data[s + off]
            if rec_len == 0:
                break   # records never cross sectors
            rec = data[s + off:s + off + rec_len]
            off += rec_len
            name_len = rec[32]
            name = rec[33:33 + name_len]
            if name in (b"\x00", b"\x01"):
                continue   # "." and ".."
            name = name.decode("ascii")
            if ";" in name:
                name = name[:name.index(";")]
            file_lsn, file_size = struct.unpack_from("<I", rec, 2)[0], struct.unpack_from("<I", rec, 10)[0]
            if rec[25] & 2:
                walk(f, file_lsn, file_size, path + name + "/", out)
            else:
                out.append((path + name, file_lsn, file_size))


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 1
    iso_path, out_dir = sys.argv[1], sys.argv[2]
    with open(iso_path, "rb") as f:
        pvd = read_sector(f, 16)
        if pvd[1:6] != b"CD001":
            print("not an ISO9660 image: " + iso_path)
            return 1
        root_lsn = struct.unpack_from("<I", pvd, 156 + 2)[0]
        root_size = struct.unpack_from("<I", pvd, 156 + 10)[0]
        files = []
        walk(f, root_lsn, root_size, "", files)
        os.makedirs(out_dir, exist_ok=True)
        total = sum(sz for _, _, sz in files)
        done = 0
        for path, lsn, size in files:
            dest = os.path.join(out_dir, *path.split("/"))
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            if not (os.path.exists(dest) and os.path.getsize(dest) == size):
                f.seek(lsn * SECTOR)
                with open(dest, "wb") as o:
                    left = size
                    while left:
                        chunk = f.read(min(left, 8 << 20))
                        o.write(chunk)
                        left -= len(chunk)
            done += size
            print(f"\r{done * 100 // max(total, 1):3d}%  {path:<40}", end="", flush=True)
        with open(os.path.join(out_dir, "files.idx"), "w") as idx:
            idx.write("# start sector, size, path (recvx.exe disc layout)\n")
            for path, lsn, size in sorted(files, key=lambda e: e[1]):
                idx.write(f"{lsn} {size} {path}\n")
    print(f"\nextracted {len(files)} files ({total / 1e6:.0f} MB) to {out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
