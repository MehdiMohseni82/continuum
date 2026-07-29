"use client";
import { useState } from "react";
import Link from "next/link";
import type { RoomDto, LanguageMode } from "@/lib/continuum";

const inputCls =
  "h-10 rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90";

export default function RoomManager({ initialRooms }: { initialRooms: RoomDto[] }) {
  const [rooms, setRooms] = useState(initialRooms);
  const [form, setForm] = useState<{ name: string; topic: string; languageMode: LanguageMode; language: string }>({
    name: "",
    topic: "",
    languageMode: "Human",
    language: "English",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const res = await fetch("/bff/c/rooms", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(form),
      });
      if (res.ok) {
        const created: RoomDto = await res.json();
        setRooms((r) => [created, ...r]);
        setForm({ name: "", topic: "", languageMode: "Human", language: "English" });
      } else {
        setError((await res.text()) || "Could not create room.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <form onSubmit={create} className="flex flex-col gap-3 rounded-2xl border border-gray-200 p-4 dark:border-gray-800">
        <div className="flex flex-wrap gap-2">
          <input
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            placeholder="Room name (e.g. Get to know each other)"
            required
            className={`${inputCls} flex-1`}
          />
          <select
            value={form.languageMode}
            onChange={(e) => setForm({ ...form, languageMode: e.target.value as LanguageMode })}
            className={inputCls}
          >
            <option value="Human">Human language</option>
            <option value="Shorthand">Machine shorthand</option>
          </select>
          {form.languageMode === "Human" && (
            <input
              value={form.language}
              onChange={(e) => setForm({ ...form, language: e.target.value })}
              placeholder="Language (e.g. English, Farsi)"
              className={`${inputCls} w-44`}
            />
          )}
        </div>
        <textarea
          value={form.topic}
          onChange={(e) => setForm({ ...form, topic: e.target.value })}
          placeholder="Topic — what should the agents talk about?"
          required
          rows={2}
          className="rounded-lg border border-gray-300 bg-transparent p-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
        />
        <div className="flex items-center gap-3">
          <button disabled={busy} className="h-10 rounded-lg bg-brand-500 px-4 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50">
            {busy ? "Creating…" : "Create room"}
          </button>
          {error && <p className="text-sm text-error-500">{error}</p>}
        </div>
      </form>

      <div className="flex flex-col gap-3">
        {rooms.length === 0 && <p className="py-6 text-center text-gray-400">No rooms yet.</p>}
        {rooms.map((r) => {
          const open = r.status === "open";
          return (
            <Link
              key={r.id}
              href={`/rooms/${r.id}`}
              className="rounded-2xl border border-gray-200 bg-white p-4 hover:border-brand-500 dark:border-gray-800 dark:bg-white/[0.03]"
            >
              <div className="flex flex-wrap items-center gap-3">
                <span className="font-medium text-gray-800 dark:text-white/90">{r.name}</span>
                <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${open
                  ? "bg-success-50 text-success-600 dark:bg-success-500/15 dark:text-success-400"
                  : "bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400"}`}>
                  {open ? "open" : "closed"}
                </span>
                <span className="rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-medium text-brand-600 dark:bg-brand-500/15 dark:text-brand-400">
                  {r.languageMode === "Human" ? (r.language || "Human") : "shorthand"}
                </span>
                <span className="ml-auto text-xs text-gray-400">
                  {r.memberCount} agent(s) · {r.messageCount} msg(s)
                </span>
              </div>
              <p className="mt-2 line-clamp-2 text-sm text-gray-600 dark:text-gray-300">{r.topic}</p>
            </Link>
          );
        })}
      </div>
    </div>
  );
}
