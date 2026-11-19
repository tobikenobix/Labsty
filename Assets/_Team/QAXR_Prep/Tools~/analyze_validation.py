"""W6 validation analysis (Roadmap §4.8): does QAXR's risk score move like people's sickness reports?

Descriptive only (n <= 5): per participant and condition, the rating curve, the rolling risk score and the
SSQ/VRSQ change from baseline, plus how often the risk score orders the two conditions the same way people do.

Inputs
  --ratings DIR   CSVs from SicknessRatingPanel: timestamp,t_s,event,rating,latency_s,participant,condition
  --risk DIR      optional, one CSV per participant and condition, named <participant>_<condition>_risk.csv,
                  columns t_s,score (the 5-10 s rolling score, 0..1, t_s since the condition started)
  --ssq FILE      optional, output of ssq_score.py with phase = baseline / post
  --plot FILE     optional PNG: rating and risk over time, one panel per participant

Usage
  python analyze_validation.py --ratings ratings/ --risk risk/ --ssq ssq_scores.csv --plot validation.png
  python analyze_validation.py --selftest
"""
import argparse
import csv
import glob
import os
import statistics
import sys
import tempfile


def read_ratings(folder):
    """{(participant, condition): [(t, rating)]} from every rating CSV in the folder."""
    out = {}
    for path in sorted(glob.glob(os.path.join(folder, "*.csv"))):
        with open(path, newline="", encoding="utf-8-sig") as f:
            for row in csv.DictReader(f):
                if row.get("event") != "rating" or not row.get("rating"):
                    continue
                key = (row["participant"], row["condition"])
                out.setdefault(key, []).append((float(row["t_s"]), float(row["rating"])))
    return out


def read_risk(folder):
    out = {}
    if not folder:
        return out
    for path in sorted(glob.glob(os.path.join(folder, "*_risk.csv"))):
        name = os.path.basename(path)[: -len("_risk.csv")]
        participant, _, condition = name.partition("_")
        with open(path, newline="", encoding="utf-8-sig") as f:
            out[(participant, condition)] = [(float(r["t_s"]), float(r["score"])) for r in csv.DictReader(f)]
    return out


def read_ssq(path):
    """{(participant, condition): post_total - baseline_total} for SSQ and VRSQ."""
    if not path:
        return {}
    with open(path, newline="", encoding="utf-8-sig") as f:
        rows = list(csv.DictReader(f))
    baseline = {r["participant"]: r for r in rows if r.get("phase") == "baseline"}
    out = {}
    for r in rows:
        if r.get("phase") != "post":
            continue
        b = baseline.get(r["participant"])
        delta = lambda k: float(r[k]) - (float(b[k]) if b else 0.0)
        out[(r["participant"], r["condition"])] = {
            "ssq_post": float(r["ssq_total"]), "ssq_delta": delta("ssq_total"),
            "vrsq_post": float(r["vrsq_total"]), "vrsq_delta": delta("vrsq_total"),
        }
    return out


def mean(xs):
    return statistics.fmean(xs) if xs else float("nan")


def summarise(ratings, risk, ssq):
    keys = sorted(set(ratings) | set(risk) | set(ssq))
    rows = []
    for k in keys:
        r = [v for _, v in ratings.get(k, [])]
        s = [v for _, v in risk.get(k, [])]
        row = {"participant": k[0], "condition": k[1],
               "n_ratings": len(r), "mean_rating": mean(r), "peak_rating": max(r) if r else float("nan"),
               "mean_risk": mean(s), "peak_risk": max(s) if s else float("nan")}
        row.update(ssq.get(k, {"ssq_post": float("nan"), "ssq_delta": float("nan"),
                               "vrsq_post": float("nan"), "vrsq_delta": float("nan")}))
        rows.append(row)
    return rows


def agreement(rows, bad="bad", fixed="fixed"):
    """Per participant: does the risk score order bad vs fixed like each human measure does? Counts only defined pairs."""
    by = {}
    for r in rows:
        by.setdefault(r["participant"], {})[r["condition"]] = r
    result = {}
    for measure in ("mean_rating", "peak_rating", "ssq_delta", "vrsq_delta"):
        agree = total = 0
        for conds in by.values():
            if bad not in conds or fixed not in conds:
                continue
            dr = conds[bad]["mean_risk"] - conds[fixed]["mean_risk"]
            dh = conds[bad][measure] - conds[fixed][measure]
            if any(map(lambda x: x != x, (dr, dh))) or dr == 0 or dh == 0:  # NaN or tie
                continue
            total += 1
            agree += (dr > 0) == (dh > 0)
        result[measure] = (agree, total)
    return result


def fmt(v):
    return "" if v != v else (f"{v:.2f}" if isinstance(v, float) else str(v))


