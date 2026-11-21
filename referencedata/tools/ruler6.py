import sys, math, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from noisept import load, XC, YC
# ruler6.py DIR (2026-11-16, after R45-rc5): the six ruler recordings of the position ideas (rev-output/hw45/ruler/
# i1-control .. i6-ab.log, vendorlog.exe --log, report mode 4, the same ruler spot for all six), scored the same way:
#  - passes: pressed reports joined across short breaks (the tip bit drops for about 12 ms now and then mid-pass;
#    ruler.py split a pass there and its trim then shrank to the shortest piece), split where the pen turns back;
#    passes of 80 mm or more kept, their first and last 150 ms left out;
#  - one line fitted to all six (the ruler did not move), one common stretch: the longest run of 0.25 mm bins that
#    every recording covers with 3+ passes each way;
#  - sideways error (9-report mean, each pass's own offset over the common stretch taken off), averaged per bin over the
#    passes (each pass weighs the same): repeatable part, slow part (+-10 mm mean), coil-scale part (the rest),
#    forward minus back, pass-to-pass scatter;
#  - the coil-scale part folded by the X and Y coil phase (R43 grid) and fitted as f(X phase) + g(Y phase): the error
#    that repeats with each axis's coils; the sideways direction is -sin(a) X + cos(a) Y, so an X error shows at
#    sin(a) of its size and a Y error at cos(a);
#  - jumps per axis, raw reports: a report more than 0.3 mm off the local trend (the 10 reports before) is a flick when
#    the position is back on the trend within 3 reports, a step when it stays off (0.2 mm over the next 5-15 reports),
#    else a burst. A physical bump is at one spot of the tablet in every recording; a firmware jump follows the coils
#    and the build.
NAMES = [('i1-control', 'rc5 control'), ('i2-cl', 'Coil Linearization'), ('i3-pax', 'Pen-Anchored'),
         ('i4-mg', 'Midpoint Gauss'), ('i5-xa', 'Cross-Axis'), ('i6-ab', 'Alt. Blend')]
BIN = 0.25
SPD = [0, 1e9]
if '--speed' in sys.argv:
    k = sys.argv.index('--speed'); SPD = [float(sys.argv[k + 1]), float(sys.argv[k + 2])]; del sys.argv[k:k + 3]
NEED = 3
if '--set' in sys.argv:
    k = sys.argv.index('--set'); NAMES = [tuple(t.split(':')) for t in sys.argv[k + 1].split(',')]; del sys.argv[k:k + 2]
if '--only' in sys.argv:
    k = sys.argv.index('--only'); keep = sys.argv[k + 1].split(','); del sys.argv[k:k + 2]
    NAMES = [n for n in NAMES if n[0] in keep]
if '--need' in sys.argv:
    k = sys.argv.index('--need'); NEED = int(sys.argv[k + 1]); del sys.argv[k:k + 2]
def phase(mm, q):
    a, p = XC if q == 0 else YC
    k = (a - mm) / p
    return k - math.floor(k)
def runmean(v, h):
    out = []; s = 0.0; n = len(v); c = [0.0]
    for x in v: s += x; c.append(s)
    for i in range(n):
        a = max(0, i - h); b = min(n, i + h + 1); out.append((c[b] - c[a]) / (b - a))
    return out
def linefit(x, y):
    n = len(x); mx = sum(x) / n; my = sum(y) / n
    sxx = sum((a - mx) ** 2 for a in x); syy = sum((b - my) ** 2 for b in y); sxy = sum((a - mx) * (b - my) for a, b in zip(x, y))
    th = 0.5 * math.atan2(2 * sxy, sxx - syy)
    return mx, my, math.cos(th), math.sin(th)
def pct(v, q):
    v = sorted(v); return v[min(len(v) - 1, int(q * len(v)))] if v else float('nan')

def groups(T, x, y, P):
    # pressed runs, joined across breaks under 400 ms and 3 mm
    G = []; cur = []; breaks = []
    for i in range(len(T)):
        if P[i] <= 0: continue
        if cur:
            j = cur[-1]; gap = T[i] - T[j]; dist = math.hypot(x[i] - x[j], y[i] - y[j])
            if gap > 400 or dist > 3:
                G.append(cur); cur = []
            elif i - j > 1 or gap > 3:
                breaks.append((T[j], gap, i - j - 1, P[j], P[i], dist))
        cur.append(i)
    if cur: G.append(cur)
    return G, breaks

