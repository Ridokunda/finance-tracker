interface Props {
  transactions: {
    id: number;
    date: string;
    description: string;
    amount: number;
    category: string;
  }[];
}

export default function TransactionsList({ transactions }: Props) {
  if (transactions.length === 0) {
    return <p>No transactions found.</p>;
  }

  return (
    <table 
      style={{ 
        width: "100%", 
        borderCollapse: "collapse", 
        marginTop: "1rem" 
      }}
    >
      <thead>
        <tr style={{ backgroundColor: "#f0f0f0" }}>
          <th style={{ padding: "8px", border: "1px solid #ccc" }}>Date</th>
          <th style={{ padding: "8px", border: "1px solid #ccc" }}>Description</th>
          <th style={{ padding: "8px", border: "1px solid #ccc" }}>Category</th>
          <th style={{ padding: "8px", border: "1px solid #ccc" }}>Amount</th>
        </tr>
      </thead>

      <tbody>
        {transactions.map((t) => (
          <tr key={t.id}>
            <td style={{ padding: "8px", border: "1px solid #ddd" }}>
              {new Date(t.date).toLocaleDateString()}
            </td>
            <td style={{ padding: "8px", border: "1px solid #ddd" }}>
              {t.description}
            </td>
            <td style={{ padding: "8px", border: "1px solid #ddd" }}>
              {t.category}
            </td>
            <td 
              style={{ 
                padding: "8px", 
                border: "1px solid #ddd",
                color: t.amount < 0 ? "red" : "green"
              }}
            >
              {t.amount.toFixed(2)}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
