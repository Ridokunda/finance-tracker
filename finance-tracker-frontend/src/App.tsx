import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import Login from "./pages/Login";
import Signup from "./pages/Signup";
import Dashboard from "./pages/Dashboard";
import TransactionsPage from "./pages/TransactionsPage";
import ProtectedRoute from "./components/ProtectedRoute";
import Budgets from "./pages/Budgets";

function App() {
  return (
    <Router>
      <Routes>
        <Route path="/budgets" element={<ProtectedRoute><Budgets /></ProtectedRoute>} />
        {/* Public routes */}
        <Route path="/login" element={<Login />} />
        <Route path="/signup" element={<Signup />} />

        {/* Protected route */}
        <Route
          path="/"
          element={
            <ProtectedRoute>
              <Dashboard />
            </ProtectedRoute>
          }
        />
        <Route
          path="/transactions"
          element={
            <ProtectedRoute>
              <TransactionsPage />
            </ProtectedRoute>
          }
        />
      </Routes>
    </Router>
  );
}

export default App;
