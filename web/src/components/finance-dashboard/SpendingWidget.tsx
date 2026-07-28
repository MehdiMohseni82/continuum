"use client";
import { useState, useRef, useEffect } from "react";
import dynamic from "next/dynamic";
import { ApexOptions } from "apexcharts";

const ReactApexChart = dynamic(() => import("react-apexcharts"), {
  ssr: false,
});
import { useTheme } from "../../context/ThemeContext";

export default function SpendingWidget() {
  const { theme } = useTheme();
  const [isOpen, setIsOpen] = useState(false);
  const [selected, setSelected] = useState("Yearly");
  const dropdownRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (
        dropdownRef.current &&
        !dropdownRef.current.contains(event.target as Node)
      ) {
        setIsOpen(false);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
    };
  }, []);

  const options: ApexOptions = {
    colors: ["#7592FF", "#7CD4FD", "#BDB4FE", "#FE9EFE", "#6FEAA6", "#D0D5DD"],
    chart: {
      fontFamily: "Outfit, sans-serif",
      type: "bar",
      height: 50,
      width: "100%",
      stacked: true,
      toolbar: {
        show: false,
      },
      animations: {
        enabled: false,
      },
    },
    plotOptions: {
      bar: {
        horizontal: true,
        barHeight: "32px",
        borderRadius: 4,
        borderRadiusApplication: "end",
        borderRadiusWhenStacked: "all",
      },
    },
    dataLabels: {
      enabled: false,
    },
    grid: {
      show: false,
      padding: {
        top: -30,
        bottom: -20,
        left: -10,
        right: 0,
      },
    },
    xaxis: {
      labels: {
        show: false,
      },
      axisBorder: {
        show: false,
      },
      axisTicks: {
        show: false,
      },
    },
    yaxis: {
      labels: {
        show: false,
      },
    },
    legend: {
      show: false,
    },
    tooltip: {
      enabled: true,
      x: {
        show: false,
      },
    },
    stroke: {
      show: true,
      width: 1,
      colors: [theme === "dark" ? "#111827" : "#ffffff"],
    },
    states: {
      hover: {
        filter: {
          type: "none",
        },
      },
      active: {
        filter: {
          type: "none",
        },
      },
    },
  };

  const series = [
    {
      name: "Activity",
      data: [45],
    },
    {
      name: "Online Purchases",
      data: [25],
    },
    {
      name: "Groceries",
      data: [15],
    },
    {
      name: "Digital Goods",
      data: [20],
    },
    {
      name: "Stationery",
      data: [10],
    },
    {
      name: "Others",
      data: [30],
    },
  ];

  return (
    <div className="rounded-2xl border border-gray-200 bg-white p-6 md:col-span-1 dark:border-gray-800 dark:bg-white/3">
      <div className="mb-8 flex items-center justify-between">
        <h3 className="text-lg font-semibold text-gray-800 dark:text-white/90">
          Spending
        </h3>

        {/* <!-- Dropdown --> */}
        <div className="relative" ref={dropdownRef}>
          <button
            type="button"
            onClick={() => setIsOpen(!isOpen)}
            className="flex h-9 items-center justify-center gap-1 rounded-lg border border-gray-200 pr-2 pl-3 text-sm font-medium text-gray-700 shadow-xs hover:bg-gray-50 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-400 dark:hover:bg-gray-900"
          >
            <span>{selected}</span>
            <svg
              width="16"
              height="16"
              viewBox="0 0 16 16"
              fill="none"
              xmlns="http://www.w3.org/2000/svg"
              className={`transition-transform ${isOpen ? "rotate-180" : ""}`}
            >
              <path
                d="M4 6L8 10L12 6"
                stroke="currentColor"
                strokeWidth="1.5"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </button>
          {isOpen && (
            <div className="absolute right-0 z-10 mt-2 w-32 rounded-lg border border-gray-200 bg-white p-1.5 shadow-lg dark:border-gray-700 dark:bg-gray-900">
              <button
                type="button"
                onClick={() => {
                  setSelected("Yearly");
                  setIsOpen(false);
                }}
                className="w-full rounded-md px-3 py-2 text-left text-sm text-gray-700 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-white/5"
              >
                Yearly
              </button>
              <button
                type="button"
                onClick={() => {
                  setSelected("Monthly");
                  setIsOpen(false);
                }}
                className="w-full rounded-md px-3 py-2 text-left text-sm text-gray-700 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-white/5"
              >
                Monthly
              </button>
            </div>
          )}
        </div>
      </div>

      <div>
        <p className="mb-1 text-xs text-gray-500 dark:text-gray-400">Total</p>
        <h4 className="mb-4 text-3xl font-medium text-gray-800 dark:text-white/90">
          $10,758
        </h4>
      </div>

      <div className="-ml-1 w-full">
        <ReactApexChart
          options={options}
          series={series}
          type="bar"
          height={50}
        />
      </div>

      <div className="mt-3 grid grid-cols-2 gap-3">
        <div className="flex items-center gap-2">
          <span className="size-2 rounded-full bg-[#7592FF]"></span>
          <span className="text-sm font-normal text-gray-700 dark:text-gray-400">
            Activity
          </span>
        </div>
        <div className="flex items-center gap-2">
          <span className="size-2 rounded-full bg-[#7CD4FD]"></span>
          <span className="text-sm font-normal text-gray-700 dark:text-gray-400">
            Online Purchases
          </span>
        </div>
        <div className="flex items-center gap-2">
          <span className="size-2 rounded-full bg-[#BDB4FE]"></span>
          <span className="text-sm font-normal text-gray-700 dark:text-gray-400">
            Groceries
          </span>
        </div>
        <div className="flex items-center gap-2">
          <span className="size-2 rounded-full bg-[#FE9EFE]"></span>
          <span className="text-sm font-normal text-gray-700 dark:text-gray-400">
            Digital Goods
          </span>
        </div>
        <div className="flex items-center gap-2">
          <span className="size-2 rounded-full bg-[#6FEAA6]"></span>
          <span className="text-sm font-normal text-gray-700 dark:text-gray-400">
            Stationery
          </span>
        </div>
        <div className="flex items-center gap-2">
          <span className="size-2 rounded-full bg-[#D0D5DD]"></span>
          <span className="text-sm font-normal text-gray-700 dark:text-gray-400">
            Others
          </span>
        </div>
      </div>
    </div>
  );
}
