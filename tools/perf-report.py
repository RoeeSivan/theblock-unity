#!/usr/bin/env python3
"""Turn a perf-sweep run into one table, with its own noise floor beside it.

    tools/perf-report.py perf-sweep-out [> report.md]

Reads the PERFPROBE RESULT lines PerfProbe.cs prints and the BootLoader timestamps the boot
already logged, and reports a per-feature delta for each of the units that owe U30b one.

THE NOISE FLOOR IS THE POINT. Every configuration is run twice, so the spread between two runs of
the SAME configuration is what this machine's measurement is worth on the day. A feature whose
delta is inside that spread is reported as "below the noise floor" and NOT as a number - which is
the difference between a measurement and a decimal point. The `baseline` rows exist only for this:
both arms are identical, so their spread is the floor for everything else.
"""

import re
import statistics
import sys
from pathlib import Path

RESULT = re.compile(r"PERFPROBE RESULT (.*)")
BOOT = re.compile(r"BootLoader: ([0-9.]+) ms - (.*)")

# feature -> what it is, for the report. Keyed to the ledger's own unit numbers so a row can be
# pasted onto the unit it settles.
UNITS = {
    "baseline": "— (noise floor: both arms identical)",
    "crowd": "U38  the twelve-body crowd",
    "lotcars": "—    the 101 parked scenery cars",
    "jetski": "—    both jetskis",
    "sea": "U36  the sea, beach and water entry",
    "ragdolls": "U35a ragdolls",
    "streetprops": "U35h breakable street props",
    "vehicledamage": "U35b vehicle damage",
    "autoshop": "U35g the auto shop",
    "falafel": "U37  the falafel stand",
    "heli": "U35c/i the police helicopter",
}

NUMERIC = ("frameMs", "worstMs", "setPass", "tris", "verts", "casters", "skinned", "texMb", "frames",
           "failed")


def parse(directory: Path):
    runs = []
    boots = []
    rejected = []
    for log in sorted(directory.glob("*.log")):
        text = log.read_text(errors="replace")

        match = RESULT.search(text)
        if match:
            row = {}
            for token in match.group(1).split():
                if "=" not in token:
                    continue
                key, value = token.split("=", 1)
                row[key] = float(value) if key in NUMERIC else value
            row["_log"] = log.name

            # A run that could not reach its pose measured SOMETHING - usually the title screen -
            # and averaging it into a delta would produce a confident wrong number rather than a
            # gap. Refuse it here, and say so, rather than letting it through quietly.
            if row.get("failed", 0):
                rejected.append(log.name)
            else:
                runs.append(row)

        stamps = BOOT.findall(text)
        if stamps:
            boots.append((log.name, [(float(ms), what) for ms, what in stamps]))
    return runs, boots, rejected


def group(runs):
    """(feature, state) -> list of runs, so repeats sit together."""
    out = {}
    for run in runs:
        out.setdefault((run["feature"], run["state"]), []).append(run)
    return out


