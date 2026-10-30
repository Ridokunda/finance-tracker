import { useEffect, useState } from "react";
import Navigation from "../components/Navigation";
import MonthSelector from "../components/MonthSelector";
import CategorySelect from "../components/CategorySelect";
import Dialog from "../components/Dialog";
import BillPaymentDialog from "../components/BillPaymentDialog";
import { currency, initialMonth, today } from "../services/finance";
import { archiveBill, getBills, getOccurrences, saveBill, unlinkPayment } from "../services/recurring";
import type { Bill, BillInput, BillOccurrence } from "../services/recurring";
import "./Dashboard.css";
import "./Planning.css";

type Draft = Omit<BillInput, "amount"> & { amount: string; id?: number };
const dateText = (date: string) => date.slice(0, 10);
const statuses = { upcoming: "Upcoming", due: "Due today", overdue: "Overdue", paid: "Paid", "payment-deleted": "Payment deleted", "payment-changed": "Payment needs correction" };

export default function RecurringBills() {
  const [month, setMonth] = useState(initialMonth);
  const [bills, setBills] = useState<Bill[]>([]);
  const [occurrences, setOccurrences] = useState<BillOccurrence[]>([]);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [payment, setPayment] = useState<BillOccurrence | null>(null);
  const [refresh, setRefresh] = useState(0);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [formError, setFormError] = useState("");
  const [status, setStatus] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      setLoading(true); setError("");
      try {
        const [plans, rows] = await Promise.all([getBills(controller.signal), getOccurrences(month, controller.signal)]);
        if (!controller.signal.aborted) { setBills(plans); setOccurrences(rows); }
      } catch (failure) { if (!controller.signal.aborted) setError((failure as Error).message); }
      finally { if (!controller.signal.aborted) setLoading(false); }
    }
    void load(); return () => controller.abort();
  }, [month, refresh]);

  async function change(action: () => Promise<unknown>, message: string) {
    setBusy(true); setError(""); setStatus("");
    try { await action(); setStatus(message); setRefresh(value => value + 1); }
    catch (failure) { setError((failure as Error).message); }
    finally { setBusy(false); }
  }
  function edit(bill?: Bill) {
    setFormError("");
    setDraft(bill ? { ...bill, amount: String(bill.amount), firstDueDate: dateText(bill.firstDueDate), endDate: bill.endDate ? dateText(bill.endDate) : null } :
      { name: "", matchText: "", amount: "", category: "Utilities", frequency: "monthly", firstDueDate: today(), endDate: null, enabled: true });
  }
  const outstanding = occurrences.filter(row => row.status !== "paid");
  return <div className="dashboard-wrap"><Navigation />
    <main className="page-content">
      <div className="recent-title"><h1>Recurring bills and subscriptions</h1><MonthSelector month={month} onChange={setMonth} disabled={busy} /></div>
      <p>Plan regular expenses and keep track of what is due. Payments count toward your budget when recorded in Transactions. Link an existing expense to avoid adding it again.</p>
      <div><button className="primary-btn" disabled={busy} onClick={() => edit()}>Add recurring bill</button></div>
      {status && <p className="planning-success" role="status">{status}</p>}
      {error && <p className="dashboard-error" role="alert">{error} <button disabled={busy} onClick={() => setRefresh(value => value + 1)}>Reload bills</button></p>}
      {loading ? <p role="status">Loading bills…</p> : !error && <>
        <section className="card">
          <h2>Payments scheduled for {month}</h2>
          <p>{outstanding.length} outstanding · {currency(outstanding.reduce((sum, row) => sum + row.amount, 0))} planned</p>
          {!occurrences.length && <p>No payments scheduled for this month.</p>}
          <div className="bill-occurrences">{occurrences.map(row => <article className="bill-occurrence" key={row.billId + row.dueDate} aria-label={`${row.name} due ${dateText(row.dueDate)}`}>
            <div><h3>{row.name}</h3><p>{dateText(row.dueDate)} · {row.category}</p><strong>{currency(row.amount)}</strong>
              {row.status === "paid" && <p>Paid {currency(row.paidAmount ?? row.amount)} on {row.paymentDate ? dateText(row.paymentDate) : ""}</p>}
            </div>
            <div className="bill-occurrence-actions"><strong className={`bill-status bill-status-${row.status}`}>{statuses[row.status]}</strong>
              {!row.transactionId ? <button className="primary-btn" disabled={busy} onClick={() => setPayment(row)}>Record or link payment</button> : <>
                <a href={`/transactions?month=${(row.paymentDate || row.dueDate).slice(0, 7)}${row.status === "payment-deleted" ? "&view=deleted" : ""}`}>{row.status === "payment-deleted" ? "Restore in Transactions" : "View transaction"}</a>
                <button className="secondary-btn" disabled={busy} onClick={() => void change(() => unlinkPayment(row.billId, dateText(row.dueDate)), "Payment unlinked. The transaction is unchanged.")}>Unlink payment</button>
              </>}
            </div>
          </article>)}</div>
        </section>
        <section className="card">
          <h2>Your schedules</h2>
          {!bills.length && <p>No recurring bills yet. Add rent, subscriptions, insurance or other regular expenses.</p>}
          <ul className="bill-schedule-list">{bills.map(bill => <li key={bill.id}>
            <div><h3>{bill.name}</h3><p>{currency(bill.amount)} · {bill.frequency} · {bill.category}</p>
              <p>First due {dateText(bill.firstDueDate)}{bill.endDate ? ` · Ends ${dateText(bill.endDate)}` : ""} · {bill.enabled ? "Active" : "Reminders paused"}</p>
            </div>
            <div className="planning-actions">
              <button className="secondary-btn" disabled={busy} onClick={() => edit(bill)}>Edit</button>
              <button className="secondary-btn" disabled={busy} onClick={() => void change(() => saveBill({ ...bill, enabled: !bill.enabled }, bill.id), bill.enabled ? "Bill reminders paused." : "Bill reminders resumed.")}>{bill.enabled ? "Pause reminders" : "Resume reminders"}</button>
              <button className="secondary-btn" disabled={busy} onClick={() => void change(() => archiveBill(bill.id), "Bill archived. Recorded payments remain in your history.")}>Archive</button>
            </div>
          </li>)}</ul>
        </section>
      </>}
      {draft && <Dialog title={draft.id ? "Edit recurring bill" : "Add recurring bill"} onClose={() => { if (!busy) setDraft(null); }}>
        <form className="budget-form" onSubmit={async event => {
          event.preventDefault(); setBusy(true); setFormError("");
          try {
            await saveBill({ ...draft, matchText: draft.matchText.trim() || draft.name, amount: Number(draft.amount) }, draft.id);
            setDraft(null); setStatus("Recurring bill saved."); setRefresh(value => value + 1);
          } catch (failure) { setFormError((failure as Error).message); }
          finally { setBusy(false); }
        }}>
          <fieldset className="planning-fields" disabled={busy}>
            <label>Bill name<input required maxLength={120} value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} /></label>
            <div className="filter-row">
              <label>Expected amount (ZAR)<input type="number" required min="0.01" step="0.01" value={draft.amount} onChange={event => setDraft({ ...draft, amount: event.target.value })} /></label>
              <CategorySelect value={draft.category} onChange={category => setDraft({ ...draft, category })} />
              <label>Repeat<select aria-label="Repeat" value={draft.frequency} onChange={event => setDraft({ ...draft, frequency: event.target.value })}><option value="weekly">Weekly</option><option value="fortnightly">Every two weeks</option><option value="monthly">Monthly</option><option value="yearly">Yearly</option></select></label>
            </div>
            <div className="filter-row">
              <label>First due date<input type="date" required value={draft.firstDueDate} onChange={event => setDraft({ ...draft, firstDueDate: event.target.value })} /></label>
              <label>End date (optional)<input type="date" min={draft.firstDueDate} value={draft.endDate || ""} onChange={event => setDraft({ ...draft, endDate: event.target.value || null })} /></label>
            </div>
            <label>Statement description contains<input maxLength={200} value={draft.matchText} placeholder="Defaults to the bill name" onChange={event => setDraft({ ...draft, matchText: event.target.value })} /></label>
            <p className="small">This phrase helps identify an existing payment and flag possible duplicate statement imports. Monthly dates use the last available day in shorter months, then return to the original day.</p>
            <label className="planning-checkbox"><input type="checkbox" checked={draft.enabled} onChange={event => setDraft({ ...draft, enabled: event.target.checked })} />Reminders enabled</label>
            <p className="small">Schedule changes affect unpaid reminders. Recorded payments stay in history. Pausing reminders does not cancel a subscription.</p>
            <button className="primary-btn">{busy ? "Saving…" : "Save recurring bill"}</button>
          </fieldset>
          {formError && <p className="dashboard-error" role="alert">{formError}</p>}
        </form>
      </Dialog>}
      {payment && <BillPaymentDialog occurrence={payment} onClose={() => setPayment(null)} onSaved={() => { setPayment(null); setStatus("Bill payment saved."); setRefresh(value => value + 1); }} />}
    </main>
  </div>;
}
