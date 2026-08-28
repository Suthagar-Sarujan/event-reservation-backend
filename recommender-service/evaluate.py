"""
Offline evaluation of the content-based recommender: Precision@k, Recall@k, NDCG@k.

Methodology
-----------
This app has almost no real booking history (a handful of bookings made during
development/testing, not organic user behaviour), so a conventional held-out-
interactions evaluation would be evaluating noise. Instead this script uses two
proxy-relevance strategies that are standard for content-based recommenders
evaluated offline without an interaction log:

1. Similar-events task (tests `similar_events`, i.e. "more like this event"):
   for a sampled seed event, the ground-truth "relevant" set is every OTHER
   currently-bookable event that shares at least one performer with the seed
   (e.g. another game featuring the same team, another show by the same
   artist). This directly tests the design choice in recommender.py that a
   shared performer is the strongest similarity signal (PERFORMER_WEIGHT = 2.0).

2. Personalized-recommendation task (tests `recommend_for_user`): for each
   performer with >=2 currently-bookable events, a synthetic user profile is
   built via leave-one-out - the performer's other event(s) become the
   simulated "booking history", and the held-out event is the ground-truth
   relevant item. This is a simulated profile, not a real user, and is
   reported as such.

Both tasks are also run against the `popular()` fallback as a non-personalized
baseline, so the report can state whether the content-based model beats naive
popularity ranking rather than just reporting numbers in isolation.

Usage
-----
    cd recommender-service
    python evaluate.py [--k 5 10] [--seed 42] [--max-samples 300] [--out results.md]

Requires the same MySQL connection as the running service (reads app/config.py).
"""

from __future__ import annotations

import argparse
import random
import sys
from collections import defaultdict
from dataclasses import dataclass, field

import numpy as np

from app.db import load_dataset
from app.recommender import ContentBasedRecommender


# ---------------------------------------------------------------------------
# Metrics - standard top-k retrieval metrics, binary relevance.
# ---------------------------------------------------------------------------

def precision_at_k(ranked_ids: list[int], relevant: set[int], k: int) -> float:
    if k <= 0:
        return 0.0
    top_k = ranked_ids[:k]
    hits = sum(1 for item in top_k if item in relevant)
    return hits / k


def recall_at_k(ranked_ids: list[int], relevant: set[int], k: int) -> float:
    if not relevant:
        return 0.0
    top_k = ranked_ids[:k]
    hits = sum(1 for item in top_k if item in relevant)
    return hits / len(relevant)


def ndcg_at_k(ranked_ids: list[int], relevant: set[int], k: int) -> float:
    top_k = ranked_ids[:k]
    dcg = sum(1.0 / np.log2(i + 2) for i, item in enumerate(top_k) if item in relevant)
    ideal_hits = min(len(relevant), k)
    idcg = sum(1.0 / np.log2(i + 2) for i in range(ideal_hits))
    return dcg / idcg if idcg > 0 else 0.0


@dataclass
class TaskResult:
    name: str
    n_samples: int
    metrics: dict[int, dict[str, float]] = field(default_factory=dict)  # k -> {precision, recall, ndcg}


def _average_metrics(rows: list[tuple[list[int], set[int]]], ks: list[int]) -> dict[int, dict[str, float]]:
    out: dict[int, dict[str, float]] = {}
    for k in ks:
        precisions = [precision_at_k(ranked, relevant, k) for ranked, relevant in rows]
        recalls = [recall_at_k(ranked, relevant, k) for ranked, relevant in rows]
        ndcgs = [ndcg_at_k(ranked, relevant, k) for ranked, relevant in rows]
        out[k] = {
            "precision": float(np.mean(precisions)) if precisions else 0.0,
            "recall": float(np.mean(recalls)) if recalls else 0.0,
            "ndcg": float(np.mean(ndcgs)) if ndcgs else 0.0,
        }
    return out


# ---------------------------------------------------------------------------
# Task 1: similar events (content-based "more like this")
# ---------------------------------------------------------------------------

def build_performer_groups(recommender: ContentBasedRecommender) -> dict[int, list[int]]:
    """performer_id -> bookable event ids featuring that performer."""
    groups: dict[int, list[int]] = defaultdict(list)
    for event_id in recommender.bookable_event_ids:
        for performer_id in recommender.event_performers.get(event_id, ()):
            groups[performer_id].append(event_id)
    return groups


def run_similar_events_task(
    recommender: ContentBasedRecommender,
    performer_groups: dict[int, list[int]],
    ks: list[int],
    max_samples: int,
    rng: random.Random,
    use_baseline: bool = False,
) -> TaskResult:
    max_k = max(ks)
    seeds = [eid for eid, group in _seed_candidates(performer_groups) if eid in recommender.bookable_event_ids]
    rng.shuffle(seeds)
    seeds = seeds[:max_samples]

    rows: list[tuple[list[int], set[int]]] = []
    for seed_id in seeds:
        relevant = _same_performer_relevant(seed_id, recommender, performer_groups)
        if not relevant:
            continue
        if use_baseline:
            ranked = [item["event_id"] for item in recommender.popular(max_k, exclude={seed_id})]
        else:
            ranked = [item["event_id"] for item in recommender.similar_events(seed_id, top_n=max_k)]
        rows.append((ranked, relevant))

    name = "Similar events (popularity baseline)" if use_baseline else "Similar events (content-based)"
    result = TaskResult(name=name, n_samples=len(rows))
    result.metrics = _average_metrics(rows, ks)
    return result


