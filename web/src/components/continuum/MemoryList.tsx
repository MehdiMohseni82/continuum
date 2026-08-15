"use client";
import { useState } from "react";
import type { MemoryDto, MemoryType } from "@/lib/continuum";
import { Card, Chip, TAG } from "@/components/bui";
import { Empty } from "@/components/bui/page";
import { Textarea } from "@/components/bui/form";

/** Memory type is a kind, so it takes a colour from the categorical palette. */
const TYPE_DOT: Record<MemoryType, string> = {
  User: TAG.violet,
  Feedback: TAG.amber,
  Project: TAG.green,
  Reference: TAG.cyan,
};

export default function MemoryList({ items, emptyText, emptyHint }: {
  items: MemoryDto[];
  emptyText: string;
  emptyHint?: string;
}) {
  const [list, setList] = useState(items);
  const [busy, setBusy] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState("");

  async function patch(id: string, body: { pinned?: boolean; content?: string; shared?: boolean }) {
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

  async function saveEdit(id: string) {
    const content = draft.trim();
    setEditing(null);
    if (content) await patch(id, { content });
  }

  // Pinned first, then by salience — mirrors the server ordering as the list mutates.
  const sorted = [...list].sort((a, b) => Number(b.pinned) - Number(a.pinned) || b.salience - a.salience);

  if (list.length === 0) {
    return (
      <Card padded={false}>
        <Empty hint={emptyHint}>{emptyText}</Empty>
      </Card>
    );
  }

  return (
    <Card padded={false} className="divide-y divide-line">
      {sorted.map((m) => (
        <article key={m.id} className="group px-4 py-3">
          <div className="mb-1.5 flex flex-wrap items-center gap-2">
            <Chip dot={TYPE_DOT[m.type]}>{m.type}</Chip>
            {m.pinned && <Chip dot={TAG.amber}>pinned</Chip>}

            {/*
              Visibility states what is true rather than offering an action, because the old two-state
              toggle said "Share" for a memory already shared with named people — under-reporting who
              could see it, which is the one thing a privacy control must never do.
            */}
            <Chip
              tone={m.shared ? "accent" : "plain"}
              title={m.shared ? "Everyone in your organization can read this" : "Only you can read this"}
            >
              {m.shared ? "Org" : "Private"}
            </Chip>

            <span className="font-mono text-[11px] text-gray-400">
              salience {m.salience.toFixed(2)}
              {m.score != null && ` · match ${m.score.toFixed(2)}`}
            </span>

            {/* Actions stay hidden until hover, but remain reachable by keyboard. */}
            <div className="ml-auto flex items-center gap-2.5 text-[12px] opacity-0 transition group-hover:opacity-100 focus-within:opacity-100">
              <button onClick={() => patch(m.id, { pinned: !m.pinned })} disabled={busy === m.id}
                className="text-gray-400 hover:text-accent-ink disabled:opacity-40">
                {m.pinned ? "Unpin" : "Pin"}
              </button>
              <button onClick={() => patch(m.id, { shared: !m.shared })} disabled={busy === m.id}
                className="text-gray-400 hover:text-accent-ink disabled:opacity-40">
                {m.shared ? "Make private" : "Share with org"}
              </button>
              <button onClick={() => { setEditing(m.id); setDraft(m.content); }} disabled={busy === m.id}
                className="text-gray-400 hover:text-accent-ink disabled:opacity-40">
                Edit
              </button>
              <button onClick={() => forget(m.id)} disabled={busy === m.id}
                className="text-gray-400 hover:text-[#ee6572] disabled:opacity-40">
                {busy === m.id ? "…" : "Forget"}
              </button>
            </div>
          </div>

          {editing === m.id ? (
            <div className="flex flex-col gap-2">
              <Textarea value={draft} onChange={(e) => setDraft(e.target.value)} autoFocus rows={3} />
              <div className="flex items-center gap-2 text-[12px]">
                <button onClick={() => saveEdit(m.id)}
                  className="rounded-control bg-accent px-2.5 py-1 font-medium text-white hover:bg-accent-ink">
                  Save
                </button>
                <button onClick={() => setEditing(null)}
                  className="rounded-control px-2.5 py-1 text-gray-500 hover:bg-stripe">
                  Cancel
                </button>
                <span className="text-gray-400">Secrets are re-redacted on save.</span>
              </div>
            </div>
          ) : (
            <p className="max-w-[80ch] text-[13px] leading-relaxed text-gray-700 dark:text-gray-200">{m.content}</p>
          )}
        </article>
      ))}
    </Card>
  );
}
