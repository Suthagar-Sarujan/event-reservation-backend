import logging

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from .db import load_dataset
from .recommender import ContentBasedRecommender
from .schemas import RecommendationResponse, UserRecommendationRequest

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("recommender")

app = FastAPI(
    title="Event Recommendation Service",
    description=(
        "Content-based event recommender built on the SeatGeek Events & Ticket "
        "Listings dataset. Ranks events by attribute similarity (type, taxonomy, "
        "performer, venue, simulated price) and falls back to a non-personalized "
        "popularity ranking when a user has no booking history yet."
    ),
    version="1.0.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

recommender = ContentBasedRecommender()


@app.on_event("startup")
def startup() -> None:
    _refresh()


def _refresh() -> None:
    logger.info("Loading SeatGeek-derived data and fitting recommender...")
    data = load_dataset()
    recommender.fit(data)
    logger.info(
        "Recommender ready: %d events (%d currently bookable)",
        len(recommender.feature_df),
        len(recommender.bookable_event_ids),
    )


@app.get("/health")
def health():
    ready = recommender.feature_df is not None
    return {
        "status": "ok" if ready else "not_ready",
        "events_indexed": 0 if not ready else len(recommender.feature_df),
        "bookable_events": len(recommender.bookable_event_ids),
    }


@app.post("/admin/refresh")
def refresh():
    _refresh()
    return {"status": "refreshed", "events_indexed": len(recommender.feature_df)}


@app.post("/recommendations/user", response_model=RecommendationResponse)
def recommend_for_user(request: UserRecommendationRequest):
    if recommender.feature_df is None:
        raise HTTPException(status_code=503, detail="Recommender not ready")
    items = recommender.recommend_for_user(request.booked_event_ids, top_n=request.top_n)
    return RecommendationResponse(items=items, personalized=bool(request.booked_event_ids))


@app.get("/recommendations/similar/{event_id}", response_model=RecommendationResponse)
def similar_events(event_id: int, top_n: int = 10):
    if recommender.feature_df is None:
        raise HTTPException(status_code=503, detail="Recommender not ready")
    if event_id not in recommender.feature_df.index:
        raise HTTPException(status_code=404, detail=f"Event {event_id} not found")
    items = recommender.similar_events(event_id, top_n=top_n)
    return RecommendationResponse(items=items, personalized=False)


@app.get("/recommendations/popular", response_model=RecommendationResponse)
def popular(top_n: int = 10):
    if recommender.feature_df is None:
        raise HTTPException(status_code=503, detail="Recommender not ready")
    items = recommender.popular(top_n=top_n)
    return RecommendationResponse(items=items, personalized=False)
