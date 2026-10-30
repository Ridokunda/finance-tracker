import { useEffect, useState } from "react";
import { currency } from "../services/finance";
import { getOccurrences } from "../services/recurring";
import type { BillOccurrence } from "../services/recurring";

export default function UpcomingBillsCard({ month, refreshKey }: { month: string; refreshKey: number }) {
  const [rows, setRows] = useState<BillOccurrence[] | null>(null);
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    getOccurrences(month, controller.signal).then(data => { if (!controller.signal.aborted) { setRows(data.filter(row => row.status !== "paid")); setError(""); } })
      .catch(() => { if (!controller.signal.aborted) { setRows([]); setError("Upcoming bills could not load."); } });
    return () => controller.abort();
  }, [month, refreshKey, retry]);
  return <section className="card upcoming-bills" aria-label="Upcoming bills">
    <div className="recent-title"><h2>Upcoming bills</h2><a href={`/recurring?month=${month}`}>Manage bills</a></div>
    <p className="small">Scheduled for {month}. Estimates count as spending only when recorded as transactions.</p>
    {error ? <p role="alert">{error} <button onClick={() => setRetry(value => value + 1)}>Retry</button></p> : rows === null ? <p role="status">Loading bills…</p> : rows.length === 0 ? <p>No outstanding bills for this month. <a href={`/recurring?month=${month}`}>Add a bill or subscription</a></p> : <>
      <p><strong>{currency(rows.reduce((sum, row) => sum + row.amount, 0))}</strong> scheduled and unpaid</p>
      <ul>{rows.slice(0, 5).map(row => <li key={row.billId + row.dueDate}><div><strong>{row.name}</strong><p>{row.dueDate.slice(0, 10)} · {row.status === "overdue" ? "Overdue" : row.status === "due" ? "Due today" : row.status.startsWith("payment-") ? "Check linked payment" : "Upcoming"}</p></div><span>{currency(row.amount)}</span></li>)}</ul>
      {rows.length > 5 && <a href={`/recurring?month=${month}`}>View all {rows.length} outstanding bills</a>}
    </>}
  </section>;
}
