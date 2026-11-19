"""Agent human-likeness check (Roadmap §4.8): agents vs recorded teammates doing the same task.

Two comparisons, both descriptive:
  1. Head motion: linear and angular head speed distributions (world frame, teleport and snap-turn frames
     excluded), compared by percentiles, the Kolmogorov-Smirnov distance and histogram overlap.
  2. Control volumes (Giannone et al. 2026): time spent in each zone and the order zones are visited,
     compared by mean dwell per zone and the normalised edit distance between visit sequences.

Inputs are qaxr_trace_v1 CSVs (Docs~/AgentSpec.md). Zones are a CSV: name,xmin,zmin,xmax,zmax (metres).

Usage
  python agent_likeness.py --agents "run1/agent_*_trace.csv" --humans "humans/*_trace.csv" --zones zones.csv --plot likeness.png
  python agent_likeness.py --selftest
"""
import argparse
import csv
import glob
import math
import os
import sys
import tempfile

import numpy as np

TELEPORT, SNAP = 8, 4


def load_trace(path):
    """Returns t (n,), head world positions (n,3), head world quaternions (n,4 xyzw), loco flags (n,)."""
    with open(path, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(line for line in f if not line.startswith("#")))
    col = lambda *names: np.array([[float(r[n]) for n in names] for r in rows])
    t = col("t")[:, 0]
    op, oq = col("origin_px", "origin_py", "origin_pz"), col("origin_qx", "origin_qy", "origin_qz", "origin_qw")
    hp, hq = col("head_px", "head_py", "head_pz"), col("head_qx", "head_qy", "head_qz", "head_qw")
    loco = np.array([int(r["loco"]) for r in rows])
    return t, op + rotate(oq, hp), qmul(oq, hq), loco


def qmul(a, b):
    ax, ay, az, aw = a.T
    bx, by, bz, bw = b.T
    return np.stack([aw * bx + ax * bw + ay * bz - az * by,
                     aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw,
                     aw * bw - ax * bx - ay * by - az * bz], axis=1)


def rotate(q, v):
    u, w = q[:, :3], q[:, 3:4]
    return v + 2 * np.cross(u, np.cross(u, v) + w * v)


def speeds(t, pos, rot, loco):
    """Linear (m/s) and angular (deg/s) head speeds between consecutive samples, skipping teleport/snap frames."""
    dt = np.diff(t)
    keep = (dt > 0) & ((loco[1:] & (TELEPORT | SNAP)) == 0)
    lin = np.linalg.norm(np.diff(pos, axis=0), axis=1)
    inv = rot[:-1] * np.array([-1, -1, -1, 1])
    dq = qmul(rot[1:], inv)
    ang = np.degrees(2 * np.arccos(np.clip(np.abs(dq[:, 3]), 0, 1)))  # abs: shortest way round
    return lin[keep] / dt[keep], ang[keep] / dt[keep]


def ks(a, b):
    """Two-sample Kolmogorov-Smirnov distance (max ECDF gap), 0 = identical, 1 = disjoint."""
    a, b = np.sort(a), np.sort(b)
    grid = np.concatenate([a, b])
    return float(np.max(np.abs(np.searchsorted(a, grid, "right") / len(a) - np.searchsorted(b, grid, "right") / len(b))))


def overlap(a, b, bins=40):
    """Histogram overlap coefficient: sum of min(p, q) over shared bins, 1 = identical."""
    hi = np.percentile(np.concatenate([a, b]), 99.5)
    p, edges = np.histogram(a, bins=bins, range=(0, hi))
    q, _ = np.histogram(b, bins=edges)
    return float(np.minimum(p / max(p.sum(), 1), q / max(q.sum(), 1)).sum())


def load_zones(path):
    if not path:
        return []
    with open(path, newline="", encoding="utf-8-sig") as f:
        return [(r["name"], float(r["xmin"]), float(r["zmin"]), float(r["xmax"]), float(r["zmax"])) for r in csv.DictReader(f)]


def control_volumes(t, pos, zones):
    """Dwell seconds per zone and the visit order (consecutive repeats collapsed, outside-all-zones dropped)."""
    dwell = {z[0]: 0.0 for z in zones}
    order = []
    for i in range(len(t) - 1):
        x, z = pos[i, 0], pos[i, 2]
        name = next((n for n, x0, z0, x1, z1 in zones if x0 <= x <= x1 and z0 <= z <= z1), None)
        if name is None:
            continue
        dwell[name] += t[i + 1] - t[i]
        if not order or order[-1] != name:
            order.append(name)
    return dwell, order


def edit_distance(a, b):
    """Levenshtein distance between two sequences, normalised by the longer length (0 = same order)."""
    if not a and not b:
        return 0.0
    prev = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        cur = [i]
        for j, y in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (x != y)))
        prev = cur
    return prev[-1] / max(len(a), len(b))


def group(paths, zones):
    lin, ang, dwells, orders = [], [], [], []
    for p in paths:
        t, pos, rot, loco = load_trace(p)
        l, a = speeds(t, pos, rot, loco)
        lin.append(l)
        ang.append(a)
        if zones:
            d, o = control_volumes(t, pos, zones)
            dwells.append(d)
            orders.append(o)
    return np.concatenate(lin), np.concatenate(ang), dwells, orders


