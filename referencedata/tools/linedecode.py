"""R42 line recorder: decode hwdual log CSVs (time_us,b0..b9) from a line-recorder measurement build.

Each copy = pages 0xF0..0xF4 in order (odd frames carry the usual 0xE0.. pages in between). Per copy: X lines c-2..c+2,
Y lines c-2..c+2 (sums; x4 when the hover stage is not 0), centre coils, the firmware's mapped positions, peaks,
pressure/status at send time, AGC delays, stage, samples. Also checks the stock formula (0x08005432) against the
firmware's position: mapped = stock(lines) + a constant offset per axis.

usage: python -I linedecode.py LOG.csv [...]      (prints a summary; import decode() for analysis)
"""
import sys

W = 1018


def stock(a, b, c, d, e, cc):
    """The firmware's position routine 0x08005432, integer division truncating toward zero."""
    def div(x, y):
        q = abs(x) // abs(y)
        return q if (x >= 0) == (y >= 0) else -q
    p1 = p2 = 0
    if b - c - d + e != 0 and a - b - c + d != 0:
        p1 = cc * W + (div((b - d) * W, b - c - d + e) if d >= b else div((b - d) * W, a - b - c + d))
    den2 = 2 * (2 * c - b - d)
    if den2 != 0:
        p2 = cc * W - div((b - d) * W, den2)
    if abs(p2 - p1) > W:
        return p2
    return div(p1 + p2, 2)


def u16(b, i):
    return b[i] | (b[i + 1] << 8)


def decode(path):
    rows = []
    for ln in open(path).read().splitlines()[1:]:
        v = [int(x) for x in ln.split(',')]
        if len(v) == 11 and v[1] == 7:
            rows.append((v[0], v[1:]))
    copies, pos_pages = [], []
    pg = [None] * 5
    expect = 0
    broken = 0
    for t, b in rows:
        if b[1] == 0xC0 and b[9] in (0xE1, 0xE5):      # position page: x, y (0.01 mm), pressure, status
            pos_pages.append((t, u16(b, 2), u16(b, 4), u16(b, 6), b[8]))
        k = b[9] - 0xF0
        if b[1] != 0xC0 or k < 0 or k > 4:
            continue
        if k != expect:
            broken += 1
            expect = 0
            if k != 0:
                continue
        pg[k] = (t, b)
        expect = k + 1
        if k < 4:
            continue
        expect = 0
        p = [x[1] for x in pg]
        stage = p[4][7]
        mul = 4 if stage else 1
        X = [u16(p[0], 2), u16(p[0], 4), u16(p[0], 6), u16(p[1], 2), u16(p[1], 4)]
        Y = [u16(p[1], 6), u16(p[2], 2), u16(p[2], 4), u16(p[2], 6), u16(p[3], 2)]
        copies.append({
            't': pg[0][0], 'seq': p[0][8], 'X': [x * mul for x in X], 'Y': [y * mul for y in Y],
            'sat': any(x == 0xFFFF for x in X + Y),
            'cx': p[1][8], 'cy': p[2][8], 'px': u16(p[3], 4), 'py': u16(p[3], 6),
            'pkx': p[3][8] & 15, 'pky': p[3][8] >> 4,
            'press': u16(p[4], 2), 'status': p[4][4], 'agcx': p[4][5], 'agcy': p[4][6], 'stage': stage, 'samples': p[4][8]})
    return copies, pos_pages, broken


def summary(path):
    copies, pos_pages, broken = decode(path)
    if not copies:
        print(path, ': no line copies')
        return
    secs = (copies[-1]['t'] - copies[0]['t']) / 1e6 or 1
    print('%s: %d copies (%.0f/s), %d out-of-order pages, %d position pages' % (path, len(copies), len(copies) / secs, broken, len(pos_pages)))
    # stock formula + constant offset == the firmware's mapped position (peak at the centre line only)
    for ax in ('X', 'Y'):
        offs = {}
        n = 0
        for c in copies:
            if c['pk' + ax.lower()] != 3 or c['sat']:
                continue
            L = c[ax]
            if c['stage']:
                continue                       # x4 copies lost their low bits; the formula is only exact in stage 0
            s = stock(L[0], L[1], L[2], L[3], L[4], c['c' + ax.lower()])
            d = (c['p' + ax.lower()] - s) & 0xFFFF
            offs[d] = offs.get(d, 0) + 1
            n += 1
        top = sorted(offs.items(), key=lambda kv: -kv[1])[:3]
        print('   %s: firmware position - stock formula, fast-stage copies with the peak on the centre line: %s of %d' % (ax, ', '.join('%d (%d)' % kv for kv in top), n))
    st = {}
    for c in copies:
        st[c['stage']] = st.get(c['stage'], 0) + 1
    print('   stages: ' + ', '.join('%d: %d' % kv for kv in sorted(st.items())) + '; in range (status 0x20): %d' % sum(1 for c in copies if c['status'] & 0x20))


if __name__ == '__main__':
    for f in sys.argv[1:]:
        summary(f)