def plot(ratings, risk, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    people = sorted({k[0] for k in set(ratings) | set(risk)})
    fig, axes = plt.subplots(len(people), 1, figsize=(8, 2.6 * len(people)), squeeze=False, sharex=True)
    for ax, p in zip(axes[:, 0], people):
        twin = ax.twinx()
        for cond, style in (("bad", "-"), ("fixed", "--")):
            if (p, cond) in ratings:
                t, v = zip(*ratings[(p, cond)])
                ax.plot(t, v, style, marker="o", color="tab:red", label=f"rating {cond}")
            if (p, cond) in risk:
                t, v = zip(*risk[(p, cond)])
                twin.plot(t, v, style, color="tab:blue", label=f"risk {cond}")
        ax.set_ylabel(f"{p}\nrating 0-10", color="tab:red")
        ax.set_ylim(0, 10)
        twin.set_ylabel("risk 0-1", color="tab:blue")
        twin.set_ylim(0, 1)
        ax.legend(loc="upper left", fontsize=7)
        twin.legend(loc="upper right", fontsize=7)
    axes[-1, 0].set_xlabel("time in condition (s)")
    fig.tight_layout()
    fig.savefig(path, dpi=120)


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ratings")
    ap.add_argument("--risk")
    ap.add_argument("--ssq")
    ap.add_argument("--plot")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args(argv)
    if a.selftest:
        return selftest()
    if not a.ratings:
        ap.error("--ratings is required")
    ratings, risk, ssq = read_ratings(a.ratings), read_risk(a.risk), read_ssq(a.ssq)
    rows = summarise(ratings, risk, ssq)
    w = csv.DictWriter(sys.stdout, list(rows[0]), lineterminator="\n")
    w.writeheader()
    for r in rows:
        w.writerow({k: fmt(v) for k, v in r.items()})
    print("\n# risk orders bad vs fixed like the human measure (agree / comparable participants):", file=sys.stderr)
    for m, (agree, total) in agreement(rows).items():
        print(f"#   {m:12s} {agree}/{total}", file=sys.stderr)
    if a.plot:
        plot(ratings, risk, a.plot)
        print(f"# plot written to {a.plot}", file=sys.stderr)


def selftest():
    d = tempfile.mkdtemp()
    os.makedirs(os.path.join(d, "ratings"))
    os.makedirs(os.path.join(d, "risk"))
    for p, (bad_r, fix_r) in {"P01": (6, 2), "P02": (3, 4)}.items():
        for cond, val, risk in (("bad", bad_r, 0.7), ("fixed", fix_r, 0.2)):
            with open(os.path.join(d, "ratings", f"{p}_{cond}_x.csv"), "w", newline="") as f:
                f.write("timestamp,t_s,event,rating,latency_s,participant,condition\n")
                f.write(f"x,0.0,start,,,{p},{cond}\n")
                for i, t in enumerate((30, 60, 90)):
                    f.write(f"x,{t}.0,prompt,,,{p},{cond}\n")
                    f.write(f"x,{t + 2}.0,rating,{val + i - 1},2.00,{p},{cond}\n")
            with open(os.path.join(d, "risk", f"{p}_{cond}_risk.csv"), "w", newline="") as f:
                f.write("t_s,score\n" + "".join(f"{t},{risk}\n" for t in range(0, 120, 5)))
    with open(os.path.join(d, "ssq.csv"), "w", newline="") as f:
        f.write("participant,condition,phase,ssq_total,vrsq_total\n")
        f.write("P01,,baseline,7.48,5\nP01,bad,post,40,30\nP01,fixed,post,10,8\n")
    rows = summarise(read_ratings(os.path.join(d, "ratings")), read_risk(os.path.join(d, "risk")), read_ssq(os.path.join(d, "ssq.csv")))
    r = {(x["participant"], x["condition"]): x for x in rows}
    assert r[("P01", "bad")]["n_ratings"] == 3 and r[("P01", "bad")]["mean_rating"] == 6, r
    assert r[("P01", "bad")]["peak_rating"] == 7, r
    assert abs(r[("P01", "bad")]["ssq_delta"] - 32.52) < 1e-9, r
    assert abs(r[("P01", "fixed")]["mean_risk"] - 0.2) < 1e-9, r
    ag = agreement(rows)
    assert ag["mean_rating"] == (1, 2), ag   # P01 agrees, P02 rated fixed worse
    assert ag["ssq_delta"] == (1, 1), ag     # only P01 has SSQ
    plot(read_ratings(os.path.join(d, "ratings")), read_risk(os.path.join(d, "risk")), os.path.join(d, "v.png"))
    assert os.path.getsize(os.path.join(d, "v.png")) > 1000
    print("analyze_validation selftest ok")


if __name__ == "__main__":
    main(sys.argv[1:])
