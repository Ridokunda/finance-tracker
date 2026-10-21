import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import "./Dashboard.css";
import StatementUploader from "../components/StatementUploader";
import {
  classifyTransaction,
  getTransactions,
  type ClassificationResult,
  type Transaction
} from "../services/transaction";
import { setBudget as updateBudget, type BudgetSummary } from "../services/budget";
import { analytics, initialMonth, monthRange, currency } from "../services/finance";
import type { MonthlySummary } from "../services/finance";
import MonthSelector from "../components/MonthSelector";
import Navigation from "../components/Navigation";
import Dialog from "../components/Dialog";

const EMPTY_BUDGET: BudgetSummary = {
  budget: 0,
  spent: 0,
  remaining: 0,
  status: "ok"
};

const CATEGORY_COLORS = ["#34d399", "#60a5fa", "#7c3aed", "#f97316", "#eab308", "#94a3b8"];
function readToken(): string {
  try {
    return localStorage.getItem("token") || "";
  } catch {
    return "";
  }
}

const formatCurrency = currency;
function formatTransactionDate(transaction: Transaction): string { return transaction.date.slice(0, 10); }

export default function Dashboard() {
  const [month, setMonth] = useState(initialMonth);
  const loadRevision = useRef(0);
  const [monthly, setMonthly] = useState<MonthlySummary | null>(null);
  const [showUploader, setShowUploader] = useState(false);
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [budgetSummary, setBudgetSummary] = useState<BudgetSummary>(EMPTY_BUDGET);
  const [loading, setLoading] = useState(true);
  const [dataError, setDataError] = useState("");
  const [budgetModalOpen, setBudgetModalOpen] = useState(false);
  const [budgetInput, setBudgetInput] = useState("");
  const [budgetError, setBudgetError] = useState("");
  const [budgetSaving, setBudgetSaving] = useState(false);
  const [classificationInput, setClassificationInput] = useState("");
  const [classification, setClassification] = useState<ClassificationResult | null>(null);
  const [classificationError, setClassificationError] = useState("");
  const [classifying, setClassifying] = useState(false);

  const loadDashboard = useCallback(async () => {
    const revision = ++loadRevision.current;
    const token = readToken();
    if (!token) {
      setLoading(false);
      window.location.href = "/login";
      return;
    }

    setLoading(true);

    const [transactionsResult, budgetResult] = await Promise.allSettled([
      getTransactions(token, undefined, undefined, monthRange(month)),
      analytics(month)
    ]);
    if (revision !== loadRevision.current) return;
    const errors: string[] = [];

    if (transactionsResult.status === "fulfilled") {
      setTransactions(transactionsResult.value);
    } else {
      setTransactions([]);
      errors.push("Transactions could not be loaded.");
    }

    if (budgetResult.status === "fulfilled") {
      setMonthly(budgetResult.value);
      setBudgetSummary(budgetResult.value);
      setBudgetInput(budgetResult.value.budget.toFixed(2));
    } else {
      setMonthly(null);
      setBudgetSummary(EMPTY_BUDGET);
      errors.push("Budget data could not be loaded.");
    }

    setDataError(errors.join(" "));
    setLoading(false);
  }, [month]);

  useEffect(() => {
    void loadDashboard();
  }, [loadDashboard]);

  const currentMonthExpenseTotal = monthly?.spent ?? 0;
  const categoryBreakdown = (monthly?.categories ?? []).filter(c => c.spent > 0).map((c, index) => ({
    name: c.category, total: c.spent, percentage: currentMonthExpenseTotal ? c.spent / currentMonthExpenseTotal * 100 : 0,
    color: CATEGORY_COLORS[index % CATEGORY_COLORS.length]
  }));
  const trendData = (monthly?.trends ?? []).map(t => ({ key: t.month, label: t.month, expenses: t.expenses, income: t.income }));

  const recentTransactions = transactions.slice(0, 5);
  const trendMax = Math.max(
    1,
    ...trendData.flatMap((month) => [month.expenses, month.income])
  );
  const trendHasData = trendData.some((month) => month.expenses > 0 || month.income > 0);

  const trendPoints = (field: "expenses" | "income") => trendData
    .map((month, index) => {
      const x = 12 + (index / Math.max(1, trendData.length - 1)) * 296;
      const y = 108 - (month[field] / trendMax) * 92;
      return `${x},${y}`;
    })
    .join(" ");

  const handleClassify = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const description = classificationInput.trim();
    if (!description) {
      setClassification(null);
      setClassificationError("Enter an expense description first.");
      return;
    }

    const token = readToken();
    setClassifying(true);
    setClassificationError("");

    try {
      setClassification(await classifyTransaction(token, description));
    } catch (error) {
      setClassification(null);
      setClassificationError(error instanceof Error ? error.message : "Unable to classify this expense.");
    } finally {
      setClassifying(false);
    }
  };

  return (
    <div className="dashboard-wrap">
      <Navigation />
      <div className="page-heading"><h1>Your monthly overview</h1><MonthSelector month={month} onChange={setMonth} /></div>
      <main className="grid">
        <div className="column">
          <div className="row-cards">
            <section className="card h-lg">
              <div className="card-title">Monthly Budget</div>{monthly?.needsReconfirmation && <p className="notice">Reconfirm this limit: earlier imports may have changed it. <a href="/budgets">Review budget</a></p>}
              <div className="card-value">{loading ? "—" : (monthly?.hasBudget ? formatCurrency(budgetSummary.budget) : "No limit set")}</div>
              <div className="card-sub" style={{ color: budgetSummary.remaining < 0 ? "#ef4444" : "#10b981" }}>
                {loading ? "Loading budget..." : monthly?.hasBudget ? `Remaining: ${formatCurrency(budgetSummary.remaining)}` : "Set a limit to track remaining funds"}
              </div>
              <button
                className="card-action"
                type="button"
                onClick={() => {
                  setBudgetInput(budgetSummary.budget.toFixed(2));
                  setBudgetError("");
                  setBudgetModalOpen(true);
                }}
              >
                Update Budget
              </button>
            </section>

            <section className="card h-lg">
              <div className="card-title">Monthly Spending</div>
              <div className="card-value">{loading ? "—" : formatCurrency(budgetSummary.spent)}</div>
              <div className="card-sub" style={{ color: budgetSummary.status === "over" ? "#ef4444" : "#6b7280" }}>
                {loading ? "Loading spending..." : budgetSummary.status === "over" ? "Over budget" : "Tracked so far"}
              </div>
            </section>
          </div>

          <section className="card">
            <div className="card-title">Expense Breakdown by Category</div>
            {loading ? (
              <div className="empty-tile">Loading category data...</div>
            ) : categoryBreakdown.length === 0 ? (
              <div className="empty-tile">No expenses recorded this month.</div>
            ) : (
              <div className="donut-wrap">
                <div className="donut">
                  <svg viewBox="0 0 120 120" role="img" aria-label="Expense breakdown by category">
                    <circle cx="60" cy="60" r="54" fill="none" stroke="#e2e8f0" strokeWidth="18" />
                    {categoryBreakdown.map((category, index) => {
                      const offset = categoryBreakdown
                        .slice(0, index)
                        .reduce((sum, item) => sum + item.percentage, 0) / 100 * 339.292;
                      return (
                        <circle
                          key={category.name}
                          cx="60"
                          cy="60"
                          r="54"
                          fill="none"
                          stroke={category.color}
                          strokeWidth="18"
                          strokeDasharray={`${category.percentage / 100 * 339.292} 339.292`}
                          strokeDashoffset={-offset}
                          transform="rotate(-90 60 60)"
                        />
                      );
                    })}
                  </svg>
                  <div className="donut-center">
                    <strong>{formatCurrency(currentMonthExpenseTotal)}</strong>
                    <span>{month}</span>
                  </div>
                </div>
                <div className="legend">
                  {categoryBreakdown.map((category) => (
                    <div className="legend-item" key={category.name}>
                      <div className="legend-dot" style={{ background: category.color }} />
                      <div>
                        <a className="legend-name" href={`/transactions?month=${month}&category=${encodeURIComponent(category.name)}`}>{category.name}</a>
                        <div className="small">{category.percentage.toFixed(1)}% · {formatCurrency(category.total)}</div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </section>

          <section className="card"><div className="card-title">Net cash flow</div><div className="card-value">{loading ? "—" : formatCurrency(monthly?.netCashFlow ?? 0)}</div><p className="small">Income minus expenses for {month}</p></section>
          <section className="card">
            <div className="card-title">Expense Classification Tool</div>
            <form className="classification" onSubmit={handleClassify}>
              <input
                value={classificationInput}
                onChange={(event) => setClassificationInput(event.target.value)}
                placeholder="Enter expense details..."
                aria-label="Expense details"
              />
              <button type="submit" disabled={classifying || !classificationInput.trim()}>
                {classifying ? "Classifying..." : "Classify Expense"}
              </button>
            </form>
            {classification && (
              <div className="classification-result">
                <span>Predicted category</span>
                <strong>{classification.category}</strong>
              </div>
            )}
            {classificationError && <div className="classification-error">{classificationError}</div>}
            {!classification && !classificationError && (
              <div className="small classification-helper">Predictions come from your connected classification service.</div>
            )}
          </section>
        </div>

        <div className="column">
          <section className="card">
            <div className="card-title recent-title">
              <span>Recent Transactions</span>
              <button
                className="auth-button"
                type="button"
                onClick={() => setShowUploader((visible) => !visible)}
              >
                {showUploader ? "Close Upload" : "Upload Statement"}
              </button>
            </div>
            {showUploader && (
              <div className="uploader-wrap">
                <StatementUploader token={readToken()} onUploaded={loadDashboard} />
              </div>
            )}
            {loading ? (
              <p className="tile-message">Loading transactions...</p>
            ) : recentTransactions.length === 0 ? (
              <p className="tile-message">No transactions found. Add one from the Transactions page.</p>
            ) : (
              <ul className="transactions">
                {recentTransactions.map((transaction) => {
                  const isExpense = transaction.amount < 0;
                  const amount = Math.abs(transaction.amount);
                  return (
                    <li key={transaction.id}>
                      <div className="tx-left">
                        <div className="tx-dot">{(transaction.description || "T").charAt(0).toUpperCase()}</div>
                        <div>
                          <div className="tx-desc">{transaction.description || "Unknown transaction"}</div>
                          <div className="tx-cat">{transaction.category || "Uncategorized"} · {formatTransactionDate(transaction)}</div>
                        </div>
                      </div>
                      <div className={isExpense ? "amount-expense" : "amount-income"}>
                        {isExpense ? "-" : "+"}{formatCurrency(amount)}
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </section>

          <section className="card">
            <div className="card-title">Spending Trends <span className="small">· last 6 months</span></div>
            <div className="chart" aria-label="Spending trends for the last six months">
              {loading ? (
                <div className="empty-chart">Loading trend data...</div>
              ) : trendHasData ? (
                <svg viewBox="0 0 320 140" preserveAspectRatio="none" role="img">
                  <line x1="12" y1="108" x2="308" y2="108" stroke="#e2e8f0" strokeWidth="1" />
                  <line x1="12" y1="62" x2="308" y2="62" stroke="#f1f5f9" strokeWidth="1" />
                  <polyline fill="none" stroke="#60a5fa" strokeWidth="3" points={trendPoints("expenses")} />
                  <polyline fill="none" stroke="#10b981" strokeWidth="2.5" points={trendPoints("income")} />
                  {trendData.map((month, index) => {
                    const x = 12 + (index / Math.max(1, trendData.length - 1)) * 296;
                    const expenseY = 108 - (month.expenses / trendMax) * 92;
                    const incomeY = 108 - (month.income / trendMax) * 92;
                    return (
                      <g key={month.key}>
                        <circle cx={x} cy={expenseY} r="3" fill="#60a5fa" />
                        <circle cx={x} cy={incomeY} r="3" fill="#10b981" />
                      </g>
                    );
                  })}
                </svg>
              ) : (
                <div className="empty-chart">No income or expense history yet.</div>
              )}
              <div className="trend-labels">
                {trendData.map((month) => <span key={month.key}>{month.label}</span>)}
              </div>
              <div className="trend-legend">
                <span><i className="trend-line expense-line" /> Expenses</span>
                <span><i className="trend-line income-line" /> Income</span>
              </div>
            </div>
          </section>

          {dataError && <div className="dashboard-error" role="alert">{dataError} <button onClick={loadDashboard}>Retry</button></div>}
        </div>
      </main>

      {budgetModalOpen && (
        <Dialog title="Update Monthly Budget" onClose={() => { if (!budgetSaving) setBudgetModalOpen(false); }}>
          <div className="dashboard-modal">

            <form
              onSubmit={async (event) => {
                event.preventDefault();
                const token = readToken();
                const nextBudget = Number(budgetInput);
                if (!token) {
                  setBudgetError("You must be logged in to update the budget.");
                  return;
                }
                if (!Number.isFinite(nextBudget) || nextBudget < 0) {
                  setBudgetError("Enter a valid non-negative number.");
                  return;
                }

                setBudgetSaving(true);
                setBudgetError("");
                try {
                  setBudgetSummary(await updateBudget(token, nextBudget, month));
                  setBudgetModalOpen(false);
                  await loadDashboard();
                } catch (error) {
                  setBudgetError(error instanceof Error ? error.message : "Unable to update budget.");
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
                <button type="button" className="secondary-btn" onClick={() => setBudgetModalOpen(false)} disabled={budgetSaving}>
                  Cancel
                </button>
                <button type="submit" className="primary-btn" disabled={budgetSaving}>
                  {budgetSaving ? "Saving..." : "Save Budget"}
                </button>
              </div>
            </form>
          </div>
        </Dialog>
      )}
    </div>
  );
}
