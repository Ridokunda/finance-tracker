from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from model.transaction_classifier import TransactionClassifier

app = FastAPI()

# Initialize and load the model at startup
classifier = TransactionClassifier(model_path='transaction_classifier_model.pkl', vectorizer_path='tfidf_vectorizer.pkl')
try:
    classifier.load_model()
except FileNotFoundError:
    print("Warning: Model not found. Train the model before making requests.")

class TransactionRequest(BaseModel):
    description: str

class TransactionResponse(BaseModel):
    category: str

@app.post("/predict", response_model=TransactionResponse)
def predict_category(request: TransactionRequest):
    if classifier.model is None:
        raise HTTPException(status_code=500, detail="Model not loaded.")
    
    category = classifier.predict(request.description)
    return {"category": category}