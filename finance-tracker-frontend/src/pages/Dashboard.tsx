import React, { useState } from "react";
import './Dashboard.css';

const transactions = [
  { id: 1, title: "Starbucks - Coffee", cat: "Food", amount: -5.5 },
  { id: 2, title: "Magur Transit - Bus Fare", cat: "Transport", amount: -2.75 },
  { id: 3, title: "Supermarket - Groceries", cat: "Food", amount: -75.2 },
  { id: 4, title: "Salary", cat: "Income", amount: 2500 },
];

export default function Dashboard() {
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(() => {
    try { return Boolean(localStorage.getItem('token')); } catch { return false; }
  });

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
          <div className="logo" />
          <h2 style={{ margin: 0 }}>FINANCE-TRACKER</h2>
        </div>
        <nav className="nav">
          <a className="active">Dashboard</a>
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
              <div className="card-title">Current Balance</div>
              <div className="card-value">$15,450.75</div>
              <div className="card-sub" style={{ color: '#10b981' }}>+62,100.50 this month</div>
            </div>
            <div className="card h-lg">
              <div className="card-title">Monthly Spending</div>
              <div className="card-value">$3,210.10</div>
              <div className="card-sub" style={{ color: '#ef4444' }}>-450.20 compared to last month</div>
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
            <div className="card-title">Recent Transactions</div>
            <ul className="transactions" style={{ listStyle: 'none', padding: 0, marginTop: 12 }}>
              {transactions.map((t) => (
                <li key={t.id}>
                  <div className="tx-left">
                    <div className="tx-dot">{t.title.charAt(0)}</div>
                    <div>
                      <div className="tx-desc">{t.title} <span style={{ color: '#94a3b8', fontSize: 12 }}>({t.amount < 0 ? `$${Math.abs(t.amount)}` : `$${t.amount}`})</span></div>
                      <div className="tx-cat">{t.cat}</div>
                    </div>
                  </div>
                  <div style={{ color: t.amount < 0 ? '#ef4444' : '#10b981' }}>{t.amount < 0 ? `-$${Math.abs(t.amount)}` : `$${t.amount}`}</div>
                </li>
              ))}
            </ul>
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
    </div>
  );
}
