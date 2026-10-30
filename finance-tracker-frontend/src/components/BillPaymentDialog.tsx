import { useEffect, useState } from "react";
import Dialog from "./Dialog";
import { currency, today } from "../services/finance";
import { payBill, paymentCandidates } from "../services/recurring";
import type { BillOccurrence } from "../services/recurring";
import type { Transaction } from "../services/transaction";

export default function BillPaymentDialog({ occurrence, onClose, onSaved }: { occurrence: BillOccurrence; onClose: () => void; onSaved: () => void }) {
  const [date, setDate] = useState(today);
  const [amount, setAmount] = useState(String(occurrence.amount));
  const [mode, setMode] = useState("link");
  const [candidates, setCandidates] = useState<Transaction[]>([]);
  const [selected, setSelected] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [separate, setSeparate] = useState(false);
  const [error, setError] = useState("");
  const [lookupError, setLookupError] = useState("");
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      setLoading(true); setSeparate(false); setLookupError("");
      if (!date || !(Number(amount) > 0)) { setCandidates([]); setLoading(false); return; }
      try {
        const rows = await paymentCandidates(occurrence.billId, date, Number(amount), controller.signal);
        if (!controller.signal.aborted) {
          setCandidates(rows); setSelected(rows.length ? String(rows[0].id) : "");
          setMode(rows.length ? "link" : "record");
        }
      } catch (failure) { if (!controller.signal.aborted) setLookupError((failure as Error).message); }
      finally { if (!controller.signal.aborted) setLoading(false); }
    }
    void load(); return () => controller.abort();
  }, [occurrence.billId, date, amount, retry]);

  return <Dialog title={`Payment for ${occurrence.name}`} onClose={() => { if (!busy) onClose(); }}>
    <p>Scheduled for {occurrence.dueDate.slice(0, 10)} · {currency(occurrence.amount)}. Link an imported or manually entered expense when it already exists.</p>
    <form className="budget-form" onSubmit={async event => {
      event.preventDefault(); setBusy(true); setError("");
      try {
        await payBill(occurrence.billId, mode === "link" ? { dueDate: occurrence.dueDate.slice(0, 10), transactionId: Number(selected) } :
          { dueDate: occurrence.dueDate.slice(0, 10), paymentDate: date, amount: Number(amount), confirmSeparateExpense: separate });
        onSaved();
      } catch (failure) { setError((failure as Error).message); }
      finally { setBusy(false); }
    }}>
      <fieldset className="planning-fields" disabled={busy}>
        <div className="filter-row">
          <label>Payment date<input type="date" required value={date} onChange={event => setDate(event.target.value)} /></label>
          <label>Payment amount (ZAR)<input type="number" required min="0.01" step="0.01" value={amount} onChange={event => setAmount(event.target.value)} /></label>
        </div>
        {loading && <p role="status">Checking existing expenses…</p>}
        {lookupError && <p className="dashboard-error" role="alert">{lookupError} <button type="button" onClick={() => setRetry(value => value + 1)}>Retry lookup</button></p>}
        <label>Payment action<select aria-label="Payment action" value={mode} onChange={event => setMode(event.target.value)}>
          <option value="link" disabled={!candidates.length}>Link an existing expense</option><option value="record">Record a new expense</option>
        </select></label>
        {candidates.length > 0 && <p className="notice">{candidates.length} possible existing payment(s) found within seven days. Linking counts the expense only once.</p>}
        {mode === "link" && <label>Existing expense<select aria-label="Existing expense" required value={selected} onChange={event => setSelected(event.target.value)}>
          <option value="">Choose an expense</option>{candidates.map(row => <option key={row.id} value={row.id}>{row.date.slice(0, 10)} · {row.description} · {currency(Math.abs(row.amount))}</option>)}
        </select></label>}
        {mode === "record" && <>
          <p>This adds one expense to Transactions. The schedule alone does not change your spending.</p>
          {candidates.length > 0 && <label className="planning-checkbox"><input type="checkbox" required checked={separate} onChange={event => setSeparate(event.target.checked)} />This is a separate expense from the possible matches.</label>}
        </>}
        <button className="primary-btn" disabled={loading || !!lookupError || (mode === "link" && !selected)}>{busy ? "Saving…" : mode === "link" ? "Link payment" : "Record payment"}</button>
      </fieldset>
      {error && <p className="dashboard-error" role="alert">{error}</p>}
    </form>
  </Dialog>;
}
