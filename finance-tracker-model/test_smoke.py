from fastapi.testclient import TestClient
from api import app, classifier

client = TestClient(app)


def test_saved_model_predicts():
    assert client.get("/health").status_code == 200
    response = client.post("/predict", json={"description": "Checkers groceries"})
    assert response.status_code == 200
    assert response.json()["category"] in classifier.model.classes_


def test_blank_description_is_rejected():
    assert client.post("/predict", json={"description": "  "}).status_code == 422


def test_missing_model_returns_recoverable_outage(monkeypatch):
    monkeypatch.setattr(classifier, "model", None)
    assert client.post("/predict", json={"description": "bus fare"}).status_code == 503