def compare(agent_paths, human_paths, zones):
    al, aa, ad, ao = group(agent_paths, zones)
    hl, ha, hd, ho = group(human_paths, zones)
    report = {}
    for name, a, h in (("head_speed_mps", al, hl), ("head_angular_dps", aa, ha)):
        report[name] = {
            "agent_p50_p90_p99": [round(float(x), 3) for x in np.percentile(a, [50, 90, 99])],
            "human_p50_p90_p99": [round(float(x), 3) for x in np.percentile(h, [50, 90, 99])],
            "ks": round(ks(a, h), 3),
            "overlap": round(overlap(a, h), 3),
        }
    if zones:
        report["dwell_s_mean"] = {z[0]: (round(float(np.mean([d[z[0]] for d in ad])), 1), round(float(np.mean([d[z[0]] for d in hd])), 1))
                                  for z in zones}
        report["order_edit_distance_mean"] = round(float(np.mean([edit_distance(a, h) for a in ao for h in ho])), 3)
    return report, (al, aa, hl, ha)


def plot(data, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    al, aa, hl, ha = data
    fig, axes = plt.subplots(1, 2, figsize=(10, 3.5))
    for ax, a, h, label in ((axes[0], al, hl, "head speed (m/s)"), (axes[1], aa, ha, "head angular speed (deg/s)")):
        hi = np.percentile(np.concatenate([a, h]), 99.5)
        ax.hist(h, bins=40, range=(0, hi), density=True, alpha=0.5, label="humans")
        ax.hist(a, bins=40, range=(0, hi), density=True, alpha=0.5, label="agents")
        ax.set_xlabel(label)
        ax.legend()
    fig.tight_layout()
    fig.savefig(path, dpi=120)


def expand(patterns):
    return sorted(p for pat in patterns for p in glob.glob(pat))


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--agents", nargs="+")
    ap.add_argument("--humans", nargs="+")
    ap.add_argument("--zones")
    ap.add_argument("--plot")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    agents, humans = expand(a.agents or []), expand(a.humans or [])
    if not agents or not humans:
        ap.error("need at least one --agents and one --humans trace")
    report, data = compare(agents, humans, load_zones(a.zones))
    for k, v in report.items():
        print(f"{k}: {v}")
    if a.plot:
        plot(data, a.plot)
        print(f"plot written to {a.plot}")


# ---- self-test: synthetic traces in the real file format ------------------------------------------------

COLUMNS = (["t", "intent", "target", "held", "loco"]
           + [f"{p}_{c}" for p in ("origin", "head", "lhand", "rhand") for c in ("px", "py", "pz", "qx", "qy", "qz", "qw")]
           + ["sel_l", "sel_r", "act_l", "act_r"])


def write_trace(path, speed, turn_dps, route, seed):
    """A walker following `route` (list of (x, z)) at `speed` m/s while yawing its head back and forth."""
    rng = np.random.default_rng(seed)
    with open(path, "w", newline="", encoding="utf-8") as f:
        f.write("# format=qaxr_trace_v1\n# hz=30\n" + ",".join(COLUMNS) + "\n")
        t, pos = 0.0, np.array(route[0], dtype=float)
        for target in route[1:]:
            target = np.array(target, dtype=float)
            while np.linalg.norm(target - pos) > speed / 30:
                pos += (target - pos) / np.linalg.norm(target - pos) * speed / 30 * (1 + 0.1 * rng.standard_normal())
                yaw = math.radians(30 * math.sin(2 * math.pi * t * turn_dps / 120))
                hq = (0, math.sin(yaw / 2), 0, math.cos(yaw / 2))
                row = [f"{t:.4f}", "Move", "", "", "1", f"{pos[0]:.4f}", "0", f"{pos[1]:.4f}", "0", "0", "0", "1",
                       "0", "1.6", "0", *[f"{v:.5f}" for v in hq]] + ["0"] * 14 + ["0", "0", "0", "0"]
                f.write(",".join(row) + "\n")
                t += 1 / 30


def selftest():
    d = tempfile.mkdtemp()
    route = [(0, 0), (5, 0), (5, 5), (0, 5)]
    zones = os.path.join(d, "zones.csv")
    with open(zones, "w") as f:
        f.write("name,xmin,zmin,xmax,zmax\nA,-1,-1,1,1\nB,4,-1,6,1\nC,4,4,6,6\n")
    for i in range(3):
        write_trace(os.path.join(d, f"human_{i}.csv"), 1.3, 60, route, i)
        write_trace(os.path.join(d, f"agent_{i}.csv"), 1.3, 60, route, 10 + i)
        write_trace(os.path.join(d, f"robot_{i}.csv"), 3.0, 200, list(reversed(route)), 20 + i)
    z = load_zones(zones)
    alike, _ = compare(glob.glob(os.path.join(d, "agent_*.csv")), glob.glob(os.path.join(d, "human_*.csv")), z)
    unlike, data = compare(glob.glob(os.path.join(d, "robot_*.csv")), glob.glob(os.path.join(d, "human_*.csv")), z)
    assert alike["head_speed_mps"]["ks"] < 0.2, alike
    assert unlike["head_speed_mps"]["ks"] > 0.8, unlike
    assert alike["head_angular_dps"]["overlap"] > unlike["head_angular_dps"]["overlap"], (alike, unlike)
    assert alike["order_edit_distance_mean"] == 0.0, alike
    assert unlike["order_edit_distance_mean"] > 0.5, unlike
    assert abs(np.median(data[2]) - 1.3) < 0.1, "speeds come back in m/s"
    assert edit_distance(list("ABC"), list("ACB")) == 2 / 3
    plot(data, os.path.join(d, "l.png"))
    print("agent_likeness selftest ok")


if __name__ == "__main__":
    main(sys.argv[1:])
