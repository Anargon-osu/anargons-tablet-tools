import sys, math
# drivestudy.py CSV [--floor F] [--summary] (2026-11-13): the rc4 drive-line study (scratch build from
# Claude-workarea/experiments/noise45/README.txt "Drive study"). Each frame = 3 samples of the training read-out:
#   slot 0 (aux & 0x3000 == 0): x, y, lvl, aux (pressure / 4, unit bit 14), sig = scan-time centres cx | cy << 6, fq
#   slot 1 (aux & 0x2000): X lines c-2..c+2 in x, y, lvl, sig, fq; aux = step | (rot code | 4) << 5
#   slot 2 (aux & 0x1000): Y lines, aux = agcX | agcY << 5 | shift << 10 (line sums >> 2 when shift)
# rot code 0 / 2: drive = the window's own centre, 1: one coil lower, 3: one coil higher (4 = valid bit).
# For each step and axis: the drive line's shortfall delta, from frames whose drives differ by one coil: with drive d
# the lines are g_d x P(k) x (1 - delta [k = d]) above the floor, so for two drives a (frame A) and a + 1 (frame B)
#   (L_A(a) / L_A(a - 1)) / (L_B(a) / L_B(a - 1)) = 1 - delta   (coil a driven in A only, a - 1 in neither)
#   (L_B(a + 1) / L_B(a + 2)) / (L_A(a + 1) / L_A(a + 2)) = 1 - delta
# medians over frame pairs close in time (the pen sits still on the stand). Floor: F per conversion x (reads - 1),
# reads = samples - AGC delay; F = 77.3 (12-bit readings) unless --floor.
FPC = 77.3
if '--floor' in sys.argv: FPC = float(sys.argv[sys.argv.index('--floor') + 1])
SAMPLES = None      # per step, from --samples a,b,c (the ladder's scan samples); default ULTRA rc4b
SAMPLES = [4, 5, 6, 9, 11, 14, 17, 20, 23, 28, 32, 36, 39, 44, 44]
if '--samples' in sys.argv: SAMPLES = [int(v) for v in sys.argv[sys.argv.index('--samples') + 1].split(',')]
def load(path):
    L = open(path).read().splitlines()
    rows = {}
    for l in L[1:]:
        v = [int(t) for t in l.split(',')]
        rows[v[0]] = v[1:]
    frames = []
    for s in sorted(rows):
        a = rows[s]
        if (a[3] & 0x3000) != 0: continue
        b, c = rows.get(s + 1), rows.get(s + 2)
        if b is None or c is None or not (b[3] & 0x2000) or not (c[3] & 0x1000): continue
        m1, m2 = b[3], c[3]
        if not (m1 >> 5) & 4: continue                      # no drive offset recorded (not a hovering frame)
        sh = 4 if (m2 >> 10) & 1 else 1
        frames.append(dict(seq=s, x=a[0], y=a[1], lvl=a[2], p=(a[3] & 0xFFF) * 4, cx=a[4] & 63, cy=(a[4] >> 6) & 31,
                           xl=[b[0] * sh, b[1] * sh, b[2] * sh, b[4] * sh, b[5] * sh],
                           yl=[c[0] * sh, c[1] * sh, c[2] * sh, c[4] * sh, c[5] * sh],
                           step=m1 & 31, rot=(m1 >> 5) & 3, agcx=m2 & 31, agcy=(m2 >> 5) & 31, sh=sh))
    return frames
def med(v):
    v = sorted(v); n = len(v)
    return v[n // 2] if n % 2 else 0.5 * (v[n // 2 - 1] + v[n // 2])
def main():
    F = load(sys.argv[1])
    print('%d study frames' % len(F))
    if '--summary' in sys.argv:
        from collections import Counter
        print('steps:', sorted(Counter(f['step'] for f in F).items()))
        print('rot codes:', sorted(Counter(f['rot'] for f in F).items()))
        print('X drive coils:', sorted(Counter(f['cx'] for f in F).items()), ' Y:', sorted(Counter(f['cy'] for f in F).items()))
        print('AGC X:', sorted(Counter(f['agcx'] for f in F).items()), ' Y:', sorted(Counter(f['agcy'] for f in F).items()))
        prev = None
        for f in F[:40]:
            print('  seq %d step %d rot %d cx %d cy %d agc %d/%d lvl %d X %s Y %s' % (f['seq'], f['step'], f['rot'], f['cx'], f['cy'], f['agcx'], f['agcy'], f['lvl'], f['xl'], f['yl']))
    # per step and axis: signals above the floor, keyed by absolute coil, per drive coil
    by = {}
    for f in F:
        if f['p'] > 0: continue
        s = SAMPLES[f['step']] if f['step'] < len(SAMPLES) else None
        if s is None: continue
        for ax, key, ck, ag in (('X', 'xl', 'cx', 'agcx'), ('Y', 'yl', 'cy', 'agcy')):
            reads = s - f[ag]
            fl = FPC * (reads - 1)
            d = f[ck]
            sig = {}
            for q in range(5): sig[d - 2 + q] = f[key][q] - fl
            by.setdefault((f['step'], ax), []).append((f['seq'], d, sig))
    print('step axis: delta (drive line shortfall, %%) from drive pairs (n), and the signal of the drive line above the floor')
    res = {}
    for k in sorted(by):
        v = by[k]
        drives = sorted(set(e[1] for e in v))
        est = []
        for a in drives:
            A = [e for e in v if e[1] == a]; B = [e for e in v if e[1] == a + 1]
            if len(A) < 10 or len(B) < 10: continue
            # median signal per coil for each drive (the pen is still within a step block)
            def mcoil(E, c): return med([e[2][c] for e in E if c in e[2]])
            try:
                LA = {c: mcoil(A, c) for c in (a - 1, a, a + 1, a + 2)}
                LB = {c: mcoil(B, c) for c in (a - 1, a, a + 1, a + 2)}
            except Exception:
                continue
            if min(list(LA.values()) + list(LB.values())) <= 0: continue
            r1 = (LA[a] / LA[a - 1]) / (LB[a] / LB[a - 1])
            r2 = (LB[a + 1] / LB[a + 2]) / (LA[a + 1] / LA[a + 2])
            est.append((1 - r1, len(A), len(B), 'drive %d vs %d (coil %d)' % (a, a + 1, a)))
            est.append((1 - r2, len(A), len(B), 'drive %d vs %d (coil %d)' % (a + 1, a, a + 1)))
        if not est: print('  step %2d %s: no drive pairs' % k); continue
        w = sum(min(e[1], e[2]) for e in est)
        dl = sum(e[0] * min(e[1], e[2]) for e in est) / w
        lvl = med([max(e[2].values()) for e in v])
        res[k] = dl
        print('  step %2d %s: delta %6.2f %%  (%s)  peak signal %.0f' % (k[0], k[1], 100 * dl, '; '.join('%s %.2f%%' % (e[3], 100 * e[0]) for e in est), lvl))
if __name__ == '__main__': main()
