# Smart Event Ticket Reservation System

A ticket reservation platform with content-based event recommendations, built on
the [SeatGeek Events & Ticket Listings dataset](https://www.kaggle.com/datasets/rebrowser/seatgeek-dataset).
Users browse and book event tickets; every recommendation shown to them carries
a short, traceable reason (e.g. *"Recommended because you've booked Boston Red
Sox before"*) generated from the same event attributes used to rank it.

## Architecture

```
Angular (frontend, :4200)
        |  HTTPS/JSON
        v
ASP.NET Core Web API (backend-api, :5248)  <-- owns users, bookings, auth (JWT)
        |  HTTP/JSON                            reads/writes MySQL directly
        v
Python FastAPI recommender (recommender-service, :8000)
        |  read-only SQL
        v
MySQL 8 (event_reservation_db)
  - venues, performers, events, event_performers, listings   <- from SeatGeek
  - users, bookings, booking_items                            <- app-owned
```

The recommender never sees user accounts or passwords - the API passes it only
the list of event ids a user has booked, keeping the two services independently
deployable/retrainable, as intended by the original design.

## Roles

Three roles share the same `users` table (`role` column: `customer` |
`organizer` | `admin`). **Access is strictly separated, not hierarchical** -
Admin is not "Organizer plus more" and Organizer is not "Customer plus more".
Each role sees exactly one panel and nothing from the other two:

- **Customer**: browse, get recommendations, book tickets, view booking
  history - via `/dashboard` and `/my-bookings`, both rendered in the same
  sidebar shell component Organizer/Admin use (`variant: 'customer'` in
  `app.routes.ts`), so all three roles get equivalent-feeling panels even
  though the content differs.
- **Organizer**: a dashboard (`/organizer`) to create their own events (new or
  existing venue, an optional performer/act, one or more ticket listings),
  edit those events' core details, add/adjust listings, and see who booked
  what - scoped to events they created (`events.created_by_user_id = their
  id`). Cannot browse or book as a customer; cannot see Admin's tools.
  Organizer-created events still appear in the normal public browse/search/
  recommendation surfaces alongside SeatGeek-imported ones -
  `created_by_user_id` (`NULL` = imported, set = organizer-created) is the
  only thing that distinguishes them there. Real prices set by organizers,
  unlike the simulated SeatGeek listing prices described below.
- **Admin**: platform oversight only (`/admin`) - promote/demote any user's
  role, view/cancel every event regardless of creator, edit core details
  (name/date/status/image) on *any* event including SeatGeek-imported ones -
  wider reach than Organizer's own-events-only edit, but still limited to
  those four fields, not venue/performer/listing data - and view every
  booking platform-wide, plus a stats dashboard. Cannot browse/book, and
  has no access to Organizer's event-creation tools.

Enforced on both ends, matching the same strict rule:
- **Backend**: `[Authorize(Roles = "Organizer")]` / `[Authorize(Roles =
  "Admin")]` on `OrganizerController`/`AdminController` - deliberately *not*
  `"Organizer,Admin"`, which would let Admin into Organizer's endpoints too.
- **Frontend**: `AuthService.isOrganizer`/`isAdmin` are exact role-string
  matches (`role === 'Organizer'`), never combined with OR into each other;
  `isStaff` (`isOrganizer() || isAdmin()`) exists only to hide
  customer-facing nav (Browse, Dashboard, My Bookings) from both back-office
  roles, never to grant one role the other's access. Route guards
  (`organizerGuard`, `adminGuard`, `customerGuard`) mirror this: a customer
  hitting `/organizer` or `/admin` is bounced to `/login` or `/`; an
  Organizer or Admin hitting `/dashboard` or `/my-bookings` is redirected to
  their *own* panel (`/organizer` or `/admin`) rather than shown the
  customer view.

This was a real bug caught mid-project: an early version computed
`isOrganizer` as `role === 'Organizer' || role === 'Admin'` (treating Admin
as a superset), which leaked an "Organizer Panel" link into the Admin's own
dropdown menu. The fix was to make every one of these checks an exact,
single-role match with no fallthrough - the rule above, not an exception.

**There is no self-service organizer signup.** Everyone registers as a
customer; an admin promotes a user to organizer (or admin) from
`/admin/users`. Since that requires an existing admin, bootstrap the very
first one directly in the database after registering normally:

```sql
UPDATE users SET role = 'admin' WHERE email = 'you@example.com';
```

Organizer-created events, venues, and performers get MySQL `AUTO_INCREMENT`
ids seeded far above anything the SeatGeek sample could plausibly assign
(events from 900,000,000, venues/performers from 5,000,000 - see the bottom of
`scripts/schema.sql`), so they can never collide with an imported SeatGeek id.

## Why one model, not a hybrid

An earlier design explored a hybrid of two models across two datasets. This
build uses **only** the SeatGeek dataset, so there is one recommender: a
content-based similarity model over event attributes (type, taxonomy,
performer, venue, and a derived price - see below). There is no
collaborative-filtering signal without a second, user-interaction dataset, so a
brand-new user with no booking history gets a non-personalized **popularity
fallback** instead of a fabricated personalization. See
[`recommender-service/app/recommender.py`](recommender-service/app/recommender.py)
for the full design note.

## Data limitations (read before extending this project)

The free Kaggle sample of the SeatGeek dataset is smaller and more constrained
than it first looks. These are handled explicitly in code, not silently:

- **Prices are locked.** Every price field (`price`, `averagePrice`,
  `dealScore`, `eventScore`, `popularityScore`, ...) is the literal string
  `"[PREMIUM]"` in the free sample, not a number. `listings.unit_price` is
  therefore a **deterministic simulated price** derived from event type,
  performer popularity, and the listing's `dealBucket` quality tier - see the
  header comment in
  [`scripts/import_seatgeek_data.py`](scripts/import_seatgeek_data.py). It is
  not real SeatGeek pricing.
- **Small dimension tables.** This sample has 253 performers and 185 venues
  (the full paid dataset has ~15,000 / ~12,000).
  Events (1,375) and listings (11,301 after filtering to events that actually
  have inventory) are usable for a working demo but not a large catalog.
- **Sports-only in this window.** `taxonomyName` is 100% `sports` in this
  sample (mlb/nba/nhl/nfl/stadium_tours) - almost no concerts/theatre are
  present, unlike a general ticket marketplace.
- **`isOpen` is unreliable.** SeatGeek's own "open for sale" flag is `false`
  for most events that still carry live listings in this snapshot (likely a
  timing artifact of the dataset's rolling 30-day export window). Bookability
  is therefore determined by actual ticket inventory
  (`quantity_remaining > 0` and the event date being in the future), not that
  flag - see `ContentBasedRecommender.fit()`.

## Project layout

```
final_project/
  scripts/                    Database schema + SeatGeek -> MySQL import pipeline
  recommender-service/        Python FastAPI content-based recommender
  backend-api/                ASP.NET Core Web API (auth, events, bookings)
  frontend/                   Angular app
  .env                        Shared local dev config (DB creds, service URLs)
```

## Setup

### Prerequisites

- MySQL 8 running locally
- Python 3.12+, .NET 9 SDK, Node 22+ / Angular CLI

### 1. Database

Create a dedicated database and app user (adjust password), then note it in `.env`:

```sql
CREATE DATABASE event_reservation_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'event_app'@'localhost' IDENTIFIED BY 'your_password_here';
GRANT ALL PRIVILEGES ON event_reservation_db.* TO 'event_app'@'localhost';
```

Copy `.env.example` to `.env` and fill in `DB_PASSWORD` (and update
`backend-api/EventReservation.Api/appsettings.json`'s `ConnectionStrings:Default`
to match).

### 2. Import the SeatGeek dataset

Download `archive.zip` from the
[Kaggle dataset page](https://www.kaggle.com/datasets/rebrowser/seatgeek-dataset).
By default the import script looks for it at `../dataset/archive.zip` relative
to `final_project/`; pass `--zip PATH_TO_archive.zip` to point it anywhere
else. If `data/seatgeek_raw/` already has the extracted dataset (it's part of
this project folder), the script reuses it and `--zip` isn't needed at all.

```bash
cd scripts
pip install -r requirements.txt
python import_seatgeek_data.py --reset
```

This creates all tables (`schema.sql`) and loads venues, performers, events,
event-performer links, and listings (with simulated prices).

### 3. Recommender service

```bash
cd recommender-service
pip install -r requirements.txt
python -m uvicorn app.main:app --port 8000
```

Health check: `GET http://127.0.0.1:8000/health`

### 4. Backend API

```bash
cd backend-api/EventReservation.Api
dotnet run --launch-profile http
```

Runs on `http://localhost:5248`. OpenAPI JSON at `/openapi/v1.json` in
Development.

### 5. Frontend

```bash
cd frontend
npm install
npx ng serve --port 4200
```

Open `http://localhost:4200`.

## How recommendations work

- **Similarity model**: every event is turned into a feature vector (event
  type, taxonomy sub-category, performer identity, venue capacity/popularity/
  location, average simulated price, performer popularity). A shared performer
  is weighted most heavily, matching how strongly a repeat performer should
  drive a match.
- **User profile**: the average feature vector of a user's booked events,
  compared by cosine similarity against all currently bookable events.
- **Cold start**: no booking history (new user, or anonymous visitor) ->
  popularity ranking (venue + performer popularity), clearly labeled as a
  trending pick rather than a personalized one.
- **Explanations**: generated by inspecting the actual overlap between a
  user's booking history (or a source event) and each candidate - a shared
  performer, matching event type, or shared venue - rather than a generic
  label, so the stated reason is always traceable to real data.

## Key API endpoints

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/auth/register`, `/login` | - | JWT auth |
| `GET /api/events` | - | Search/filter/paginate bookable events |
| `GET /api/events/{id}` | - | Event detail + live listings |
| `GET /api/events/{id}/similar` | - | Content-based similar events |
| `GET /api/recommendations/for-you` | optional | Personalized (or popularity fallback) |
| `POST /api/bookings` | required | Book a listing (inventory-safe) |
| `GET /api/bookings/me` | required | Booking history |
| `POST /api/organizer/events` | Organizer/Admin | Create an event (venue + listings) |
| `GET /api/organizer/events`, `/{id}` | Organizer/Admin | My events, one event's detail |
| `PUT /api/organizer/events/{id}` | Organizer/Admin | Edit name/date/status |
| `POST /api/organizer/events/{id}/listings`, `PUT /api/organizer/listings/{id}` | Organizer/Admin | Add/edit listings |
| `GET /api/organizer/events/{id}/bookings` | Organizer/Admin | Sales for one event |
| `GET /api/admin/stats` | Admin | Platform-wide dashboard stats |
| `GET /api/admin/users`, `PATCH /api/admin/users/{id}/role` | Admin | List users, change role |
| `GET /api/admin/events`, `POST /api/admin/events/{id}/cancel` | Admin | List/cancel any event |
| `GET /api/admin/bookings` | Admin | Every booking platform-wide |

## Known limitations / future work

- Simulated pricing (see above) - would be replaced by real `price` values on
  a paid SeatGeek data plan.
- No payment gateway - booking is a reservation/checkout simulation.
- Recommender feature matrix is rebuilt on service startup or via
  `POST /admin/refresh`, not on a schedule.
- No automated test suite yet beyond the default Angular smoke test and manual
  end-to-end verification of the auth, browse, recommend, booking, organizer,
  and admin flows.
- No self-service organizer signup or an "apply to become an organizer" flow -
  admin promotion is the only path (see Roles above).
- Organizer event edits are limited to name/date/status and listing
  price/quantity - there's no venue change, image upload, or event deletion
  once created (admin can cancel, not delete).
