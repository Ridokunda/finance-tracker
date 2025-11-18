import { useEffect, useState, useCallback } from "react";
import type { MouseEvent } from "react";
import { getTransactions } from "../services/transaction";
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

          {token && (
            <TransactionFilters
              token={token}
              setTransactions={handleFiltersUpdate}
              onLoadingChange={setLoading}
              onError={handleFilterError}
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
    </div>
  );
}
