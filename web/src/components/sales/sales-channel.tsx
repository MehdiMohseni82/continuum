"use client";
import dynamic from "next/dynamic";
import { Dropdown } from "../ui/dropdown/Dropdown";
import { DropdownItem } from "../ui/dropdown/DropdownItem";
import { MoreDotIcon } from "@/icons";
// Dynamically import the ReactApexChart component
const Chart = dynamic(() => import("react-apexcharts"), {
  ssr: false,
});
import type { ApexOptions } from "apexcharts";
import { useState } from "react";

const darkCount = 14;
const lightCount = 14;
const grayCount = 14;

const chartOptions: ApexOptions = {
  series: [{ data: Array(darkCount + lightCount + grayCount).fill(100) }],
  colors: [
    ...Array(darkCount).fill("#465FFF"),
    ...Array(lightCount).fill("#36BFFA"),
    ...Array(grayCount).fill("#E4E7EC"),
  ],
  chart: {
    fontFamily: "Outfit, sans-serif",
    type: "bar",
    height: 32,
    sparkline: { enabled: true },
    toolbar: { show: false },
    animations: { enabled: false },
  },
  plotOptions: {
    bar: {
      horizontal: false,
      distributed: true,
      columnWidth: "70%",
      borderRadius: 1,
      borderRadiusApplication: "around",
    },
  },
  dataLabels: { enabled: false },
  xaxis: {
    labels: { show: false },
    axisBorder: { show: false },
    axisTicks: { show: false },
  },
  yaxis: { show: false },
  grid: {
    show: false,
    padding: { top: 0, right: 0, bottom: 0, left: 0 },
  },
  tooltip: { enabled: false },
  legend: { show: false },
};

const chartSeries: ApexOptions["series"] = [
  { data: Array(darkCount + lightCount + grayCount).fill(100) },
];

