import { api } from "./finance";
export type PreviewRow = {
  id: number;
  rowNumber: number;
  date: string | null;
  description: string;
  amount: number | null;
  category: string;
  error: string | null;
  possibleDuplicate: boolean;
  included: boolean;
  keepDuplicate?: boolean;
};
export type Preview = {
  id?: string;
  expiresAt: string;
  confirmedAt: string | null;
  confirmedCount: number;
  notices: string[];
  columns: string[];
  rows: PreviewRow[];
  statementStart?: string;
  statementEnd?: string;
};
export type Mapping = {
  dateColumn: string;
  descriptionColumn: string;
  amountColumn: string;
  dateFormat: string;
  delimiter: string;
};
export async function previewStatement(file: File, mapping: Mapping) {
  const form = new FormData();
  form.append("file", file);
  Object.entries(mapping).forEach(([key, value]) => form.append(key, value));
  return api<Preview>("statement/preview", { method: "POST", body: form });
}
export const confirmStatement = (preview: Preview) =>
  api<{ id: string; count: number }>(`statement/${preview.id}/confirm`, {
    method: "POST",
    body: JSON.stringify({
      rows: preview.rows.map(({ id, category, included, keepDuplicate }) => ({
        id,
        category,
        included,
        keepDuplicate: !!keepDuplicate,
      })),
    }),
  });
