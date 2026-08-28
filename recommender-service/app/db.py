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

# Total listed inventory per event (sold or not) - used both as a capacity
# proxy when venues.capacity is unknown (see schema.sql note: "0/NULL means
# unknown in source data") and as the denominator for occupancy.
LISTING_CAPACITY_SQL = """
SELECT event_id, SUM(quantity) AS listed_quantity
FROM listings
GROUP BY event_id
"""

# Real tickets sold through the app itself (not SeatGeek's static import data,
# which never depletes - see scripts/import_seatgeek_data.py). This is the
# only genuine demand signal available today; DemandModel treats it as both a
# feature (current_bookings) and, for events whose date has passed, the
# training label (see app/demand.py).
CONFIRMED_BOOKINGS_SQL = """
SELECT l.event_id, SUM(bi.quantity) AS tickets_booked
FROM booking_items bi
JOIN bookings b ON b.booking_id = bi.booking_id
JOIN listings l ON l.listing_id = bi.listing_id
WHERE b.status = 'confirmed'
GROUP BY l.event_id
"""


def load_dataset() -> dict[str, pd.DataFrame]:
    with engine.connect() as conn:
        events = pd.read_sql(EVENTS_SQL, conn)
        event_performers = pd.read_sql(EVENT_PERFORMERS_SQL, conn)
        performers = pd.read_sql(PERFORMERS_SQL, conn)
        venues = pd.read_sql(VENUES_SQL, conn)
        listing_prices = pd.read_sql(LISTING_PRICE_SQL, conn)
        listing_capacity = pd.read_sql(LISTING_CAPACITY_SQL, conn)
        confirmed_bookings = pd.read_sql(CONFIRMED_BOOKINGS_SQL, conn)
    return {
        "events": events,
        "event_performers": event_performers,
        "performers": performers,
        "venues": venues,
        "listing_prices": listing_prices,
        "listing_capacity": listing_capacity,
        "confirmed_bookings": confirmed_bookings,
    }
