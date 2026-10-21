import { useState } from "react";
import { confirmStatement, previewStatement } from "../services/statement";
import type { Mapping, Preview } from "../services/statement";
import { currency } from "../services/finance";
import CategorySelect from "./CategorySelect";
import Dialog from "./Dialog";
export default function StatementUploader({
  onUploaded,
}: {
  token: string;
  onUploaded?: () => void | Promise<void>;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [mapping, setMapping] = useState<Mapping>({
    dateColumn: "Date",
    descriptionColumn: "Description",
    amountColumn: "Amount",
    dateFormat: "yyyy-MM-dd",
    delimiter: "",
  });
  const [preview, setPreview] = useState<Preview | null>(null);
  const [reviewOpen, setReviewOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [status, setStatus] = useState("");
  const [columns, setColumns] = useState<string[]>([]);
  const [page, setPage] = useState(0);
  const selected = preview?.rows.filter((r) => r.included) ?? [];
  return (
    <div className="statement-uploader">
      <p>
        Generic CSV or text-based ABSA PDF · maximum 10 MB. PDF layout
        verification is pending sanitized samples.
      </p>
      <label>
        Statement file
        <input
          type="file"
          accept=".csv,.pdf"
          disabled={busy}
          onChange={(e) => {
            setFile(e.target.files?.[0] ?? null);
            setPreview(null);
            setColumns([]);
            setStatus("");
            setError("");
          }}
        />
      </label>
      {file?.name.toLowerCase().endsWith(".csv") && (
        <fieldset disabled={busy}>
          <legend>CSV format</legend>
          <div className="filter-row">
            {(["dateColumn", "descriptionColumn", "amountColumn"] as const).map(
              (key) => (
                <label key={key}>
                  {key === "dateColumn"
                    ? "Date column"
                    : key === "descriptionColumn"
                      ? "Description column"
                      : "Signed amount column"}
                  {columns.length ? (
                    <select
                      value={mapping[key]}
                      onChange={(e) => {
                        setMapping({ ...mapping, [key]: e.target.value });
                        setPreview(null);
                      }}
                    >
                      <option value="">Select column</option>
                      {columns.map((c, i) => (
                        <option key={`${i}-${c}`}>{c}</option>
                      ))}
                    </select>
                  ) : (
                    <input
                      value={mapping[key]}
                      onChange={(e) => {
                        setMapping({ ...mapping, [key]: e.target.value });
                        setPreview(null);
                      }}
                    />
                  )}
                </label>
              ),
            )}
            <label>
              Date format
              <select
                value={mapping.dateFormat}
                onChange={(e) => {
                  setMapping({ ...mapping, dateFormat: e.target.value });
                  setPreview(null);
                }}
              >
                {[
                  "yyyy-MM-dd",
                  "dd/MM/yyyy",
                  "MM/dd/yyyy",
                  "dd-MM-yyyy",
                  "MM-dd-yyyy",
                  "yyyy/MM/dd",
                ].map((f) => (
                  <option key={f}>{f}</option>
                ))}
              </select>
            </label>
            <label>
              Delimiter
              <select
                value={mapping.delimiter}
                onChange={(e) => {
                  setMapping({ ...mapping, delimiter: e.target.value });
                  setPreview(null);
                }}
              >
                <option value="">Detect automatically</option>
                <option value=",">Comma</option>
                <option value=";">Semicolon</option>
                <option value={"\t"}>Tab</option>
                <option value="|">Pipe</option>
              </select>
            </label>
          </div>
          <p>
            Positive amounts are inflows; negative amounts are expenses. Use a
            decimal point. Choose the date order explicitly.
          </p>
        </fieldset>
      )}
      <button
        className="primary-btn"
        disabled={!file || busy}
        onClick={async () => {
          if (!file) return;
          if (file.size > 10 * 1024 * 1024) {
            setError("Statements must be 10 MB or smaller.");
            return;
          }
          setBusy(true);
          setError("");
          setStatus("");
          try {
            const result = await previewStatement(file, mapping);
            setColumns(result.columns);
            setPreview(result);
            setPage(0);
            setReviewOpen(result.rows.length > 0);
            if (!result.rows.length) setStatus(result.notices.join(" "));
          } catch (error) {
            setError((error as Error).message);
          } finally {
            setBusy(false);
          }
        }}
      >
        {busy ? "Processing…" : "Preview statement"}
      </button>
      {preview?.id && !preview.confirmedAt && (
        <button className="secondary-btn" onClick={() => setReviewOpen(true)}>
          Resume review
        </button>
      )}
      {!reviewOpen && error && (
        <p role="alert" className="dashboard-error">
          {error}
        </p>
      )}
      <p role="status">{status}</p>
      {reviewOpen && preview && (
        <Dialog
          title="Review statement"
          onClose={() => {
            if (!busy) setReviewOpen(false);
          }}
        >
          <p>
            Nothing is saved to transactions until you confirm. Preview expires{" "}
            {new Date(preview.expiresAt).toLocaleString("en-ZA", {
              timeZone: "Africa/Johannesburg",
            })}{" "}
            SAST.
          </p>
          {preview.statementStart && (
            <p>
              Statement period: {preview.statementStart.slice(0, 10)} –{" "}
              {preview.statementEnd?.slice(0, 10)}
            </p>
          )}
          {preview.notices.map((notice) => (
            <p className="notice" role="status" key={notice}>
              {notice}
            </p>
          ))}
          <p>
            Possible duplicates start excluded. To retain a legitimate repeat,
            select Include and Keep duplicate. If another import has since added
            a matching row, use Keep duplicate after reviewing the conflict.
          </p>
          <div className="table-scroll">
            <table className="finance-table">
              <thead>
                <tr>
                  <th>Include</th>
                  <th>Date</th>
                  <th>Description / validation</th>
                  <th>Signed amount</th>
                  <th>Category</th>
                  <th>Duplicate review</th>
                </tr>
              </thead>
              <tbody>
                {preview.rows.slice(page * 50, page * 50 + 50).map((row) => (
                  <tr key={row.id}>
                    <td>
                      <input
                        type="checkbox"
                        aria-label={`Include row ${row.rowNumber}`}
                        checked={row.included}
                        disabled={!!row.error || busy}
                        onChange={(e) =>
                          setPreview({
                            ...preview,
                            rows: preview.rows.map((r) =>
                              r.id === row.id
                                ? { ...r, included: e.target.checked }
                                : r,
                            ),
                          })
                        }
                      />
                    </td>
                    <td>{row.date?.slice(0, 10) || "Invalid date"}</td>
                    <td>
                      {row.description}
                      <span className="row-error">{row.error}</span>
                      {row.possibleDuplicate && (
                        <strong>Possible duplicate</strong>
                      )}
                    </td>
                    <td>
                      {row.amount === null
                        ? "Invalid amount"
                        : currency(row.amount)}
                    </td>
                    <td>
                      <CategorySelect
                        label={`Category row ${row.rowNumber}`}
                        value={row.category}
                        onChange={(category) =>
                          setPreview({
                            ...preview,
                            rows: preview.rows.map((r) =>
                              r.id === row.id ? { ...r, category } : r,
                            ),
                          })
                        }
                      />
                    </td>
                    <td>
                      <label>
                        <input
                          type="checkbox"
                          checked={!!row.keepDuplicate}
                          disabled={busy || !!row.error}
                          onChange={(e) =>
                            setPreview({
                              ...preview,
                              rows: preview.rows.map((r) =>
                                r.id === row.id
                                  ? { ...r, keepDuplicate: e.target.checked }
                                  : r,
                              ),
                            })
                          }
                        />
                        Keep duplicate
                      </label>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {preview.rows.length > 50 && (
            <div className="filter-row">
              <button disabled={page === 0} onClick={() => setPage(page - 1)}>
                Previous rows
              </button>
              <span>
                Page {page + 1} of {Math.ceil(preview.rows.length / 50)}
              </span>
              <button
                disabled={(page + 1) * 50 >= preview.rows.length}
                onClick={() => setPage(page + 1)}
              >
                Next rows
              </button>
            </div>
          )}
          <p>
            {selected.length} selected · Income{" "}
            {currency(
              selected.reduce((s, r) => s + Math.max(r.amount ?? 0, 0), 0),
            )}{" "}
            · Expenses{" "}
            {currency(
              selected.reduce((s, r) => s - Math.min(r.amount ?? 0, 0), 0),
            )}
          </p>
          {error && (
            <p role="alert" className="dashboard-error">
              {error}
            </p>
          )}
          <button
            className="primary-btn"
            disabled={
              busy ||
              !selected.length ||
              selected.some((r) => r.possibleDuplicate && !r.keepDuplicate)
            }
            onClick={async () => {
              setBusy(true);
              setError("");
              try {
                const result = await confirmStatement(preview);
                setPreview({
                  ...preview,
                  confirmedAt: new Date().toISOString(),
                });
                setReviewOpen(false);
                setStatus(
                  `Imported ${result.count} transactions. Budget limits are unchanged.`,
                );
                await onUploaded?.();
              } catch (error) {
                setError((error as Error).message);
              } finally {
                setBusy(false);
              }
            }}
          >
            {busy ? "Confirming…" : `Confirm ${selected.length} transactions`}
          </button>
        </Dialog>
      )}
    </div>
  );
}
