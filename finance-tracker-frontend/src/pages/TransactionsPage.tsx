import { useCallback, useEffect, useState } from "react";
import {
  classifyTransaction,
  createTransaction,
  getTransactions,
  updateTransaction,
} from "../services/transaction";
import type { Transaction } from "../services/transaction";
import { initialMonth, monthRange, today, token } from "../services/finance";
import Navigation from "../components/Navigation";
import MonthSelector from "../components/MonthSelector";
import CategorySelect from "../components/CategorySelect";
import Dialog from "../components/Dialog";
import StatementUploader from "../components/StatementUploader";
import TransactionsList from "../components/TransactionsList";
import "./Dashboard.css";
export default function TransactionsPage() {
  const [month, setMonth] = useState(initialMonth);
  const [range, setRange] = useState(() => monthRange(initialMonth()));
  const [category, setCategory] = useState(
    () => new URLSearchParams(location.search).get("category") || "",
  );
  const [type, setType] = useState("");
  const [search, setSearch] = useState("");
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [showImport, setShowImport] = useState(false);
  const [edit, setEdit] = useState<Partial<Transaction> | null>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState("");
  const [suggestion, setSuggestion] = useState("");
  const load = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setError("");
      try {
        const data = await getTransactions(
          token(),
          type,
          category,
          { ...range, search },
          signal,
        );
        if (!signal?.aborted) setTransactions(data);
      } catch (error) {
        if (!signal?.aborted) setError((error as Error).message);
      } finally {
        if (!signal?.aborted) setLoading(false);
      }
    },
    [type, category, range, search],
  );
  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load, refresh]);
  return (
    <div className="dashboard-wrap">
      <Navigation />
      <main className="page-content">
        <div className="recent-title">
          <h1>Transactions</h1>
          <MonthSelector
            month={month}
            onChange={(value) => {
              setMonth(value);
              setRange(monthRange(value));
            }}
          />
        </div>
        <div className="filter-row">
          <button
            className="primary-btn"
            onClick={() => {
              setEdit({
                date: today(),
                description: "",
                amount: 0,
                category: "Uncategorized",
              });
              setFormError("");
              setSuggestion("");
            }}
          >
            Add transaction
          </button>
          <button
            className="secondary-btn"
            onClick={() => setShowImport(!showImport)}
          >
            {showImport ? "Hide import" : "Import statement"}
          </button>
        </div>
        {showImport && (
          <section className="card">
            <StatementUploader
              token={token()}
              onUploaded={() => setRefresh((r) => r + 1)}
            />
          </section>
        )}
        <section className="card">
          <div className="filter-row">
            <label>
              Description search
              <input
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </label>
            <label>
              From
              <input
                type="date"
                value={range.from}
                onChange={(e) => setRange({ ...range, from: e.target.value })}
              />
            </label>
            <label>
              To
              <input
                type="date"
                value={range.to}
                onChange={(e) => setRange({ ...range, to: e.target.value })}
              />
            </label>
            <CategorySelect value={category} onChange={setCategory} allowAll />
            <label>
              Type
              <select value={type} onChange={(e) => setType(e.target.value)}>
                <option value="">All types</option>
                <option value="income">Income</option>
                <option value="expense">Expense</option>
              </select>
            </label>
          </div>
          {error && (
            <p className="dashboard-error" role="alert">
              {error} <button onClick={() => void load()}>Retry</button>
            </p>
          )}
          {loading ? (
            <p role="status">Loading transactions…</p>
          ) : (
            <div className="table-scroll">
              <TransactionsList
                transactions={transactions}
                onEdit={(transaction) => {
                  setEdit(transaction);
                  setFormError("");
                  setSuggestion("");
                }}
              />
            </div>
          )}
        </section>
        {edit && (
          <Dialog
            title={edit.id ? "Edit transaction" : "Add transaction"}
            onClose={() => {
              if (!busy) setEdit(null);
            }}
          >
            <form
              className="budget-form"
              onSubmit={async (event) => {
                event.preventDefault();
                setBusy(true);
                setFormError("");
                const payload = {
                  date: edit.date!.slice(0, 10),
                  description: edit.description!,
                  amount: Number(edit.amount),
                  category: edit.category,
                };
                try {
                  if (edit.id)
                    await updateTransaction(token(), edit.id, payload);
                  else await createTransaction(token(), payload);
                  setEdit(null);
                  setRefresh((r) => r + 1);
                } catch (error) {
                  setFormError((error as Error).message);
                } finally {
                  setBusy(false);
                }
              }}
            >
              <label>
                Date
                <input
                  type="date"
                  required
                  value={edit.date?.slice(0, 10)}
                  onChange={(e) => setEdit({ ...edit, date: e.target.value })}
                />
              </label>
              <label>
                Description
                <input
                  required
                  maxLength={2000}
                  value={edit.description}
                  onChange={(e) =>
                    setEdit({ ...edit, description: e.target.value })
                  }
                />
              </label>
              <label>
                Signed amount (ZAR)
                <input
                  type="number"
                  required
                  step="0.01"
                  value={edit.amount}
                  onChange={(e) =>
                    setEdit({ ...edit, amount: Number(e.target.value) })
                  }
                />
              </label>
              <p>
                Negative amounts are expenses; positive amounts are inflows.
              </p>
              <CategorySelect
                value={edit.category || "Uncategorized"}
                onChange={(category) => setEdit({ ...edit, category })}
              />
              <button
                className="secondary-btn"
                type="button"
                disabled={busy || !edit.description?.trim()}
                onClick={async () => {
                  setBusy(true);
                  setFormError("");
                  try {
                    const result = await classifyTransaction(
                      token(),
                      edit.description!,
                    );
                    setSuggestion(result.category);
                  } catch (error) {
                    setFormError((error as Error).message);
                  } finally {
                    setBusy(false);
                  }
                }}
              >
                Suggest category
              </button>
              {suggestion && (
                <p>
                  Suggested: {suggestion}{" "}
                  <button
                    type="button"
                    onClick={() => setEdit({ ...edit, category: suggestion })}
                  >
                    Use suggestion
                  </button>
                </p>
              )}
              {formError && (
                <p role="alert" className="dashboard-error">
                  {formError}
                </p>
              )}
              <button className="primary-btn" disabled={busy || !edit.amount}>
                {busy ? "Saving…" : "Save transaction"}
              </button>
            </form>
          </Dialog>
        )}
      </main>
    </div>
  );
}
