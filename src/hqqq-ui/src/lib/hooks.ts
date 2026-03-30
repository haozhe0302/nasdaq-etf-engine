import { useState, useEffect } from "react";
import {
  getMarketSnapshot,
  getConstituentSnapshot,
  getHistorySnapshot,
  getSystemSnapshot,
  getAppStatus,
} from "./mock";

export function useMarketData() {
  const [data, setData] = useState(getMarketSnapshot);
  useEffect(() => {
    const id = setInterval(() => setData(getMarketSnapshot()), 1000);
    return () => clearInterval(id);
  }, []);
  return data;
}

export function useConstituentData() {
  const [data, setData] = useState(getConstituentSnapshot);
  useEffect(() => {
    const id = setInterval(() => setData(getConstituentSnapshot()), 1000);
    return () => clearInterval(id);
  }, []);
  return data;
}

export function useHistoryData() {
  return useState(getHistorySnapshot)[0];
}

export function useSystemData() {
  const [data, setData] = useState(getSystemSnapshot);
  useEffect(() => {
    const id = setInterval(() => setData(getSystemSnapshot()), 1000);
    return () => clearInterval(id);
  }, []);
  return data;
}

export function useAppStatus() {
  const [data, setData] = useState(getAppStatus);
  useEffect(() => {
    const id = setInterval(() => setData(getAppStatus()), 1000);
    return () => clearInterval(id);
  }, []);
  return data;
}
