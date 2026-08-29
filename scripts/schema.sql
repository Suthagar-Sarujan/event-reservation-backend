-- Smart Event Ticket Reservation System
-- Database schema (MySQL 8.0)
--
-- Dimension tables (venues, performers, events, listings) are seeded from the
-- SeatGeek Events & Ticket Listings dataset (rebrowser/seatgeek-dataset on Kaggle)
-- by scripts/import_seatgeek_data.py, but events/venues/performers can ALSO be
-- created directly by organizers through the app (OrganizerController). To keep
-- the two sources from ever colliding on primary keys, organizer-created rows
-- use MySQL AUTO_INCREMENT seeded far above any id SeatGeek could plausibly
-- assign (see the ALTER TABLE ... AUTO_INCREMENT statements below) - the import
-- script inserts SeatGeek's real, much smaller ids explicitly and never touches
-- the counter itself.

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

DROP TABLE IF EXISTS gate_scan_histories;
DROP TABLE IF EXISTS gate_user_assignments;
DROP TABLE IF EXISTS gates;
DROP TABLE IF EXISTS booking_risk_assessments;
DROP TABLE IF EXISTS user_event_ticket_counts;
DROP TABLE IF EXISTS user_preferences;
DROP TABLE IF EXISTS booking_items;
DROP TABLE IF EXISTS bookings;
DROP TABLE IF EXISTS listings;
DROP TABLE IF EXISTS event_performers;
DROP TABLE IF EXISTS events;
DROP TABLE IF EXISTS performers;
DROP TABLE IF EXISTS venues;
DROP TABLE IF EXISTS users;

-- ---------------------------------------------------------------------------
-- Application-owned tables
-- ---------------------------------------------------------------------------

