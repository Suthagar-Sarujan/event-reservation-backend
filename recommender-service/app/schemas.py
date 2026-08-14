from pydantic import BaseModel


class RecommendationItem(BaseModel):
    event_id: int
    score: float
    reason: str


class UserRecommendationRequest(BaseModel):
    booked_event_ids: list[int] = []
    top_n: int = 10


class RecommendationResponse(BaseModel):
    items: list[RecommendationItem]
    personalized: bool
