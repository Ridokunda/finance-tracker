import { useCallback, useEffect, useState } from "react";
import { getTransactions } from "../services/transaction";

interface Props {
  token: string;
  setTransactions: (data: unknown[]) => void;
  onLoadingChange?: (value: boolean) => void;
  onError?: (message: string) => void;
}

export default function TransactionFilters({ token, setTransactions, onLoadingChange, onError }: Props) {
  const [type, setType] = useState("");
  const [category, setCategory] = useState("");

  const loadFiltered = useCallback(async () => {
    if (!token) {
      setTransactions([]);
      onError?.("Authentication required.");
      return;
    }

    try {
      onLoadingChange?.(true);
      const data = await getTransactions(token, type, category);
      setTransactions(data);
      onError?.("");
    } catch (err) {
      console.error("Failed to fetch filtered transactions", err);
      setTransactions([]);
      onError?.("Unable to fetch transactions right now.");
    } finally {
      onLoadingChange?.(false);
    }
  }, [token, type, category, onError, onLoadingChange, setTransactions]);

  useEffect(() => {
    loadFiltered();
  }, [loadFiltered]);

  return (
    <div style={{ marginBottom: "1rem", display: "flex", gap: "1rem" }}>
      <select 
        value={type} 
        onChange={(e) => setType(e.target.value)}
      >
        <option value="">All Types</option>
        <option value="income">Income</option>
        <option value="expense">Expense</option>
      </select>

      <select 
        value={category} 
        onChange={(e) => setCategory(e.target.value)}
      >
        <option value="">All Categories</option>
        <option value="Income">Income</option>
        <option value="Groceries">Groceries</option>
        <option value="Transport">Transport</option>
        <option value="Shopping">Shopping</option>
        <option value="Utilities">Utilities</option>
        <option value="Rent">Rent</option>
        <option value="Uncategorized">Uncategorized</option>
      </select>
    </div>
  );
}
