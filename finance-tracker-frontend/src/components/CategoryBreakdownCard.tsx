import { useState } from "react";
import { currency } from "../services/finance";
import type { CategoryTotal } from "../services/finance";

const CATEGORY_COLORS = [
  "#34d399",
  "#60a5fa",
  "#7c3aed",
  "#f97316",
  "#eab308",
  "#94a3b8",
  "#ec4899",
  "#14b8a6",
];

type ComparisonMode = "mom" | "yoy";

function polarToCartesian(cx: number, cy: number, radius: number, angleDeg: number): [number, number] {
  const rad = (angleDeg * Math.PI) / 180;
  return [cx + radius * Math.cos(rad), cy + radius * Math.sin(rad)];
}

function donutSlicePath(
  cx: number,
  cy: number,
  innerRadius: number,
  outerRadius: number,
  startDeg: number,
  endDeg: number,
): string {
  if (endDeg - startDeg >= 359.99) {
    return [
      `M ${cx} ${cy - outerRadius}`,
      `A ${outerRadius} ${outerRadius} 0 1 0 ${cx} ${cy + outerRadius}`,
      `A ${outerRadius} ${outerRadius} 0 1 0 ${cx} ${cy - outerRadius}`,
      "Z",
      `M ${cx} ${cy - innerRadius}`,
      `A ${innerRadius} ${innerRadius} 0 1 1 ${cx} ${cy + innerRadius}`,
      `A ${innerRadius} ${innerRadius} 0 1 1 ${cx} ${cy - innerRadius}`,
      "Z",
    ].join(" ");
  }
  const [x1, y1] = polarToCartesian(cx, cy, outerRadius, startDeg);
  const [x2, y2] = polarToCartesian(cx, cy, outerRadius, endDeg);
  const [x3, y3] = polarToCartesian(cx, cy, innerRadius, endDeg);
  const [x4, y4] = polarToCartesian(cx, cy, innerRadius, startDeg);
  const largeArc = endDeg - startDeg > 180 ? 1 : 0;
  return [
    `M ${x1} ${y1}`,
    `A ${outerRadius} ${outerRadius} 0 ${largeArc} 1 ${x2} ${y2}`,
    `L ${x3} ${y3}`,
    `A ${innerRadius} ${innerRadius} 0 ${largeArc} 0 ${x4} ${y4}`,
    "Z",
  ].join(" ");
}

function formatSignedCurrency(value: number): string {
  const prefix = value > 0 ? "+" : value < 0 ? "-" : "";
  return `${prefix}${currency(Math.abs(value))}`;
}

function deltaTone(change: number | null | undefined): string {
  if (change === null || change === undefined || change === 0) return "delta-neutral";
  return change > 0 ? "delta-up" : "delta-down";
}

function formatCompactDelta(
  previous: number,
  change: number | null | undefined,
  percent: number | null | undefined,
): string {
  if (change === null || change === undefined) return "—";
  if (previous === 0 && change > 0) return "New";
  if (change === 0) return "0%";
  const arrow = change > 0 ? "▲" : "▼";
  if (percent !== null && percent !== undefined) {
    const sign = percent > 0 ? "+" : "";
    return `${arrow} ${sign}${percent.toFixed(1)}%`;
  }
  return `${arrow} ${formatSignedCurrency(change)}`;
}

function formatVerboseDelta(
  label: string,
  previous: number,
  change: number | null | undefined,
  percent: number | null | undefined,
): string {
  if (change === null || change === undefined) return `No ${label} data`;
  if (previous === 0 && change > 0) return `New vs ${label} (${currency(0)} prior)`;
  if (change === 0) return `Unchanged vs ${label} (${currency(previous)})`;
  const arrow = change > 0 ? "▲" : "▼";
  const pctText =
    percent !== null && percent !== undefined
      ? ` (${percent > 0 ? "+" : ""}${percent.toFixed(1)}%)`
      : "";
  return `${arrow} ${formatSignedCurrency(change)}${pctText} vs ${label} (${currency(previous)})`;
}

