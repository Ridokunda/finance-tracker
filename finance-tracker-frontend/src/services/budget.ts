export interface BudgetSummary {
  budget: number;
  spent: number;
  remaining: number;
  status: string;
}

async function readBudgetResponse(res: Response): Promise<BudgetSummary> {
  const payload = await res.json().catch(() => ({}));

  if (!res.ok) {
    const message = payload && typeof payload === "object" && "message" in payload
      ? String(payload.message)
      : payload && typeof payload === "object" && "title" in payload
        ? String(payload.title)
        : `Failed to load budget (${res.status})`;
    throw new Error(message);
  }

  const result = payload && typeof payload === "object" ? payload as Record<string, unknown> : {};
  return {
    budget: typeof result.budget === "number" ? result.budget : 0,
    spent: typeof result.spent === "number" ? result.spent : 0,
    remaining: typeof result.remaining === "number" ? result.remaining : 0,
    status: typeof result.status === "string" ? result.status : "ok"
  };
}

export async function getBudget(token: string): Promise<BudgetSummary> {
  const res = await fetch("/api/budget/summary", {
    headers: { Authorization: `Bearer ${token}` }
  });

  return readBudgetResponse(res);
}

export async function setBudget(token: string, amount: number): Promise<BudgetSummary> {
  const res = await fetch("/api/budget/set", {
    method: "POST",
    headers: { 
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}` 
    },
    body: JSON.stringify({ limitAmount: amount })
  });

  return readBudgetResponse(res);
}
