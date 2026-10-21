import { useState } from "react";
import { register } from "../services/auth";
import "./Auth.css";
import "./Dashboard.css";
export default function Signup() {
  const [username, setUsername] = useState("");
  const [passwordHash, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  return (
    <div className="dashboard-wrap">
      <header className="topbar">
        <div className="brand">
          <img className="logo" src="/logo.png" alt="" />
          <h2>FINANCE-TRACKER</h2>
        </div>
        <nav className="nav">
          <a href="/login">Login</a>
          <a href="/signup">Sign up</a>
        </nav>
      </header>
      <main className="auth-wrapper">
        <div className="card auth-card">
          <form
            onSubmit={async (event) => {
              event.preventDefault();
              setBusy(true);
              setError("");
              try {
                await register({ username, passwordHash });
                location.assign("/login");
              } catch (error) {
                setError(
                  (error as Error).message ||
                    "Registration failed. Please try again.",
                );
              } finally {
                setBusy(false);
              }
            }}
          >
            <h1>Create account</h1>
            <label>
              Username
              <input
                className="auth-input"
                autoComplete="username"
                required
                value={username}
                onChange={(e) => setUsername(e.target.value)}
              />
            </label>
            <label>
              Password
              <input
                className="auth-input"
                type="password"
                autoComplete="new-password"
                required
                value={passwordHash}
                onChange={(e) => setPassword(e.target.value)}
              />
            </label>
            <button className="auth-button" disabled={busy}>
              {busy ? "Creating…" : "Create account"}
            </button>
            {error && (
              <p role="alert" className="auth-error">
                {error}
              </p>
            )}
            <p>
              <a href="/login">Back to login</a>
            </p>
          </form>
        </div>
      </main>
    </div>
  );
}
