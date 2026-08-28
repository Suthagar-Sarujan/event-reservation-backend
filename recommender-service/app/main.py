import logging

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from .db import load_dataset
from .demand import DemandModel
from .recommender import ContentBasedRecommender
from .schemas import (
    DemandModelInfo,
    DemandPrediction,
    DemandPredictionListRequest,
    RecommendationResponse,
    UserRecommendationRequest,
)

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
demand_model = DemandModel()


@app.on_event("startup")
def startup() -> None:
    _refresh()
    _refresh_demand(force=False)


def _refresh() -> None:
    logger.info("Loading SeatGeek-derived data and fitting recommender...")
    data = load_dataset()
    recommender.fit(data)
    logger.info(
        "Recommender ready: %d events (%d currently bookable)",
        len(recommender.feature_df),
        len(recommender.bookable_event_ids),
    )


def _refresh_demand(force: bool) -> None:
    data = load_dataset()
    demand_model.fit(data, force=force)
    meta = demand_model.metadata()
    logger.info("Demand model ready: mode=%s, training_rows=%d", meta["mode"], meta["training_row_count"])


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
    # Rebuilds demand features (new/edited events, latest booking counts)
    # without retraining the model's learned weights - see DemandModel.fit,
    # which only retrains when force=True. Cheap enough to run on every
    # catalog change; the actual training pass stays an explicit action.
    _refresh_demand(force=False)
    return {"status": "refreshed", "events_indexed": len(recommender.feature_df)}


@app.post("/recommendations/user", response_model=RecommendationResponse)
def recommend_for_user(request: UserRecommendationRequest):
    if recommender.feature_df is None:
        raise HTTPException(status_code=503, detail="Recommender not ready")
    items = recommender.recommend_for_user(
        request.booked_event_ids,
        preferred_event_types=request.preferred_event_types,
        preferred_genres=request.preferred_genres,
        top_n=request.top_n,
    )
    personalized = bool(request.booked_event_ids or request.preferred_event_types or request.preferred_genres)
    return RecommendationResponse(items=items, personalized=personalized)


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


@app.post("/demand/predict", response_model=list[DemandPrediction])
def predict_demand(request: DemandPredictionListRequest):
    if demand_model.features is None:
        raise HTTPException(status_code=503, detail="Demand model not ready")
    return demand_model.predict_many(event_ids=request.event_ids, only_upcoming=request.only_upcoming)


@app.get("/demand/predict/{event_id}", response_model=DemandPrediction)
def predict_demand_for_event(event_id: int):
    if demand_model.features is None:
        raise HTTPException(status_code=503, detail="Demand model not ready")
    prediction = demand_model.predict(event_id)
    if prediction is None:
        raise HTTPException(status_code=404, detail=f"Event {event_id} not found")
    return prediction


@app.get("/demand/model-info", response_model=DemandModelInfo)
def demand_model_info():
    return demand_model.metadata()


@app.post("/demand/retrain", response_model=DemandModelInfo)
def retrain_demand():
    logger.info("Retraining demand model on request...")
    _refresh_demand(force=True)
    return demand_model.metadata()