export default function CategoryBreakdownCard({
  month,
  categories,
  totalSpent,
  loading,
}: {
  month: string;
  categories: CategoryTotal[];
  totalSpent: number;
  loading: boolean;
}) {
  const [comparison, setComparison] = useState<ComparisonMode>("mom");
  const [hoveredName, setHoveredName] = useState<string | null>(null);
  const [pinnedName, setPinnedName] = useState<string | null>(null);

  const items = categories
    .filter((c) => c.spent > 0)
    .map((c, index) => ({
      raw: c,
      name: c.category,
      total: c.spent,
      percentage: totalSpent ? (c.spent / totalSpent) * 100 : 0,
      color: CATEGORY_COLORS[index % CATEGORY_COLORS.length],
    }));

  const activeName = hoveredName ?? pinnedName;
  const activeItem = items.find((item) => item.name === activeName) ?? null;

  const getDelta = (c: CategoryTotal) =>
    comparison === "mom"
      ? {
          previous: c.previousMonthSpent ?? 0,
          change: c.monthOverMonthChange,
          percent: c.monthOverMonthPercent,
        }
      : {
          previous: c.previousYearSpent ?? 0,
          change: c.yearOverYearChange,
          percent: c.yearOverYearPercent,
        };

  return (
    <section className="card" aria-label="Expense breakdown by category">
      <div className="insights-header">
        <div className="card-title">Expense Breakdown by Category</div>
        {!loading && items.length > 0 && (
          <div className="segmented-toggle" role="group" aria-label="Category comparison period">
            <button
              type="button"
              aria-pressed={comparison === "mom"}
              onClick={() => setComparison("mom")}
            >
              vs Last month
            </button>
            <button
              type="button"
              aria-pressed={comparison === "yoy"}
              onClick={() => setComparison("yoy")}
            >
              vs Last year
            </button>
          </div>
        )}
      </div>

      {loading ? (
        <div className="empty-tile">Loading category data...</div>
      ) : items.length === 0 ? (
        <div className="empty-tile">No expenses recorded this month.</div>
      ) : (
        <>
          <div className="donut-wrap">
            <div className="donut">
              <svg viewBox="0 0 120 120" role="img" aria-label="Expense breakdown by category">
                <circle cx="60" cy="60" r="47" fill="none" stroke="#e2e8f0" strokeWidth="18" />
                {items.map((category, index) => {
                  const startPct = items
                    .slice(0, index)
                    .reduce((sum, item) => sum + item.percentage, 0);
                  const startDeg = -90 + startPct * 3.6;
                  const endDeg = startDeg + category.percentage * 3.6;
                  const isActive = activeItem?.name === category.name;
                  const isDimmed = activeItem !== null && !isActive;
                  return (
                    <path
                      key={category.name}
                      className="donut-slice"
                      d={donutSlicePath(60, 60, 38, isActive ? 59 : 56, startDeg, endDeg)}
                      fill={category.color}
                      fillRule="evenodd"
                      stroke={items.length > 1 ? "#ffffff" : "none"}
                      strokeWidth={items.length > 1 ? 1.2 : 0}
                      opacity={isDimmed ? 0.35 : 1}
                      tabIndex={0}
                      role="button"
                      aria-label={`${category.name}: ${currency(category.total)} (${category.percentage.toFixed(1)}%)`}
                      aria-pressed={pinnedName === category.name}
                      onMouseEnter={() => setHoveredName(category.name)}
                      onMouseLeave={() => setHoveredName(null)}
                      onFocus={() => setHoveredName(category.name)}
                      onBlur={() => setHoveredName(null)}
                      onClick={() =>
                        setPinnedName((prev) => (prev === category.name ? null : category.name))
                      }
                      onKeyDown={(event) => {
                        if (event.key === "Enter" || event.key === " ") {
                          event.preventDefault();
                          setPinnedName((prev) =>
                            prev === category.name ? null : category.name,
                          );
                        }
                      }}
                    />
                  );
                })}
              </svg>
              <div className="donut-center">
                <strong>
                  {currency(activeItem ? activeItem.total : totalSpent)}
                </strong>
                <span>
                  {activeItem
                    ? `${activeItem.name} · ${activeItem.percentage.toFixed(1)}%`
                    : month}
                </span>
              </div>
            </div>

            <div className="legend">
              {items.map((category) => {
                const delta = getDelta(category.raw);
                const isActive = activeItem?.name === category.name;
                const isDimmed = activeItem !== null && !isActive;
                return (
                  <div
                    className={`legend-item legend-item-interactive${isActive ? " is-active" : ""}${isDimmed ? " is-dimmed" : ""}`}
                    key={category.name}
                    onMouseEnter={() => setHoveredName(category.name)}
                    onMouseLeave={() => setHoveredName(null)}
                    onFocus={() => setHoveredName(category.name)}
                    onBlur={() => setHoveredName(null)}
                  >
                    <div className="legend-dot" style={{ background: category.color }} />
                    <div className="legend-main">
                      <div className="legend-top">
                        <a
                          className="legend-name"
                          href={`/transactions?month=${month}&category=${encodeURIComponent(category.name)}`}
                        >
                          {category.name}
                        </a>
                        {delta.change !== undefined && (
                          <span className={`delta-pill ${deltaTone(delta.change)}`}>
                            {formatCompactDelta(delta.previous, delta.change, delta.percent)}
                          </span>
                        )}
                      </div>
                      <div className="small">
                        {category.percentage.toFixed(1)}% · {currency(category.total)}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          <div
            className={`chart-tooltip${activeItem ? "" : " chart-tooltip-muted"}`}
            role="status"
            aria-live="polite"
            aria-label="Category insight details"
          >
            {activeItem ? (
              <>
                <div className="chart-tooltip-header">
                  <span className="chart-tooltip-title">
                    <span
                      className="legend-dot"
                      style={{ background: activeItem.color, marginTop: 0 }}
                    />
                    <strong>{activeItem.name}</strong>
                  </span>
                  <strong>
                    {currency(activeItem.total)} ({activeItem.percentage.toFixed(1)}% of spend)
                  </strong>
                </div>

                {activeItem.raw.limit !== null && (
                  <div className="small">
                    Budget:{" "}
                    {currency(activeItem.raw.effectiveLimit ?? activeItem.raw.limit)} available
                    {(activeItem.raw.rolloverAmount ?? 0) > 0
                      ? ` (${currency(activeItem.raw.limit)} base + ${currency(activeItem.raw.rolloverAmount ?? 0)} rollover)`
                      : ""}{" "}
                    · {currency(activeItem.raw.remaining ?? 0)} remaining ({activeItem.raw.status})
                  </div>
                )}

                <div className="chart-tooltip-comparisons">
                  <span className={`delta-pill ${deltaTone(activeItem.raw.monthOverMonthChange)}`}>
                    {formatVerboseDelta(
                      "last month",
                      activeItem.raw.previousMonthSpent ?? 0,
                      activeItem.raw.monthOverMonthChange,
                      activeItem.raw.monthOverMonthPercent,
                    )}
                  </span>
                  <span className={`delta-pill ${deltaTone(activeItem.raw.yearOverYearChange)}`}>
                    {formatVerboseDelta(
                      "last year",
                      activeItem.raw.previousYearSpent ?? 0,
                      activeItem.raw.yearOverYearChange,
                      activeItem.raw.yearOverYearPercent,
                    )}
                  </span>
                </div>
              </>
            ) : (
              <span>
                Hover, tap, or focus a slice or category to inspect budget progress and period changes.
              </span>
            )}
          </div>
        </>
      )}
    </section>
  );
}
