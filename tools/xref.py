#!/usr/bin/env python3
"""Find branch/call targets and literal references in the reference code window.

usage: python -I tools/xref.py IMAGE ADDR [ADDR...]
Scans 0x08003800 through 0x0800C02F; it is not a whole-flash reference search.
"""
import importlib.util
from pathlib import Path
import struct
import sys

spec = importlib.util.spec_from_file_location("firmware_disassembler", Path(__file__).with_name("dis.py"))
disassembler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(disassembler)
img = disassembler.load(sys.argv[1]); d = disassembler.Dis(img)
targets = [int(x, 16) for x in sys.argv[2:]]
a = 0x08003800
while a < 0x0800C030:
    size, txt, cm = d.one(a)
    for t in targets:
        if f'{t:#010x}' in txt.split()[-1] or (cm and f'={t:#010x}' in cm):
            print(f'{a:08x}: {txt}   ; {cm}')
    a += size
