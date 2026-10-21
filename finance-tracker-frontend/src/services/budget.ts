import { api, initialMonth, monthQuery } from "./finance";
import type { MonthlySummary } from "./finance";
export interface BudgetSummary {
  budget: number;
  spent: number;
  remaining: number;
  status: string;
}
export const getBudget = (_token: string, month = initialMonth()) =>
  api<MonthlySummary>(`budget/summary?${monthQuery(month)}`);
export const setBudget = (
  _token: string,
  amount: number,
  month = initialMonth(),
) => {
  const [year, number] = month.split("-").map(Number);
  return api<MonthlySummary>("budget/set", {
    method: "POST",
    body: JSON.stringify({ year, month: number, limitAmount: amount }),
  });
};
