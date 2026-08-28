"""
Unit tests for the evaluation metrics in evaluate.py, checked against
hand-computed expected values so the numbers that go into the report are
trustworthy.

Run: cd recommender-service && pytest test_evaluate.py -v
"""

import math

from evaluate import ndcg_at_k, precision_at_k, recall_at_k


def test_precision_at_k_counts_hits_in_top_k():
    ranked = [1, 2, 3, 4, 5]
    relevant = {2, 4, 99}  # 99 is not in the ranked list at all
    assert precision_at_k(ranked, relevant, k=5) == 2 / 5
    assert precision_at_k(ranked, relevant, k=2) == 1 / 2  # only item 2 of [1, 2] is relevant
    assert precision_at_k(ranked, relevant, k=1) == 0 / 1  # item 1 alone is not relevant


def test_precision_at_k_zero_k_is_zero():
    assert precision_at_k([1, 2, 3], {1}, k=0) == 0.0


def test_recall_at_k_divides_by_total_relevant_not_k():
    ranked = [1, 2, 3, 4, 5]
    relevant = {2, 4, 10}  # only 2 of the 3 relevant items are even in the ranked list
    assert recall_at_k(ranked, relevant, k=5) == 2 / 3


def test_recall_at_k_empty_relevant_set_is_zero():
    assert recall_at_k([1, 2, 3], set(), k=3) == 0.0


def test_ndcg_at_k_perfect_ranking_is_one():
    # Both relevant items occupy the first two positions - the best possible ranking.
    ranked = [1, 2, 3, 4, 5]
    relevant = {1, 2}
    assert ndcg_at_k(ranked, relevant, k=5) == 1.0


def test_ndcg_at_k_matches_hand_computed_value():
    # Relevant items at ranks 2 and 4 (0-indexed positions 1 and 3).
    ranked = [1, 2, 3, 4, 5]
    relevant = {2, 4}

    dcg = 1 / math.log2(1 + 2) + 1 / math.log2(3 + 2)  # positions 1 and 3 (0-indexed)
    idcg = 1 / math.log2(0 + 2) + 1 / math.log2(1 + 2)  # ideal: both relevant items ranked first
    expected = dcg / idcg

    assert math.isclose(ndcg_at_k(ranked, relevant, k=5), expected, rel_tol=1e-9)
    assert math.isclose(ndcg_at_k(ranked, relevant, k=5), 0.6509, abs_tol=1e-4)


def test_ndcg_at_k_no_relevant_items_found_is_zero():
    assert ndcg_at_k([1, 2, 3], {99}, k=3) == 0.0


def test_ndcg_at_k_no_relevant_items_exist_is_zero():
    # Nothing to rank against - defined as 0, not undefined/NaN.
    assert ndcg_at_k([1, 2, 3], set(), k=3) == 0.0


def test_metrics_ignore_items_beyond_k():
    # A relevant item ranked 6th shouldn't count toward @5 metrics at all.
    ranked = [1, 2, 3, 4, 5, 6]
    relevant = {6}
    assert precision_at_k(ranked, relevant, k=5) == 0.0
    assert recall_at_k(ranked, relevant, k=5) == 0.0
    assert ndcg_at_k(ranked, relevant, k=5) == 0.0
    # But it should count at k=6.
    assert recall_at_k(ranked, relevant, k=6) == 1.0
