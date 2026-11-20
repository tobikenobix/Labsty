"""SSQ + VRSQ scoring for the QAXR validation sessions (Roadmap §4.8).

One questionnaire, two scores: the full 16-item SSQ (Kennedy et al. 1993) and the 9-item VRSQ
(Kim et al. 2018) computed from the same answers. Items are rated 0-3 (none, slight, moderate, severe),
numbered as in Kim et al. Table 1:

   1 general discomfort   5 difficulty focusing      9 difficulty concentrating  13 dizzy (eyes closed)
   2 fatigue              6 increased salivation    10 fullness of head          14 vertigo
   3 headache             7 sweating                11 blurred vision            15 stomach awareness
   4 eyestrain            8 nausea                  12 dizzy (eyes open)         16 burping

Usage:
  python ssq_score.py --template > answers.csv     # empty sheet to fill in
  python ssq_score.py answers.csv > scores.csv     # one score row per answer row
  python ssq_score.py --selftest

Input CSV: any extra columns (participant, condition, phase = baseline/bad/fixed, ...) pass through
unchanged, plus q1..q16. Report n <= 5 descriptively, no significance tests.
"""
import csv
import sys

SSQ = {  # component: (items, weight)
    "ssq_nausea": ((1, 6, 7, 8, 9, 15, 16), 9.54),
    "ssq_oculomotor": ((1, 2, 3, 4, 5, 9, 11), 7.58),
    "ssq_disorientation": ((5, 8, 10, 11, 12, 13, 14), 13.92),
}
VRSQ = {  # component: (items, max raw sum); headache moves to disorientation in the VRSQ
    "vrsq_oculomotor": ((1, 2, 4, 5), 12),
    "vrsq_disorientation": ((3, 10, 11, 13, 14), 15),
}
ITEMS = [f"q{i}" for i in range(1, 17)]


def score(answers):
    """answers: dict item number (1..16) -> 0..3. Returns dict of scores rounded to 2 decimals."""
    for i in range(1, 17):
        if answers.get(i) not in (0, 1, 2, 3):
            raise ValueError(f"q{i} must be 0, 1, 2 or 3, got {answers.get(i)!r}")
    raw = {name: sum(answers[i] for i in items) for name, (items, _) in SSQ.items()}
    out = {name: raw[name] * w for name, (_, w) in SSQ.items()}
    out["ssq_total"] = sum(raw.values()) * 3.74
    for name, (items, maximum) in VRSQ.items():
        out[name] = sum(answers[i] for i in items) / maximum * 100
    out["vrsq_total"] = (out["vrsq_oculomotor"] + out["vrsq_disorientation"]) / 2
    return {k: round(v, 2) for k, v in out.items()}


def main(path):
    with open(path, newline="", encoding="utf-8-sig") as f:
        rows = list(csv.DictReader(f))
    if not rows:
        sys.exit("no rows")
    passthrough = [c for c in rows[0] if c not in ITEMS]
    writer = None
    for n, row in enumerate(rows, start=2):  # line 1 is the header
        try:
            scores = score({i: int(row[f"q{i}"]) for i in range(1, 17)})
        except (KeyError, ValueError, TypeError) as e:  # missing column, blank or short row
            sys.exit(f"{path} line {n}: {e}")
        if writer is None:
            writer = csv.DictWriter(sys.stdout, passthrough + list(scores), lineterminator="\n")
            writer.writeheader()
        writer.writerow({**{c: row[c] for c in passthrough}, **scores})


def selftest():
    ones = score({i: 1 for i in range(1, 17)})
    assert ones["ssq_nausea"] == 66.78, ones          # 7 items x 9.54
    assert ones["ssq_oculomotor"] == 53.06, ones      # 7 x 7.58
    assert ones["ssq_disorientation"] == 97.44, ones  # 7 x 13.92
    assert ones["ssq_total"] == 78.54, ones           # 21 x 3.74
    assert ones["vrsq_oculomotor"] == ones["vrsq_disorientation"] == ones["vrsq_total"] == 33.33, ones
    worst = score({i: 3 for i in range(1, 17)})
    assert worst["vrsq_total"] == 100.0, worst
    zero = score({i: 0 for i in range(1, 17)})
    assert all(v == 0 for v in zero.values()), zero
    only_headache = score({**{i: 0 for i in range(1, 17)}, 3: 3})
    assert only_headache["vrsq_oculomotor"] == 0 and only_headache["vrsq_disorientation"] == 20.0, only_headache
    try:
        score({i: 4 for i in range(1, 17)})
        raise AssertionError("out-of-range answer accepted")
    except ValueError:
        pass
    print("ssq_score selftest ok")


if __name__ == "__main__":
    if len(sys.argv) == 2 and sys.argv[1] == "--selftest":
        selftest()
    elif len(sys.argv) == 2 and sys.argv[1] == "--template":
        print(",".join(["participant", "condition", "phase"] + ITEMS))
    elif len(sys.argv) == 2:
        main(sys.argv[1])
    else:
        sys.exit(__doc__)
