import { useState } from "react";
import { currency } from "../services/finance";
import type { TrendPoint } from "../services/finance";

type TrendView = "cashflow" | "savings";

function compactCurrency(value: number): string {
  const abs = Math.abs(value);
  const sign = value < 0 ? "-" : "";
  if (abs >= 1_000_000) return `${sign}R${(abs / 1_000_000).toFixed(1)}m`;
  if (abs >= 1_000) return `${sign}R${(abs / 1_000).toFixed(abs >= 10_000 ? 0 : 1)}k`;
  return `${sign}R${Math.round(abs)}`;
}

function shortMonthLabel(yyyyMm: string): string {
  const [year, month] = yyyyMm.split("-").map(Number);
  if (!year || !month) return yyyyMm;
  return new Intl.DateTimeFormat("en-ZA", {
    month: "short",
    year: "2-digit",
    timeZone: "UTC",
  }).format(new Date(Date.UTC(year, month - 1, 1)));
}

export default function SpendingTrendsCard({
  trends,
  selectedMonth,
  loading,
}: {
  trends: TrendPoint[];
  selectedMonth: string;
  loading: boolean;
}) {
  const [view, setView] = useState<TrendView>("cashflow");
  const [hoveredMonth, setHoveredMonth] = useState<string | null>(null);
  const [pinnedMonth, setPinnedMonth] = useState<string | null>(null);

  const normalized = trends.map((point) => {
    const netCashFlow = point.netCashFlow ?? point.income - point.expenses;
    const savingsRate =
      point.savingsRate !== undefined
        ? point.savingsRate
        : point.income > 0
          ? Math.round(((point.income - point.expenses) / point.income) * 1000) / 10
          : null;
    return { ...point, netCashFlow, savingsRate };
  });

  const hasData = normalized.some((m) => m.expenses > 0 || m.income > 0);
  const activeKey = hoveredMonth ?? pinnedMonth ?? selectedMonth;
  const activePoint =
    normalized.find((m) => m.month === activeKey) ?? normalized.at(-1) ?? null;

  const xLeft = 44;
  const xRight = 322;
  const yTop = 16;
  const yBottom = 136;
  const chartHeight = yBottom - yTop;

  const xForIndex = (index: number) =>
    normalized.length <= 1
      ? (xLeft + xRight) / 2
      : xLeft + (index / (normalized.length - 1)) * (xRight - xLeft);

  const trendMax = Math.max(
    1,
    ...normalized.flatMap((m) => [m.expenses, m.income]),
  );

  const yForMoney = (val: number) => yBottom - (val / trendMax) * chartHeight;

  const linePoints = (field: "expenses" | "income") =>
    normalized.map((m, i) => `${xForIndex(i)},${yForMoney(m[field])}`).join(" ");

  const areaPoints = (field: "expenses" | "income") =>
    normalized.length === 0
      ? ""
      : `${xForIndex(0)},${yBottom} ${linePoints(field)} ${xForIndex(normalized.length - 1)},${yBottom}`;

  // Savings rate scale (clamped cleanly to [-100, 100] visually while showing exact value in tooltip)
  const ratedValues = normalized
    .map((m) => m.savingsRate)
    .filter((v): v is number => v !== null)
    .map((v) => Math.max(-100, Math.min(100, v)));
  const maxRate = Math.max(25, ...ratedValues, 0);
  const minRate = Math.min(0, ...ratedValues);
  const rateSpan = Math.max(1, maxRate - minRate);
  const yForRate = (rate: number) => {
    const clamped = Math.max(minRate, Math.min(maxRate, rate));
    return yTop + ((maxRate - clamped) / rateSpan) * chartHeight;
  };
  const zeroRateY = yForRate(0);

  return (
    <section className="card" aria-label="Spending trends">
      <div className="insights-header">
        <div className="card-title">
          Spending Trends <span className="small">· last 6 months</span>
        </div>
        {!loading && hasData && (
          <div className="segmented-toggle" role="group" aria-label="Trend view">
            <button
              type="button"
              aria-pressed={view === "cashflow"}
              onClick={() => setView("cashflow")}
            >
              Income &amp; Expenses
            </button>
            <button
              type="button"
              aria-pressed={view === "savings"}
              onClick={() => setView("savings")}
            >
              Savings Rate
            </button>
          </div>
        )}
      </div>

      <div className="chart" aria-label="Spending trends for the last six months">
        {loading ? (
          <div className="empty-chart">Loading trend data...</div>
        ) : !hasData ? (
          <div className="empty-chart">No income or expense history yet.</div>
        ) : (
          <>
            {view === "cashflow" ? (
              <svg
                className="trend-chart-svg"
                viewBox="0 0 340 156"
                role="img"
                aria-label="Six-month income and expenses trend chart"
                onMouseLeave={() => setHoveredMonth(null)}
              >
                {[0, 0.5, 1].map((ratio) => {
                  const val = trendMax * ratio;
                  const y = yForMoney(val);
                  return (
                    <g key={ratio}>
                      <line
                        x1={xLeft}
                        y1={y}
                        x2={xRight}
                        y2={y}
                        stroke={ratio === 0 ? "#cbd5e1" : "#e2e8f0"}
                        strokeDasharray={ratio === 0 ? undefined : "3 3"}
                        strokeWidth="1"
                      />
                      <text
                        x={xLeft - 6}
                        y={y + 3}
                        textAnchor="end"
                        fill="#64748b"
                        fontSize="9"
                      >
                        {compactCurrency(val)}
                      </text>
                    </g>
                  );
                })}

                <polygon fill="#10b981" fillOpacity="0.10" points={areaPoints("income")} />
                <polygon fill="#60a5fa" fillOpacity="0.12" points={areaPoints("expenses")} />

                <polyline
                  fill="none"
                  stroke="#60a5fa"
                  strokeWidth="2.8"
                  strokeLinejoin="round"
                  points={linePoints("expenses")}
                />
                <polyline
                  fill="none"
                  stroke="#10b981"
                  strokeWidth="2.5"
                  strokeLinejoin="round"
                  points={linePoints("income")}
                />

                {normalized.map((point, index) => {
                  const x = xForIndex(index);
                  const expenseY = yForMoney(point.expenses);
                  const incomeY = yForMoney(point.income);
                  const isActive = activePoint?.month === point.month;
                  const bandWidth = (xRight - xLeft) / Math.max(1, normalized.length);
                  return (
                    <g key={point.month}>
                      {isActive && (
                        <line
                          x1={x}
                          y1={yTop}
                          x2={x}
                          y2={yBottom}
                          stroke="#94a3b8"
                          strokeDasharray="3 3"
                          strokeWidth="1"
                        />
                      )}
                      <circle
                        cx={x}
                        cy={expenseY}
                        r={isActive ? 5 : 3.2}
                        fill="#2563eb"
                        stroke="#ffffff"
                        strokeWidth="1.5"
                      />
                      <circle
                        cx={x}
                        cy={incomeY}
                        r={isActive ? 5 : 3.2}
                        fill="#10b981"
                        stroke="#ffffff"
                        strokeWidth="1.5"
                      />
                      <rect
                        x={x - bandWidth / 2}
                        y={yTop}
                        width={bandWidth}
                        height={chartHeight}
                        fill="transparent"
                        style={{ cursor: "pointer" }}
                        onMouseEnter={() => setHoveredMonth(point.month)}
                        onClick={() => setPinnedMonth(point.month)}
                      />
                    </g>
                  );
                })}
              </svg>
            ) : (
              <svg
                className="trend-chart-svg"
                viewBox="0 0 340 156"
                role="img"
                aria-label="Six-month savings rate chart"
                onMouseLeave={() => setHoveredMonth(null)}
              >
                {[maxRate, 0, ...(minRate < 0 ? [minRate] : [])].map((tick) => {
                  const y = yForRate(tick);
                  return (
                    <g key={tick}>
                      <line
                        x1={xLeft}
                        y1={y}
                        x2={xRight}
                        y2={y}
                        stroke={tick === 0 ? "#94a3b8" : "#e2e8f0"}
                        strokeDasharray={tick === 0 ? undefined : "3 3"}
                        strokeWidth="1"
                      />
                      <text
                        x={xLeft - 6}
                        y={y + 3}
                        textAnchor="end"
                        fill="#64748b"
                        fontSize="9"
                      >
                        {Math.round(tick)}%
                      </text>
                    </g>
                  );
                })}

                {normalized.map((point, index) => {
                  const x = xForIndex(index);
                  const isActive = activePoint?.month === point.month;
                  const barWidth = 24;
                  const bandWidth = (xRight - xLeft) / Math.max(1, normalized.length);
                  const rate = point.savingsRate;
                  const barY = rate === null ? zeroRateY : Math.min(zeroRateY, yForRate(rate));
                  const barHeight =
                    rate === null
                      ? 0
                      : Math.max(3, Math.abs(yForRate(rate) - zeroRateY));
                  return (
                    <g key={point.month}>
                      {rate !== null ? (
                        <rect
                          x={x - barWidth / 2}
                          y={barY}
                          width={barWidth}
                          height={barHeight}
                          rx="4"
                          fill={rate >= 0 ? "#10b981" : "#ef4444"}
                          opacity={isActive ? 1 : 0.72}
                          stroke={isActive ? "#071133" : "none"}
                          strokeWidth="1.2"
                        />
                      ) : (
                        <circle cx={x} cy={zeroRateY} r="3" fill="#cbd5e1" />
                      )}
                      <rect
                        x={x - bandWidth / 2}
                        y={yTop}
                        width={bandWidth}
                        height={chartHeight}
                        fill="transparent"
                        style={{ cursor: "pointer" }}
                        onMouseEnter={() => setHoveredMonth(point.month)}
                        onClick={() => setPinnedMonth(point.month)}
                      />
                    </g>
                  );
                })}
              </svg>
            )}

            <div className="trend-month-buttons" role="group" aria-label="Inspect trend month">
              {normalized.map((point) => {
                const isActive = activePoint?.month === point.month;
                return (
                  <button
                    key={point.month}
                    type="button"
                    className="trend-month-btn"
                    aria-label={`Inspect ${point.month}`}
                    aria-pressed={isActive}
                    onMouseEnter={() => setHoveredMonth(point.month)}
                    onMouseLeave={() => setHoveredMonth(null)}
                    onFocus={() => setHoveredMonth(point.month)}
                    onBlur={() => setHoveredMonth(null)}
                    onClick={() => setPinnedMonth(point.month)}
                  >
                    {shortMonthLabel(point.month)}
                  </button>
                );
              })}
            </div>

            {view === "cashflow" ? (
              <div className="trend-legend">
                <span>
                  <i className="trend-line expense-line" /> Expenses
                </span>
                <span>
                  <i className="trend-line income-line" /> Income
                </span>
              </div>
            ) : (
              <div className="trend-legend">
                <span>
                  <i className="trend-line income-line" /> Positive savings
                </span>
                <span>
                  <i className="trend-line" style={{ background: "#ef4444" }} /> Deficit
                </span>
              </div>
            )}

            {activePoint && (
              <div
                className="chart-tooltip"
                role="status"
                aria-live="polite"
                aria-label="Trend month details"
              >
                <div className="chart-tooltip-header">
                  <strong>{activePoint.month}</strong>
                  <span
                    className={`delta-pill ${
                      activePoint.savingsRate === null
                        ? "delta-neutral"
                        : activePoint.savingsRate >= 0
                          ? "delta-good"
                          : "delta-bad"
                    }`}
                  >
                    {activePoint.savingsRate === null
                      ? "No income"
                      : `Savings rate: ${activePoint.savingsRate.toFixed(1)}%`}
                  </span>
                </div>
                <div className="chart-tooltip-grid">
                  <div className="chart-tooltip-metric">
                    <span>Income</span>
                    <strong>{currency(activePoint.income)}</strong>
                  </div>
                  <div className="chart-tooltip-metric">
                    <span>Expenses</span>
                    <strong>{currency(activePoint.expenses)}</strong>
                  </div>
                  <div className="chart-tooltip-metric">
                    <span>Net cash flow</span>
                    <strong
                      className={
                        activePoint.netCashFlow < 0 ? "amount-expense" : "amount-income"
                      }
                    >
                      {activePoint.netCashFlow > 0 ? "+" : ""}
                      {currency(activePoint.netCashFlow)}
                    </strong>
                  </div>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </section>
  );
}
