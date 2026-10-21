export default function MonthSelector({
  month,
  onChange,
  disabled = false,
}: {
  month: string;
  onChange: (value: string) => void;
  disabled?: boolean;
}) {
  return (
    <label className="month-selector">
      Month{" "}
      <input
        type="month"
        disabled={disabled}
        min="1900-01"
        max="9998-12"
        value={month}
        onChange={(event) => {
          if (!event.target.value) return;
          localStorage.setItem("month", event.target.value);
          const url = new URL(location.href);
          url.searchParams.set("month", event.target.value);
          history.replaceState(null, "", url);
          onChange(event.target.value);
        }}
      />
    </label>
  );
}
