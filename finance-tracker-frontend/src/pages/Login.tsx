import { useState, type FormEvent } from "react";
import { login } from "../services/auth";

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
        window.location.href = "/dashboard";
      } else {
        setError("Invalid credentials");
      }
    } catch {
      setError("Something broke. Not your fault (probably).");
    }
  }

  return (
    <form onSubmit={handleLogin}>
      <h2>Login</h2>
      <input 
        type="text"
        placeholder="Username"
        value={username}
        onChange={e => setUsername(e.target.value)}
      />
      <input 
        type="password"
        placeholder="Password"
        value={passwordHash}
        onChange={e => setPassword(e.target.value)}
      />
      <button type="submit">Login</button>

      {error && <p>{error}</p>}
    </form>
  );
}
