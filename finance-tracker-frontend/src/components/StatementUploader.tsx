import { useState } from 'react';
import { uploadStatement } from '../services/statement';
export default

function StatementUploader({ token, onUploaded }: { token: string; onUploaded?: () => void | Promise<void> }) {
  const [file, setFile] = useState<File | null>(null);
  const [status, setStatus] = useState("");

  const handleUpload = async () => {
    if (!file) {
      setStatus("Choose a file first.");
      return;
    }

    try {
      const result = await uploadStatement(file, token);
      setStatus(`Uploaded ${result.count} transactions.`);
      await onUploaded?.();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to upload statement.');
    }
  };

  return (
    <div>
      <input 
        type="file" 
        accept=".csv,text/csv,.pdf,application/pdf" 
        onChange={e => setFile(e.target.files?.[0] || null)}
      />
      <button onClick={handleUpload}>Upload</button>
      <p>{status}</p>
    </div>
  );
}
