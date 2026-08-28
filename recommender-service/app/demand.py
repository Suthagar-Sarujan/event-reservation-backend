"""
Ticket demand prediction for the Smart Event Ticket Reservation System.

Architecture (see README for the full diagram):

    Historical booking data -> preprocessing -> feature engineering
        -> model training -> saved model -> prediction service -> backend API
        -> organizer dashboard

Data reality check (read before changing thresholds): at the time this was
built, the app-owned `bookings`/`booking_items` tables have zero real rows in
a fresh install - SeatGeek's imported `listings.quantity_remaining` never
depletes on its own (see scripts/import_seatgeek_data.py), so there is no
historical "tickets actually sold" ground truth to regress against yet. This
mirrors the exact cold-start problem the recommender already solves for (see
recommender.py's design note): rather than fabricate a fake label, the model
runs in one of two modes -

- HEURISTIC mode (used until enough real demand history exists): a
  transparent, explainable weighted score over real, non-invented signals
  already in the schema - venue popularity, performer popularity, listed
  price, and (once any exist) actual in-app bookings so far. This is not a
  placeholder that gets thrown away later - it stays the fallback forever for
  events too new to have completed, and is why every event still gets a
  prediction on day one.
- ML mode (activates automatically once >= MIN_TRAINING_SAMPLES completed
  events have real booking history): trains a scikit-learn regressor where
  the label is the actual final ticket count for events whose date has
  already passed (their booking count can't change further), predicting the
  same target for upcoming events. Retraining is explicit (POST
  /demand/retrain) or on service startup - never on every dashboard load, per
  the required architecture.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
from sklearn.ensemble import GradientBoostingRegressor
from sklearn.metrics import mean_absolute_error

logger = logging.getLogger("recommender.demand")

MODEL_DIR = Path(__file__).resolve().parent / "models"
MODEL_PATH = MODEL_DIR / "demand_model.joblib"
META_PATH = MODEL_DIR / "demand_model_meta.json"

MIN_TRAINING_SAMPLES = 20
FEATURE_COLUMNS = [
    "capacity",
    "days_to_event",
    "avg_price",
    "venue_popularity",
    "performer_popularity",
    "month",
    "day_of_week",
    "current_bookings",
]

LOW_OCCUPANCY_THRESHOLD = 0.40
HIGH_OCCUPANCY_THRESHOLD = 0.70


def _minmax(series: pd.Series) -> pd.Series:
    values = series.astype(float)
    lo, hi = values.min(), values.max()
    if hi - lo < 1e-9:
        return pd.Series(0.0, index=values.index)
    return (values - lo) / (hi - lo)


def _demand_level(occupancy: float) -> str:
    if occupancy >= HIGH_OCCUPANCY_THRESHOLD:
        return "HIGH"
    if occupancy >= LOW_OCCUPANCY_THRESHOLD:
        return "MEDIUM"
    return "LOW"


@dataclass
class ModelMetadata:
    version: str
    trained_at: str
    training_row_count: int
    mode: str  # "ml" or "heuristic"
    mae: float | None = None
    feature_columns: list[str] = field(default_factory=lambda: list(FEATURE_COLUMNS))


class DemandModel:
    def __init__(self) -> None:
        self.features: pd.DataFrame | None = None  # indexed by event_id, includes meta columns
        self.model: GradientBoostingRegressor | None = None
        self.meta: ModelMetadata | None = None

    # ------------------------------------------------------------------
    def fit(self, data: dict[str, pd.DataFrame], force: bool = False) -> None:
        """Builds features for every event, then either loads a previously
        saved model or trains a fresh one. force=True always retrains (used
        by POST /demand/retrain)."""
        self.features = self._build_features(data)

        if not force and MODEL_PATH.exists() and META_PATH.exists():
            try:
                self.model = joblib.load(MODEL_PATH)
                with open(META_PATH, encoding="utf-8") as f:
                    self.meta = ModelMetadata(**json.load(f))
                logger.info("Loaded saved demand model (mode=%s, trained_at=%s)", self.meta.mode, self.meta.trained_at)
                return
            except Exception:  # noqa: BLE001 - a corrupt/incompatible artifact should never crash startup
                logger.exception("Failed to load saved demand model, retraining from scratch")

        self._train_and_save()

    def _train_and_save(self) -> None:
        assert self.features is not None
        completed = self.features[(self.features["days_to_event"] < 0) & (self.features["current_bookings"] > 0)]

        if len(completed) >= MIN_TRAINING_SAMPLES:
            X = completed[FEATURE_COLUMNS].fillna(0.0)
            y = completed["current_bookings"].astype(float)
            model = GradientBoostingRegressor(random_state=42, n_estimators=150, max_depth=3, learning_rate=0.08)
            model.fit(X, y)
            mae = float(mean_absolute_error(y, model.predict(X)))
            self.model = model
            self.meta = ModelMetadata(
                version=datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S"),
                trained_at=datetime.now(timezone.utc).isoformat(),
                training_row_count=len(completed),
                mode="ml",
                mae=mae,
            )
            logger.info("Trained ML demand model on %d completed events (train MAE=%.1f tickets)", len(completed), mae)
        else:
            self.model = None
            self.meta = ModelMetadata(
                version=datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S"),
                trained_at=datetime.now(timezone.utc).isoformat(),
                training_row_count=len(completed),
                mode="heuristic",
                mae=None,
            )
            logger.info(
                "Only %d completed events have booking history (need %d) - using heuristic demand scoring",
                len(completed), MIN_TRAINING_SAMPLES,
            )

        MODEL_DIR.mkdir(parents=True, exist_ok=True)
        if self.model is not None:
            joblib.dump(self.model, MODEL_PATH)
        elif MODEL_PATH.exists():
            MODEL_PATH.unlink()
        with open(META_PATH, "w", encoding="utf-8") as f:
            json.dump(self.meta.__dict__, f)

    # ------------------------------------------------------------------
    def _build_features(self, data: dict[str, pd.DataFrame]) -> pd.DataFrame:
        events = data["events"].merge(data["venues"], on="venue_id", how="left", suffixes=("", "_venue"))
        events = events.merge(data["listing_prices"], on="event_id", how="left")
        events = events.merge(data["listing_capacity"], on="event_id", how="left")
        events = events.merge(data["confirmed_bookings"], on="event_id", how="left")

        performer_popularity = dict(zip(data["performers"]["performer_id"], data["performers"]["popularity"]))
        event_performers = data["event_performers"].groupby("event_id")["performer_id"].apply(set).to_dict()
        events["performer_avg_popularity"] = events["event_id"].map(
            lambda eid: np.mean([performer_popularity.get(p, 0) for p in event_performers.get(eid, [])])
            if event_performers.get(eid)
            else 0
        )

        now = pd.Timestamp.now(tz="UTC").tz_localize(None)
        events["days_to_event"] = (events["datetime_utc"] - now).dt.days
        events["month"] = events["datetime_utc"].dt.month
        events["day_of_week"] = events["datetime_utc"].dt.dayofweek

        # capacity: venue's stated capacity when known, otherwise fall back to
        # total listed inventory for the event (see LISTING_CAPACITY_SQL) -
        # schema.sql documents venue capacity as "0/NULL means unknown".
        venue_capacity = events["capacity"].fillna(0)
        events["capacity"] = np.where(venue_capacity > 0, venue_capacity, events["listed_quantity"].fillna(0))
        events["current_bookings"] = events["tickets_booked"].fillna(0)
        events["avg_price"] = events["avg_price"].fillna(events["avg_price"].median())
        events["venue_popularity"] = events["popularity_score"].fillna(0)
        events["performer_popularity"] = events["performer_avg_popularity"].fillna(0)

        events = events.set_index("event_id")
        return events

    # ------------------------------------------------------------------
    def _heuristic_occupancy(self, row: pd.Series, all_rows: pd.DataFrame) -> float:
        venue_norm = _minmax(all_rows["venue_popularity"]).loc[row.name]
        perf_norm = _minmax(all_rows["performer_popularity"]).loc[row.name]
        # Cheaper-than-average listings are treated as more attractive
        # (higher expected demand); avg_price is already backfilled to the
        # dataset median for events with no live listings.
        price_norm = 1.0 - _minmax(all_rows["avg_price"]).loc[row.name]

        signal_score = 0.4 * venue_norm + 0.3 * perf_norm + 0.3 * price_norm

        capacity = max(float(row["capacity"]), 1.0)
        current_occupancy = min(float(row["current_bookings"]) / capacity, 1.0)
        # Blend the content signal with real bookings-so-far - as an event
        # actually sells tickets, that real signal should dominate the
        # heuristic guess about it.
        blended = 0.6 * signal_score + 0.4 * current_occupancy
        return float(np.clip(blended, 0.0, 1.0))

    def _predict_row(self, event_id: int, row: pd.Series) -> dict:
        capacity = max(float(row["capacity"]), 1.0)
        current_bookings = float(row["current_bookings"])

        if self.model is not None:
            X = pd.DataFrame([row[FEATURE_COLUMNS].fillna(0.0)])
            predicted = float(self.model.predict(X)[0])
        else:
            occupancy = self._heuristic_occupancy(row, self.features)
            predicted = occupancy * capacity

        # A prediction can never fall below what's already booked.
        predicted = max(predicted, current_bookings)
        predicted = min(predicted, capacity)
        occupancy = predicted / capacity if capacity > 0 else 0.0

        return {
            "event_id": int(event_id),
            "event_name": str(row.get("name", "")),
            "datetime_utc": row["datetime_utc"].isoformat() if pd.notna(row["datetime_utc"]) else None,
            "capacity": int(capacity),
            "current_bookings": int(current_bookings),
            "predicted_demand": int(round(predicted)),
            "expected_occupancy": round(occupancy, 4),
            "demand_level": _demand_level(occupancy),
        }

    # ------------------------------------------------------------------
    def predict(self, event_id: int) -> dict | None:
        if self.features is None or event_id not in self.features.index:
            return None
        return self._predict_row(event_id, self.features.loc[event_id])

    def predict_many(self, event_ids: list[int] | None = None, only_upcoming: bool = True) -> list[dict]:
        if self.features is None:
            return []
        rows = self.features
        if event_ids is not None:
            rows = rows.loc[rows.index.isin(event_ids)]
        if only_upcoming:
            rows = rows[rows["days_to_event"] >= 0]
        return [self._predict_row(eid, row) for eid, row in rows.iterrows()]

    def metadata(self) -> dict:
        if self.meta is None:
            return {"version": None, "trained_at": None, "training_row_count": 0, "mode": "untrained", "mae": None}
        return self.meta.__dict__
