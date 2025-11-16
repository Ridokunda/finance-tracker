import { useState, type FormEvent, type MouseEvent } from "react";
import { register } from "../services/auth";
import './Auth.css';
import './Dashboard.css';

export default function Signup() {
  const [username, setUsername] = useState("");
  const [passwordHash, setPassword] = useState("");
  const [message, setMessage] = useState("");
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

  async function handleSignup(e : FormEvent<HTMLFormElement>) {
    e.preventDefault();

    try {
      await register({ username, passwordHash });
      setMessage("successfully registered");
      setTimeout(() => {
        window.location.href = '/login';
      }, 1200);
    
    } catch {
      setMessage('Registration failed');
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
          <form onSubmit={handleSignup}>
            <h2>Sign Up</h2>

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

            <button className="auth-button" type="submit">Create Account</button>

            {message && <p className="auth-help">{message}</p>}
          </form>
        </div>
      </div>
    </div>
  );
}

