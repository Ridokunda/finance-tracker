import React, { useState, useEffect } from "react";
import './Dashboard.css';
import StatementUploader from '../components/StatementUploader';
import { getTransactions } from '../services/statement';
import { getBudget, setBudget as updateBudget } from "../services/budget";

type Transaction = {
  id: number;
  description: string;
  cat: string;
  amount: number;
};

export default function Dashboard() {
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(() => {
    try { return Boolean(localStorage.getItem('token')); } catch { return false; }
  });
  const [showUploader, setShowUploader] = useState<boolean>(false);
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [budget, setBudget] = useState(0);
  const [spent, setSpent] = useState(0);
  const [remaining, setRemaining] = useState(0);
  const [budgetModalOpen, setBudgetModalOpen] = useState(false);
  const [budgetInput, setBudgetInput] = useState("");
  const [budgetError, setBudgetError] = useState("");
  const [budgetSaving, setBudgetSaving] = useState(false);


  useEffect(() => {
    const fetchTransactions = async () => {
      try {
        const token = localStorage.getItem('token');
        if (token) {
          const result = await getTransactions(token);
          setTransactions(result.data || result || []);
        }
      } catch (error) {
        console.error('Failed to fetch transactions:', error);
        // Fallback to dummy data if API fails
        setTransactions([
          { id: 1, description: "Starbucks - Coffee", cat: "Food", amount: -5.5 },
          { id: 2, description: "Magur Transit - Bus Fare", cat: "Transport", amount: -2.75 },
          { id: 3, description: "Supermarket - Groceries", cat: "Food", amount: -75.2 },
          { id: 4, description: "Salary", cat: "Income", amount: 2500 },
        ]);
      } finally {
        setLoading(false);
      }
    };
    const token = localStorage.getItem("token") || "";

    const fetchBudget = async () => {
      const summary = await getBudget(token);
      setBudget(summary.budget);
      setSpent(summary.spent ?? 0);
      setRemaining(summary.remaining ?? 0);
      setBudgetInput(summary.budget.toString());
    };

    fetchBudget();
    fetchTransactions();
  }, []);

  function handleAuthClick(e: React.MouseEvent) {
    e.preventDefault();
    if (isLoggedIn) {
      // log out
      try { localStorage.removeItem('token'); } catch { /* ignore */ }
      setIsLoggedIn(false);
      window.location.href = '/login';
    } else {
      // go to login
      window.location.href = '/login';
    }
  }
  const categories = [
    { name: "Rent & Utilities", value: 30, color: "#34d399" },
    { name: "Food & Dining", value: 26, color: "#60a5fa" },
    { name: "Shopping", value: 10, color: "#7c3aed" },
    { name: "Entertainment", value: 10, color: "#f97316" },
    { name: "Others", value: 15, color: "#94a3b8" },
  ];

  const trendPoints = [10, 22, 18, 26, 30, 35];

  const donutCirc = (v: number) => `${(v / 100) * 339.292}`; // circumference approx for r=54

  return (
    <div className="dashboard-wrap">
      {/* styles moved to Dashboard.css */}

      <div className="topbar">
        <div className="brand">
          <img src="/logo.png" alt="Finance Tracker logo" className="logo" />
          <h2 style={{ margin: 0 }}>FINANCE-TRACKER</h2>
        </div>
        <nav className="nav">
          <a href="/" className="active">Dashboard</a>
          <a href="/transactions">Transactions</a>
          <a>Expenses</a>
          <a>Reports</a>
          <a>Categories</a>
          <a>Settings</a>
          <a onClick={handleAuthClick} role="button" style={{ cursor: 'pointer' }}>{isLoggedIn ? 'Logout' : 'Login'}</a>
        </nav>
      </div>

      <div className="grid">
        <div className="column">
          <div className="row-cards">
            <div className="card h-lg">
              <div className="card-title">Monthly Budget</div>
              <div className="card-value">${budget.toFixed(2)}</div>
              <div className="card-sub" style={{ color: remaining < 0 ? "#ef4444" : "#10b981" }}>
                Remaining: ${remaining.toFixed(2)}
              </div>
              <button
                className="card-action"
                type="button"
                onClick={() => {
                  setBudgetInput(budget.toFixed(2));
                  setBudgetError("");
                  setBudgetModalOpen(true);
                }}
              >
                Update Budget
              </button>
            </div>
            <div className="card h-lg">
              <div className="card-title">Monthly Spending</div>
              <div className="card-value">${spent.toFixed(2)}</div>
              <div className="card-sub" style={{ color: spent > budget ? '#ef4444' : '#6b7280' }}>
                {spent > budget ? "Over budget" : "Tracked so far"}
              </div>
            </div>
          </div>

          <div className="card">
            <div className="card-title">Expense Breakdown by Category</div>
            <div className="donut-wrap" style={{ marginTop: 6 }}>
              <div className="donut">
                <svg viewBox="0 0 120 120">
                  <circle cx="60" cy="60" r="54" fill="#f1f5f9" />
                  {categories.map((c, i) => {
                    const offset = categories.slice(0, i).reduce((s, x) => s + x.value, 0) / 100 * 339.292;
                    return (
                      <circle
                        key={c.name}
                        cx="60"
                        cy="60"
                        r="54"
                        fill="transparent"
                        stroke={c.color}
                        strokeWidth="18"
                        strokeDasharray={`${donutCirc(c.value)} 339.292`}
                        strokeDashoffset={-offset}
                        strokeLinecap="butt"
                        transform="rotate(-90 60 60)"
                      />
                    );
                  })}
                </svg>
              </div>
              <div className="legend">
                {categories.map((c) => (
                  <div className="legend-item" key={c.name}>
                    <div className="legend-dot" style={{ background: c.color }} />
                    <div>
                      <div style={{ fontSize: 14 }}>{c.name}</div>
                      <div className="small">{c.value}%</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="card">
            <div className="card-title">Expense Classification Tool</div>
            <div className="classification">
              <input placeholder="Enter expense details..." />
              <button>Classify Expense</button>
            </div>
            <div className="small" style={{ marginTop: 10 }}>Predicted Category: Food & Dining (Confidence: 92%)</div>
          </div>
        </div>

        <div className="column">
          <div className="card">
            <div className="card-title" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              Recent Transactions
              <button 
                className="auth-button" 
                style={{ fontSize: '12px', padding: '6px 10px' }}
                onClick={() => setShowUploader(!showUploader)}
              >
                Upload Statement
              </button>
            </div>
            {showUploader && (
              <div style={{ marginBottom: 12 }}>
                <StatementUploader token={localStorage.getItem('token') || ''} />
              </div>
            )}
            {loading ? (
              <p style={{ textAlign: 'center', color: '#6b7280' }}>Loading transactions...</p>
            ) : (
            <ul className="transactions" style={{ listStyle: 'none', padding: 0, marginTop: 12 }}>
              {transactions.map((t) => (
                <li key={t.id || Math.random()}>
                  <div className="tx-left">
                    <div className="tx-dot">{(t.description || 'T').charAt(0)}</div>
                    <div>
                      <div className="tx-desc">{t.description || 'Unknown Transaction'} <span style={{ color: '#94a3b8', fontSize: 12 }}>({t.amount && t.amount < 0 ? `$${Math.abs(t.amount)}` : `$${t.amount || 0}`})</span></div>
                      <div className="tx-cat">{t.cat || 'Uncategorized'}</div>
                    </div>
                  </div>
                  <div style={{ color: (t.amount && t.amount < 0) ? '#ef4444' : '#10b981' }}>{t.amount && t.amount < 0 ? `-$${Math.abs(t.amount)}` : `$${t.amount || 0}`}</div>
                </li>
              ))}
            </ul>
            )}
          </div>

          <div className="card">
            <div className="card-title">Spending Trends</div>
            <div className="chart" style={{ marginTop: 10 }}>
              <svg viewBox="0 0 300 100" preserveAspectRatio="none" style={{ width: '100%', height: 120 }}>
                <polyline
                  fill="none"
                  stroke="#60a5fa"
                  strokeWidth={3}
                  points={trendPoints.map((p, i) => `${(i / (trendPoints.length - 1)) * 300},${100 - (p / 40) * 100}`).join(' ')}
                />
                <polyline
                  fill="none"
                  stroke="#10b981"
                  strokeWidth={2}
                  points={trendPoints.map((p, i) => `${(i / (trendPoints.length - 1)) * 300},${100 - (p / 40) * 100 + 12}`).join(' ')}
                />
              </svg>
              <div className="trend-legend">
                <span><i style={{ width: 10, height: 6, background: '#60a5fa', display: 'inline-block', borderRadius: 2 }} /> Expenses</span>
                <span><i style={{ width: 10, height: 6, background: '#10b981', display: 'inline-block', borderRadius: 2 }} /> Income</span>
              </div>
            </div>
          </div>

        </div>
      </div>

      {budgetModalOpen && (
        <div className="dashboard-modal-overlay" role="dialog" aria-modal="true">
          <div className="dashboard-modal">
            <h3>Update Monthly Budget</h3>
            <form
              onSubmit={async (event) => {
                event.preventDefault();
                const token = localStorage.getItem("token") || "";
                if (!token) {
                  setBudgetError("You must be logged in to update the budget.");
                  return;
                }

                const nextBudget = Number(budgetInput);
                if (!Number.isFinite(nextBudget) || nextBudget < 0) {
                  setBudgetError("Enter a valid non-negative number.");
                  return;
                }

                setBudgetSaving(true);
                setBudgetError("");

                try {
                  const summary = await updateBudget(token, nextBudget);
                  const updatedBudget = typeof summary.budget === "number" ? summary.budget : nextBudget;
                  const updatedRemaining = typeof summary.remaining === "number"
                    ? summary.remaining
                    : typeof summary.spent === "number"
                      ? updatedBudget - summary.spent
                      : updatedBudget;

                  setBudget(updatedBudget);
                  setSpent(summary.spent ?? spent);
                  setRemaining(updatedRemaining);
                  setBudgetModalOpen(false);
                } catch (err) {
                  const message = err instanceof Error && err.message ? err.message : "Unable to update budget.";
                  setBudgetError(message);
                } finally {
                  setBudgetSaving(false);
                }
              }}
              className="dashboard-modal-form"
            >
              <label>
                <span>Monthly Budget</span>
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  value={budgetInput}
                  onChange={(event) => setBudgetInput(event.target.value)}
                  disabled={budgetSaving}
                  autoFocus
                  required
                />
              </label>

              {budgetError && <p className="modal-error">{budgetError}</p>}

              <div className="dashboard-modal-actions">
                <button
                  type="button"
                  className="secondary-btn"
                  onClick={() => setBudgetModalOpen(false)}
                  disabled={budgetSaving}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="primary-btn"
                  disabled={budgetSaving}
                >
                  {budgetSaving ? "Saving..." : "Save Budget"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
