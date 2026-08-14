"""
Loads the SeatGeek Events & Ticket Listings dataset (rebrowser/seatgeek-dataset,
https://www.kaggle.com/datasets/rebrowser/seatgeek-dataset) into the
event_reservation_db MySQL database defined by scripts/schema.sql.

Data limitation handled here: the free Kaggle sample locks every price-related
field (price, averagePrice, dealScore, eventScore, popularityScore, ...) behind
a literal "[PREMIUM]" placeholder string - there is no real price in the sample.
To still have a working reservation/checkout flow, listings.unit_price is a
DERIVED, DETERMINISTIC price simulated from non-locked signals (event type,
performer popularity, venue, and the listing's dealBucket quality tier). It is
clearly a simulation, not real SeatGeek pricing - see README.md "Data
limitations" section.

Usage:
    python import_seatgeek_data.py [--zip PATH_TO_archive.zip] [--reset]
"""

import argparse
import hashlib
import os
import zipfile
from pathlib import Path

import pandas as pd
from dotenv import load_dotenv
from sqlalchemy import create_engine, text

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent
DEFAULT_ZIP = PROJECT_ROOT.parent / "dataset" / "archive.zip"
EXTRACT_DIR = PROJECT_ROOT / "data" / "seatgeek_raw"

# Illustrative base price (USD) per event type, used only to seed the
# simulated pricing model described above - not sourced from SeatGeek.
TYPE_BASE_PRICE = {
    "mlb": 45.0,
    "nba": 85.0,
    "nhl": 70.0,
    "nfl": 120.0,
    "stadium_tours": 35.0,
    "baseball": 40.0,
}
DEFAULT_BASE_PRICE = 55.0

# Lower dealBucket = better deal in the source data, so it maps to a lower
# simulated price multiplier.
DEAL_BUCKET_MULTIPLIER = {0: 0.60, 1: 0.75, 2: 0.90, 3: 1.10, 4: 1.30, 5: 1.50, 6: 1.80, 7: 1.00}


def load_env():
    load_dotenv(PROJECT_ROOT / ".env")
    return {
        "host": os.environ.get("DB_HOST", "127.0.0.1"),
        "port": os.environ.get("DB_PORT", "3306"),
        "name": os.environ.get("DB_NAME", "event_reservation_db"),
        "user": os.environ.get("DB_USER", "event_app"),
        "password": os.environ.get("DB_PASSWORD", ""),
    }


def ensure_extracted(zip_path: Path) -> Path:
    if EXTRACT_DIR.exists() and any(EXTRACT_DIR.iterdir()):
        return EXTRACT_DIR
    if not zip_path.exists():
        raise FileNotFoundError(
            f"Dataset zip not found at {zip_path}. Pass --zip to point at archive.zip "
            "downloaded from https://www.kaggle.com/datasets/rebrowser/seatgeek-dataset"
        )
    EXTRACT_DIR.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path) as zf:
        zf.extractall(EXTRACT_DIR)
    return EXTRACT_DIR


def load_dedup(glob_pattern: str, base_dir: Path, id_col: str) -> pd.DataFrame:
    files = sorted(base_dir.glob(glob_pattern))
    if not files:
        raise FileNotFoundError(f"No files matched {glob_pattern} under {base_dir}")
    frames = [pd.read_parquet(f) for f in files]
    df = pd.concat(frames, ignore_index=True)
    df = df.sort_values("_lastSeenAt").drop_duplicates(id_col, keep="last")
    return df


def price_jitter(listing_id: str) -> float:
    """Deterministic pseudo-random value in [0.85, 1.15] seeded from the listing id,
    so re-running the import produces stable prices instead of new random ones."""
    digest = hashlib.md5(listing_id.encode("utf-8")).hexdigest()
    fraction = int(digest[:8], 16) / 0xFFFFFFFF
    return 0.85 + fraction * 0.30


