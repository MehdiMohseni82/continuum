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
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState("");

  async function patch(id: string, body: { pinned?: boolean; content?: string }) {
    setBusy(id);
    try {
      const res = await fetch(`/bff/c/memory/${id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (res.ok) {
        const updated: MemoryDto = await res.json();
        setList((l) => l.map((m) => (m.id === id ? updated : m)));
      }
    } finally {
      setBusy(null);
    }
  }

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

  function startEdit(m: MemoryDto) {
    setEditing(m.id);
    setDraft(m.content);
  }

  async function saveEdit(id: string) {
    const content = draft.trim();
    setEditing(null);
    if (content) await patch(id, { content });
  }

  // Pinned first, then by salience — mirror the server ordering as the list mutates.
  const sorted = [...list].sort(
    (a, b) => Number(b.pinned) - Number(a.pinned) || b.salience - a.salience,
  );

  if (list.length === 0) return <p className="py-10 text-center text-gray-400">{emptyText}</p>;

  return (
    <div className="flex flex-col gap-3">
      {sorted.map((m) => (
        <div
          key={m.id}
          className={`group rounded-2xl border bg-white p-4 dark:bg-white/[0.03] ${
            m.pinned ? "border-orange-200 dark:border-orange-500/30" : "border-gray-200 dark:border-gray-800"
          }`}
        >
          <div className="mb-2 flex flex-wrap items-center gap-3">
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${typeColor[m.type]}`}>{m.type}</span>
            <span className="font-mono text-xs text-gray-400">salience {m.salience.toFixed(2)}</span>
            {m.score != null && <span className="font-mono text-xs text-gray-400">match {m.score.toFixed(2)}</span>}
            <div className="ml-auto flex items-center gap-3 text-xs font-medium opacity-0 transition group-hover:opacity-100">
              <button
                onClick={() => patch(m.id, { pinned: !m.pinned })}
                disabled={busy === m.id}
                className={`${m.pinned ? "text-orange-500 opacity-100" : "text-gray-400 hover:text-orange-500"} disabled:opacity-40`}
                title={m.pinned ? "Unpin" : "Pin — keeps salience at max"}
              >
                {m.pinned ? "📌 Pinned" : "Pin"}
              </button>
              <button
                onClick={() => startEdit(m)}
                disabled={busy === m.id}
                className="text-gray-400 hover:text-brand-500 disabled:opacity-40"
              >
                Edit
              </button>
              <button
                onClick={() => forget(m.id)}
                disabled={busy === m.id}
                className="text-gray-400 hover:text-error-500 disabled:opacity-40"
              >
                {busy === m.id ? "…" : "Forget"}
              </button>
            </div>
            {m.pinned && (
              <span className="order-first text-xs font-medium text-orange-500 opacity-100 group-hover:hidden">📌 pinned</span>
            )}
          </div>

          {editing === m.id ? (
            <div className="flex flex-col gap-2">
              <textarea
                value={draft}
                onChange={(e) => setDraft(e.target.value)}
                autoFocus
                rows={3}
                className="w-full rounded-lg border border-gray-300 bg-transparent p-3 text-sm text-gray-700 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-gray-200"
              />
              <div className="flex gap-2 text-xs font-medium">
                <button onClick={() => saveEdit(m.id)} className="rounded-lg bg-brand-500 px-3 py-1.5 text-white hover:bg-brand-600">
                  Save
                </button>
                <button onClick={() => setEditing(null)} className="rounded-lg px-3 py-1.5 text-gray-500 hover:bg-gray-100 dark:hover:bg-gray-800">
                  Cancel
                </button>
                <span className="self-center text-gray-400">Secrets are re-redacted on save.</span>
              </div>
            </div>
          ) : (
            <p className="text-sm leading-relaxed text-gray-700 dark:text-gray-200">{m.content}</p>
          )}
        </div>
      ))}
    </div>
  );
}
