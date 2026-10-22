FROM python:3.11-slim

WORKDIR /app

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

COPY requirements-lock.txt ./
RUN python -m pip install --no-cache-dir -r requirements-lock.txt

COPY api.py ./
COPY model/transaction_classifier.py ./model/transaction_classifier.py
COPY transaction_classifier_model.pkl tfidf_vectorizer.pkl ./

EXPOSE 8000

USER 10001:10001

CMD ["python", "-m", "uvicorn", "api:app", "--host", "0.0.0.0", "--port", "8000"]