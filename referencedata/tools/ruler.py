import sys, math
sys.path.insert(0, __import__('os').path.dirname(__import__('os').path.abspath(__file__)))
from noisept import load, XC, YC
# ruler.py LOG (2026-11-13, R45-rc4): true position error from pen strokes along a ruler, recorded through the normal
# firmware's cursor reports (vendorlog.exe --log, report mode 4, 1/200 mm). The ruler is straight, so the sideways
# distance from a straight line fitted to all strokes is the tablet's error (plus pen angle and pressure changes).
# Per stroke: sideways error after a 9-report running mean (frame noise mostly out). Then the error by position along
# the ruler, averaged per direction: the repeatable part (the same at that spot in every pass) and the part that
# depends on the direction (switch points move with the direction).
def strokes(T, X, Y, P):
    out = []; cur = []
    for i in range(len(T)):
        if P[i] <= 0 or (cur and T[i] - T[cur[-1]] > 20):
            if len(cur) > 200: out.append(cur)
            cur = []
            if P[i] <= 0: continue
        cur.append(i)
    if len(cur) > 200: out.append(cur)
    return out
def runmean(v, h):
    return [sum(v[max(0, i - h):i + h + 1]) / len(v[max(0, i - h):i + h + 1]) for i in range(len(v))]
def linefit(x, y):
    n = len(x); mx = sum(x) / n; my = sum(y) / n
    sxx = sum((a - mx) ** 2 for a in x); syy = sum((b - my) ** 2 for b in y); sxy = sum((a - mx) * (b - my) for a, b in zip(x, y))
    th = 0.5 * math.atan2(2 * sxy, sxx - syy)
    return mx, my, math.cos(th), math.sin(th)
def pct(v, q):
    v = sorted(v); return v[min(len(v) - 1, int(q * len(v)))]
def main():
    T, S, X, Y, P = load(sys.argv[1])
    x = [v / 200.0 for v in X]; y = [v / 200.0 for v in Y]
    St = strokes(T, X, Y, P)
    allx = [x[i] for s in St for i in s]; ally = [y[i] for s in St for i in s]
    mx, my, ux, uy = linefit(allx, ally)
    print('%d reports, %d strokes; line at %.1f deg through (%.1f, %.1f) mm' % (len(T), len(St), math.degrees(math.atan2(uy, ux)), mx, my))
    bins = {}; per = []
    for k, s in enumerate(St):
        a = [(x[i] - mx) * ux + (y[i] - my) * uy for i in s]          # along the ruler
        d = [-(x[i] - mx) * uy + (y[i] - my) * ux for i in s]         # sideways
        dirn = 1 if a[-1] > a[0] else -1
        ds = runmean(d, 4)
        # the stroke's own straight line (the ruler may shift a little between passes; angle kept from the global fit)
        off = sum(ds) / len(ds)
        e = [v - off for v in ds]
        sp = abs(a[-1] - a[0]) / ((T[s[-1]] - T[s[0]]) / 1000.0)
        per.append((k, dirn, len(s), abs(a[-1] - a[0]), sp, off, max(abs(v) for v in e), pct([abs(v) for v in e], .99)))
        for av, ev in zip(a, e):
            b = int(math.floor(av / 0.25)); bins.setdefault((dirn, b), []).append(ev)
    print('stroke  dir  reports  length mm  speed mm/s  ruler offset mm  max|err| mm  99% mm')
    for k, dirn, n, L, sp, off, m, q in per:
        print('%5d  %+d  %6d  %8.1f  %9.1f  %+12.3f  %10.3f  %6.3f' % (k + 1, dirn, n, L, sp, off, m, q))
    # repeatable error by position along the ruler (0.25 mm bins with 3+ passes' worth of reports)
    for dirn in (1, -1):
        E = {b: sum(v) / len(v) for (dd, b), v in bins.items() if dd == dirn and len(v) >= 30}
        if not E: continue
        vals = [E[b] for b in sorted(E)]
        print('direction %+d: repeatable error over %.0f mm of ruler: p-p %.3f mm, max|E| %.3f, sd %.3f'
              % (dirn, 0.25 * len(vals), max(vals) - min(vals), max(abs(v) for v in vals), math.sqrt(sum(v * v for v in vals) / len(vals))))
    both = [b for (dd, b) in bins if dd == 1 and (-1, b) in bins and len(bins[(1, b)]) >= 30 and len(bins[(-1, b)]) >= 30]
    if both:
        df = [sum(bins[(1, b)]) / len(bins[(1, b)]) - sum(bins[(-1, b)]) / len(bins[(-1, b)]) for b in sorted(both)]
        print('forward minus back at the same spot: max %.3f mm, rms %.3f (direction-dependent error)' % (max(abs(v) for v in df), math.sqrt(sum(v * v for v in df) / len(df))))
        # scatter between passes at the same spot (what a single pass adds on top of the repeatable error)
        sc = []
        for (dd, b), v in bins.items():
            if len(v) >= 30:
                m = sum(v) / len(v); sc += [u - m for u in v]
        print('pass-to-pass scatter at the same spot: sd %.3f mm, 99%% |.| %.3f' % (math.sqrt(sum(u * u for u in sc) / len(sc)), pct([abs(u) for u in sc], .99)))
    # where the biggest repeatable errors sit, as coil phase (R43 grid)
    for dirn in (1, -1):
        E = sorted(((sum(v) / len(v), b) for (dd, b), v in bins.items() if dd == dirn and len(v) >= 30), key=lambda r: -abs(r[0]))[:5]
        lst = []
        for ev, b in E:
            av = (b + 0.5) * 0.25; px = mx + av * ux; py = my + av * uy
            kx = (XC[0] - px) / XC[1]; ky = (YC[0] - py) / YC[1]
            lst.append('%+.3f at (%.1f, %.1f) phase X %.2f Y %.2f' % (ev, px, py, kx % 1, ky % 1))
        print('largest, direction %+d: ' % dirn + '; '.join(lst))
