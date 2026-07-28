"use client";

import { ApexOptions } from "apexcharts";
import dynamic from "next/dynamic";

const ReactApexChart = dynamic(() => import("react-apexcharts"), {
  ssr: false,
});

export default function RadialChartTwo() {
  const options: ApexOptions = {
    chart: {
      type: "radialBar",
      height: 300,
      toolbar: { show: false },
      fontFamily: "Outfit, sans-serif",
    },
    colors: ["#262E89", "#FEB273", "#BDB4FE"],
    plotOptions: {
      radialBar: {
        startAngle: -90,
        endAngle: 90,
        offsetY: 60,
        hollow: {
          size: "25%",
          background: "transparent",
        },
        track: {
          background: "#F4F5F5",
          strokeWidth: "100%",
          margin: 2,
        },
        dataLabels: {
          name: { show: false },
          value: { show: false },
          total: { show: false },
        },
      },
    },
    stroke: {
      lineCap: "butt",
    },
    legend: {
      show: false,
    },
    tooltip: {
      enabled: false,
    },
  };

  const series = [75, 55, 40];

  return (
    <div className="max-w-full">
      <div id="chartFortyOne">
        <ReactApexChart
          options={options}
          series={series}
          type="radialBar"
          height={300}
        />
      </div>
    </div>
  );
}
