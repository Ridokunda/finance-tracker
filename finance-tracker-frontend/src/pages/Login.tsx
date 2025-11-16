import { useState, type FormEvent, type MouseEvent } from "react";
import { login } from "../services/auth";
import './Auth.css';
import './Dashboard.css';

export default function Login() {
  const [username, setUsername] = useState("");
  const [passwordHash, setPassword] = useState("");
  const [error, setError] = useState("");
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(() => {
    try { return Boolean(localStorage.getItem('token')); } catch { return false; }
  });

  function handleAuthClick(e: MouseEvent) {
    e.preventDefault();
    if (isLoggedIn) {
      try { localStorage.removeItem('token'); } catch { /* ignore */ }
      setIsLoggedIn(false);
      window.location.href = '/login';
    } else {
      window.location.href = '/login';
    }
  }

  async function handleLogin(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();

    try {
      const res = await login({ username, passwordHash });
      if (res.token) {
        localStorage.setItem("token", res.token);
        window.location.href = "/";
      } else {
        setError("Invalid credentials");
      }
    } catch {
      setError("Something broke. Not your fault (probably).");
    }
  }

  return (
    <div className="dashboard-wrap">
      <div className="topbar">
        <div className="brand">
          <div className="logo" />
          <h2 style={{ margin: 0 }}>FINANCE-TRACKER</h2>
        </div>
        <nav className="nav">
          <a>Dashboard</a>
          <a>Expenses</a>
          <a>Reports</a>
          <a>Categories</a>
          <a>Settings</a>
          <a onClick={handleAuthClick} role="button" style={{ cursor: 'pointer' }}>{isLoggedIn ? 'Logout' : 'Login'}</a>
        </nav>
      </div>

      <div className="auth-wrapper">
        <div className="card auth-card">
          <form onSubmit={handleLogin}>
            <h2>Login</h2>

          <input
            className="auth-input"
            type="text"
            placeholder="Username"
            value={username}
            onChange={e => setUsername(e.target.value)}
          />

          <input
            className="auth-input"
            type="password"
            placeholder="Password"
            value={passwordHash}
            onChange={e => setPassword(e.target.value)}
          />

            <button className="auth-button" type="submit">Login</button>

            {error && <p className="auth-error">{error}</p>}
            <p className="auth-help" style={{ marginTop: 4 }}>
              Don't have an account? <a href="/signup">Sign up</a>
            </p>
          </form>
        </div>
      </div>
    </div>
  );
}