def build_venues(raw_dir: Path) -> pd.DataFrame:
    df = pd.read_parquet(raw_dir / "venues" / "data.parquet")
    out = pd.DataFrame({
        "venue_id": df["venueId"].astype(int),
        "name": df["name"],
        "slug": df["slug"],
        "address_street": df["addressStreet"],
        "address_city": df["addressCity"],
        "address_state": df["addressState"],
        "address_country": df["addressCountry"],
        "address_postal_code": df["addressPostalCode"],
        "timezone": df["timezone"],
        "latitude": df["latitude"],
        "longitude": df["longitude"],
        "capacity": df["capacity"].fillna(0).astype(int),
        "popularity_score": df["score"],
        "popularity_count": df["popularity"].fillna(0).astype(int),
        "metro_code": df["metroCode"],
    })
    return out.where(pd.notnull(out), None)


def build_performers(raw_dir: Path, valid_venue_ids: set) -> pd.DataFrame:
    df = pd.read_parquet(raw_dir / "performers" / "data.parquet")
    home_venue = df["homeVenueId"].apply(
        lambda v: int(v) if pd.notna(v) and int(v) in valid_venue_ids else None
    )
    out = pd.DataFrame({
        "performer_id": df["performerId"].astype(int),
        "name": df["name"],
        "short_name": df["shortName"],
        "type": df["type"],
        "slug": df["slug"],
        "taxonomy_name": df["taxonomyName"],
        "taxonomy_sub_name": df["taxonomySubName"],
        "home_venue_id": home_venue,
        "score": df["score"],
        "popularity": df["popularity"].fillna(0).astype(int),
        "is_event": df["isEvent"].astype(int),
        "division_name": df["divisionName"],
        "division_short_name": df["divisionShortName"],
    })
    return out.where(pd.notnull(out), None)


def build_events(raw_dir: Path, valid_venue_ids: set) -> pd.DataFrame:
    df = load_dedup("events/data/*.parquet", raw_dir, "eventId")
    df = df[df["venueId"].notna() & df["venueId"].astype(int).isin(valid_venue_ids)]
    out = pd.DataFrame({
        "event_id": df["eventId"].astype(int),
        "name": df["name"],
        "short_name": df["shortName"],
        "type": df["type"],
        "taxonomy_name": df["taxonomyName"],
        "taxonomy_sub_name": df["taxonomySubName"],
        "venue_id": df["venueId"].astype(int),
        "datetime_utc": df["datetimeUtc"].dt.tz_localize(None),
        "end_datetime_utc": df["endDatetimeUtc"].dt.tz_localize(None),
        "date_tbd": df["dateTbd"].astype(int),
        "time_tbd": df["timeTbd"].astype(int),
        "status": df["status"],
        "schedule_status": df["scheduleStatus"],
        "is_open": df["isOpen"].astype(int),
        "is_ga": df["isGa"].astype(int),
        "seat_selection_enabled": df["seatSelectionEnabled"].astype(int),
        "url": df["url"],
        "created_at_source": df["createdAt"].dt.tz_localize(None),
        "announce_date": df["announceDate"].dt.tz_localize(None),
    })
    events_df = out.where(pd.notnull(out), None)
    performer_ids_by_event = dict(zip(df["eventId"].astype(int), df["performerIds"]))
    return events_df, performer_ids_by_event


def build_event_performers(performer_ids_by_event: dict, valid_performer_ids: set) -> pd.DataFrame:
    rows = []
    for event_id, performer_ids in performer_ids_by_event.items():
        for pid in performer_ids:
            pid = int(pid)
            if pid in valid_performer_ids:
                rows.append({"event_id": event_id, "performer_id": pid})
    return pd.DataFrame(rows).drop_duplicates()


