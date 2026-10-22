import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { currency, monthRange, netCashFlow, today } from "../services/finance";
import type { DateRange } from "../services/finance";

type Period = "this-month" | "last-month" | "last-five-days" | "custom";

function presetRange(period: Exclude<Period, "custom">): DateRange {
  const date = today();
  if (period === "this-month") return monthRange(date.slice(0, 7));

  // Work with calendar dates in UTC so the browser's timezone cannot shift a day.
  const start = new Date(`${date}T00:00:00Z`);
  if (period === "last-month") {
    start.setUTCDate(1);
    start.setUTCMonth(start.getUTCMonth() - 1);
    return monthRange(start.toISOString().slice(0, 7));
  }
  start.setUTCDate(start.getUTCDate() - 4);
  return { from: start.toISOString().slice(0, 10), to: date };
}

function formatDate(date: string) {
  return new Intl.DateTimeFormat("en-ZA", {
    day: "numeric", month: "short", year: "numeric", timeZone: "UTC",
  }).format(new Date(`${date}T00:00:00Z`));
}

export default function NetCashFlowCard({ refreshKey = 0 }: { refreshKey?: number }) {
  const [period, setPeriod] = useState<Period>("this-month");
  const [customRange, setCustomRange] = useState(() => presetRange("this-month"));
  const [draft, setDraft] = useState(customRange);
  const [dateError, setDateError] = useState("");
  const [retry, setRetry] = useState(0);
  const [result, setResult] = useState<{ key: string; value?: number; error?: string } | null>(null);
  const { from, to } = period === "custom" ? customRange : presetRange(period);
  const requestKey = `${from}/${to}/${refreshKey}/${retry}`;
  const loading = result?.key !== requestKey;

  useEffect(() => {
    const controller = new AbortController();
    netCashFlow({ from, to }, controller.signal).then(
      (summary) => {
        if (!controller.signal.aborted) setResult({ key: requestKey, value: summary.netCashFlow });
      },
      () => {
        if (!controller.signal.aborted) setResult({ key: requestKey, error: "Cash flow could not be loaded." });
      },
    );
    return () => controller.abort();
  }, [from, to, requestKey]);

  function applyCustomPeriod(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft.from || !draft.to || draft.from > draft.to) {
      setDateError("Choose a start date on or before the end date.");
      return;
    }
    setDateError("");
    setCustomRange({ ...draft });
  }

  return (
    <section className="card cash-flow-card" aria-label="Net cash flow">
      <div className="cash-flow-heading">
        <div className="card-title">Net cash flow</div>
        <label>
          Period
          <select
            aria-label="Net cash flow period"
            value={period}
            onChange={(event) => {
              setPeriod(event.target.value as Period);
              setDateError("");
            }}
          >
            <option value="this-month">This month</option>
            <option value="last-month">Last month</option>
            <option value="last-five-days">Last five days</option>
            <option value="custom">Custom period</option>
          </select>
        </label>
      </div>
      {period === "custom" && (
        <form className="cash-flow-dates" onSubmit={applyCustomPeriod}>
          <label>
            Start date
            <input type="date" min="1900-01-01" max="9998-12-31" required value={draft.from}
              onChange={(event) => { setDraft({ ...draft, from: event.target.value }); setDateError(""); }} />
          </label>
          <label>
            End date
            <input type="date" min="1900-01-01" max="9998-12-31" required value={draft.to}
              onChange={(event) => { setDraft({ ...draft, to: event.target.value }); setDateError(""); }} />
          </label>
          <button type="submit" className="primary-btn">Apply period</button>
          {dateError && <p className="cash-flow-error" role="alert">{dateError}</p>}
        </form>
      )}
      <div aria-live="polite" aria-busy={loading}>
        <div className="card-value">{loading || result?.error ? "—" : currency(result?.value ?? 0)}</div>
        {loading && <span className="small">Loading cash flow...</span>}
        {!loading && result?.error && (
          <p className="cash-flow-error" role="alert">
            {result.error} <button type="button" className="secondary-btn" onClick={() => setRetry(value => value + 1)}>Retry</button>
          </p>
        )}
      </div>
      <p className="small">Income minus expenses from {formatDate(from)} to {formatDate(to)} (inclusive).</p>
    </section>
  );
}
