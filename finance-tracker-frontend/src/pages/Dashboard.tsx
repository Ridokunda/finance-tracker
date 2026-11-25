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
import NetCashFlowCard from "../components/NetCashFlowCard";
import BudgetWarnings from "../components/BudgetWarnings";
import UpcomingBillsCard from "../components/UpcomingBillsCard";
import CategoryBreakdownCard from "../components/CategoryBreakdownCard";
import SpendingTrendsCard from "../components/SpendingTrendsCard";
import TopMerchantsCard from "../components/TopMerchantsCard";
import SavingsRateCard from "../components/SavingsRateCard";
import "./Planning.css";
import "../components/Insights.css";

const EMPTY_BUDGET: BudgetSummary = {
  budget: 0,
  spent: 0,
  remaining: 0,
  status: "ok"
};

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
  const [cashFlowRevision, setCashFlowRevision] = useState(0);
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

  const recentTransactions = transactions.slice(0, 5);
  const momSpending = monthly?.comparisons?.monthOverMonth;
  const yoySpending = monthly?.comparisons?.yearOverYear;
  const derivedSavingsRate =
    monthly?.savingsRate !== undefined
      ? monthly.savingsRate
      : monthly && monthly.income > 0
        ? Math.round(((monthly.income - monthly.spent) / monthly.income) * 1000) / 10
        : null;

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
          {!loading && monthly && <BudgetWarnings summary={monthly} month={month} />}
          <UpcomingBillsCard key={month} month={month} refreshKey={cashFlowRevision} />
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
              {!loading && (momSpending || yoySpending) && (
                <div className="spending-deltas">
                  {momSpending && (momSpending.previous > 0 || budgetSummary.spent > 0) && (
                    <span
                      className={`delta-pill ${
                        momSpending.change > 0
                          ? "delta-up"
                          : momSpending.change < 0
                            ? "delta-down"
                            : "delta-neutral"
                      }`}
                    >
                      {momSpending.previous === 0 && momSpending.change > 0
                        ? "New vs last mo"
                        : momSpending.change === 0
                          ? "0% vs last mo"
                          : `${momSpending.change > 0 ? "▲ +" : "▼ "}${momSpending.changePercent?.toFixed(1) ?? "0.0"}% vs last mo`}
                    </span>
                  )}
                  {yoySpending && yoySpending.previous > 0 && yoySpending.changePercent !== null && (
                    <span
                      className={`delta-pill ${
                        yoySpending.change > 0
                          ? "delta-up"
                          : yoySpending.change < 0
                            ? "delta-down"
                            : "delta-neutral"
                      }`}
                    >
                      {yoySpending.change === 0
                        ? "0% YoY"
                        : `${yoySpending.change > 0 ? "▲ +" : "▼ "}${yoySpending.changePercent.toFixed(1)}% YoY`}
                    </span>
                  )}
                </div>
              )}
            </section>

            <SavingsRateCard
              income={monthly?.income ?? 0}
              netCashFlow={monthly?.netCashFlow ?? 0}
              savingsRate={derivedSavingsRate}
              previousMonthSavingsRate={monthly?.previousMonthSavingsRate ?? null}
              loading={loading}
            />
          </div>

          <CategoryBreakdownCard
            month={month}
            categories={monthly?.categories ?? []}
            totalSpent={monthly?.spent ?? 0}
            loading={loading}
          />

          <NetCashFlowCard refreshKey={cashFlowRevision} />
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
                <StatementUploader token={readToken()} onUploaded={() => {
                  setCashFlowRevision(revision => revision + 1);
                  return loadDashboard();
                }} />
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

          <SpendingTrendsCard
            trends={monthly?.trends ?? []}
            selectedMonth={month}
            loading={loading}
          />

          <TopMerchantsCard
            month={month}
            merchants={monthly?.topMerchants ?? []}
            loading={loading}
          />

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
