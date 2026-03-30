import { useRef, useEffect } from "react";
import * as echarts from "echarts";

interface ChartProps {
  option: echarts.EChartsOption;
  className?: string;
}

export function Chart({ option, className }: ChartProps) {
  const el = useRef<HTMLDivElement>(null);
  const chart = useRef<echarts.ECharts | null>(null);

  useEffect(() => {
    if (!el.current) return;
    chart.current = echarts.init(el.current);
    const ro = new ResizeObserver(() => chart.current?.resize());
    ro.observe(el.current);
    return () => {
      ro.disconnect();
      chart.current?.dispose();
      chart.current = null;
    };
  }, []);

  useEffect(() => {
    chart.current?.setOption(option);
  }, [option]);

  return <div ref={el} className={className ?? "h-full w-full"} />;
}
