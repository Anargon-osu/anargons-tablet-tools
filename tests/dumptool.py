#!/usr/bin/env python3
"""Helpers for tests/run-tests.sh (run with python3 -I).
  dumptool.py check FILE             prints feb / dec if FILE is an untouched 64 KB stock dump, else exits 1
  dumptool.py damage IN OUT          OUT = IN with the damage seen on real tablets: everything from 0x0800C188 erased,
                                     and the bootloader settings page 0x08003400 erased except its boot-state word
  dumptool.py hexcrc HEX END         CRC-32 (hex) of 0x08003800..END in an Intel HEX file
"""
import re
import sys
import zlib
from pathlib import Path

KNOWN = {'feb': (0x0800C7CC, 0xB65DB693), 'dec': (0x0800C7D8, 0x4525CA51)}
VERSIONS = {'feb': '2025-02-14_release001', 'dec': '2025-12-23_release001'}   # their dumps/<version>/ folder names
# the firmware's own version, a UTF-16 USB string such as 2025-02-14_release001 (a build of this tool has none)
VERSION_STRING = re.compile(rb'(?:[0-9]\x00){4}-\x00(?:[0-9]\x00){2}-\x00(?:[0-9]\x00){2}_\x00(?:[A-Za-z0-9]\x00){1,24}')


def build_string(b):
    """the firmware version a 64 KB dump names itself (2025-02-14_release001), or None"""
    m = VERSION_STRING.search(b, 0x3800)
    return m.group(0).decode('utf-16-le') if m else None


def build_of(b):
    if len(b) != 0x10000:
        return None
    for name, (end, crc) in KNOWN.items():
        if zlib.crc32(b[0x3800:end - 0x08000000]) == crc:
            return name
    return None


def hex_crc(path, end):
    """Check every HEX record before using it as a firmware test oracle."""
    memory = bytearray(b'\xff' * 0x10000)
    base = 0
    ended = False
    seen = set()
    for line in Path(path).read_text(encoding='ascii').splitlines():
        line = line.strip()
        if not line:
            continue
        if ended or not line.startswith(':'):
            raise ValueError('invalid HEX record order')
        record = bytes.fromhex(line[1:])
        if len(record) < 5 or len(record) != record[0] + 5 or sum(record) & 255:
            raise ValueError('invalid HEX length or checksum')
        count, address, kind = record[0], int.from_bytes(record[1:3], 'big'), record[3]
        data = record[4:-1]
        if kind == 0:
            offset = base + address - 0x08000000
            if offset < 0 or offset + count > len(memory):
                raise ValueError('HEX data outside flash')
            for index, value in enumerate(data, offset):
                if index in seen:
                    raise ValueError('overlapping HEX data')
                seen.add(index)
                memory[index] = value
        elif kind == 1 and count == 0 and address == 0:
            ended = True
        elif kind == 4 and count == 2 and address == 0:
            base = int.from_bytes(data, 'big') << 16
        elif kind == 5 and count == 4 and address == 0:
            pass
        else:
            raise ValueError('unsupported HEX record')
    if not ended or any(index not in seen for index in range(0x3800, end - 0x08000000)):
        raise ValueError('incomplete HEX image')
    return zlib.crc32(memory[0x3800:end - 0x08000000])


def main(a):
    if a[0] == 'check':
        n = build_of(open(a[1], 'rb').read())
        if not n:
            sys.exit(a[1] + ': not an untouched stock 64 KB dump of a known build')
        print(n)
    elif a[0] == 'damage':
        b = bytearray(open(a[1], 'rb').read())
        b[0xC188:0x10000] = b'\xff' * (0x10000 - 0xC188)
        b[0x3404:0x3434] = b'\xff' * 0x30
        open(a[2], 'wb').write(b)
    elif a[0] == 'hexcrc':
        print('%08X' % hex_crc(a[1], int(a[2], 16)))


if __name__ == '__main__':
    main(sys.argv[1:])
