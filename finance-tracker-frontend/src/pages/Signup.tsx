import { useState, type FormEvent } from "react";
import { register } from "../services/auth";

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
    <form onSubmit={handleSignup}>
      <h2>Sign Up</h2>

      <input 
        type="email"
        placeholder="Email"
        value={email}
        onChange={e => setEmail(e.target.value)}
      />

      <input 
        type="password"
        placeholder="Password"
        value={password}
        onChange={e => setPassword(e.target.value)}
      />

      <button type="submit">Create Account</button>

      {message && <p>{message}</p>}
    </form>
  );
}

