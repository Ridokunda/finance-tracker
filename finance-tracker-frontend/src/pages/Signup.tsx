import { useState, type FormEvent } from "react";
import { register } from "../services/auth";
import './Dashboard.css';

export default function Signup() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [message, setMessage] = useState("");

  async function handleSignup(e : FormEvent<HTMLFormElement>) {
    e.preventDefault();

    const res = await register({ email, password });
    setMessage(res.message);
  }

  return (
    <div className="auth-wrapper">
      <div className="card auth-card">
        <form onSubmit={handleSignup}>
          <h2>Sign Up</h2>

          <input
            className="auth-input"
            type="email"
            placeholder="Email"
            value={email}
            onChange={e => setEmail(e.target.value)}
          />

          <input
            className="auth-input"
            type="password"
            placeholder="Password"
            value={password}
            onChange={e => setPassword(e.target.value)}
          />

          <button className="auth-button" type="submit">Create Account</button>

          {message && <p className="auth-help">{message}</p>}
        </form>
      </div>
    </div>
  );
}

