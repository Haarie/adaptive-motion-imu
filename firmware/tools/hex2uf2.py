#!/usr/bin/env python3
"""Intel HEX -> UF2 for the nRF52840 UF2 bootloader (nice!nano / ProMicro nRF52840).
Usage: hex2uf2.py in.hex out.uf2 [family_id=0xADA52840]"""
import struct, sys

def read_hex(path):
    mem, base = {}, 0
    for line in open(path):
        line = line.strip()
        if not line.startswith(':'):
            continue
        b = bytes.fromhex(line[1:])
        n, addr, typ, data = b[0], (b[1] << 8) | b[2], b[3], b[4:4 + b[0]]
        if (sum(b) & 0xFF) != 0:
            raise SystemExit(f"bad checksum: {line}")
        if typ == 0:
            for i, v in enumerate(data):
                mem[base + addr + i] = v
        elif typ == 2:
            base = int.from_bytes(data, 'big') << 4
        elif typ == 4:
            base = int.from_bytes(data, 'big') << 16
        elif typ == 1:
            break
    return mem

def to_uf2(mem, family):
    pages = sorted({a & ~0xFF for a in mem})
    blocks = []
    for i, p in enumerate(pages):
        data = bytes(mem.get(p + k, 0xFF) for k in range(256))
        hdr = struct.pack('<IIIIIIII', 0x0A324655, 0x9E5D5157, 0x00002000, p, 256, i, len(pages), family)
        blk = hdr + data + bytes(476 - 256) + struct.pack('<I', 0x0AB16F30)
        assert len(blk) == 512
        blocks.append(blk)
    return b''.join(blocks), pages

if __name__ == '__main__':
    fam = int(sys.argv[3], 0) if len(sys.argv) > 3 else 0xADA52840
    mem = read_hex(sys.argv[1])
    out, pages = to_uf2(mem, fam)
    if pages[0] < 0x26000:
        raise SystemExit(f"image starts at 0x{pages[0]:X} - would overwrite the SoftDevice/bootloader")
    open(sys.argv[2], 'wb').write(out)
    print(f"{sys.argv[2]}: {len(pages)} blocks, 0x{pages[0]:X}..0x{pages[-1] + 255:X}")
