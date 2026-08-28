# Recommender evaluation results

Seed: 42 · max samples/task: 300

| Task | n | P@5 | R@5 | NDCG@5 | P@10 | R@10 | NDCG@10 |
|---|---|---|---|---|---|---|---|
| Similar events (content-based) | 240 | 0.877 | 0.799 | 1.000 | 0.602 | 0.988 | 1.000 |
| Similar events (popularity baseline) | 240 | 0.065 | 0.045 | 0.066 | 0.062 | 0.083 | 0.076 |
| Personalized (simulated leave-one-out profiles) | 62 | 0.200 | 1.000 | 0.918 | 0.100 | 1.000 | 0.918 |
| Personalized (popularity baseline) | 62 | 0.003 | 0.016 | 0.008 | 0.006 | 0.065 | 0.023 |
