import { api } from "./finance";

export type CategoryRule = { id: number; pattern: string; category: string; enabled: boolean };
export type RuleInput = Omit<CategoryRule, "id">;
export const getRules = (signal?: AbortSignal) => api<CategoryRule[]>("category-rules", { signal });
export const saveRule = (rule: RuleInput, id?: number) => api<CategoryRule>(id ? `category-rules/${id}` : "category-rules", {
  method: id ? "PUT" : "POST", body: JSON.stringify(rule),
});
export const deleteRule = (id: number) => api<void>(`category-rules/${id}`, { method: "DELETE" });

export function matchingRule(rules: CategoryRule[], description: string) {
  const text = description.trim().replace(/\s+/g, " ").toLowerCase();
  return rules.filter(rule => rule.enabled && text.includes(rule.pattern.toLowerCase()))
    .sort((a, b) => b.pattern.length - a.pattern.length || a.id - b.id)[0];
}
