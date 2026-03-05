import pandas as pd
from sklearn.model_selection import train_test_split
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import classification_report, accuracy_score
import joblib
import os

class TransactionClassifier:
    def __init__(self, model_path='transaction_classifier_model.pkl', vectorizer_path='tfidf_vectorizer.pkl'):
        self.model_path = model_path
        self.vectorizer_path = vectorizer_path
        self.model = None
        self.vectorizer = None

    def load_data(self, file_path):
        """Load transaction data from CSV file."""
        df = pd.read_csv(file_path)
        # Select only description and category columns
        df = df[['description', 'category']]
        return df

    def preprocess_data(self, df):
        """Preprocess the data: handle missing values, etc."""
        df = df.dropna()  # Remove rows with missing values
        return df

    def train(self, file_path):
        """Train the classifier model."""
        # Load and preprocess data
        df = self.load_data(file_path)
        df = self.preprocess_data(df)

        # Split features and target
        X = df['description']
        y = df['category']

        # Split into train and test sets
        X_train, X_test, y_train, y_test = train_test_split(X, y, test_size=0.2, random_state=42, stratify=y)

        # Vectorize the text data
        self.vectorizer = TfidfVectorizer(max_features=5000, stop_words='english')
        X_train_vec = self.vectorizer.fit_transform(X_train)
        X_test_vec = self.vectorizer.transform(X_test)

        # Train the model
        self.model = LogisticRegression(random_state=42, max_iter=1000)
        self.model.fit(X_train_vec, y_train)

        # Evaluate the model
        y_pred = self.model.predict(X_test_vec)
        print("Accuracy:", accuracy_score(y_test, y_pred))
        print("Classification Report:")
        print(classification_report(y_test, y_pred))

        # Save the model and vectorizer
        self.save_model()

    def predict(self, description):
        """Predict the category for a given description."""
        if self.model is None or self.vectorizer is None:
            self.load_model()

        # Vectorize the input
        desc_vec = self.vectorizer.transform([description])

        # Predict
        prediction = self.model.predict(desc_vec)
        return prediction[0]

    def save_model(self):
        """Save the trained model and vectorizer."""
        joblib.dump(self.model, self.model_path)
        joblib.dump(self.vectorizer, self.vectorizer_path)
        print(f"Model saved to {self.model_path}")
        print(f"Vectorizer saved to {self.vectorizer_path}")

    def load_model(self):
        """Load the trained model and vectorizer."""
        if os.path.exists(self.model_path) and os.path.exists(self.vectorizer_path):
            self.model = joblib.load(self.model_path)
            self.vectorizer = joblib.load(self.vectorizer_path)
            print("Model and vectorizer loaded successfully.")
        else:
            raise FileNotFoundError("Model or vectorizer file not found. Please train the model first.")

# Example usage
if __name__ == "__main__":
    # Initialize classifier
    classifier = TransactionClassifier()

    # Train the model (replace 'transactions.csv' with your actual file path)
    classifier.train('../data/realistic_synthetic_bank_transactions.csv')

    # To predict a category
    prediction = classifier.predict("bolt ride")
    print(f"Predicted category: {prediction}")
    pass
