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