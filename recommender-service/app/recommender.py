"""
Content-based event recommender for the Smart Event Ticket Reservation System.

Design note: the literature review this project started from proposed a hybrid
of two models trained on two datasets. Per project scope, only the SeatGeek
Events & Ticket Listings dataset is used, so there is one model here: a
content-based similarity recommender over event attributes (type, taxonomy,
performer, venue, simulated price - see scripts/import_seatgeek_data.py for
why price is simulated). There is no collaborative-filtering signal available
without a second, user-interaction dataset, so a brand-new user with no
booking history falls back to a non-personalized popularity ranking - the
recommender never invents personalization it doesn't have data for.

Every ranked result carries a short, rule-based reason built by inspecting the
actual attributes two events share (performer, type, venue) rather than a
free-floating text label, so the explanation stays traceable to the same data
that produced the ranking.
"""

from __future__ import annotations

import numpy as np
import pandas as pd
from sklearn.metrics.pairwise import cosine_similarity


def _minmax(series: pd.Series) -> pd.Series:
    values = series.astype(float)
    lo, hi = values.min(), values.max()
    if hi - lo < 1e-9:
        return pd.Series(0.0, index=values.index)
    return (values - lo) / (hi - lo)


class ContentBasedRecommender:
    PERFORMER_WEIGHT = 2.0  # a shared performer is the strongest similarity signal

    def __init__(self) -> None:
        self.feature_df: pd.DataFrame | None = None
        self.meta: pd.DataFrame | None = None
        self.event_performers: dict[int, set[int]] = {}
        self.performer_names: dict[int, str] = {}
        self.bookable_event_ids: set[int] = set()
        self.similarity_matrix: np.ndarray | None = None
        self.event_order: list[int] = []

    def fit(self, data: dict[str, pd.DataFrame]) -> None:
        events = data["events"].merge(data["venues"], on="venue_id", how="left", suffixes=("", "_venue"))
        events = events.merge(data["listing_prices"], on="event_id", how="left")

        now = pd.Timestamp.utcnow().tz_localize(None)
        is_future = events["datetime_utc"] > now
        tickets_remaining = events["tickets_remaining"].fillna(0)
        # Note: SeatGeek's own `isOpen` flag is dropped from this check - in this
        # sample it is false for most events that still carry live listings
        # (likely a snapshot-timing artifact of the "last 30 days" export window),
        # so actual ticket inventory (tickets_remaining > 0) is the more reliable
        # signal of whether an event can still be booked.
        bookable = (events["status"] == "normal") & is_future & (tickets_remaining > 0)
        self.bookable_event_ids = set(events.loc[bookable, "event_id"])

        event_performers = data["event_performers"]
        self.event_performers = (
            event_performers.groupby("event_id")["performer_id"].apply(set).to_dict()
        )
        self.performer_names = dict(zip(data["performers"]["performer_id"], data["performers"]["name"]))
        performer_popularity = dict(zip(data["performers"]["performer_id"], data["performers"]["popularity"]))

        type_dummies = pd.get_dummies(events["type"].fillna("unknown"), prefix="type")
        taxsub_dummies = pd.get_dummies(events["taxonomy_sub_name"].fillna("unknown"), prefix="subtax")

        perf_pivot = (
            event_performers.assign(val=1.0)
            .pivot_table(index="event_id", columns="performer_id", values="val", fill_value=0.0)
        )
        perf_pivot = perf_pivot.reindex(events["event_id"]).fillna(0.0)
        perf_pivot.index = events.index
        perf_pivot.columns = [f"perf_{c}" for c in perf_pivot.columns]
        perf_pivot = perf_pivot * self.PERFORMER_WEIGHT

        events["performer_avg_popularity"] = events["event_id"].map(
            lambda eid: np.mean([performer_popularity.get(p, 0) for p in self.event_performers.get(eid, [])])
            if self.event_performers.get(eid)
            else 0
        )

        numeric = pd.DataFrame(
            {
                "venue_capacity": _minmax(events["capacity"].fillna(0)),
                "venue_popularity": _minmax(events["popularity_score"].fillna(0)),
                "venue_lat": _minmax(events["latitude"].fillna(0)),
                "venue_lng": _minmax(events["longitude"].fillna(0)),
                "avg_price": _minmax(events["avg_price"].fillna(events["avg_price"].median())),
                "performer_popularity": _minmax(events["performer_avg_popularity"]),
            }
        )

        feature_df = pd.concat(
            [type_dummies.reset_index(drop=True), taxsub_dummies.reset_index(drop=True),
             perf_pivot.reset_index(drop=True), numeric.reset_index(drop=True)],
            axis=1,
        ).fillna(0.0)
        feature_df.index = events["event_id"].values

        self.feature_df = feature_df
        self.meta = events.set_index("event_id")
        self.event_order = list(feature_df.index)
        self.similarity_matrix = cosine_similarity(feature_df.values)

    # ------------------------------------------------------------------
    def _event_label(self, event_id: int) -> str:
        row = self.meta.loc[event_id]
        return f"{row['name']} ({row['type'].upper()})"

    def _reason_between(self, from_event_id: int, to_event_id: int, lead_in: str) -> str:
        from_performers = self.event_performers.get(from_event_id, set())
        to_performers = self.event_performers.get(to_event_id, set())
        shared_performers = from_performers & to_performers
        if shared_performers:
            name = self.performer_names.get(next(iter(shared_performers)), "a shared performer")
            return f"{lead_in} it features {name}."

        from_row, to_row = self.meta.loc[from_event_id], self.meta.loc[to_event_id]
        if from_row["type"] == to_row["type"]:
            return f"{lead_in} it's the same event type ({to_row['type'].upper()})."
        if from_row["venue_id"] == to_row["venue_id"]:
            return f"{lead_in} it's at the same venue ({to_row['name_venue']})."
        if from_row["taxonomy_sub_name"] == to_row["taxonomy_sub_name"]:
            return f"{lead_in} it's a similar category ({to_row['taxonomy_sub_name']})."
        return f"{lead_in} its overall profile (type, venue and performer mix) is close to what you're looking at."

    def _reason_for_profile(self, booked_event_ids: list[int], candidate_id: int) -> str:
        booked_performers: set[int] = set()
        booked_types: dict[str, int] = {}
        booked_venue_ids: set[int] = set()
        for eid in booked_event_ids:
            if eid not in self.meta.index:
                continue
            booked_performers |= self.event_performers.get(eid, set())
            row = self.meta.loc[eid]
            booked_types[row["type"]] = booked_types.get(row["type"], 0) + 1
            booked_venue_ids.add(row["venue_id"])

        candidate_performers = self.event_performers.get(candidate_id, set())
        shared_performers = booked_performers & candidate_performers
        if shared_performers:
            name = self.performer_names.get(next(iter(shared_performers)), "a performer you've booked")
            return f"Recommended because you've booked {name} before."

        candidate_row = self.meta.loc[candidate_id]
        top_type = max(booked_types, key=booked_types.get) if booked_types else None
        if top_type and candidate_row["type"] == top_type:
            return f"Recommended because you often book {top_type.upper()} events."
        if candidate_row["venue_id"] in booked_venue_ids:
            return f"Recommended because you've booked events at {candidate_row['name_venue']} before."
        return "Recommended based on the type, venue and performer mix of events you've booked."

    # ------------------------------------------------------------------
    def similar_events(self, event_id: int, top_n: int = 10) -> list[dict]:
        if event_id not in self.feature_df.index:
            return []
        idx = self.event_order.index(event_id)
        scores = self.similarity_matrix[idx]
        ranked = sorted(
            (
                (eid, score)
                for eid, score in zip(self.event_order, scores)
                if eid != event_id and eid in self.bookable_event_ids
            ),
            key=lambda pair: pair[1],
            reverse=True,
        )[:top_n]
        return [
            {
                "event_id": int(eid),
                "score": round(float(score), 4),
                "reason": self._reason_between(event_id, eid, "Similar because"),
            }
            for eid, score in ranked
        ]

    def recommend_for_user(self, booked_event_ids: list[int], top_n: int = 10) -> list[dict]:
        known_booked = [eid for eid in booked_event_ids if eid in self.feature_df.index]
        if not known_booked:
            return self.popular(top_n, exclude=set(booked_event_ids))

        profile_vector = self.feature_df.loc[known_booked].mean(axis=0).values.reshape(1, -1)
        scores = cosine_similarity(profile_vector, self.feature_df.values)[0]
        excluded = set(booked_event_ids)
        ranked = sorted(
            (
                (eid, score)
                for eid, score in zip(self.event_order, scores)
                if eid not in excluded and eid in self.bookable_event_ids
            ),
            key=lambda pair: pair[1],
            reverse=True,
        )[:top_n]
        return [
            {
                "event_id": int(eid),
                "score": round(float(score), 4),
                "reason": self._reason_for_profile(known_booked, eid),
            }
            for eid, score in ranked
        ]

    def popular(self, top_n: int = 10, exclude: set[int] | None = None) -> list[dict]:
        exclude = exclude or set()
        candidates = self.meta.loc[list(self.bookable_event_ids - exclude)].copy()
        if candidates.empty:
            return []
        candidates["venue_pop_norm"] = _minmax(candidates["popularity_score"].fillna(0))
        candidates["perf_pop_norm"] = _minmax(candidates["performer_avg_popularity"])
        candidates["trend_score"] = 0.6 * candidates["venue_pop_norm"] + 0.4 * candidates["perf_pop_norm"]
        top = candidates.sort_values("trend_score", ascending=False).head(top_n)
        return [
            {
                "event_id": int(eid),
                "score": round(float(row["trend_score"]), 4),
                "reason": "Popular pick - no booking history yet, so this is a trending, high-demand event rather than a personalized match.",
            }
            for eid, row in top.iterrows()
        ]