def build_listings(raw_dir: Path, events_df: pd.DataFrame, performers_df: pd.DataFrame,
                    event_performers_df: pd.DataFrame) -> pd.DataFrame:
    df = load_dedup("event-listings/data/*.parquet", raw_dir, "listingId")
    df["eventId"] = df["eventId"].astype(int)
    valid_event_ids = set(events_df["event_id"])
    df = df[df["eventId"].isin(valid_event_ids)].copy()

    event_type = events_df.set_index("event_id")["type"].to_dict()

    perf_popularity = performers_df.set_index("performer_id")["popularity"].to_dict()
    max_popularity = max(perf_popularity.values()) if perf_popularity else 1
    event_to_performers = event_performers_df.groupby("event_id")["performer_id"].apply(list).to_dict()

    def performer_multiplier(event_id: int) -> float:
        perf_ids = event_to_performers.get(event_id, [])
        if not perf_ids:
            return 1.0
        pops = [perf_popularity.get(pid, 0) for pid in perf_ids]
        avg_pop = sum(pops) / len(pops)
        normalized = avg_pop / max_popularity if max_popularity else 0
        return 1.0 + normalized * 1.5

    def simulate_price(row) -> float:
        base = TYPE_BASE_PRICE.get(event_type.get(row["eventId"]), DEFAULT_BASE_PRICE)
        bucket = row["dealBucket"]
        bucket_mult = DEAL_BUCKET_MULTIPLIER.get(int(bucket) if pd.notna(bucket) else 7, 1.0)
        perf_mult = performer_multiplier(row["eventId"])
        jitter = price_jitter(row["listingId"])
        return round(base * bucket_mult * perf_mult * jitter, 2)

    df["unit_price"] = df.apply(simulate_price, axis=1)

    out = pd.DataFrame({
        "listing_id": df["listingId"],
        "event_id": df["eventId"],
        "section": df["section"],
        "section_full": df["sectionFull"],
        "row_label": df["row"],
        "quantity": df["quantity"].astype(int),
        "quantity_remaining": df["quantity"].astype(int),
        "deal_bucket": df["dealBucket"],
        "delivery_type": df["deliveryType"],
        "marketplace": df["marketplace"],
        "split_type": df["splitType"],
        "in_hand_date": df["inHandDate"].dt.tz_localize(None) if hasattr(df["inHandDate"], "dt") else None,
        "unit_price": df["unit_price"],
    })
    return out.where(pd.notnull(out), None)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--zip", type=Path, default=DEFAULT_ZIP)
    parser.add_argument("--reset", action="store_true", help="Drop and recreate all tables first")
    args = parser.parse_args()

    env = load_env()
    engine = create_engine(
        f"mysql+pymysql://{env['user']}:{env['password']}@{env['host']}:{env['port']}/{env['name']}"
    )

    if args.reset:
        print("Applying schema.sql (drops and recreates all tables)...")
        with open(SCRIPT_DIR / "schema.sql", "r", encoding="utf-8") as f:
            statements = [s.strip() for s in f.read().split(";") if s.strip()]
        with engine.begin() as conn:
            for stmt in statements:
                conn.execute(text(stmt))

    raw_dir = ensure_extracted(args.zip)
    print(f"Reading SeatGeek data from {raw_dir}")

    venues_df = build_venues(raw_dir)
    valid_venue_ids = set(venues_df["venue_id"])
    print(f"Venues: {len(venues_df)}")

    performers_df = build_performers(raw_dir, valid_venue_ids)
    valid_performer_ids = set(performers_df["performer_id"])
    print(f"Performers: {len(performers_df)}")

    events_df, performer_ids_by_event = build_events(raw_dir, valid_venue_ids)
    print(f"Events: {len(events_df)}")

    event_performers_df = build_event_performers(performer_ids_by_event, valid_performer_ids)
    print(f"Event-performer links: {len(event_performers_df)}")

    listings_df = build_listings(raw_dir, events_df, performers_df, event_performers_df)
    print(f"Listings: {len(listings_df)} (across {listings_df['event_id'].nunique()} events)")

    with engine.begin() as conn:
        conn.execute(text("SET FOREIGN_KEY_CHECKS=0"))
        for table_name, frame in [
            ("venues", venues_df),
            ("performers", performers_df),
            ("events", events_df),
            ("event_performers", event_performers_df),
            ("listings", listings_df),
        ]:
            conn.execute(text(f"DELETE FROM {table_name}"))
            frame.to_sql(table_name, conn, if_exists="append", index=False, method="multi", chunksize=500)
            print(f"Loaded {len(frame)} rows into {table_name}")
        conn.execute(text("SET FOREIGN_KEY_CHECKS=1"))

    print("Import complete.")


if __name__ == "__main__":
    main()
