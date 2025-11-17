export async function uploadStatement(file: File, token: string) {
  const form = new FormData();
  form.append("file", file);

  const res = await fetch("/api/statement/upload", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`
    },
    body: form
  });

  if (!res.ok) {
    const errorText = await res.text().catch(() => '');
    throw new Error(errorText || `HTTP ${res.status}`);
  }

  const contentType = res.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    return res.json();
  }
  
  const text = await res.text();
  return { message: text, count: 0 };
}

export async function getTransactions(token: string) {
  const res = await fetch("/api/statement", {
    method: "GET",
    headers: {
      Authorization: `Bearer ${token}`
    }
  });

  if (!res.ok) {
    const errorText = await res.text().catch(() => '');
    throw new Error(errorText || `HTTP ${res.status}`);
  }

  const contentType = res.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    return res.json();
  }
  
  const text = await res.text();
  return { data: [], message: text };
}
