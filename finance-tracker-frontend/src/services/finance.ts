export const token = () => localStorage.getItem("token") || "";
export const currency = (value: number) =>
  new Intl.NumberFormat("en-ZA", { style: "currency", currency: "ZAR" }).format(
    value,
  );
export const today = () =>
  new Intl.DateTimeFormat("en-CA", {
    timeZone: "Africa/Johannesburg",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
export const initialMonth = () => {
  const query = new URLSearchParams(location.search).get("month");
  const stored = localStorage.getItem("month");
  return (
    [query, stored].find(
      (value) => value && /^\d{4}-(0[1-9]|1[0-2])$/.test(value),
    ) || today().slice(0, 7)
  );
};
export const monthQuery = (month: string) => {
  const [year, number] = month.split("-");
  return `year=${year}&month=${number}`;
};
export const monthRange = (month: string) => {
  const [year, number] = month.split("-").map(Number);
  return {
    from: `${month}-01`,
    to: `${month}-${new Date(Date.UTC(year, number, 0)).getUTCDate()}`,
  };
};
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api/${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${token()}`,
      ...(init.body instanceof FormData
        ? {}
        : { "Content-Type": "application/json" }),
      ...init.headers,
    },
  });
  const text = await response.text();
  if (!response.ok) {
    if (response.status === 401) {
      localStorage.removeItem("token");
      location.assign("/login");
    }
    let message = text || `Request failed (${response.status}). Try again.`;
    try {
      const parsed = JSON.parse(text);
      message = parsed.message || parsed.title || message;
    } catch {
      /* Plain text API error. */
    }
    throw new Error(message);
  }
  return text ? (JSON.parse(text) as T) : (undefined as T);
}
export type CategoryTotal = {
  category: string;
  limit: number | null;
  spent: number;
  remaining: number | null;
  progress: number | null;
  status: string;
};
export type MonthlySummary = {
  year: number;
  month: number;
  hasBudget: boolean;
  budget: number;
  spent: number;
  remaining: number;
  status: string;
  needsReconfirmation: boolean;
  income: number;
  netCashFlow: number;
  categories: CategoryTotal[];
  trends: { month: string; income: number; expenses: number }[];
};
export const analytics = (month: string) =>
  api<MonthlySummary>(`budget/analytics?${monthQuery(month)}`);
export type DateRange = { from: string; to: string };
export const netCashFlow = (range: DateRange, signal?: AbortSignal) =>
  api<{ netCashFlow: number }>(`transaction/net-cash-flow?${new URLSearchParams(range)}`, { signal });
