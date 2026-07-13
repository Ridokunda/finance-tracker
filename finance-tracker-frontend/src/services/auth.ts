export async function register(data: { username: string; passwordHash: string }) {
  const res = await fetch("/api/auth/register", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data)
  });

  const contentType = res.headers.get("content-type") || "";
  if (!res.ok) {
    const msg = await res.text().catch(() => "");
    throw new Error(msg || `HTTP ${res.status}`);
  }
  if (contentType.includes("application/json")) {
    return res.json();
  }
  const text = await res.text();
  return { message: text || "ok" } as { message: string };
}

export async function login(data: { username: string; passwordHash: string }) {
  const res = await fetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data)
  });

  return res.json();
}
export async function logout(token: string) {
  const res = await fetch("/api/auth/logout", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`
    }
  });

  if (!res.ok) {
    const msg = await res.text().catch(() => "");
    throw new Error(msg || `HTTP ${res.status}`);
  }

  const contentType = res.headers.get("content-type") || "";
  if (contentType.includes("application/json")) {
    return res.json();
  }

  const text = await res.text().catch(() => "");
  return { message: text || "Logged out" } as { message: string };
}
