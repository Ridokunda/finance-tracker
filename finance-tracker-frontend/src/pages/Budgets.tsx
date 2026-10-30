import { useCallback, useEffect, useRef, useState } from "react";
import { analytics, api, currency, initialMonth } from "../services/finance";
import type { MonthlySummary } from "../services/finance";
import Navigation from "../components/Navigation";
import MonthSelector from "../components/MonthSelector";
import CategorySelect from "../components/CategorySelect";
import BudgetWarnings from "../components/BudgetWarnings";
import BudgetWarningSettings from "../components/BudgetWarningSettings";
import "../pages/Dashboard.css";
import "./Planning.css";
export default function Budgets() {
  const [month, setMonth] = useState(initialMonth);
  const loadRevision = useRef(0);
  const [summary, setSummary] = useState<MonthlySummary | null>(null);
  const [overall, setOverall] = useState("");
  const [limits, setLimits] = useState<{ category: string; amount: string }[]>(
    [],
  );
  const [category, setCategory] = useState("Groceries");
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [busy, setBusy] = useState(false);
  const load = useCallback(async () => {
    const revision = ++loadRevision.current;
    setBusy(true);
    setError("");
    setStatus("");
    setSummary(null);
    try {
      const data = await analytics(month);
      if (revision !== loadRevision.current) return;
      setSummary(data);
      setOverall(data.hasBudget ? String(data.budget) : "");
      setLimits(
        data.categories
          .filter((c) => c.limit !== null)
          .map((c) => ({ category: c.category, amount: String(c.limit) })),
      );
    } catch (error) {
      if (revision === loadRevision.current) setError((error as Error).message);
    } finally {
      if (revision === loadRevision.current) setBusy(false);
    }
  }, [month]);
  useEffect(() => {
    void load();
  }, [load]);
  const allocated =
    limits.reduce(
      (sum, c) => sum + Math.round(Number(c.amount || 0) * 100),
      0,
    ) / 100;
  return (
    <div className="dashboard-wrap">
      <Navigation />
      <main className="page-content">
        <div className="recent-title">
          <h1>Monthly budgets</h1>
          <MonthSelector month={month} onChange={setMonth} disabled={busy} />
        </div>
        <p>
          Allocate category limits within your overall monthly limit. Unused
          funds do not roll over.
        </p>
        {error && (
          <p role="alert" className="dashboard-error">
            {error} <button onClick={load}>Reload</button>
          </p>
        )}
        {busy && <p role="status">Loading or saving budget…</p>}
        {summary && (
          <>
            <BudgetWarnings summary={summary} month={month} showEmpty />
            <BudgetWarningSettings key={month} initial={summary.warningSettings} onSaved={async () => {
              const revision = loadRevision.current;
              const data = await analytics(month);
              if (revision === loadRevision.current) setSummary(data);
            }} />
            <form
              className="card budget-form"
              onSubmit={async (event) => {
                event.preventDefault();
                setBusy(true);
                setError("");
                setStatus("");
                const [year, number] = month.split("-").map(Number);
                try {
                  const data = await api<MonthlySummary>("budget/set", {
                    method: "POST",
                    body: JSON.stringify({
                      year,
                      month: number,
                      limitAmount: Number(overall),
                      categoryLimits: limits.map((c) => ({
                        category: c.category,
                        limitAmount: Number(c.amount),
                      })),
                    }),
                  });
                  setSummary(data);
                  setStatus("Budget saved.");
                } catch (error) {
                  setError((error as Error).message);
                } finally {
                  setBusy(false);
                }
              }}
            >
              {summary.needsReconfirmation && (
                <p role="alert" className="notice">
                  Please reconfirm this limit. Earlier imports may have changed
                  it; the original value cannot be reconstructed. Review the
                  amount and save.
                </p>
              )}
              <label>
                Overall monthly limit (ZAR)
                <input
                  type="number"
                  min="0"
                  step="0.01"
                  required
                  value={overall}
                  onChange={(e) => setOverall(e.target.value)}
                />
              </label>
              <p>
                Allocated: {currency(allocated)} · Unallocated:{" "}
                {currency(Number(overall || 0) - allocated)}
              </p>
              {limits.map((limit, index) => (
                <div className="budget-allocation" key={limit.category}>
                  <label>
                    {limit.category} limit (ZAR)
                    <input
                      type="number"
                      min="0"
                      step="0.01"
                      required
                      value={limit.amount}
                      onChange={(e) =>
                        setLimits(
                          limits.map((c, i) =>
                            i === index ? { ...c, amount: e.target.value } : c,
                          ),
                        )
                      }
                    />
                  </label>
                  <button
                    type="button"
                    className="secondary-btn"
                    onClick={() =>
                      setLimits(limits.filter((_, i) => i !== index))
                    }
                  >
                    Remove {limit.category}
                  </button>
                </div>
              ))}
              <div className="filter-row">
                <CategorySelect
                  value={category}
                  onChange={setCategory}
                  label="Allocate category"
                />
                <button
                  type="button"
                  className="secondary-btn"
                  disabled={limits.some(
                    (c) => c.category.toLowerCase() === category.toLowerCase(),
                  )}
                  onClick={() =>
                    setLimits([...limits, { category, amount: "" }])
                  }
                >
                  Add category limit
                </button>
              </div>
              {allocated > Number(overall || 0) && (
                <p role="alert">
                  Category limits exceed the overall monthly limit.
                </p>
              )}
              <div className="filter-row">
                <button
                  className="primary-btn"
                  disabled={busy || allocated > Number(overall || 0)}
                >
                  Save budget
                </button>
                <button
                  type="button"
                  className="secondary-btn"
                  disabled={busy}
                  onClick={async () => {
                    setBusy(true);
                    setError("");
                    const [year, number] = month.split("-").map(Number);
                    try {
                      await api("budget/copy-previous", {
                        method: "POST",
                        body: JSON.stringify({ year, month: number }),
                      });
                      await load();
                      setStatus("Previous month’s limits copied.");
                    } catch (error) {
                      setError((error as Error).message);
                    } finally {
                      setBusy(false);
                    }
                  }}
                >
                  Replace with previous month’s limits
                </button>
              </div>
              <p role="status">{status}</p>
            </form>
            <section className="card">
              <h2>Spending against limits</h2>
              <p>
                Overall: {currency(summary.spent)} spent ·{" "}
                {summary.hasBudget
                  ? `${currency(summary.remaining)} remaining${summary.remaining < 0 ? " · Over budget" : ""}`
                  : "No limit set"}
              </p>
              {summary.categories.length === 0 && (
                <p>No category spending or limits for this month.</p>
              )}
              {summary.categories.map((c) => (
                <article className="category-progress" key={c.category}>
                  <a
                    href={`/transactions?month=${month}&category=${encodeURIComponent(c.category)}`}
                  >
                    {c.category}
                  </a>
                  <p>
                    {currency(c.spent)} spent ·{" "}
                    {c.limit === null
                      ? "No limit set"
                      : `${currency(c.limit)} limit · ${currency(c.remaining!)} remaining`}
                  </p>
                  <strong
                    className={
                      c.status === "Over budget" ? "amount-expense" : ""
                    }
                  >
                    {c.status}
                  </strong>
                  {c.limit !== null && (
                    <progress
                      aria-label={`${c.category} budget used`}
                      max={Math.max(c.limit, 1)}
                      value={Math.min(c.spent, Math.max(c.limit, 1))}
                    />
                  )}
                </article>
              ))}
            </section>
          </>
        )}
      </main>
    </div>
  );
}
