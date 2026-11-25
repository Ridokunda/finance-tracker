import { currency } from "../services/finance";
import type { MerchantTotal } from "../services/finance";

function merchantDeltaLabel(merchant: MerchantTotal): { text: string; tone: string } {
  const change = merchant.monthOverMonthChange;
  if (change === null || change === undefined) {
    return { text: "—", tone: "delta-neutral" };
  }
  if (merchant.previousMonthSpent === 0 && change > 0) {
    return { text: "New vs last mo", tone: "delta-neutral" };
  }
  if (change === 0) {
    return { text: "Same as last mo", tone: "delta-neutral" };
  }
  const arrow = change > 0 ? "▲" : "▼";
  const sign = change > 0 ? "+" : "-";
  return {
    text: `${arrow} ${sign}${currency(Math.abs(change))} vs last mo`,
    tone: change > 0 ? "delta-up" : "delta-down",
  };
}

export default function TopMerchantsCard({
  month,
  merchants,
  loading,
}: {
  month: string;
  merchants: MerchantTotal[];
  loading: boolean;
}) {
  const topSpend = merchants.length > 0 ? Math.max(...merchants.map((m) => m.spent), 1) : 1;

  return (
    <section className="card top-merchants" aria-label="Top merchants">
      <div className="insights-header">
        <div className="card-title">Top Merchants</div>
        <span className="small">{month}</span>
      </div>

      {loading ? (
        <p className="tile-message">Loading merchant breakdown...</p>
      ) : merchants.length === 0 ? (
        <p className="tile-message">No merchant spending recorded this month.</p>
      ) : (
        <ul className="merchant-list">
          {merchants.map((merchant) => {
            const delta = merchantDeltaLabel(merchant);
            const barWidth = Math.max(4, Math.min(100, (merchant.spent / topSpend) * 100));
            return (
              <li className="merchant-item" key={merchant.merchant}>
                <div className="merchant-row">
                  <div className="merchant-meta">
                    <a
                      className="merchant-link"
                      href={`/transactions?month=${month}&search=${encodeURIComponent(merchant.searchQuery)}`}
                    >
                      {merchant.merchant}
                    </a>
                    <div className="merchant-sub">
                      <span>{merchant.primaryCategory}</span>
                      <span>·</span>
                      <span>
                        {merchant.transactionCount}{" "}
                        {merchant.transactionCount === 1 ? "transaction" : "transactions"}
                      </span>
                      <span className={`delta-pill ${delta.tone}`}>{delta.text}</span>
                    </div>
                  </div>
                  <div className="merchant-amount">
                    <strong>{currency(merchant.spent)}</strong>
                    <span className="small">{merchant.sharePercent.toFixed(1)}%</span>
                  </div>
                </div>
                <div className="merchant-bar-track" aria-hidden="true">
                  <div className="merchant-bar-fill" style={{ width: `${barWidth}%` }} />
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