if __name__ == "__main__" and "--trim" not in sys.argv: main()
# second pass (ruler.py LOG --trim): full passes only (50 mm or longer), the first and last 150 ms of each stroke and the
# outer 10 mm of the ruler dropped. The repeatable error is split into the slow part (a +-10 mm running mean: tablet
# warp, or the ruler bending under the hand) and the coil-scale part (what is left: coil waves and switch steps).
# Local steps: a line through 15 reports on each side of every report, the sideways gap between them where they meet.
def trimmed(fn):
    T, S, X, Y, P = load(fn)
    x = [v / 200.0 for v in X]; y = [v / 200.0 for v in Y]
    St = []
    for s in strokes(T, X, Y, P):
        if math.hypot(x[s[-1]] - x[s[0]], y[s[-1]] - y[s[0]]) < 50: continue
        s = [i for i in s if T[i] - T[s[0]] >= 150 and T[s[-1]] - T[i] >= 150]
        St.append(s)
    mx, my, ux, uy = linefit([x[i] for s in St for i in s], [y[i] for s in St for i in s])
    A = [[(x[i] - mx) * ux + (y[i] - my) * uy for i in s] for s in St]
    lo = max(min(a) for a in A) + 10; hi = min(max(a) for a in A) - 10
    bins = {}; steps = []
    for s, a in zip(St, A):
        d = [-(x[i] - mx) * uy + (y[i] - my) * ux for i in s]
        dirn = 1 if a[-1] > a[0] else -1
        ds = runmean(d, 4); off = sum(ds) / len(ds)
        for av, dv in zip(a, ds):
            if lo <= av <= hi: bins.setdefault((dirn, int(math.floor(av / 0.25))), []).append(dv - off)
        for i in range(15, len(d) - 15, 3):
            if not lo <= a[i] <= hi: continue
            def lf(ix):
                ts = [T[s[j]] for j in ix]; vs = [d[j] for j in ix]; n = len(ts); mt = sum(ts) / n; mv = sum(vs) / n
                sxx = sum((t - mt) ** 2 for t in ts); b = sum((t - mt) * (v - mv) for t, v in zip(ts, vs)) / sxx if sxx else 0
                tm = 0.5 * (T[s[i - 1]] + T[s[i]]); return mv + b * (tm - mt)
            steps.append(abs(lf(range(i, i + 15)) - lf(range(i - 15, i))))
    print('trimmed: %d passes, ruler from %.0f to %.0f mm along the line (%.0f mm)' % (len(St), lo, hi, hi - lo))
    for dirn in (1, -1):
        keys = sorted(b for (dd, b) in bins if dd == dirn and len(bins[(dd, b)]) >= 30)
        E = [sum(bins[(dirn, b)]) / len(bins[(dirn, b)]) for b in keys]
        slow = runmean(E, 40); fast = [e - s for e, s in zip(E, slow)]
        print('direction %+d: repeatable p-p %.3f mm; slow part p-p %.3f; coil-scale part p-p %.3f, max|.| %.3f, sd %.3f'
              % (dirn, max(E) - min(E), max(slow) - min(slow), max(fast) - min(fast), max(abs(v) for v in fast), math.sqrt(sum(v * v for v in fast) / len(fast))))
    both = sorted(b for (dd, b) in bins if dd == 1 and (-1, b) in bins and len(bins[(1, b)]) >= 30 and len(bins[(-1, b)]) >= 30)
    df = [sum(bins[(1, b)]) / len(bins[(1, b)]) - sum(bins[(-1, b)]) / len(bins[(-1, b)]) for b in both]
    dfs = runmean(df, 40); dff = [a - b for a, b in zip(df, dfs)]
    print('forward minus back, coil-scale part: max %.3f mm, rms %.3f' % (max(abs(v) for v in dff), math.sqrt(sum(v * v for v in dff) / len(dff))))
    steps.sort()
    print('local sideways steps (15 reports each side): median %.3f, 90%% %.3f, 99%% %.3f, max %.3f mm; over 0.1 mm: %.1f %%'
          % (steps[len(steps) // 2], steps[int(.9 * len(steps))], steps[int(.99 * len(steps))], steps[-1], 100.0 * sum(1 for v in steps if v > 0.1) / len(steps)))
if __name__ == '__main__' and '--trim' in sys.argv: trimmed(sys.argv[1])
