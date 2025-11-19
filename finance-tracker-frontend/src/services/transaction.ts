export async function getTransactions(token: string, type?: string, category?: string): Promise<unknown[]> {
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

export interface CreateTransactionPayload {
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

function normalizeTransactions(payload: unknown): unknown[] {
    if (Array.isArray(payload)) {
        return payload;
    }

    if (payload && typeof payload === "object") {
        const record = payload as Record<string, unknown>;
        if (Array.isArray(record.data)) {
            return record.data;
        }
        if (Array.isArray(record.$values)) {
            return record.$values;
        }
    }

    return [];
}