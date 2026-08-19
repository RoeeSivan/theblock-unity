#!/usr/bin/env python3
"""Compare rendered frames against a noise floor.

    tools/pixel-diff.py <floor-a.png> <floor-b.png> <changed.png> [more.png ...]

The first two images are TWO RENDERS OF THE SAME THING. Their difference is the floor - the amount
this renderer disagrees with itself for reasons that have nothing to do with the change under test.
Every later image is compared against the first, and its number only means something if it clears
that floor.

⚠ THE FLOOR IS NOT ZERO AND ASSUMING IT IS HAS ALREADY BEEN WRONG HERE. Animated water put 33% of
pixels in disagreement between two renders of an unchanged scene, which is larger than any change
worth making; the jetski's decimation had to be re-shot with the sea hidden before its 0.900% meant
anything. Temporal anti-aliasing, a swaying prop and a day-night clock all do the same thing.

Reports, per image:
  * pixels differing at all, as a share of the frame
  * pixels differing by more than 8/255 on any channel - a change a person could actually see
  * the mean and worst absolute difference
"""

import sys

import numpy as np
from PIL import Image

VISIBLE = 8  # per-channel 0-255 difference below which nobody is going to notice anything


def load(path: str) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.int16)


def compare(a: np.ndarray, b: np.ndarray, label: str) -> float:
    if a.shape != b.shape:
        print(f"  {label:<28} SHAPE MISMATCH {a.shape} vs {b.shape}")
        return float("nan")

    delta = np.abs(a - b)
    per_pixel = delta.max(axis=2)
    total = per_pixel.size

    any_diff = float((per_pixel > 0).sum()) / total * 100.0
    visible = float((per_pixel > VISIBLE).sum()) / total * 100.0
    print(f"  {label:<28} {any_diff:6.3f}% differ   {visible:6.3f}% visibly   "
          f"mean {delta.mean():5.2f}/255   worst {int(delta.max()):3d}/255")
    return visible


def main() -> None:
    if len(sys.argv) < 4:
        raise SystemExit("usage: pixel-diff.py <floor-a.png> <floor-b.png> <changed.png> [...]")

    reference = load(sys.argv[1])

    print("noise floor - two renders that should be identical:")
    floor = compare(reference, load(sys.argv[2]), "floor")

    print("\nagainst the reference:")
    for path in sys.argv[3:]:
        got = compare(reference, load(path), path.rsplit("/", 1)[-1])
        if got != got:
            continue
        verdict = "BELOW THE FLOOR - no evidence of a visual change" if got <= floor \
            else f"ABOVE THE FLOOR by {got - floor:.3f} points - look at it"
        print(f"  {'':<28} -> {verdict}")


if __name__ == "__main__":
    main()