def spread(values):
    """Half the range: how far one run of a configuration sits from another."""
    return (max(values) - min(values)) / 2 if len(values) > 1 else float("nan")


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: perf-report.py <sweep-output-dir>")
    directory = Path(sys.argv[1])
    runs, boots, rejected = parse(directory)
    if not runs:
        sys.exit(f"perf-report: no usable PERFPROBE RESULT lines under {directory} "
                 f"({len(rejected)} rejected as failed)")

    grouped = group(runs)

    # The floor, from the baseline rows: both arms are the same world, so anything they disagree
    # by is measurement noise and nothing else.
    base = [r["frameMs"] for key, rs in grouped.items() if key[0] == "baseline" for r in rs]
    floor = spread(base) if len(base) > 1 else float("nan")

    print("# U30b — per-feature frame deltas, measured on the Player\n")
    print(f"{len(runs)} runs across {len({r['feature'] for r in runs})} features, "
          f"{directory}.\n")
    if base:
        print(f"**Noise floor: ±{floor:.2f} ms.** Taken from {len(base)} runs of the identical "
              f"baseline configuration (mean {statistics.mean(base):.1f} ms). A delta smaller than "
              f"this is not a measurement.\n")
    if rejected:
        print(f"⚠ **{len(rejected)} run(s) rejected** — the probe could not reach its pose, so what "
              f"they sampled is not what they were asked to sample: "
              f"{', '.join(sorted(rejected))}.\n")

    print("| unit | pose | on, ms | off, ms | Δ frame | Δ tris | Δ setPass | verdict |")
    print("| --- | --- | --- | --- | --- | --- | --- | --- |")

    for feature in UNITS:
        on = grouped.get((feature, "on"))
        off = grouped.get((feature, "off"))
        if not on or not off:
            continue

        on_ms = statistics.mean(r["frameMs"] for r in on)
        off_ms = statistics.mean(r["frameMs"] for r in off)
        d_ms = on_ms - off_ms
        d_tris = statistics.mean(r["tris"] for r in on) - statistics.mean(r["tris"] for r in off)
        d_pass = statistics.mean(r["setPass"] for r in on) - statistics.mean(r["setPass"] for r in off)

        arm_spread = max(spread([r["frameMs"] for r in on]), spread([r["frameMs"] for r in off]))
        limit = max(floor, arm_spread) if floor == floor else arm_spread

        # Did the two arms actually measure the same world twice? `skinned` is the tell: if one
        # run of an arm saw 145 visible skinned meshes and the other saw 542, the crowd was still
        # streaming in when the sample started and the pair is not an A/B of anything.
        on_skin = [r["skinned"] for r in on]
        unsettled = spread(on_skin) > 0.25 * max(1.0, statistics.mean(on_skin))

        # Checked BEFORE the noise floor on purpose: an unsettled pair usually has a huge spread,
        # so the floor would swallow it and report "below the noise floor", which reads as "this
        # feature is free". The opposite is true - the measurement did not happen.
        if unsettled:
            verdict = "**UNSETTLED** — the arms did not sample the same world"
        elif limit == limit and abs(d_ms) <= limit:
            verdict = f"below the noise floor (±{limit:.2f})"
        elif abs(d_tris) < 250_000:
            # ⚠ THE GUARD THAT MATTERS. Milliseconds drift on a loaded laptop; triangle counts do
            # not drift at all, because they are exact counts the renderer publishes. So a large
            # Δms with a Δtris of essentially zero is the machine warming up between two runs, not
            # a feature costing anything - and without this it prints as a confident finding. The
            # falafel stand "handed back 10.6 ms" on a geometry delta of 0.06M, which is the shape
            # of a thermal ramp and not the shape of a saving.
            verdict = f"drift, not a delta ({d_ms:+.1f} ms on ~0 geometry)"
        elif d_ms > 0:
            verdict = f"**costs {d_ms:.1f} ms**"
        else:
            verdict = f"**hands back {-d_ms:.1f} ms**"

        print(f"| {UNITS[feature]} | {on[0]['pose']} | {on_ms:.1f} | {off_ms:.1f} | "
              f"{d_ms:+.2f} | {d_tris / 1e6:+.2f}M | {d_pass:+.0f} | {verdict} |")

    # --- boot, for free, on every launch ------------------------------------------------------
    if boots:
        print("\n## Boot, from the same runs\n")
        worst = []
        for name, stamps in boots:
            gaps = [(stamps[i + 1][0] - stamps[i][0], stamps[i][1], stamps[i + 1][1])
                    for i in range(len(stamps) - 1)]
            if gaps:
                worst.append((max(gaps)[0], name, max(gaps)))
        if worst:
            worst.sort(reverse=True)
            print(f"{len(boots)} boots logged. Worst single gap per run, largest first:\n")
            print("| run | gap, ms | between |")
            print("| --- | --- | --- |")
            for ms, name, (gap, a, b) in worst[:8]:
                print(f"| {name} | {gap:.0f} | {a} → {b} |")

    print("\n## Raw runs\n")
    print("| log | feature | state | frame ms | worst ms | setPass | tris | casters | skinned | tex MB |")
    print("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for run in sorted(runs, key=lambda r: (r["feature"], r["state"], r["_log"])):
        print(f"| {run['_log']} | {run['feature']} | {run['state']} | {run['frameMs']:.1f} | "
              f"{run['worstMs']:.0f} | {run['setPass']:.0f} | {run['tris'] / 1e6:.2f}M | "
              f"{run['casters']:.0f} | {run['skinned']:.0f} | {run['texMb']:.0f} |")


if __name__ == "__main__":
    main()
