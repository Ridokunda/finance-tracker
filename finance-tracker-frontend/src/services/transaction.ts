export interface Transaction {
    id: number;
    date: string;
    description: string;
    amount: number;
    category: string;
}

export interface ClassificationResult {
    category: string;
}

export async function getTransactions(token: string, type?: string, category?: string): Promise<Transaction[]> {
    if (!token) {
        return [];
    }

    const params = new URLSearchParams();
    if (type) params.append("type", type);
    if (category) params.append("category", category);

    const query = params.toString();
    const url = query ? `/api/transaction?${query}` : "/api/transaction";

    const res = await fetch(url, {
        headers: {
            Authorization: `Bearer ${token}`
        }
    });

    if (!res.ok) {
        const message = await res.text().catch(() => "");
        throw new Error(message || `Failed to fetch transactions (${res.status})`);
    }

    const payload = await res.json().catch(() => []);
    return normalizeTransactions(payload);
}

export async function classifyTransaction(token: string, description: string): Promise<ClassificationResult> {
    if (!token) {
        throw new Error("Authentication required.");
    }

    const res = await fetch("/api/transaction/classify", {
        method: "POST",
        headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ description })
    });

    const payload = await res.json().catch(() => ({}));
    if (!res.ok) {
        const message = payload && typeof payload === "object" && "message" in payload
            ? String(payload.message)
            : payload && typeof payload === "object" && "title" in payload
                ? String(payload.title)
                : `Failed to classify transaction (${res.status})`;
        throw new Error(message);
    }

    const result = payload && typeof payload === "object" ? payload as Record<string, unknown> : {};
    const predictedCategory = typeof result.category === "string"
        ? result.category
        : typeof result.Category === "string"
            ? result.Category
            : "";

    if (!predictedCategory) {
        throw new Error("The classification service returned no category.");
    }

    return { category: predictedCategory };
}

export interface CreateTransactionPayload {
    date: string;
    description: string;
    amount: number;
    category?: string;
}

export interface UpdateTransactionPayload {
    date: string;
    description: string;
    amount: number;
    category?: string;
}

export async function createTransaction(token: string, payload: CreateTransactionPayload): Promise<unknown> {
    if (!token) {
        throw new Error("Authentication required.");
    }

    const res = await fetch("/api/transaction", {
        method: "POST",
        headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify(payload)
    });

    if (!res.ok) {
        const message = await res.text().catch(() => "");
        throw new Error(message || `Failed to create transaction (${res.status})`);
    }

    return res.json().catch(() => ({}));
}

export async function updateTransaction(token: string, id: number, payload: UpdateTransactionPayload): Promise<unknown> {
    if (!token) {
        throw new Error("Authentication required.");
    }

    const res = await fetch(`/api/transaction/${id}`, {
        method: "PUT",
        headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: JSON.stringify(payload)
    });

    if (!res.ok) {
        const message = await res.text().catch(() => "");
        throw new Error(message || `Failed to update transaction (${res.status})`);
    }

    return res.json().catch(() => ({}));
}

function normalizeTransactions(payload: unknown): Transaction[] {
    if (Array.isArray(payload)) {
        return payload as Transaction[];
    }

    if (payload && typeof payload === "object") {
        const record = payload as Record<string, unknown>;
        if (Array.isArray(record.data)) {
            return record.data as Transaction[];
        }
        if (Array.isArray(record.$values)) {
            return record.$values as Transaction[];
        }
    }

    return [];
}
