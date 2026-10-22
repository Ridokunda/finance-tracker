import { useEffect, useRef } from "react";
import { currency } from "../services/finance";
import type { Transaction } from "../services/transaction";

interface Props {
  transactions: Transaction[];
  selected: number[];
  onSelectionChange: (ids: number[]) => void;
  onEdit: (transaction: Transaction) => void;
  onDelete: (ids: number[]) => void;
  onRestore: (ids: number[]) => void;
  deleted: boolean;
  disabled: boolean;
}

export default function TransactionsList({ transactions, selected, onSelectionChange, onEdit, onDelete, onRestore, deleted, disabled }: Props) {
  const selectAll = useRef<HTMLInputElement>(null);
  const listedIds = transactions.slice(0, 5000).map(t => t.id);
  const allSelected = listedIds.length > 0 && listedIds.every(id => selected.includes(id));
  useEffect(() => {
    if (selectAll.current) selectAll.current.indeterminate = selected.length > 0 && !allSelected;
  }, [selected.length, allSelected]);

  if (transactions.length === 0) return <p>{deleted ? "No deleted transactions match these filters." : "No transactions found."}</p>;

  return (
    <table className="finance-table transaction-table">
      <caption className="sr-only">{deleted ? "Deleted transactions" : "Transactions"}</caption>
      <thead>
        <tr>
          <th scope="col"><input ref={selectAll} type="checkbox" aria-label="Select all listed transactions"
            checked={allSelected} disabled={disabled}
            onChange={event => onSelectionChange(event.target.checked ? listedIds : [])} /></th>
          <th scope="col">Date</th><th scope="col">Description</th><th scope="col">Category</th>
          <th scope="col">Amount</th><th scope="col">Actions</th>
        </tr>
      </thead>
      <tbody>
        {transactions.map(transaction => (
          <tr key={transaction.id} className={selected.includes(transaction.id) ? "is-selected" : undefined}>
            <td><input type="checkbox" aria-label={`Select transaction ${transaction.description}`}
              checked={selected.includes(transaction.id)}
              disabled={disabled || (!selected.includes(transaction.id) && selected.length >= 5000)}
              onChange={event => onSelectionChange(event.target.checked ? [...selected, transaction.id] : selected.filter(id => id !== transaction.id))} /></td>
            <td>{transaction.date.slice(0, 10)}</td><td>{transaction.description}</td><td>{transaction.category}</td>
            <td className={transaction.amount < 0 ? "amount-expense" : "amount-income"}>{currency(transaction.amount)}</td>
            <td><div className="transaction-actions">{deleted ? (
              <button className="secondary-btn" disabled={disabled} onClick={() => onRestore([transaction.id])}>Restore</button>
            ) : (<>
              <button className="secondary-btn" disabled={disabled} onClick={() => onEdit(transaction)}>Edit</button>
              <button className="danger-btn" disabled={disabled} onClick={() => onDelete([transaction.id])}>Delete</button>
            </>)}</div></td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
