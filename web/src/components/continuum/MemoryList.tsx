"use client";
import { useState } from "react";
import type { MemoryDto, MemoryType } from "@/lib/continuum";

const typeColor: Record<MemoryType, string> = {
  User: "bg-blue-light-50 text-blue-light-600 dark:bg-blue-light-500/15 dark:text-blue-light-400",
  Feedback: "bg-orange-50 text-orange-600 dark:bg-orange-500/15 dark:text-orange-400",
  Project: "bg-success-50 text-success-600 dark:bg-success-500/15 dark:text-success-400",
  Reference: "bg-brand-50 text-brand-600 dark:bg-brand-500/15 dark:text-brand-400",
};

export default function MemoryList({ items, emptyText }: { items: MemoryDto[]; emptyText: string }) {
  const [list, setList] = useState(items);
  const [busy, setBusy] = useState<string | null>(null);

  async function forget(id: string) {
    if (!confirm("Forget this memory? This can't be undone.")) return;
    setBusy(id);
    try {
      const res = await fetch(`/bff/c/memory/${id}`, { method: "DELETE" });
      if (res.ok) setList((l) => l.filter((m) => m.id !== id));
    } finally {
      setBusy(null);
    }
  }

  if (list.length === 0) return <p className="py-10 text-center text-gray-400">{emptyText}</p>;

  return (
    <div className="flex flex-col gap-3">
      {list.map((m) => (
        <div key={m.id} className="group rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
          <div className="mb-2 flex flex-wrap items-center gap-3">
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${typeColor[m.type]}`}>{m.type}</span>
            {m.pinned && <span className="text-xs font-medium text-orange-500">📌 pinned</span>}
            <span className="font-mono text-xs text-gray-400">salience {m.salience.toFixed(2)}</span>
            {m.score != null && <span className="font-mono text-xs text-gray-400">match {m.score.toFixed(2)}</span>}
            <button
              onClick={() => forget(m.id)}
              disabled={busy === m.id}
              className="ml-auto text-xs font-medium text-gray-400 opacity-0 transition hover:text-error-500 group-hover:opacity-100 disabled:opacity-40"
            >
              {busy === m.id ? "…" : "Forget"}
            </button>
          </div>
          <p className="text-sm leading-relaxed text-gray-700 dark:text-gray-200">{m.content}</p>
        </div>
      ))}
    </div>
  );
}
