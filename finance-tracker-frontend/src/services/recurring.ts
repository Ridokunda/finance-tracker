import { api, monthQuery } from "./finance";
import type { Transaction } from "./transaction";

export type Bill = { id: number; name: string; matchText: string; amount: number; category: string; frequency: string; firstDueDate: string; endDate: string | null; enabled: boolean; nextDueDate: string | null };
export type BillInput = Omit<Bill, "id" | "nextDueDate">;
export type BillOccurrence = { billId: number; name: string; category: string; amount: number; dueDate: string; status: "upcoming" | "due" | "overdue" | "paid" | "payment-deleted" | "payment-changed"; transactionId: number | null; paymentDate: string | null; paidAmount: number | null };
export const getBills = (signal?: AbortSignal) => api<Bill[]>("recurring-bills", { signal });
export const getOccurrences = (month: string, signal?: AbortSignal) => api<BillOccurrence[]>(`recurring-bills/occurrences?${monthQuery(month)}`, { signal });
export const saveBill = (bill: BillInput, id?: number) => api<Bill>(id ? `recurring-bills/${id}` : "recurring-bills", { method: id ? "PUT" : "POST", body: JSON.stringify(bill) });
export const archiveBill = (id: number) => api<void>(`recurring-bills/${id}`, { method: "DELETE" });
export const paymentCandidates = (id: number, date: string, amount: number, signal?: AbortSignal) => api<Transaction[]>(`recurring-bills/${id}/payment-candidates?${new URLSearchParams({ date, amount: String(amount) })}`, { signal });
export const payBill = (id: number, data: { dueDate: string; paymentDate?: string; amount?: number; transactionId?: number; confirmSeparateExpense?: boolean }) => api<{ transactionId: number }>(`recurring-bills/${id}/payments`, { method: "POST", body: JSON.stringify(data) });
export const unlinkPayment = (id: number, dueDate: string) => api<void>(`recurring-bills/${id}/payments?${new URLSearchParams({ dueDate })}`, { method: "DELETE" });
