import { useEffect, useState } from "react";
import CategorySelect from "../components/CategorySelect";
import Navigation from "../components/Navigation";
import { deleteRule, getRules, saveRule } from "../services/rules";
import type { CategoryRule, RuleInput } from "../services/rules";
import "./Dashboard.css";
import "./TransactionsPage.css";

const emptyRule: RuleInput = { pattern: "", category: "Groceries", enabled: true };

export default function CategoryRules() {
  const [rules, setRules] = useState<CategoryRule[]>([]);
  const [form, setForm] = useState<RuleInput>(emptyRule);
  const [editingId, setEditingId] = useState<number>();
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [refresh, setRefresh] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    getRules(controller.signal).then(setRules).catch(failure => {
      if (!controller.signal.aborted) setError((failure as Error).message);
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [refresh]);

  function reset() { setEditingId(undefined); setForm(emptyRule); }
  async function change(action: () => Promise<unknown>, message: string) {
    setBusy(true); setError(""); setStatus("");
    try { await action(); setStatus(message); }
    catch (failure) { setError((failure as Error).message); }
    finally { setBusy(false); }
  }

  return <div className="dashboard-wrap"><Navigation />
    <main className="page-content">
      <h1>Category rules</h1>
      <p>Automatically categorize descriptions containing a phrase, such as Checkers → Groceries. Matching ignores letter case and repeated spaces. The longest matching phrase wins; ties use the oldest rule. Your rules take priority over model suggestions and work when the classifier is unavailable.</p>
      {error && <p role="alert" className="dashboard-error">{error} <button disabled={busy} onClick={() => { setError(""); setRefresh(value => value + 1); }}>Reload rules</button></p>}
      {status && <p role="status" className="success-notice">{status}</p>}
      <form className="card budget-form" onSubmit={event => {
        event.preventDefault();
        void change(async () => {
          const saved = await saveRule(form, editingId);
          setRules(previous => [...previous.filter(rule => rule.id !== saved.id), saved]);
          reset();
        }, editingId ? "Category rule updated." : "Category rule saved.");
      }}>
        <h2>{editingId ? "Edit category rule" : "Add category rule"}</h2>
        <fieldset className="transaction-fields" disabled={busy || loading}>
          <div className="filter-row">
            <label>Description contains<input required maxLength={200} value={form.pattern} placeholder="e.g. Checkers" onChange={event => setForm({ ...form, pattern: event.target.value })} /></label>
            <CategorySelect value={form.category} onChange={category => setForm({ ...form, category })} />
          </div>
          <label className="checkbox-label"><input type="checkbox" checked={form.enabled} onChange={event => setForm({ ...form, enabled: event.target.checked })} />Rule enabled</label>
          <div className="transaction-actions">
            <button className="primary-btn" disabled={!form.pattern.trim()}>{editingId ? "Save rule changes" : "Save category rule"}</button>
            {editingId && <button className="secondary-btn" type="button" onClick={reset}>Cancel edit</button>}
          </div>
        </fieldset>
        <p className="small">Saving a rule applies it to future suggestions and statement previews. To update existing transactions, select them in Transactions and apply a category.</p>
      </form>
      <section className="card">
        <h2>Saved rules</h2>
        {loading ? <p role="status">Loading category rules…</p> : rules.length === 0 ? <p>No rules yet. Add a phrase above or save a rule while entering a transaction.</p> :
          <ul className="category-rule-list">{[...rules].sort((a, b) => b.pattern.length - a.pattern.length || a.id - b.id).map(rule => <li key={rule.id}>
            <div><strong>{rule.pattern}</strong><p>{rule.category} · {rule.enabled ? "Enabled" : "Disabled"}</p></div>
            <div className="transaction-actions">
              <button className="secondary-btn" disabled={busy} onClick={() => { setEditingId(rule.id); setForm({ pattern: rule.pattern, category: rule.category, enabled: rule.enabled }); setStatus(""); }}>Edit</button>
              <button className="secondary-btn" disabled={busy} onClick={() => void change(async () => {
                const saved = await saveRule({ ...rule, enabled: !rule.enabled }, rule.id);
                setRules(previous => previous.map(item => item.id === saved.id ? saved : item));
                if (editingId === rule.id) setForm(previous => ({ ...previous, enabled: saved.enabled }));
              }, rule.enabled ? "Category rule disabled." : "Category rule enabled.")}>{rule.enabled ? "Disable" : "Enable"}</button>
              <button className="danger-btn" disabled={busy} onClick={() => void change(async () => {
                await deleteRule(rule.id); setRules(previous => previous.filter(item => item.id !== rule.id));
                if (editingId === rule.id) reset();
              }, "Category rule deleted.")}>Delete</button>
            </div>
          </li>)}</ul>}
      </section>
    </main>
  </div>;
}