def split_turns(g, a):
    # split where the pen turns back by 20 mm or more
    out = [g]; done = False
    while not done:
        done = True; nxt = []
        for s in out:
            A = [a[i] for i in s]
            for m in (A.index(max(A)), A.index(min(A))):
                if 0 < m < len(A) - 1 and abs(A[m] - A[0]) > 20 and abs(A[m] - A[-1]) > 20:
                    nxt.append(s[:m + 1]); nxt.append(s[m + 1:]); done = False; break
            else:
                nxt.append(s)
        out = nxt
    return out

def jumps(T, V, s, q, inside):
    # V: one axis in mm; s: a pass's report indices. Returns (flicks, steps, bursts) as lists of (index, size)
    fl = []; st = []; bu = []; k = 11
    while k < len(s) - 16:
        i = s[k]
        v = (V[s[k - 1]] - V[s[k - 11]]) / 10.0
        pred = lambda m: V[s[k - 1]] + v * (m - (k - 1))
        off = V[i] - pred(k)
        if abs(off) > 0.3 and inside(i):
            back = any(abs(V[s[k + m]] - pred(k + m)) < 0.12 for m in (1, 2, 3))
            later = sum(V[s[m]] - pred(m) for m in range(k + 5, k + 16)) / 11.0
            if back: fl.append((i, off))
            elif abs(later) > 0.2 and later * off > 0: st.append((i, later))
            else: bu.append((i, off))
            k += 4
        else:
            k += 1
    return fl, st, bu

