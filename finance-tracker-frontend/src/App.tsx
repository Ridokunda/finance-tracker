import { useEffect, useState } from "react";

function App() {
  const [message, setMessage] = useState("");

  useEffect(() => {
    fetch("/api/test")
      .then(res => res.json())
      .then(data => setMessage(data.message))
      .catch(() => setMessage("Error connecting to API"));
  }, []);

  return (
    <div style={{ padding: "2rem" }}>
      <h1>Frontend</h1>
      <p>API says: {message}</p>
    </div>
  );
}

export default App;
