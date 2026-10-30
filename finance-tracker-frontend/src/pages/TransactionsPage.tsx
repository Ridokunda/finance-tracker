import { useCallback, useEffect, useState } from "react";
import { bulkCategory, classifyTransaction, createTransaction, deleteTransactions, getTransactions, restoreTransactions, updateTransaction } from "../services/transaction";
import type { Transaction } from "../services/transaction";
import { initialMonth, monthRange, today, token } from "../services/finance";
import { getRules, matchingRule, saveRule } from "../services/rules";
import type { CategoryRule } from "../services/rules";
import Navigation from "../components/Navigation";
import MonthSelector from "../components/MonthSelector";
import CategorySelect from "../components/CategorySelect";
import Dialog from "../components/Dialog";
import StatementUploader from "../components/StatementUploader";
import TransactionsList from "../components/TransactionsList";
import "./Dashboard.css";
import "./TransactionsPage.css";

type TransactionForm = { id?: number; date: string; description: string; amount: string; type: "income" | "expense"; category: string; useRule: boolean };

export default function TransactionsPage() {
  const [month, setMonth] = useState(initialMonth);
  const [range, setRange] = useState(() => monthRange(initialMonth()));
  const [category, setCategory] = useState(() => new URLSearchParams(location.search).get("category") || "");
  const [type, setType] = useState("");
  const [search, setSearch] = useState("");
  const [deleted, setDeleted] = useState(() => new URLSearchParams(location.search).get("view") === "deleted");
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [selected, setSelected] = useState<number[]>([]);
  const [bulkTarget, setBulkTarget] = useState("Uncategorized");
  const [undo, setUndo] = useState<number[]>([]);
  const [status, setStatus] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [showImport, setShowImport] = useState(false);
  const [edit, setEdit] = useState<TransactionForm | null>(null);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState("");
  const [suggestion, setSuggestion] = useState("");
  const [rules, setRules] = useState<CategoryRule[]>([]);
  const [rulesError, setRulesError] = useState("");
  const [rulePattern, setRulePattern] = useState("");
  const [ruleStatus, setRuleStatus] = useState("");
  const rule = edit?.useRule ? matchingRule(rules, edit.description) : undefined;
  const editCategory = rule?.category ?? edit?.category ?? "Uncategorized";

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError("");
    setSelected([]);
    try {
      const data = await getTransactions(token(), type, category, { ...range, search, deleted: String(deleted) }, signal);
      if (!signal?.aborted) setTransactions(data);
    } catch (failure) {
      if (!signal?.aborted) { setTransactions([]); setError((failure as Error).message); }
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [type, category, range, search, deleted]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load, refresh]);
  useEffect(() => {
    const controller = new AbortController();
    getRules(controller.signal).then(setRules).catch(() => {
      if (!controller.signal.aborted) setRulesError("Saved rules could not load. Use Suggest category or choose a category manually.");
    });
    return () => controller.abort();
  }, []);

  async function mutate(action: () => Promise<unknown>, message: string, after?: () => void) {
    setBusy(true); setError(""); setStatus("");
    try {
      await action();
      after?.();
      setStatus(message);
      setSelected([]);
      setRefresh(value => value + 1);
    } catch (failure) { setError((failure as Error).message); }
    finally { setBusy(false); }
  }
  function remove(ids: number[]) {
    void mutate(() => deleteTransactions(ids), `Deleted ${ids.length} transaction(s). You can restore them from Deleted.`, () => setUndo(ids));
  }
  function restore(ids: number[]) {
    void mutate(() => restoreTransactions(ids), `Restored ${ids.length} transaction(s).`, () => setUndo(previous => previous.filter(id => !ids.includes(id))));
  }
  function openForm(transaction?: Transaction) {
    setEdit(transaction ? { ...transaction, amount: String(Math.abs(transaction.amount)), type: transaction.amount > 0 ? "income" : "expense", useRule: false } :
      { date: today(), description: "", amount: "", type: "expense", category: "Uncategorized", useRule: true });
    setFormError(""); setSuggestion(""); setRulePattern(""); setRuleStatus("");
  }

  return (
    <div className="dashboard-wrap">
      <Navigation />
      <main className="page-content">
        <div className="recent-title"><h1>Transactions</h1>
          <MonthSelector month={month} disabled={busy} onChange={value => { setMonth(value); setRange(monthRange(value)); }} />
        </div>
        <div className="filter-row">
          <button className="primary-btn" disabled={busy} onClick={() => openForm()}>Add transaction</button>
          <button className="secondary-btn" disabled={busy} onClick={() => setShowImport(!showImport)}>{showImport ? "Hide import" : "Import statement"}</button>
          <a className="secondary-btn" href="/rules">Manage category rules</a>
        </div>
        {showImport && <section className="card"><StatementUploader token={token()} onUploaded={() => setRefresh(value => value + 1)} /></section>}
        <section className="card transaction-ledger">
          <fieldset className="filter-row transaction-filters" disabled={busy}>
            <label>Description search<input type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
            <label>From<input type="date" value={range.from} onChange={event => setRange({ ...range, from: event.target.value })} /></label>
            <label>To<input type="date" value={range.to} onChange={event => setRange({ ...range, to: event.target.value })} /></label>
            <CategorySelect value={category} onChange={setCategory} allowAll />
            <label>Type<select aria-label="Type" value={type} onChange={event => setType(event.target.value)}><option value="">All types</option><option value="income">Income</option><option value="expense">Expense</option></select></label>
            <label>View<select aria-label="View" value={deleted ? "deleted" : "active"} onChange={event => setDeleted(event.target.value === "deleted")}><option value="active">Active transactions</option><option value="deleted">Deleted transactions</option></select></label>
          </fieldset>
          {deleted && <p className="notice">Deleted transactions do not count toward budgets or analytics. Restore them here whenever needed. Date and category filters also apply to this view.</p>}
          {status && <p className="success-notice" role="status">{status}</p>}
          {undo.length > 0 && <div className="undo-notice"><span>Last deletion: {undo.length} transaction(s).</span>
            <button className="secondary-btn" disabled={busy} onClick={() => restore(undo)}>Undo deletion</button>
            <button className="secondary-btn" disabled={busy} onClick={() => setUndo([])}>Dismiss</button>
          </div>}
          {error && <p className="dashboard-error" role="alert">{error} <button disabled={busy} onClick={() => setRefresh(value => value + 1)}>Retry</button></p>}
          {selected.length > 0 && !loading && <div className="bulk-toolbar" aria-label="Selected transaction actions">
            <strong>{selected.length} selected</strong>
            {!deleted && <><CategorySelect label="Category for selected transactions" value={bulkTarget} onChange={setBulkTarget} />
              <button className="secondary-btn" disabled={busy} onClick={() => void mutate(() => bulkCategory(selected, bulkTarget), `Updated ${selected.length} transaction(s).`)}>Apply category</button>
              <button className="danger-btn" disabled={busy} onClick={() => remove(selected)}>Delete selected</button></>}
            {deleted && <button className="primary-btn" disabled={busy} onClick={() => restore(selected)}>Restore selected</button>}
            <button className="secondary-btn" disabled={busy} onClick={() => setSelected([])}>Clear selection</button>
          </div>}
          {transactions.length > 5000 && <p className="small">You can select up to 5,000 transactions at a time. Select all chooses the first 5,000 matching rows.</p>}
          {!loading && transactions.length > 0 && <p className="small mobile-table-hint">Swipe across the table to see categories, amounts and actions.</p>}
          {loading ? <p role="status">Loading transactions…</p> : <div className="table-scroll">
            <TransactionsList transactions={transactions} selected={selected} onSelectionChange={setSelected}
              onEdit={openForm} onDelete={remove} onRestore={restore} deleted={deleted} disabled={busy} />
          </div>}
        </section>
        {edit && <Dialog title={edit.id ? "Edit transaction" : "Add transaction"} onClose={() => { if (!busy) setEdit(null); }}>
          <form className="budget-form" onSubmit={async event => {
            event.preventDefault(); setBusy(true); setFormError("");
            const payload = { date: edit.date.slice(0, 10), description: edit.description, amount: Number(edit.amount), type: edit.type, category: editCategory };
            try {
              if (edit.id) await updateTransaction(token(), edit.id, payload);
              else await createTransaction(token(), payload);
              setEdit(null); setStatus("Transaction saved."); setRefresh(value => value + 1);
            } catch (failure) { setFormError((failure as Error).message); }
            finally { setBusy(false); }
          }}>
            <fieldset className="transaction-fields" disabled={busy}>
              <label>Date<input type="date" required value={edit.date.slice(0, 10)} onChange={event => setEdit({ ...edit, date: event.target.value })} /></label>
              <label>Description<input required maxLength={2000} value={edit.description} onChange={event => { setEdit({ ...edit, description: event.target.value }); setSuggestion(""); }} /></label>
              <div className="filter-row">
                <label>Transaction type<select aria-label="Transaction type" value={edit.type} onChange={event => {
                  const next = event.target.value as TransactionForm["type"];
                  setEdit({ ...edit, type: next, category: ["Income", "Uncategorized"].includes(edit.category) ? (next === "income" ? "Income" : "Uncategorized") : edit.category });
                }}><option value="expense">Expense (money out)</option><option value="income">Income (money in)</option></select></label>
                <label>Amount (ZAR)<input type="number" required min="0.01" step="0.01" value={edit.amount} onChange={event => setEdit({ ...edit, amount: event.target.value })} /></label>
              </div>
              <p className="small">Enter a positive amount. The transaction type determines whether money comes in or goes out.</p>
              <CategorySelect value={editCategory} onChange={value => { setEdit({ ...edit, category: value, useRule: false }); setSuggestion(""); }} />
              {rule && <p className="success-notice">Saved rule applied: description contains “{rule.pattern}”. You can choose a different category for this transaction.</p>}
              {rulesError && <p className="notice">{rulesError}</p>}
              <button className="secondary-btn" type="button" disabled={!edit.description.trim()} onClick={async () => {
                setBusy(true); setFormError("");
                try {
                  const result = await classifyTransaction(token(), edit.description);
                  setEdit({ ...edit, category: result.category, useRule: false });
                  setSuggestion(result.source === "rule" ? `Saved rule “${result.matchedPattern}” applied.` : "Suggested category applied. You can change it before saving.");
                } catch (failure) { setFormError((failure as Error).message); }
                finally { setBusy(false); }
              }}>Suggest category</button>
              {suggestion && <p role="status">{suggestion}</p>}
              <details className="save-rule">
                <summary>Always use this category for similar descriptions</summary>
                <label>Description contains<input maxLength={200} value={rulePattern} placeholder="e.g. Checkers" onChange={event => setRulePattern(event.target.value)} /></label>
                <p>Category: <strong>{editCategory}</strong>. This rule applies to future suggestions and statement previews.</p>
                <button className="secondary-btn" type="button" disabled={!rulePattern.trim()} onClick={async () => {
                  setBusy(true); setFormError(""); setRuleStatus("");
                  try {
                    const saved = await saveRule({ pattern: rulePattern, category: editCategory, enabled: true });
                    setRules(previous => [...previous.filter(item => item.id !== saved.id), saved]);
                    setRuleStatus("Category rule saved.");
                  } catch (failure) { setFormError((failure as Error).message); }
                  finally { setBusy(false); }
                }}>Save category rule</button>
                {ruleStatus && <p role="status">{ruleStatus}</p>}
              </details>
            </fieldset>
            {formError && <p role="alert" className="dashboard-error">{formError}</p>}
            <button className="primary-btn" disabled={busy || !(Number(edit.amount) > 0)}>{busy ? "Saving…" : "Save transaction"}</button>
          </form>
        </Dialog>}
      </main>
    </div>
  );
}