def main():
    d = sys.argv[1]
    R = {}
    for key, name in NAMES:
        T, S, X, Y, P = load(os.path.join(d, key + '.log'))
        R[key] = dict(T=T, S=S, x=[v / 200.0 for v in X], y=[v / 200.0 for v in Y], P=P)
    # a first line from all pressed reports, to split turns
    ax = []; ay = []
    for key, _ in NAMES:
        r = R[key]; ax += [r['x'][i] for i in range(len(r['T'])) if r['P'][i] > 0]; ay += [r['y'][i] for i in range(len(r['T'])) if r['P'][i] > 0]
    mx, my, ux, uy = linefit(ax, ay)
    for key, _ in NAMES:
        r = R[key]; x = r['x']; y = r['y']; T = r['T']
        a = [(x[i] - mx) * ux + (y[i] - my) * uy for i in range(len(T))]
        G, br = groups(T, x, y, r['P'])
        Ps = []
        for g in G:
            for s in split_turns(g, a):
                if abs(a[s[-1]] - a[s[0]]) < 80: continue
                s = [i for i in s if T[i] - T[s[0]] >= 150 and T[s[-1]] - T[i] >= 150]
                if len(s) > 200:
                    v = abs(a[s[-1]] - a[s[0]]) / ((T[s[-1]] - T[s[0]]) / 1000.0)
                    if SPD[0] <= v <= SPD[1]: Ps.append(s)
        r['passes'] = Ps; r['breaks'] = br
    # the final line, from the kept passes of all six
    ax = [R[k]['x'][i] for k, _ in NAMES for s in R[k]['passes'] for i in s]
    ay = [R[k]['y'][i] for k, _ in NAMES for s in R[k]['passes'] for i in s]
    mx, my, ux, uy = linefit(ax, ay)
    th = math.atan2(uy, ux)
    out = []
    pr = lambda t: (out.append(t), print(t))
    pr('line at %.2f deg through (%.1f, %.1f) mm (all six); sideways = %.3f X %+.3f Y' % (math.degrees(th), mx, my, -uy, ux))
    cover = {}
    for key, _ in NAMES:
        r = R[key]; x = r['x']; y = r['y']
        r['a'] = [(x[i] - mx) * ux + (y[i] - my) * uy for i in range(len(x))]
        r['d'] = [-(x[i] - mx) * uy + (y[i] - my) * ux for i in range(len(x))]
        for s in r['passes']:
            A = [r['a'][i] for i in s]; dirn = 1 if A[-1] > A[0] else -1
            for b in range(int(math.floor(min(A) / BIN)), int(math.floor(max(A) / BIN)) + 1):
                cover[(key, dirn, b)] = cover.get((key, dirn, b), 0) + 1
    allb = sorted(set(b for (_, _, b) in cover))
    ok = [b for b in allb if all(cover.get((k, dd, b), 0) >= NEED for k, _ in NAMES for dd in (1, -1))]
    best = (0, 0); run = []
    for b in ok:
        if run and b == run[-1] + 1: run.append(b)
        else: run = [b]
        if len(run) > best[1] - best[0]: best = (run[0], run[-1] + 1)
    lo = best[0] * BIN + 2; hi = best[1] * BIN - 2
    pr('common stretch: %.1f to %.1f mm along the line (%.0f mm), covered by %d+ passes each way in all six; pass speeds %.0f-%.0f mm/s' % (lo, hi, hi - lo, NEED, SPD[0], min(SPD[1], 9999)))
    for key, name in NAMES:
        r = R[key]; pr('   %-20s pass speeds (mm/s): %s' % (name, ' '.join('%.0f' % (abs(r['a'][s[-1]] - r['a'][s[0]]) / ((r['T'][s[-1]] - r['T'][s[0]]) / 1000.0)) for s in r['passes'])))
    pr('')
    summary = []
    for key, name in NAMES:
        r = R[key]; T = r['T']; P = r['P']; a = r['a']; dd = r['d']
        bins = {}; scat = {}
        sp = []; pres = []; dts = []
        for s in r['passes']:
            A = [a[i] for i in s]; dirn = 1 if A[-1] > A[0] else -1
            ds = runmean([dd[i] for i in s], 4)
            inn = [v for v, av in zip(ds, A) if lo <= av <= hi]
            if len(inn) < 100: continue
            off = sum(inn) / len(inn)
            per = {}
            for v, av in zip(ds, A):
                if lo <= av <= hi: per.setdefault(int(math.floor(av / BIN)), []).append(v - off)
            for b, vs in per.items(): bins.setdefault((dirn, b), []).append(sum(vs) / len(vs))
            sp.append(abs(A[-1] - A[0]) / ((T[s[-1]] - T[s[0]]) / 1000.0))
            pres += [P[i] for i in s]
            dts += [T[s[j]] - T[s[j - 1]] for j in range(1, len(s))]
        E = {}; Q = {}
        for dirn in (1, -1):
            keys = sorted(b for (d2, b) in bins if d2 == dirn and len(bins[(d2, b)]) >= 3)
            E[dirn] = dict((b, sum(bins[(dirn, b)]) / len(bins[(dirn, b)])) for b in keys)
            Q[dirn] = [math.sqrt(sum((v - E[dirn][b]) ** 2 for v in bins[(dirn, b)]) / (len(bins[(dirn, b)]) - 1)) for b in keys]
        both = sorted(set(E[1]) & set(E[-1]))
        res = dict(name=name, passes=sum(1 for _ in r['passes']))
        pr('== %s (%s): %d passes (%d +, %d -), speed median %.0f mm/s, pressure median %d (10-90 %% %d-%d), frames 1 ms apart %.1f %%'
           % (name, key, res['passes'], len(set(1 for _ in [0])) and sum(1 for s in r['passes'] if a[s[-1]] > a[s[0]]), sum(1 for s in r['passes'] if a[s[-1]] < a[s[0]]),
              pct(sp, .5), pct(pres, .5), pct(pres, .1), pct(pres, .9), 100.0 * sum(1 for t in dts if t < 1.5) / max(1, len(dts))))
        coil = {}
        for dirn in (1, -1):
            ks = sorted(E[dirn]); vals = [E[dirn][b] for b in ks]
            slow = runmean(vals, 40); fast = [v - s2 for v, s2 in zip(vals, slow)]
            coil[dirn] = dict(zip(ks, fast))
            pr('   direction %+d: repeatable p-p %.3f mm; slow p-p %.3f; coil-scale p-p %.3f, sd %.3f; pass scatter sd %.3f'
               % (dirn, max(vals) - min(vals), max(slow) - min(slow), max(fast) - min(fast), math.sqrt(sum(v * v for v in fast) / len(fast)), math.sqrt(sum(v * v for v in Q[dirn]) / len(Q[dirn]))))
            res['cs%+d' % dirn] = max(fast) - min(fast); res['sd%+d' % dirn] = math.sqrt(sum(v * v for v in fast) / len(fast))
        df = [coil[1][b] - coil[-1][b] for b in both if b in coil[1] and b in coil[-1]]
        pr('   forward minus back (coil-scale): max %.3f mm, rms %.3f' % (max(abs(v) for v in df), math.sqrt(sum(v * v for v in df) / len(df))))
        res['fb'] = math.sqrt(sum(v * v for v in df) / len(df))
        # fold the mean coil-scale error (both directions) by X and Y phase: f(phx) + g(phy)
        cb = [b for b in both if b in coil[1] and b in coil[-1]]
        ev = [(coil[1][b] + coil[-1][b]) / 2 for b in cb]
        px = []; py = []
        for b in cb:
            av = (b + 0.5) * BIN; xx = mx + av * ux; yy = my + av * uy
            px.append(int(phase(xx, 0) * 16) % 16); py.append(int(phase(yy, 1) * 16) % 16)
        f = [0.0] * 16; g = [0.0] * 16
        for it in range(20):
            for arr, idx, other, oidx in ((f, px, g, py), (g, py, f, px)):
                acc = [[] for _ in range(16)]
                for e, i, j in zip(ev, idx, oidx): acc[i].append(e - other[j])
                for i in range(16): arr[i] = sum(acc[i]) / len(acc[i]) if acc[i] else 0.0
        resid = [e - f[i] - g[j] for e, i, j in zip(ev, px, py)]
        tot = math.sqrt(sum(e * e for e in ev) / len(ev))
        pr('   coil-locked: X phase p-p %.3f mm sideways (X error about %.3f), Y phase p-p %.3f (Y error about %.3f); rms %.3f -> %.3f after both'
           % (max(f) - min(f), (max(f) - min(f)) / abs(uy), max(g) - min(g), (max(g) - min(g)) / abs(ux), tot, math.sqrt(sum(e * e for e in resid) / len(resid))))
        pr('      X phase 0..15/16: ' + ' '.join('%+.2f' % v for v in f))
        pr('      Y phase 0..15/16: ' + ' '.join('%+.2f' % v for v in g))
        res['fx'] = max(f) - min(f); res['fy'] = max(g) - min(g)
        # tip breaks inside joined passes
        br = r['breaks']
        tb = [b for b in br if b[2] > 0]
        pr('   breaks inside a pass: %d (tip off %d of them, %s ms; pressure before / after median %d / %d); report gaps over 3 ms with the tip down: %d'
           % (len(br), len(tb), '/'.join('%.0f' % b[1] for b in tb[:8]), pct([b[3] for b in tb], .5) if tb else 0, pct([b[4] for b in tb], .5) if tb else 0, sum(1 for b in br if b[2] == 0)))
        # jumps per axis
        tpress = sum((T[s[-1]] - T[s[0]]) / 1000.0 for s in r['passes'])
        js = {}
        for q, V in ((0, r['x']), (1, r['y'])):
            F = []; St = []; Bu = []
            for s in r['passes']:
                fl, st, bu = jumps(T, V, s, q, lambda i: lo <= a[i] <= hi and r['x'][i] > 3 and r['y'][i] > 3); F += fl; St += st; Bu += bu
            js[q] = (F, St, Bu)
            ph = [phase(V[i], q) for i, _ in F]
            mid = sum(1 for p in ph if 0.35 <= p <= 0.65)
            pr('   %s: flicks %d (%.1f/s, median %.2f mm, %d %% at a coil midpoint +-0.15), steps %d (%.1f/s, median %.2f), bursts %d'
               % ('XY'[q], len(F), len(F) / tpress, pct([abs(o) for _, o in F], .5), 100 * mid // max(1, len(F)), len(St), len(St) / tpress, pct([abs(o) for _, o in St], .5), len(Bu)))
            if F:
                hist = [0] * 10
                for p in ph: hist[min(9, int(p * 10))] += 1
                pr('      flicks by %s coil phase (0 = coil centre, 0.5 = midpoint), tenths: %s' % ('XY'[q], ' '.join(str(h) for h in hist)))
        res['fl'] = (len(js[0][0]) + len(js[1][0])) / tpress; res['st'] = (len(js[0][1]) + len(js[1][1])) / tpress
        r['steps'] = [a[i] for q in (0, 1) for i, _ in js[q][1]] + [a[i] for q in (0, 1) for i, _ in js[q][2]]
        r['flk'] = [a[i] for q in (0, 1) for i, _ in js[q][0]]
        summary.append(res)
        pr('')
    # physical bumps: steps / bursts at the same spot of the ruler in other recordings
    pr('== same spot in other recordings (within 1 mm along the line): steps and bursts, then flicks')
    for key, name in NAMES:
        for lab, field in (('steps+bursts', 'steps'), ('flicks', 'flk')):
            mine = R[key][field]; n3 = 0
            others = [sorted(R[k][field]) for k, _ in NAMES if k != key]
            for av in mine:
                c = sum(1 for o in others if any(abs(av - w) < 1.0 for w in o))
                if c >= 3: n3 += 1
            pr('   %-20s %-12s %4d, of them at a spot shared with 3+ other recordings: %d' % (name, lab, len(mine), n3))
    pr('')
    pr('summary            passes  coil p-p +/-   coil sd +/-    fwd-back rms  X-locked  Y-locked  flicks/s  steps/s')
    for s in summary:
        pr('%-18s %5d   %.3f/%.3f    %.3f/%.3f    %.3f        %.3f     %.3f     %5.1f    %5.1f'
           % (s['name'], s['passes'], s['cs+1'], s['cs-1'], s['sd+1'], s['sd-1'], s['fb'], s['fx'], s['fy'], s['fl'], s['st']))
    if len(sys.argv) > 2: open(sys.argv[2], 'w').write('\n'.join(out) + '\n')
if __name__ == '__main__': main()
