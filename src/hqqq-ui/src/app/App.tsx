import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import { AppShell } from "@/layout/AppShell";
import { MarketPage } from "@/pages/MarketPage";
import { ConstituentsPage } from "@/pages/ConstituentsPage";
import { HistoryPage } from "@/pages/HistoryPage";
import { SystemPage } from "@/pages/SystemPage";

export function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppShell />}>
          <Route index element={<Navigate to="/market" replace />} />
          <Route path="market" element={<MarketPage />} />
          <Route path="constituents" element={<ConstituentsPage />} />
          <Route path="history" element={<HistoryPage />} />
          <Route path="system" element={<SystemPage />} />
          <Route path="*" element={<Navigate to="/market" replace />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