CREATE TABLE users (
    user_id        INT AUTO_INCREMENT PRIMARY KEY,
    full_name       VARCHAR(150) NOT NULL,
    email            VARCHAR(190) NOT NULL UNIQUE,
    password_hash     VARCHAR(255) NOT NULL,
    role               ENUM('customer','organizer','admin','gateuser') NOT NULL DEFAULT 'customer',
    theme_preference   ENUM('light','dark','system') NOT NULL DEFAULT 'system',
    created_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------------
-- Dimension tables - rows either imported from SeatGeek or created by an
-- organizer (see created_by_user_id on events/venues/performers: NULL means
-- SeatGeek-sourced, set means organizer-created).
-- ---------------------------------------------------------------------------

CREATE TABLE venues (
    venue_id            INT AUTO_INCREMENT PRIMARY KEY,
    name                VARCHAR(255) NOT NULL,
    slug                VARCHAR(255),
    address_street      VARCHAR(255),
    address_city        VARCHAR(120),
    address_state       VARCHAR(50),
    address_country     VARCHAR(80),
    address_postal_code VARCHAR(20),
    timezone            VARCHAR(60),
    latitude            DECIMAL(9,6),
    longitude           DECIMAL(9,6),
    capacity            INT,                 -- 0/NULL means unknown in source data
    popularity_score    DECIMAL(5,4),        -- SeatGeek 'score', 0-1 scale
    popularity_count    INT,                 -- SeatGeek 'popularity', raw count
    metro_code          INT,
    created_by_user_id  INT NULL,            -- NULL = imported from SeatGeek
    CONSTRAINT fk_venue_creator FOREIGN KEY (created_by_user_id) REFERENCES users(user_id) ON DELETE SET NULL
) ENGINE=InnoDB;

CREATE TABLE performers (
    performer_id        INT AUTO_INCREMENT PRIMARY KEY,
    name                VARCHAR(255) NOT NULL,
    short_name          VARCHAR(150),
    type                VARCHAR(50),
    slug                VARCHAR(255),
    taxonomy_name        VARCHAR(50),
    taxonomy_sub_name    VARCHAR(50),
    home_venue_id        INT,
    score                DECIMAL(5,4),
    popularity           INT,
    is_event             TINYINT(1) DEFAULT 0,
    division_name        VARCHAR(100),
    division_short_name  VARCHAR(50),
    created_by_user_id   INT NULL,           -- NULL = imported from SeatGeek
    CONSTRAINT fk_performer_home_venue FOREIGN KEY (home_venue_id) REFERENCES venues(venue_id),
    CONSTRAINT fk_performer_creator FOREIGN KEY (created_by_user_id) REFERENCES users(user_id) ON DELETE SET NULL
) ENGINE=InnoDB;

CREATE TABLE events (
    event_id                BIGINT AUTO_INCREMENT PRIMARY KEY,
    name                     VARCHAR(500) NOT NULL,
    short_name               VARCHAR(300),
    type                     VARCHAR(50),
    taxonomy_name             VARCHAR(50),
    taxonomy_sub_name         VARCHAR(50),
    venue_id                  INT NOT NULL,
    datetime_utc               DATETIME NOT NULL,
    end_datetime_utc            DATETIME,
    date_tbd                   TINYINT(1) DEFAULT 0,
    time_tbd                   TINYINT(1) DEFAULT 0,
    status                     VARCHAR(30),
    schedule_status             VARCHAR(50),
    is_open                    TINYINT(1) DEFAULT 0,
    is_ga                      TINYINT(1) DEFAULT 0,
    seat_selection_enabled      TINYINT(1) DEFAULT 0,
    url                        VARCHAR(500),
    created_at_source            DATETIME,
    announce_date                DATETIME,
    created_by_user_id            INT NULL,   -- NULL = imported from SeatGeek, set = organizer-created
    image_url                     VARCHAR(500), -- organizer-supplied event photo (they own the rights) - SeatGeek-imported events have none
    CONSTRAINT fk_event_venue FOREIGN KEY (venue_id) REFERENCES venues(venue_id),
    CONSTRAINT fk_event_creator FOREIGN KEY (created_by_user_id) REFERENCES users(user_id) ON DELETE SET NULL,
    INDEX idx_events_datetime (datetime_utc),
    INDEX idx_events_type (type),
    INDEX idx_events_taxonomy (taxonomy_name, taxonomy_sub_name),
    INDEX idx_events_creator (created_by_user_id)
) ENGINE=InnoDB;

CREATE TABLE event_performers (
    event_id      BIGINT NOT NULL,
    performer_id  INT NOT NULL,
    PRIMARY KEY (event_id, performer_id),
    CONSTRAINT fk_ep_event FOREIGN KEY (event_id) REFERENCES events(event_id) ON DELETE CASCADE,
    CONSTRAINT fk_ep_performer FOREIGN KEY (performer_id) REFERENCES performers(performer_id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE listings (
    -- utf8mb4_bin: SeatGeek listing IDs are case-sensitive (e.g. "4vXcj9j2YnX" and
    -- "4vXcj9j2YNX" are two different listings), so a case-insensitive collation
    -- would silently collide distinct rows onto the same primary key. Organizer-
    -- created listings get an app-generated "org-" prefixed id (see
    -- OrganizerController) that can never collide with a SeatGeek listingId.
    listing_id           VARCHAR(100) COLLATE utf8mb4_bin PRIMARY KEY,
    event_id             BIGINT NOT NULL,
    section               VARCHAR(50),
    section_full           VARCHAR(150),
    row_label              VARCHAR(20),
    quantity               INT NOT NULL,
    quantity_remaining      INT NOT NULL,
    deal_bucket             TINYINT,          -- 0=Amazing .. 7=Other (SeatGeek deal-quality tier) - NULL for organizer listings
    delivery_type           VARCHAR(30),
    marketplace              VARCHAR(40),
    split_type                VARCHAR(255),
    in_hand_date               DATETIME,
    unit_price                 DECIMAL(10,2) NOT NULL,  -- simulated for SeatGeek rows, real for organizer rows - see README
    listing_status               ENUM('available','sold_out') NOT NULL DEFAULT 'available',
    CONSTRAINT fk_listing_event FOREIGN KEY (event_id) REFERENCES events(event_id) ON DELETE CASCADE,
    INDEX idx_listings_event (event_id),
    INDEX idx_listings_price (unit_price)
) ENGINE=InnoDB;

CREATE TABLE bookings (
    booking_id          INT AUTO_INCREMENT PRIMARY KEY,
    booking_reference     VARCHAR(20) NOT NULL UNIQUE,
    user_id                INT NOT NULL,
    event_id                BIGINT NOT NULL,
    status                    ENUM('confirmed','cancelled') NOT NULL DEFAULT 'confirmed',
    total_amount               DECIMAL(10,2) NOT NULL,
    created_at                   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    payment_reference             VARCHAR(20) NULL,  -- mock payment confirmation id, no real gateway is integrated
    checked_in_at                   DATETIME NULL,   -- set once, at the door, by TicketVerificationController
    checked_out_at                  DATETIME NULL,   -- set once, by a Gate User's check-out scan, only after checked_in_at is set
    email_status                     VARCHAR(255) NOT NULL DEFAULT 'pending',  -- 'pending' | 'sent' | 'failed', see SmtpEmailService
    email_sent_at                      DATETIME NULL,   -- set on a successful confirmation-email send, cleared to NULL on a failed attempt
    email_attempts                       INT NOT NULL DEFAULT 0,  -- incremented on every send attempt, auto or resend
    CONSTRAINT fk_booking_user FOREIGN KEY (user_id) REFERENCES users(user_id),
    CONSTRAINT fk_booking_event FOREIGN KEY (event_id) REFERENCES events(event_id),
    INDEX idx_bookings_user (user_id)
) ENGINE=InnoDB;

CREATE TABLE booking_items (
    booking_item_id    INT AUTO_INCREMENT PRIMARY KEY,
    booking_id           INT NOT NULL,
    listing_id             VARCHAR(100) COLLATE utf8mb4_bin NOT NULL,
    quantity                 INT NOT NULL,
    unit_price                 DECIMAL(10,2) NOT NULL,
    subtotal                     DECIMAL(10,2) NOT NULL,
    CONSTRAINT fk_bi_booking FOREIGN KEY (booking_id) REFERENCES bookings(booking_id) ON DELETE CASCADE,
    CONSTRAINT fk_bi_listing FOREIGN KEY (listing_id) REFERENCES listings(listing_id)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------------
-- Recommendation personalization (app-owned)
-- ---------------------------------------------------------------------------

-- One row per customer, captured by the onboarding questionnaire and editable
-- afterward. event_types/music_genres are comma-joined free-form labels from
-- a small fixed frontend option set (same convention as
-- booking_risk_assessments.reasons) - matched as case-insensitive, word-
-- boundary substrings against events.type/taxonomy_name/taxonomy_sub_name/name
-- by the recommender service (see recommender-service/app/recommender.py).
CREATE TABLE user_preferences (
    user_id               INT PRIMARY KEY,
    event_types           VARCHAR(255) NOT NULL DEFAULT '',
    music_genres          VARCHAR(255) NOT NULL DEFAULT '',
    atmosphere            VARCHAR(50) NULL,
    attendance_frequency  VARCHAR(50) NULL,
    created_at            DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at            DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_up_user FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------------
-- Fraud / booking-abuse prevention (app-owned)
-- ---------------------------------------------------------------------------

-- Atomic per-(user, event) ticket counter enforcing the "max N tickets per
-- account per event" cap race-condition-free - see BookingRepository.CreateAsync,
-- which locks and increments this row inside the same transaction as the
-- listing inventory decrement.
CREATE TABLE user_event_ticket_counts (
    user_id         INT NOT NULL,
    event_id        BIGINT NOT NULL,
    tickets_booked  INT NOT NULL DEFAULT 0,
    updated_at      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, event_id),
    CONSTRAINT fk_uetc_user  FOREIGN KEY (user_id)  REFERENCES users(user_id)   ON DELETE CASCADE,
    CONSTRAINT fk_uetc_event FOREIGN KEY (event_id) REFERENCES events(event_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- One row per booking attempt that was risk-evaluated (allowed, flagged, or
-- blocked) - covers both a "BookingRisk" and "FraudDetectionLog" role in one
-- table since they describe the same event. ip_address is for fraud/security
-- review only, never exposed to the customer-facing API.
CREATE TABLE booking_risk_assessments (
    booking_risk_id     BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id             INT NOT NULL,
    event_id            BIGINT NOT NULL,
    booking_id          INT NULL,            -- NULL when blocked before a booking row ever existed
    ip_address          VARCHAR(45) NULL,
    requested_quantity  INT NOT NULL,
    risk_score          INT NOT NULL,
    risk_level          ENUM('low','medium','high') NOT NULL,
    decision            ENUM('allowed','flagged','blocked') NOT NULL,
    reasons             VARCHAR(500) NOT NULL,
    created_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_bra_user    FOREIGN KEY (user_id)    REFERENCES users(user_id)    ON DELETE CASCADE,
    CONSTRAINT fk_bra_event   FOREIGN KEY (event_id)   REFERENCES events(event_id)  ON DELETE CASCADE,
    CONSTRAINT fk_bra_booking FOREIGN KEY (booking_id) REFERENCES bookings(booking_id) ON DELETE SET NULL,
    INDEX idx_bra_user (user_id),
    INDEX idx_bra_event (event_id),
    INDEX idx_bra_ip (ip_address),
    INDEX idx_bra_created (created_at)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------------
-- Gate management & QR ticket scanning (app-owned)
-- ---------------------------------------------------------------------------

-- Venue-level physical entry point (e.g. "Gate A") - reusable across events,
-- deliberately NOT tied to one event via a foreign key. A Gate User's scan
-- session instead supplies the eventId client-side (see GateService.
-- ScanTicketAsync), which verifies the ticket's booking belongs to that event.
CREATE TABLE gates (
    gate_id             INT AUTO_INCREMENT PRIMARY KEY,
    name                VARCHAR(150) NOT NULL UNIQUE,
    description         VARCHAR(500) NULL,
    status              ENUM('active','inactive') NOT NULL DEFAULT 'active',
    created_by_user_id  INT NULL,
    created_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_gate_creator FOREIGN KEY (created_by_user_id) REFERENCES users(user_id) ON DELETE SET NULL,
    INDEX idx_gates_status (status)
) ENGINE=InnoDB;

-- Join row granting a Gate User staff account permission to scan at a
-- specific gate. A user can be assigned to multiple gates.
CREATE TABLE gate_user_assignments (
    gate_id              INT NOT NULL,
    user_id              INT NOT NULL,
    assigned_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    assigned_by_user_id  INT NULL,
    PRIMARY KEY (gate_id, user_id),
    CONSTRAINT fk_gua_gate     FOREIGN KEY (gate_id)             REFERENCES gates(gate_id) ON DELETE CASCADE,
    CONSTRAINT fk_gua_user     FOREIGN KEY (user_id)             REFERENCES users(user_id) ON DELETE CASCADE,
    CONSTRAINT fk_gua_assigner FOREIGN KEY (assigned_by_user_id) REFERENCES users(user_id) ON DELETE SET NULL,
    INDEX idx_gua_user (user_id)
) ENGINE=InnoDB;

-- One row per scan attempt at a gate, success or failure, for audit -
-- mirroring booking_risk_assessments' role as an append-only attempt log.
-- booking_id/event_id are nullable because a scan can fail before a booking
-- was ever resolved (e.g. malformed code, or a gate-permission rejection
-- that never even looks one up). gate_id is nullable for the same reason on
-- the gate side: a request can name a gate id that doesn't exist at all
-- (stale client state, forged request), and the row still needs to be
-- logged without violating the FK to a row that isn't there.
CREATE TABLE gate_scan_histories (
    scan_id             BIGINT AUTO_INCREMENT PRIMARY KEY,
    gate_id             INT NULL,
    scanned_by_user_id  INT NOT NULL,
    booking_id          INT NULL,
    scanned_code        VARCHAR(255) NOT NULL,  -- raw scanned text, kept for audit even on failure
    event_id            BIGINT NULL,
    scan_type           ENUM('checkin','checkout') NOT NULL DEFAULT 'checkin',
    status               ENUM('success','failed') NOT NULL,
    failure_reason        VARCHAR(500) NULL,     -- human-readable message text, NULL on success
    scanned_at             DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_gsh_gate    FOREIGN KEY (gate_id)            REFERENCES gates(gate_id)       ON DELETE RESTRICT,
    CONSTRAINT fk_gsh_user    FOREIGN KEY (scanned_by_user_id) REFERENCES users(user_id)       ON DELETE RESTRICT,
    CONSTRAINT fk_gsh_booking FOREIGN KEY (booking_id)         REFERENCES bookings(booking_id) ON DELETE SET NULL,
    CONSTRAINT fk_gsh_event   FOREIGN KEY (event_id)           REFERENCES events(event_id)     ON DELETE SET NULL,
    INDEX idx_gsh_gate (gate_id),
    INDEX idx_gsh_status (status),
    INDEX idx_gsh_scanned_at (scanned_at)
) ENGINE=InnoDB;

-- Reserve id space for organizer-created rows well above anything SeatGeek's
-- sample data uses (observed max: events ~18.3M, venues ~467K, performers
-- ~793K), so the import script's explicit SeatGeek ids never collide with
-- ids MySQL auto-assigns to organizer-created rows.
ALTER TABLE events AUTO_INCREMENT = 900000000;
ALTER TABLE venues AUTO_INCREMENT = 5000000;
ALTER TABLE performers AUTO_INCREMENT = 5000000;

SET FOREIGN_KEY_CHECKS = 1;
