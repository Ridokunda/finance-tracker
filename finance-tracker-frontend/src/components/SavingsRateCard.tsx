import { currency } from "../services/finance";

export default function SavingsRateCard({
  income,
  netCashFlow,
  savingsRate,
  previousMonthSavingsRate,
  loading,
}: {
  income: number;
  netCashFlow: number;
  savingsRate: number | null;
  previousMonthSavingsRate: number | null;
  loading: boolean;
}) {
  const rateDelta =
    savingsRate !== null && previousMonthSavingsRate !== null
      ? Math.round((savingsRate - previousMonthSavingsRate) * 10) / 10
      : null;

  const isPositive = savingsRate !== null && savingsRate >= 0;
  const barWidth =
    savingsRate === null ? 0 : Math.min(100, Math.abs(savingsRate));

  return (
    <section className="card h-lg savings-card" aria-label="Savings rate">
      <div className="card-title">Savings Rate</div>
      <div
        className="card-value"
        style={{
          color:
            loading || savingsRate === null
              ? undefined
              : isPositive
                ? "var(--success)"
                : "var(--danger)",
        }}
      >
        {loading ? "—" : savingsRate === null ? "—" : `${savingsRate.toFixed(1)}%`}
      </div>
      <div className="card-sub">
        {loading
          ? "Loading savings rate..."
          : savingsRate === null
            ? "No income recorded this month"
            : netCashFlow >= 0
              ? `${currency(netCashFlow)} saved of ${currency(income)}`
              : `${currency(Math.abs(netCashFlow))} deficit on ${currency(income)}`}
      </div>
      {!loading && savingsRate !== null && (
        <div className="savings-bar-track" aria-hidden="true">
          <div
            className={`savings-bar-fill ${isPositive ? "savings-bar-positive" : "savings-bar-negative"}`}
            style={{ width: `${barWidth}%` }}
          />
        </div>
      )}
      {!loading && rateDelta !== null && (
        <div className="spending-deltas">
          <span
            className={`delta-pill ${
              rateDelta > 0
                ? "delta-good"
                : rateDelta < 0
                  ? "delta-bad"
                  : "delta-neutral"
            }`}
          >
            {rateDelta > 0 ? "▲ +" : rateDelta < 0 ? "▼ " : ""}
            {rateDelta.toFixed(1)} pp vs last month
          </span>
        </div>
      )}
    </section>
  );
}
