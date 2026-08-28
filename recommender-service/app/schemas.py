from pydantic import BaseModel


class RecommendationItem(BaseModel):
    event_id: int
    score: float
    reason: str


class UserRecommendationRequest(BaseModel):
    booked_event_ids: list[int] = []
    # Free-form labels from the onboarding questionnaire (see UserPreference on
    # the backend), e.g. preferred_event_types=["Music Concerts"],
    # preferred_genres=["Rock", "EDM"]. Matched as case-insensitive substrings
    # against each event's type/taxonomy/name - see ContentBasedRecommender.
    preferred_event_types: list[str] = []
    preferred_genres: list[str] = []
    top_n: int = 10


class RecommendationResponse(BaseModel):
    items: list[RecommendationItem]
    personalized: bool


class DemandPrediction(BaseModel):
    event_id: int
    event_name: str
    datetime_utc: str | None
    capacity: int
    current_bookings: int
    predicted_demand: int
    expected_occupancy: float
    demand_level: str


class DemandPredictionListRequest(BaseModel):
    event_ids: list[int] | None = None
    only_upcoming: bool = True


class DemandModelInfo(BaseModel):
    version: str | None
    trained_at: str | None
    training_row_count: int
    mode: str
    mae: float | None
