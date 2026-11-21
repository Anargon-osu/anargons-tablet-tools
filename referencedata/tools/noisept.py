import sys, math
# noisept.py LOG [--bins] (2026-11-13): still-pen noise at one point, from a vendorlog.exe --log recording of the cursor
# reports (report mode 4, ID 2, 12 bytes: status, x, y (1/200 mm), pressure, tilt). Prints where the pen is (and its
# phase within the coil, from the R43 coil grid), the spread and frame-to-frame steps per axis, the step size
# distribution, how the noise changes per second, and the strongest repeating periods (a DFT of each axis).
XC = (163.53, 5.080); YC = (96.42, 5.083)        # R43: coil k centre in reported mm = a - pitch k
def load(fn):
    T = []; X = []; Y = []; P = []; S = []
    for l in open(fn).read().splitlines()[1:]:
        p = l.split(',')
        if len(p) < 3: continue
        b = bytes.fromhex(p[2])
        if len(b) < 8 or b[0] != 2: continue
        T.append(float(p[0])); S.append(b[1]); X.append(b[2] | b[3] << 8); Y.append(b[4] | b[5] << 8); P.append(b[6] | b[7] << 8)
    return T, S, X, Y, P
def med(v):
    v = sorted(v); return v[len(v) // 2]
def main():
    T, S, X, Y, P = load(sys.argv[1])
    n = len(T)
    print('%d reports, %.1f s, status %s, pressure > 0 in %d' % (n, (T[-1] - T[0]) / 1000, sorted(set('%02x' % s for s in S)), sum(1 for p in P if p > 0)))
    for name, V, (a, pitch) in (('X', X, XC), ('Y', Y, YC)):
        mm = [v / 200.0 for v in V]
        m = sum(mm) / n
        sd = math.sqrt(sum((v - m) ** 2 for v in mm) / n)
        d = [mm[i] - mm[i - 1] for i in range(1, n)]
        rms = math.sqrt(sum(x * x for x in d) / len(d))
        k = (a - m) / pitch
        print('== %s at %.2f mm (coil %.2f: phase %.2f from coil %d)  spread sd %.4f mm, p-p %.3f, frame-to-frame rms %.4f mm, median |step| %.4f'
              % (name, m, k, k - math.floor(k), math.floor(k), sd, max(mm) - min(mm), rms, med([abs(x) for x in d])))
        for lim in (0.01, 0.02, 0.05, 0.1, 0.2):
            print('   steps over %.2f mm: %d (%.2f %%)' % (lim, sum(1 for x in d if abs(x) > lim), 100.0 * sum(1 for x in d if abs(x) > lim) / len(d)))
        # per second: spread and mean
        sec = []
        i0 = 0
        while i0 < n:
            i1 = i0
            while i1 < n and T[i1] - T[i0] < 1000: i1 += 1
            seg = mm[i0:i1]
            if len(seg) > 10:
                ms = sum(seg) / len(seg)
                sec.append((ms, math.sqrt(sum((v - ms) ** 2 for v in seg) / len(seg)), max(seg) - min(seg)))
            i0 = i1
        print('   per second, sd (mm):   ' + ' '.join('%.3f' % s[1] for s in sec))
        print('   per second, mean - first (mm): ' + ' '.join('%+.2f' % (s[0] - sec[0][0]) for s in sec))
        # DFT of the frame-to-frame series at 1000/s: strongest periods 2..400 frames
        mm0 = [v - m for v in mm]
        best = []
        for per in list(range(2, 41)) + list(range(42, 401, 2)):
            f = 2 * math.pi / per
            c = s = 0.0
            for i in range(0, n, 1):
                c += mm0[i] * math.cos(f * i); s += mm0[i] * math.sin(f * i)
            amp = 2 * math.sqrt(c * c + s * s) / n
            best.append((amp, per))
        best.sort(reverse=True)
        print('   strongest periods (frames = ms at 1000/s): ' + ', '.join('%d (%.4f mm)' % (p, a) for a, p in best[:6]))
if __name__ == '__main__': main()
