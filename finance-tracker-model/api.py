from pathlib import Path
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field, field_validator
from model.transaction_classifier import TransactionClassifier

app = FastAPI(title="Finance Tracker Classifier")
base = Path(__file__).resolve().parent
classifier = TransactionClassifier(
    model_path=str(base / "transaction_classifier_model.pkl"),
    vectorizer_path=str(base / "tfidf_vectorizer.pkl"),
)
try:
    classifier.load_model()
except FileNotFoundError:
    pass


class TransactionRequest(BaseModel):
    description: str = Field(min_length=1, max_length=2000)

    @field_validator("description")
    @classmethod
    def nonblank(cls, value: str) -> str:
        if not value.strip():
            raise ValueError("Description must not be blank.")
        return value.strip()


class TransactionResponse(BaseModel):
    category: str


@app.get("/health")
def health():
    if classifier.model is None or classifier.vectorizer is None:
        raise HTTPException(status_code=503, detail="Model not loaded.")
    return {"status": "ready"}


@app.post("/predict", response_model=TransactionResponse)
def predict_category(request: TransactionRequest):
    if classifier.model is None or classifier.vectorizer is None:
        raise HTTPException(status_code=503, detail="Model not loaded.")
    return {"category": str(classifier.predict(request.description))}