def _seed_candidates(performer_groups: dict[int, list[int]]):
    for performer_id, events in performer_groups.items():
        if len(events) >= 2:
            for eid in events:
                yield eid, events


def _same_performer_relevant(
    seed_id: int, recommender: ContentBasedRecommender, performer_groups: dict[int, list[int]]
) -> set[int]:
    relevant: set[int] = set()
    for performer_id in recommender.event_performers.get(seed_id, ()):
        for eid in performer_groups.get(performer_id, ()):
            if eid != seed_id:
                relevant.add(eid)
    return relevant


# ---------------------------------------------------------------------------
# Task 2: personalized recommendations via simulated leave-one-out profiles
# ---------------------------------------------------------------------------

def run_personalized_task(
    recommender: ContentBasedRecommender,
    performer_groups: dict[int, list[int]],
    ks: list[int],
    max_samples: int,
    rng: random.Random,
    use_baseline: bool = False,
) -> TaskResult:
    max_k = max(ks)
    eligible_performers = [pid for pid, events in performer_groups.items() if len(events) >= 2]
    rng.shuffle(eligible_performers)

    rows: list[tuple[list[int], set[int]]] = []
    for performer_id in eligible_performers:
        if len(rows) >= max_samples:
            break
        events = performer_groups[performer_id]
        held_out = rng.choice(events)
        history = [eid for eid in events if eid != held_out]
        if not history:
            continue
        relevant = {held_out}
        if use_baseline:
            ranked = [item["event_id"] for item in recommender.popular(max_k, exclude=set(history))]
        else:
            ranked = [item["event_id"] for item in recommender.recommend_for_user(history, top_n=max_k)]
        rows.append((ranked, relevant))

    name = "Personalized (popularity baseline)" if use_baseline else "Personalized (simulated leave-one-out profiles)"
    result = TaskResult(name=name, n_samples=len(rows))
    result.metrics = _average_metrics(rows, ks)
    return result


# ---------------------------------------------------------------------------
# Reporting
# ---------------------------------------------------------------------------

def format_results_markdown(results: list[TaskResult], ks: list[int]) -> str:
    lines = ["| Task | n | " + " | ".join(f"P@{k} | R@{k} | NDCG@{k}" for k in ks) + " |"]
    lines.append("|---" * (1 + 1 + 3 * len(ks)) + "|")
    for r in results:
        cells = [r.name, str(r.n_samples)]
        for k in ks:
            m = r.metrics[k]
            cells += [f"{m['precision']:.3f}", f"{m['recall']:.3f}", f"{m['ndcg']:.3f}"]
        lines.append("| " + " | ".join(cells) + " |")
    return "\n".join(lines)


def print_results(results: list[TaskResult], ks: list[int]) -> None:
    for r in results:
        print(f"\n{r.name}  (n={r.n_samples})")
        print(f"  {'k':>4}  {'Precision':>10}  {'Recall':>10}  {'NDCG':>10}")
        for k in ks:
            m = r.metrics[k]
            print(f"  {k:>4}  {m['precision']:>10.3f}  {m['recall']:>10.3f}  {m['ndcg']:>10.3f}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--k", type=int, nargs="+", default=[5, 10], help="k values to evaluate (default: 5 10)")
    parser.add_argument("--seed", type=int, default=42, help="random seed for sampling (default: 42)")
    parser.add_argument("--max-samples", type=int, default=300, help="max seeds/profiles per task (default: 300)")
    parser.add_argument("--out", type=str, default=None, help="optional path to write a Markdown results table")
    args = parser.parse_args()

    ks = sorted(args.k)
    rng = random.Random(args.seed)

    print("Loading SeatGeek-derived data and fitting recommender...")
    data = load_dataset()
    recommender = ContentBasedRecommender()
    recommender.fit(data)
    print(f"Fitted on {len(recommender.feature_df)} events ({len(recommender.bookable_event_ids)} bookable).\n")

    performer_groups = build_performer_groups(recommender)

    results = [
        run_similar_events_task(recommender, performer_groups, ks, args.max_samples, random.Random(args.seed)),
        run_similar_events_task(
            recommender, performer_groups, ks, args.max_samples, random.Random(args.seed), use_baseline=True
        ),
        run_personalized_task(recommender, performer_groups, ks, args.max_samples, random.Random(args.seed)),
        run_personalized_task(
            recommender, performer_groups, ks, args.max_samples, random.Random(args.seed), use_baseline=True
        ),
    ]

    if any(r.n_samples == 0 for r in results):
        print(
            "WARNING: at least one task had zero eligible samples (no performer has >=2 "
            "currently-bookable events). Metrics for that task are meaningless - re-run "
            "after the dataset refreshes, or lower the bookability bar.",
            file=sys.stderr,
        )

    print_results(results, ks)

    if args.out:
        table = format_results_markdown(results, ks)
        with open(args.out, "w", encoding="utf-8") as f:
            f.write("# Recommender evaluation results\n\n")
            f.write(f"Seed: {args.seed} · max samples/task: {args.max_samples}\n\n")
            f.write(table + "\n")
        print(f"\nWrote Markdown table to {args.out}")


if __name__ == "__main__":
    main()
