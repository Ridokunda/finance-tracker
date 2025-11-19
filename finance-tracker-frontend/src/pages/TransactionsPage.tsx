import { useEffect, useState, useCallback } from "react";
import type { ChangeEvent, FormEvent, MouseEvent } from "react";
import { createTransaction, getTransactions } from "../services/transaction";
import { uploadStatement } from "../services/statement";
import TransactionFilters from "../components/TransactionFilters.tsx";
import TransactionsList from "../components/TransactionsList.tsx";
import "./Dashboard.css";
import "./TransactionsPage.css";

interface Transaction {
  id: number;
  date: string;
  description: string;
  amount: number;
  category: string;
}

interface TransactionFormState {
  date: string;
  description: string;
  category: string;
  amount: string;
}

const createInitialFormState = (): TransactionFormState => ({
  date: new Date().toISOString().split("T")[0],
  description: "",
  category: "",
  amount: ""
});

type ModalMode = "transaction" | "statement";

function normalizeTransactions(payload: unknown): Transaction[] {
  if (Array.isArray(payload)) {
    return payload as Transaction[];
  }

  if (payload && typeof payload === "object") {
    const record = payload as Record<string, unknown>;
    if (Array.isArray(record.data)) {
      return record.data as Transaction[];
    }
    if (Array.isArray(record.$values)) {
      return record.$values as Transaction[];
    }
  }

  return [];
}

