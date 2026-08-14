import pandas as pd
from sqlalchemy import create_engine

from .config import DATABASE_URL

engine = create_engine(DATABASE_URL, pool_pre_ping=True)

# Only the SeatGeek-derived dimension tables are read here. Users and bookings
# stay owned by the ASP.NET Core API - the caller passes booked event ids in
# the request body instead, so this service never needs the app's user data.

EVENTS_SQL = """
SELECT event_id, name, type, taxonomy_name, taxonomy_sub_name, venue_id,
       datetime_utc, status, is_open, seat_selection_enabled
FROM events
"""

EVENT_PERFORMERS_SQL = "SELECT event_id, performer_id FROM event_performers"

PERFORMERS_SQL = """
SELECT performer_id, name, type, taxonomy_name, taxonomy_sub_name, score, popularity
FROM performers
"""

VENUES_SQL = """
SELECT venue_id, name, address_city, address_state, address_country,
       latitude, longitude, capacity, popularity_score
FROM venues
"""

LISTING_PRICE_SQL = """
SELECT event_id, AVG(unit_price) AS avg_price, SUM(quantity_remaining) AS tickets_remaining
FROM listings
WHERE listing_status = 'available' AND quantity_remaining > 0
GROUP BY event_id
"""


def load_dataset() -> dict[str, pd.DataFrame]:
    with engine.connect() as conn:
        events = pd.read_sql(EVENTS_SQL, conn)
        event_performers = pd.read_sql(EVENT_PERFORMERS_SQL, conn)
        performers = pd.read_sql(PERFORMERS_SQL, conn)
        venues = pd.read_sql(VENUES_SQL, conn)
        listing_prices = pd.read_sql(LISTING_PRICE_SQL, conn)
    return {
        "events": events,
        "event_performers": event_performers,
        "performers": performers,
        "venues": venues,
        "listing_prices": listing_prices,
    }
