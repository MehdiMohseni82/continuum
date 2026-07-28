"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Dropdown } from "../ui/dropdown/Dropdown";
import type { Me } from "@/lib/continuum";

export default function UserDropdown() {
  const router = useRouter();
  const [isOpen, setIsOpen] = useState(false);
  const [me, setMe] = useState<Me | null>(null);

  useEffect(() => {
    fetch("/bff/c/auth/me", { credentials: "include" })
      .then((r) => (r.ok ? r.json() : null))
      .then(setMe)
      .catch(() => setMe(null));
  }, []);

  async function logout() {
    await fetch("/bff/c/auth/logout", { method: "POST", credentials: "include" });
    router.push("/login");
    router.refresh();
  }

  const initial = (me?.displayName || "C").trim().charAt(0).toUpperCase();
  const name = me?.displayName ?? "Continuum";

  return (
    <div className="relative">
      <button onClick={() => setIsOpen((o) => !o)} className="flex items-center gap-2 text-gray-700 dark:text-gray-400">
        <span className="flex h-11 w-11 items-center justify-center rounded-full bg-brand-500 text-lg font-bold text-white">{initial}</span>
        <span className="hidden font-medium text-theme-sm sm:block">{name}</span>
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
          <span className="block text-sm font-semibold text-gray-800 dark:text-white/90">{name}</span>
          <span className="mt-0.5 block text-xs text-gray-500 dark:text-gray-400">
            {me ? (me.isLegacy ? "Legacy token session" : `${me.email} · ${me.role}`) : "Not signed in"}
          </span>
        </div>

        <ul className="flex flex-col py-2">
          <li>
            <Link href="/settings" onClick={() => setIsOpen(false)} className="block rounded-lg px-3 py-2 text-sm text-gray-600 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-white/[0.03]">
              Settings & tokens
            </Link>
          </li>
          {me?.role === "Admin" && (
            <li>
              <Link href="/users" onClick={() => setIsOpen(false)} className="block rounded-lg px-3 py-2 text-sm text-gray-600 hover:bg-gray-50 dark:text-gray-300 dark:hover:bg-white/[0.03]">
                Manage users
              </Link>
            </li>
          )}
        </ul>

        <div className="border-t border-gray-100 pt-2 dark:border-gray-700">
          {me && !me.isLegacy ? (
            <button onClick={logout} className="block w-full rounded-lg px-3 py-2 text-left text-sm text-error-500 hover:bg-error-50 dark:hover:bg-error-500/10">
              Sign out
            </button>
          ) : (
            <Link href="/login" onClick={() => setIsOpen(false)} className="block rounded-lg px-3 py-2 text-sm text-brand-500 hover:bg-brand-50 dark:hover:bg-brand-500/10">
              Sign in
            </Link>
          )}
        </div>
      </Dropdown>
    </div>
  );
}