export default function TransactionsPage() {
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [token, setToken] = useState<string>("");
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(() => {
    try {
      return Boolean(localStorage.getItem("token"));
    } catch {
      return false;
    }
  });
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string>("");
  const [isModalOpen, setModalOpen] = useState<boolean>(false);
  const [formData, setFormData] = useState<TransactionFormState>(createInitialFormState);
  const [formSubmitting, setFormSubmitting] = useState<boolean>(false);
  const [formError, setFormError] = useState<string>("");
  const [refreshKey, setRefreshKey] = useState<number>(0);
  const [modalMode, setModalMode] = useState<ModalMode>("transaction");
  const [statementFile, setStatementFile] = useState<File | null>(null);

  useEffect(() => {
    let storedToken = "";
    try {
      storedToken = localStorage.getItem("token") || "";
    } catch {
      storedToken = "";
    }

    if (!storedToken) {
      window.location.href = "/login";
      return;
    }

    setToken(storedToken);
    setIsLoggedIn(true);
  }, []);

  useEffect(() => {
    if (!token) {
      return;
    }

    const load = async () => {
      setLoading(true);
      try {
        const data = await getTransactions(token);
        setTransactions(normalizeTransactions(data));
        setError("");
      } catch (err) {
        console.error("Failed to fetch transactions", err);
        setError("Unable to fetch transactions right now.");
        setTransactions([]);
      } finally {
        setLoading(false);
      }
    };

    load();
  }, [token]);

  const handleAuthClick = (e: MouseEvent<HTMLAnchorElement>) => {
    e.preventDefault();
    if (isLoggedIn) {
      try {
        localStorage.removeItem("token");
      } catch {
        /* ignore */
      }
      setIsLoggedIn(false);
    }
    window.location.href = "/login";
  };

  const handleFiltersUpdate = useCallback((data: unknown[]) => {
    setTransactions(normalizeTransactions(data));
    setLoading(false);
    setError("");
  }, []);

  const handleFilterError = useCallback((message: string) => {
    setError(message);
  }, []);

  const handleAddClick = () => {
    setModalMode("transaction");
    setStatementFile(null);
    setFormData(createInitialFormState());
    setFormError("");
    setModalOpen(true);
  };

  const handleModalClose = () => {
    setModalOpen(false);
    setFormError("");
    setModalMode("transaction");
    setFormData(createInitialFormState());
    setStatementFile(null);
  };

  const handleModeChange = (mode: ModalMode) => {
    if (mode === modalMode) {
      return;
    }

    setModalMode(mode);
    setFormError("");

    if (mode === "transaction") {
      setFormData(createInitialFormState());
    } else {
      setStatementFile(null);
    }
  };

  const handleFormChange = (field: keyof TransactionFormState) => (event: ChangeEvent<HTMLInputElement>) => {
    const { value } = event.target;
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  const handleFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const [file] = event.target.files || [];
    setStatementFile(file ?? null);
  };

  const handleFormSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (!token) {
      setFormError("Authentication required.");
      return;
    }

    if (modalMode === "transaction") {
      const description = formData.description.trim();
      if (!description) {
        setFormError("Description is required.");
        return;
      }

      const amountValue = Number(formData.amount);
      if (!Number.isFinite(amountValue) || amountValue === 0) {
        setFormError("Amount must be a non-zero number.");
        return;
      }

      const isoDate = formData.date ? new Date(formData.date).toISOString() : new Date().toISOString();

      setFormSubmitting(true);
      setFormError("");

      try {
        await createTransaction(token, {
          date: isoDate,
          description,
          amount: amountValue,
          category: formData.category.trim() || undefined
        });

        setError("");
        setLoading(true);
        setRefreshKey((prev) => prev + 1);
        handleModalClose();
      } catch (err) {
        const message = err instanceof Error && err.message ? err.message : "Unable to save transaction.";
        setFormError(message);
      } finally {
        setFormSubmitting(false);
      }

      return;
    }

    if (!statementFile) {
      setFormError("Please choose a CSV statement file to upload.");
      return;
    }

    setFormSubmitting(true);
    setFormError("");

    try {
      await uploadStatement(statementFile, token);

      setError("");
      setLoading(true);
      setRefreshKey((prev) => prev + 1);
      handleModalClose();
    } catch (err) {
      const message = err instanceof Error && err.message ? err.message : "Unable to upload statement.";
      setFormError(message);
    } finally {
      setFormSubmitting(false);
    }
  };

  return (
    <div className="dashboard-wrap transactions-page">
      <div className="topbar">
        <div className="brand">
          <div className="logo" />
          <h2 style={{ margin: 0 }}>FINANCE-TRACKER</h2>
        </div>
        <nav className="nav">
          <a href="/">Dashboard</a>
          <a href="/transactions" className="active">Transactions</a>
          <a>Expenses</a>
          <a>Reports</a>
          <a>Categories</a>
          <a>Settings</a>
          <a onClick={handleAuthClick} role="button" style={{ cursor: "pointer" }}>{isLoggedIn ? "Logout" : "Login"}</a>
        </nav>
      </div>

      <div className="transactions-area">
        <div className="transactions-card">
          <div className="transactions-card-header">
            <h2>Transactions</h2>
            <p className="small">Review and filter your financial activity.</p>
          </div>

          <div className="transactions-card-actions">
            <button
              type="button"
              className="primary-btn"
              onClick={handleAddClick}
            >
              Add Transaction / Statement
            </button>
          </div>

          {token && (
            <TransactionFilters
              token={token}
              setTransactions={handleFiltersUpdate}
              onLoadingChange={setLoading}
              onError={handleFilterError}
              refreshKey={refreshKey}
            />
          )}

          {loading ? (
            <p style={{ color: "#6b7280" }}>Loading transactions...</p>
          ) : error ? (
            <p style={{ color: "#ef4444" }}>{error}</p>
          ) : (
            <TransactionsList transactions={transactions} />
          )}
        </div>
      </div>

      {isModalOpen && (
        <div className="transactions-modal-overlay" role="dialog" aria-modal="true">
          <div className="transactions-modal">
            <h3>{modalMode === "transaction" ? "Add Transaction" : "Upload Statement"}</h3>

            <div className="transactions-modal-toggle" role="tablist" aria-label="Add options">
              <button
                type="button"
                className={`toggle-option ${modalMode === "transaction" ? "is-active" : ""}`}
                onClick={() => handleModeChange("transaction")}
                aria-pressed={modalMode === "transaction"}
                disabled={formSubmitting}
              >
                Single Transaction
              </button>
              <button
                type="button"
                className={`toggle-option ${modalMode === "statement" ? "is-active" : ""}`}
                onClick={() => handleModeChange("statement")}
                aria-pressed={modalMode === "statement"}
                disabled={formSubmitting}
              >
                Upload Statement
              </button>
            </div>

            <form className="transactions-modal-form" onSubmit={handleFormSubmit}>
              {modalMode === "transaction" ? (
                <>
                  <label>
                    <span>Date</span>
                    <input
                      type="date"
                      value={formData.date}
                      onChange={handleFormChange("date")}
                      required
                      disabled={formSubmitting}
                    />
                  </label>

                  <label>
                    <span>Description</span>
                    <input
                      type="text"
                      value={formData.description}
                      onChange={handleFormChange("description")}
                      placeholder="e.g. Grocery run"
                      required
                      autoFocus
                      disabled={formSubmitting}
                    />
                  </label>

                  <label>
                    <span>Category</span>
                    <input
                      type="text"
                      value={formData.category}
                      onChange={handleFormChange("category")}
                      placeholder="Optional"
                      disabled={formSubmitting}
                    />
                  </label>

                  <label>
                    <span>Amount</span>
                    <input
                      type="number"
                      value={formData.amount}
                      onChange={handleFormChange("amount")}
                      placeholder="Use positive for income, negative for expenses"
                      step="0.01"
                      required
                      disabled={formSubmitting}
                    />
                  </label>
                </>
              ) : (
                <>
                  <p className="modal-helper-text">
                    Upload a CSV statement exported from your bank. We will import each row as a transaction and auto-categorize known vendors.
                  </p>

                  <label>
                    <span>Statement (CSV)</span>
                    <input
                      type="file"
                      accept=".csv,text/csv"
                      onChange={handleFileChange}
                      required
                      disabled={formSubmitting}
                      autoFocus
                    />
                  </label>

                  {statementFile && (
                    <p className="selected-file">Selected file: {statementFile.name}</p>
                  )}
                </>
              )}

              {formError && <p className="form-error">{formError}</p>}

              <div className="transactions-modal-actions">
                <button
                  type="button"
                  className="secondary-btn"
                  onClick={handleModalClose}
                  disabled={formSubmitting}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="primary-btn"
                  disabled={formSubmitting}
                >
                  {modalMode === "transaction"
                    ? formSubmitting ? "Saving..." : "Save Transaction"
                    : formSubmitting ? "Uploading..." : "Upload Statement"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
