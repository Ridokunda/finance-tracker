# Finance Tracker frontend

React 19 + TypeScript + Vite. See the [repository README](../README.md) for complete startup, database and classifier configuration, supported statement limitations, demo credentials, screenshots and test commands.

```powershell
npm ci
$env:VITE_API_TARGET = 'http://127.0.0.1:5260'
npm run dev -- --host 127.0.0.1 --port 5174
```

Checks: `npm run lint`, `npm run build`, `npx playwright test`. Browser tests require the seeded API with the classifier outage endpoint described in the root README.
