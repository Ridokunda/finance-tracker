import React, { useState } from 'react';
import { uploadStatement } from '../services/statement';
export default

function StatementUploader({ token }: { token: string }) {
  const [file, setFile] = useState<File | null>(null);
  const [status, setStatus] = useState("");

  const handleUpload = async () => {
    if (!file) {
      setStatus("Choose a file first.");
      return;
    }

    const result = await uploadStatement(file, token);
    setStatus(`Uploaded ${result.count} transactions.`);
  };

  return (
    <div>
      <input 
        type="file" 
        accept=".csv" 
        onChange={e => setFile(e.target.files?.[0] || null)}
      />
      <button onClick={handleUpload}>Upload</button>
      <p>{status}</p>
    </div>
  );
}
