import { currency } from "../services/finance";
import type { MonthlySummary } from "../services/finance";

export default function BudgetWarnings({ summary, month, showEmpty = false }: { summary: MonthlySummary; month: string; showEmpty?: boolean }) {
  const warnings = summary.warnings ?? [];
  if (!warnings.length && !showEmpty) return null;
  return <section className="card budget-warnings" aria-label="Budget warnings" aria-live="polite">
    <h2>Budget warnings</h2>
    {!warnings.length && <p>{summary.warningSettings?.enabled === false ? "Budget warnings are turned off." : summary.needsReconfirmation ? "Reconfirm your budget before warnings can be calculated." : !summary.hasBudget ? "Set a budget to receive spending warnings." : "No budget warnings for this month."}</p>}
    <ul>{warnings.map(warning => <li key={warning.scope + warning.category} className={warning.level === "approaching" ? "notice" : "dashboard-error"}>
      <a href={`/transactions?month=${month}${warning.category ? `&category=${encodeURIComponent(warning.category)}` : ""}`}><strong>{warning.category || "Overall budget"}</strong></a>
      <p>{currency(warning.spent)} spent of {currency(warning.limit)}{warning.percentUsed !== null ? ` (${warning.percentUsed}%)` : ""}. {warning.level === "over" ? `Over by ${currency(warning.spent - warning.limit)}.` : warning.level === "reached" ? "Limit reached." : "Approaching your limit."}</p>
    </li>)}</ul>
  </section>;
}