export default function SalesChannel() {
  const [isOpen, setIsOpen] = useState(false);

  function toggleDropdown() {
    setIsOpen(!isOpen);
  }

  function closeDropdown() {
    setIsOpen(false);
  }
  return (
    <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/3">
      <div className="mb-4 flex items-center justify-between gap-2">
        <div>
          <h3 className="text-lg font-medium text-gray-800 dark:text-white/90">
            Sales by Channel
          </h3>
          <p className="text-sm text-gray-500 dark:text-gray-400">
            Channel performance overview
          </p>
        </div>
        <div className="relative h-fit">
          <button onClick={toggleDropdown} className="dropdown-toggle">
            <MoreDotIcon className="text-gray-400 hover:text-gray-700 dark:hover:text-gray-300" />
          </button>
          <Dropdown
            isOpen={isOpen}
            onClose={closeDropdown}
            className="w-40 p-2"
          >
            <DropdownItem
              onItemClick={closeDropdown}
              className="flex w-full font-normal text-left text-gray-500 rounded-lg hover:bg-gray-100 hover:text-gray-700 dark:text-gray-400 dark:hover:bg-white/5 dark:hover:text-gray-300"
            >
              View More
            </DropdownItem>
            <DropdownItem
              onItemClick={closeDropdown}
              className="flex w-full font-normal text-left text-gray-500 rounded-lg hover:bg-gray-100 hover:text-gray-700 dark:text-gray-400 dark:hover:bg-white/5 dark:hover:text-gray-300"
            >
              Delete
            </DropdownItem>
          </Dropdown>
        </div>
      </div>
      <div className="mb-6 flex items-center gap-2">
        <h3 className="text-3xl text-gray-800 dark:text-white/90">75</h3>
        <span className="text-success-600 flex items-center text-sm">
          <svg
            width="16"
            height="16"
            viewBox="0 0 16 16"
            fill="none"
            xmlns="http://www.w3.org/2000/svg"
          >
            <path
              d="M7.9974 2.66602L7.9974 13.3336M4 6.66334L7.99987 2.66602L12 6.66334"
              stroke="#039855"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
          3.2%
        </span>
        <span className="text-sm text-gray-500 dark:text-gray-400">
          Increased vs last week
        </span>
      </div>
      <Chart
        options={chartOptions}
        series={chartSeries}
        type="bar"
        height={32}
      />
      <div className="mt-5 rounded-xl border border-gray-200 dark:border-gray-800">
        <div className="flex gap-6 border-b border-gray-200 px-4 py-3 dark:border-gray-800">
          <div className="flex items-center gap-1.5">
            <span className="bg-brand-500 inline-block size-2 rounded-full"></span>
            <span className="text-sm text-gray-700 dark:text-gray-400">
              {" "}
              Website
            </span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className="bg-blue-light-400 inline-block size-2 rounded-full"></span>
            <span className="text-sm text-gray-700 dark:text-gray-400">
              {" "}
              Email
            </span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className="inline-block size-2 rounded-full bg-gray-200"></span>
            <span className="text-sm text-gray-700 dark:text-gray-400">
              Social Media
            </span>
          </div>
        </div>
        <div className="p-4">
          <table className="w-full">
            <thead>
              <tr>
                <th className="pb-2 text-left text-sm font-normal text-gray-400">
                  Channels
                </th>
                <th className="pb-2 text-left text-sm font-normal text-gray-400">
                  Metric
                </th>
                <th className="pb-2 text-right text-sm font-normal text-gray-400">
                  Total
                </th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td className="py-2 text-sm text-gray-700 dark:text-gray-400">
                  Website
                </td>
                <td className="py-2 text-sm text-gray-700 dark:text-gray-400">
                  35
                </td>
                <td className="py-2 text-right text-sm text-gray-700 dark:text-gray-400">
                  <span className="flex items-center justify-end gap-1.5">
                    <svg
                      width="16"
                      height="16"
                      viewBox="0 0 16 16"
                      fill="none"
                      xmlns="http://www.w3.org/2000/svg"
                    >
                      <path
                        d="M7.9974 2.66602L7.9974 13.3336M4 6.66334L7.99987 2.66602L12 6.66334"
                        stroke="#039855"
                        strokeWidth="1.5"
                        strokeLinecap="round"
                        strokeLinejoin="round"
                      />
                    </svg>
                    5.2%
                  </span>
                </td>
              </tr>
              <tr>
                <td className="py-2 text-sm text-gray-700 dark:text-gray-400">
                  Email
                </td>
                <td className="py-2 text-sm text-gray-700 dark:text-gray-400">
                  25
                </td>
                <td className="py-2 text-right text-sm text-gray-700 dark:text-gray-400">
                  <span className="text-error-600 flex items-center justify-end gap-1.5">
                    <svg
                      width="16"
                      height="16"
                      viewBox="0 0 16 16"
                      fill="none"
                      xmlns="http://www.w3.org/2000/svg"
                    >
                      <path
                        d="M7.9974 13.3336L7.9974 2.66602M4 9.33619L7.99987 13.3335L12 9.33619"
                        stroke="#D92D20"
                        strokeWidth="1.5"
                        strokeLinecap="round"
                        strokeLinejoin="round"
                      />
                    </svg>
                    5.2%
                  </span>
                </td>
              </tr>
              <tr>
                <td className="py-2 text-sm text-gray-700 dark:text-gray-400">
                  Social Media
                </td>
                <td className="py-2 text-sm text-gray-700 dark:text-gray-400">
                  59
                </td>
                <td className="py-2 text-right text-sm text-gray-700 dark:text-gray-400">
                  <span className="text-success-600 flex items-center justify-end gap-1.5">
                    <svg
                      width="16"
                      height="16"
                      viewBox="0 0 16 16"
                      fill="none"
                      xmlns="http://www.w3.org/2000/svg"
                    >
                      <path
                        d="M7.9974 2.66602L7.9974 13.3336M4 6.66334L7.99987 2.66602L12 6.66334"
                        stroke="#039855"
                        strokeWidth="1.5"
                        strokeLinecap="round"
                        strokeLinejoin="round"
                      />
                    </svg>
                    5.2%
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
