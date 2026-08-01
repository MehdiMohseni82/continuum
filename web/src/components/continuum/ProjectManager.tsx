"use client";
import { useState } from "react";
import type { WorkspaceDto } from "@/lib/continuum";

export default function ProjectManager({ items }: { items: WorkspaceDto[] }) {
  const [list, setList] = useState(items);
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function startEdit(w: WorkspaceDto) {
    setError(null);
    setEditing(w.id);
    setDraft(w.displayName);
  }

  async function save(id: string) {
    const displayName = draft.trim();
    if (!displayName) return;
    setBusy(id);
    setError(null);
    try {
      const res = await fetch(`/bff/c/workspaces/${id}/display-name`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ displayName }),
      });
      if (res.ok) {
        setList((l) => l.map((w) => (w.id === id ? { ...w, displayName } : w)));
        setEditing(null);
      } else if (res.status === 403) {
        setError("Only admins can rename projects.");
      } else {
        setError(`Rename failed (${res.status}).`);
      }
    } catch {
      setError("Rename failed — network error.");
    } finally {
      setBusy(null);
    }
  }

  const sorted = [...list].sort((a, b) => b.sessionCount - a.sessionCount);

  if (list.length === 0) return <p className="py-10 text-center text-gray-400">No projects tracked yet.</p>;

  return (
    <div className="flex flex-col gap-3">
      {error && (
        <p className="rounded-lg bg-error-50 px-3 py-2 text-sm text-error-600 dark:bg-error-500/15 dark:text-error-400">
          {error}
        </p>
      )}
      <div className="overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
        <div className="max-w-full overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-gray-200 text-left text-xs uppercase tracking-wide text-gray-400 dark:border-gray-800">
                <th className="px-5 py-3 font-medium">Name</th>
                <th className="px-5 py-3 font-medium">Project key</th>
                <th className="px-5 py-3 text-right font-medium">Sessions</th>
                <th className="px-5 py-3 text-right font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((w) => (
                <tr
                  key={w.id}
                  className="group border-b border-gray-100 last:border-0 hover:bg-gray-50 dark:border-gray-800/60 dark:hover:bg-white/[0.02]"
                >
                  <td className="px-5 py-3">
                    {editing === w.id ? (
                      <input
                        value={draft}
                        onChange={(e) => setDraft(e.target.value)}
                        autoFocus
                        onKeyDown={(e) => {
                          if (e.key === "Enter") save(w.id);
                          if (e.key === "Escape") setEditing(null);
                        }}
                        className="h-9 w-72 max-w-full rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
                      />
                    ) : (
                      <span className="font-medium text-gray-800 dark:text-white/90">{w.displayName}</span>
                    )}
                  </td>
                  <td className="px-5 py-3 font-mono text-xs text-gray-400">{w.projectKey}</td>
                  <td className="px-5 py-3 text-right tabular-nums text-gray-600 dark:text-gray-300">{w.sessionCount}</td>
                  <td className="px-5 py-3 text-right">
                    {editing === w.id ? (
                      <div className="flex justify-end gap-2 text-xs font-medium">
                        <button
                          onClick={() => save(w.id)}
                          disabled={busy === w.id}
                          className="rounded-lg bg-brand-500 px-3 py-1.5 text-white hover:bg-brand-600 disabled:opacity-40"
                        >
                          {busy === w.id ? "…" : "Save"}
                        </button>
                        <button
                          onClick={() => setEditing(null)}
                          className="rounded-lg px-3 py-1.5 text-gray-500 hover:bg-gray-100 dark:hover:bg-gray-800"
                        >
                          Cancel
                        </button>
                      </div>
                    ) : (
                      <button
                        onClick={() => startEdit(w)}
                        className="text-xs font-medium text-gray-400 opacity-0 transition hover:text-brand-500 group-hover:opacity-100"
                      >
                        Rename
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
