import { useState } from "react";
import { api } from "../services/finance";
import type { MonthlySummary } from "../services/finance";

type Preferences = MonthlySummary["warningSettings"];
export default function BudgetWarningSettings({ initial, onSaved }: { initial: Preferences; onSaved: (value: Preferences) => void | Promise<void> }) {
  const [enabled, setEnabled] = useState(initial.enabled);
  const [threshold, setThreshold] = useState(String(initial.thresholdPercent));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  return <form className="card budget-form" onSubmit={async event => {
    event.preventDefault(); setBusy(true); setError(""); setStatus("");
    try {
      const saved = await api<Preferences>("budget/warning-settings", { method: "PUT", body: JSON.stringify({ enabled, thresholdPercent: Number(threshold) }) });
      await onSaved(saved); setStatus("Budget warning settings saved.");
    } catch (failure) { setError((failure as Error).message); }
    finally { setBusy(false); }
  }}>
    <h2>Warning preferences</h2>
    <fieldset className="planning-fields" disabled={busy}>
      <label className="planning-checkbox"><input type="checkbox" checked={enabled} onChange={event => setEnabled(event.target.checked)} />Enable budget warnings</label>
      <label>Early warning at (%)<input type="number" min="1" max="99" step="1" required value={threshold} onChange={event => setThreshold(event.target.value)} /></label>
      <p className="small">Applies to your overall and category budgets for every month. Enabled warnings also appear when spending reaches or exceeds 100%. Warnings use recorded transactions.</p>
      <button className="primary-btn">{busy ? "Saving…" : "Save warning preferences"}</button>
    </fieldset>
    {error && <p role="alert" className="dashboard-error">{error}</p>}
    {status && <p role="status">{status}</p>}
  </form>;
}
