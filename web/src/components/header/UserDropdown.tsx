"use client";
import Link from "next/link";
import { useState } from "react";
import { Dropdown } from "../ui/dropdown/Dropdown";

const links = [
  { href: "/", label: "Overview" },
  { href: "/memory", label: "Memory" },
  { href: "/agents", label: "Agents & Bus" },
  { href: "/redaction", label: "Redaction" },
];

export default function UserDropdown() {
  const [isOpen, setIsOpen] = useState(false);

  return (
    <div className="relative">
      <button
        onClick={() => setIsOpen((o) => !o)}
        className="flex items-center gap-2 text-gray-700 dark:text-gray-400"
      >
        <span className="flex h-11 w-11 items-center justify-center rounded-full bg-brand-500 text-lg font-bold text-white">C</span>
        <span className="hidden font-medium text-theme-sm sm:block">Continuum</span>
        <svg
          className={`hidden stroke-gray-500 transition-transform duration-200 dark:stroke-gray-400 sm:block ${isOpen ? "rotate-180" : ""}`}
          width="18" height="20" viewBox="0 0 18 20" fill="none" xmlns="http://www.w3.org/2000/svg"
        >
          <path d="M4.3125 8.65625L9 13.3437L13.6875 8.65625" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      <Dropdown
        isOpen={isOpen}
        onClose={() => setIsOpen(false)}
        className="absolute right-0 mt-[17px] flex w-[260px] flex-col rounded-2xl border border-gray-200 bg-white p-3 shadow-theme-lg dark:border-gray-800 dark:bg-gray-dark"
      >
        <div className="border-b border-gray-100 pb-3 dark:border-gray-700">
          <span className="block text-sm font-semibold text-gray-800 dark:text-white/90">Continuum</span>
          <span className="mt-0.5 block text-xs text-gray-500 dark:text-gray-400">Your external brain for Claude Code</span>
        </div>

        <ul className="flex flex-col py-2">
          {links.map((l) => (
            <li key={l.href}>
              <Link
                href={l.href}
                onClick={() => setIsOpen(false)}
                className="block rounded-lg px-3 py-2 text-sm text-gray-600 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-white/[0.03]"
              >
                {l.label}
              </Link>
            </li>
          ))}
        </ul>

        <div className="border-t border-gray-100 pt-3 dark:border-gray-700">
          <span className="flex items-center gap-2 px-3 text-xs text-gray-400">
            <span className="h-2 w-2 rounded-full bg-success-500" />
            continuum.dotnet-talk.com
          </span>
        </div>
      </Dropdown>
    </div>
  );
}
