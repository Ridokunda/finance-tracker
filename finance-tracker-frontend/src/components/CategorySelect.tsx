import { useEffect, useState } from "react";
import { api, token } from "../services/finance";

// Keep the built-in choices aligned with FinanceRules.Categories in the API.
const defaultCategories = [
  "Income",
  "Groceries",
  "Transport",
  "Shopping",
  "Utilities",
  "Rent",
  "Food",
  "Entertainment",
  "Healthcare",
  "Education",
  "Insurance",
  "Fees",
  "Uncategorized",
];

let cached:
  | { token: string; time: number; promise: Promise<string[]> }
  | undefined;
function choices() {
  const currentToken = token();
  if (
    !cached ||
    cached.token !== currentToken ||
    Date.now() - cached.time > 30000
  ) {
    cached = {
      token: currentToken,
      time: Date.now(),
      promise: api<string[]>("transaction/categories"),
    };
    cached.promise.catch(() => {
      cached = undefined;
    });
  }
  return cached.promise;
}
export default function CategorySelect({
  value,
  onChange,
  label = "Category",
  allowAll = false,
}: {
  value: string;
  onChange: (value: string) => void;
  label?: string;
  allowAll?: boolean;
}) {
  const [categories, setCategories] = useState<string[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    choices()
      .then(setCategories)
      .catch(() =>
        setError(
          "Custom categories could not load. You can still select a standard category.",
        ),
      );
  }, []);
  return (
    <label>
      {label}
      <select
        aria-label={label}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {allowAll && <option value="">All categories</option>}
        {[
          ...new Set([
            ...defaultCategories,
            ...categories,
            ...(value ? [value] : []),
          ]),
        ]
          .sort()
          .map((category) => (
            <option key={category}>{category}</option>
          ))}
      </select>
      {error && <span role="status">{error}</span>}
    </label>
  );
}
