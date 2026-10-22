import { NavLink } from "react-router-dom";
import { logout } from "../services/auth";
import { token } from "../services/finance";
export default function Navigation() {
  return (
    <header className="topbar">
      <div className="brand">
        <img src="/logo.png" alt="" className="logo" />
        <h2>FINANCE-TRACKER</h2>
      </div>
      <nav className="nav" aria-label="Main navigation">
        <NavLink to="/">Dashboard</NavLink>
        <NavLink to="/transactions">Transactions</NavLink>
        <NavLink to="/budgets">Budgets</NavLink>
        <NavLink to="/rules">Category rules</NavLink>
        <button
          className="secondary-btn"
          onClick={async () => {
            try {
              await logout(token());
            } finally {
              localStorage.removeItem("token");
              location.assign("/login");
            }
          }}
        >
          Logout
        </button>
      </nav>
    </header>
  );
}
