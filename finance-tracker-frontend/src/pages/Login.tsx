import { useState, type FormEvent } from "react";
import { login } from "../services/auth";
import './Dashboard.css';

export default function Login() {
  const [username, setUsername] = useState("");
  const [passwordHash, setPassword] = useState("");
  const [error, setError] = useState("");

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
        </form>
      </div>
    </div>
  );
}
