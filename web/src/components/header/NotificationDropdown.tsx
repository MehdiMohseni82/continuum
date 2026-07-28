"use client";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { Dropdown } from "../ui/dropdown/Dropdown";
import type { NotificationDto } from "@/lib/continuum";

const SEEN_KEY = "continuum:notif:lastSeen";

function ago(iso: string): string {
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
  if (s < 60) return "just now";
  if (s < 3600) return `${Math.floor(s / 60)}m ago`;
  if (s < 86400) return `${Math.floor(s / 3600)}h ago`;
  return `${Math.floor(s / 86400)}d ago`;
}

export default function NotificationDropdown() {
  const [isOpen, setIsOpen] = useState(false);
  const [items, setItems] = useState<NotificationDto[]>([]);
  const [lastSeen, setLastSeen] = useState(0);

  useEffect(() => {
    setLastSeen(Number(localStorage.getItem(SEEN_KEY) ?? 0));
  }, []);

  const refresh = useCallback(async () => {
    try {
      const res = await fetch("/bff/c/notifications?take=25", { cache: "no-store" });
      if (res.ok) setItems(await res.json());
    } catch {
      /* offline — keep last items */
    }
  }, []);

  useEffect(() => {
    refresh();
    const t = setInterval(refresh, 8000);
    return () => clearInterval(t);
  }, [refresh]);

  const unread = items.filter((i) => new Date(i.timestamp).getTime() > lastSeen).length;

  function open() {
    setIsOpen(true);
    const now = Date.now();
    localStorage.setItem(SEEN_KEY, String(now));
    setLastSeen(now);
  }

  return (
    <div className="relative">
      <button
        onClick={() => (isOpen ? setIsOpen(false) : open())}
        className="relative flex h-11 w-11 items-center justify-center rounded-full border border-gray-200 bg-white text-gray-500 transition-colors hover:bg-gray-100 hover:text-gray-700 dark:border-gray-800 dark:bg-gray-900 dark:text-gray-400 dark:hover:bg-gray-800 dark:hover:text-white"
      >
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 z-10 flex h-4 min-w-4 items-center justify-center rounded-full bg-orange-500 px-1 text-[10px] font-semibold text-white">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-orange-400 opacity-60" />
            <span className="relative">{unread > 9 ? "9+" : unread}</span>
          </span>
        )}
        <svg className="fill-current" width="20" height="20" viewBox="0 0 20 20" xmlns="http://www.w3.org/2000/svg">
          <path
            fillRule="evenodd"
            clipRule="evenodd"
            d="M10.75 2.29248C10.75 1.87827 10.4143 1.54248 10 1.54248C9.58583 1.54248 9.25004 1.87827 9.25004 2.29248V2.83613C6.08266 3.20733 3.62504 5.9004 3.62504 9.16748V14.4591H3.33337C2.91916 14.4591 2.58337 14.7949 2.58337 15.2091C2.58337 15.6234 2.91916 15.9591 3.33337 15.9591H16.6667C17.0809 15.9591 17.4167 15.6234 17.4167 15.2091C17.4167 14.7949 17.0809 14.4591 16.6667 14.4591H16.375V9.16748C16.375 5.9004 13.9174 3.20733 10.75 2.83613V2.29248ZM8.00004 17.7085C8.00004 18.1228 8.33583 18.4585 8.75004 18.4585H11.25C11.6643 18.4585 12 18.1228 12 17.7085C12 17.2943 11.6643 16.9585 11.25 16.9585H8.75004C8.33583 16.9585 8.00004 17.2943 8.00004 17.7085Z"
            fill="currentColor"
          />
        </svg>
      </button>

      <Dropdown
        isOpen={isOpen}
        onClose={() => setIsOpen(false)}
        className="absolute -right-[240px] mt-[17px] flex max-h-[480px] w-[350px] flex-col rounded-2xl border border-gray-200 bg-white p-3 shadow-theme-lg dark:border-gray-800 dark:bg-gray-dark sm:w-[361px] lg:right-0"
      >
        <div className="mb-3 flex items-center justify-between border-b border-gray-100 pb-3 dark:border-gray-700">
          <h5 className="text-lg font-semibold text-gray-800 dark:text-gray-200">Bus activity</h5>
          <span className="text-xs text-gray-400">{items.length} recent</span>
        </div>

        <ul className="flex flex-col overflow-y-auto no-scrollbar">
          {items.length === 0 ? (
            <li className="py-10 text-center text-sm text-gray-400">No agent activity yet.</li>
          ) : (
            items.map((n) => (
              <li key={n.id}>
                <Link
                  href="/agents"
                  onClick={() => setIsOpen(false)}
                  className="flex gap-3 rounded-lg p-3 hover:bg-gray-50 dark:hover:bg-white/[0.03]"
                >
                  <span className="mt-0.5 text-lg leading-none">{n.kind === "handoff" ? "📦" : "💬"}</span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium text-gray-800 dark:text-white/90">{n.title}</span>
                    <span className="block truncate text-xs text-gray-500 dark:text-gray-400">{n.detail}</span>
                    <span className="mt-0.5 block text-[11px] text-gray-400">{ago(n.timestamp)}</span>
                  </span>
                  {n.severity === "warning" && <span className="mt-1 h-2 w-2 shrink-0 rounded-full bg-orange-400" />}
                </Link>
              </li>
            ))
          )}
        </ul>

        <Link
          href="/agents"
          onClick={() => setIsOpen(false)}
          className="mt-3 block rounded-lg border border-gray-200 py-2 text-center text-sm font-medium text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-white/[0.03]"
        >
          Open Agents &amp; Bus
        </Link>
      </Dropdown>
    </div>
  );
}
