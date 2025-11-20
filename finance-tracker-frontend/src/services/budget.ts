export async function getBudget(token: string) {
  const res = await fetch("/api/budget/summary", {
    headers: { Authorization: `Bearer ${token}` }
  });

  return res.json();
}

export async function setBudget(token: string, amount: number) {
  const res = await fetch("/api/budget/set", {
    method: "POST",
    headers: { 
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}` 
    },
    body: JSON.stringify({ limitAmount: amount })
  });

  return res.json();
}
