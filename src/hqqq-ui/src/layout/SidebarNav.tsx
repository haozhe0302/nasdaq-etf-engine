import { NavLink } from "react-router-dom";

const navItems = [
  { to: "/market", label: "Market" },
  { to: "/constituents", label: "Constituents" },
  { to: "/history", label: "History" },
  { to: "/system", label: "System" },
] as const;

export function SidebarNav() {
  return (
    <nav className="flex w-48 shrink-0 flex-col border-r border-edge bg-surface pt-3">
      <span className="px-4 pb-2 text-[11px] font-semibold uppercase tracking-wider text-muted">
        Navigation
      </span>
      {navItems.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          className={({ isActive }) =>
            isActive
              ? "border-r-2 border-accent bg-accent/10 px-4 py-1.5 text-sm text-accent"
              : "px-4 py-1.5 text-sm text-muted hover:text-content"
          }
        >
          {item.label}
        </NavLink>
      ))}
    </nav>
  );
}
