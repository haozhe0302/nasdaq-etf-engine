import { Outlet } from "react-router-dom";
import { TopStatusBar } from "./TopStatusBar";
import { SidebarNav } from "./SidebarNav";

export function AppShell() {
  return (
    <div className="flex h-screen flex-col bg-canvas text-content">
      <TopStatusBar />
      <div className="flex flex-1 overflow-hidden">
        <SidebarNav />
        <main className="flex-1 overflow-y-auto p-4">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
