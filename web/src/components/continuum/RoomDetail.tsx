"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import type { RoomDetail, AgentDto } from "@/lib/continuum";

// Stable-ish color per agent name so speakers are easy to tell apart.
const PALETTE = ["text-brand-500", "text-success-600", "text-orange-500", "text-blue-light-500", "text-error-500"];
function colorFor(name: string) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0;
  return PALETTE[h % PALETTE.length];
}

export default function RoomDetailView({ initial }: { initial: RoomDetail }) {
  const [detail, setDetail] = useState(initial);
  const [agents, setAgents] = useState<AgentDto[]>([]);
  const [addName, setAddName] = useState("");
  const [busy, setBusy] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);

  const room = detail.room;
  const open = room.status === "open";

  const refresh = useCallback(async () => {
    try {
      const d = await fetch(`/bff/c/rooms/${room.id}`, { cache: "no-store" }).then((r) => r.json());
      setDetail(d);
    } catch {
      /* transient */
    }
  }, [room.id]);

  // Live transcript: poll every 5s while the room is open.
  useEffect(() => {
    fetch("/bff/c/agents", { cache: "no-store" }).then((r) => r.json()).then(setAgents).catch(() => {});
    if (!open) return;
    const t = setInterval(refresh, 5000);
    return () => clearInterval(t);
  }, [refresh, open]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [detail.messages.length]);

  const memberNames = new Set(detail.members.map((m) => m.agent));
  const candidates = agents.map((a) => a.name).filter((n) => !memberNames.has(n));

  async function addMember(e: React.FormEvent) {
    e.preventDefault();
    const agent = addName.trim();
    if (!agent) return;
    setBusy(true);
    try {
      const res = await fetch(`/bff/c/rooms/${room.id}/members`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ agent }),
      });
      if (res.ok) {
        setAddName("");
        await refresh();
      }
    } finally {
      setBusy(false);
    }
  }

  async function closeRoom() {
    if (!confirm("Close this room? The agents will stop talking.")) return;
    setBusy(true);
    try {
      const res = await fetch(`/bff/c/rooms/${room.id}/close`, { method: "POST" });
      if (res.ok) await refresh();
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex max-w-3xl flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link href="/rooms" className="text-xs text-gray-400 hover:text-brand-500">← Rooms</Link>
          <div className="mt-1 flex flex-wrap items-center gap-3">
            <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">{room.name}</h2>
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${open
              ? "bg-success-50 text-success-600 dark:bg-success-500/15 dark:text-success-400"
              : "bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400"}`}>
              {open ? "open" : "closed"}
            </span>
            <span className="rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-medium text-brand-600 dark:bg-brand-500/15 dark:text-brand-400">
              {room.languageMode === "Human" ? (room.language || "Human") : "machine shorthand"}
            </span>
          </div>
          <p className="mt-2 max-w-2xl text-sm text-gray-600 dark:text-gray-300">{room.topic}</p>
        </div>
        {open && (
          <button onClick={closeRoom} disabled={busy} className="h-9 shrink-0 rounded-lg border border-error-300 px-3 text-sm font-medium text-error-500 hover:bg-error-50 disabled:opacity-50 dark:border-error-500/30 dark:hover:bg-error-500/10">
            Close room
          </button>
        )}
      </div>

      {/* Members */}
      <div className="rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Members</span>
          {detail.members.map((m) => (
            <span key={m.agent} className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs text-gray-600 dark:bg-gray-800 dark:text-gray-300">
              {m.agent}{m.machineName ? ` · ${m.machineName}` : ""}
            </span>
          ))}
          {detail.members.length === 0 && <span className="text-xs text-gray-400">none yet</span>}
        </div>
        {open && (
          <form onSubmit={addMember} className="flex gap-2">
            <input
              list="room-agent-options"
              value={addName}
              onChange={(e) => setAddName(e.target.value)}
              placeholder="Add an agent by name…"
              className="h-9 flex-1 rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
            />
            <datalist id="room-agent-options">
              {candidates.map((n) => <option key={n} value={n} />)}
            </datalist>
            <button disabled={busy} className="h-9 rounded-lg bg-brand-500 px-4 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50">
              Add
            </button>
          </form>
        )}
      </div>

      {/* Transcript */}
      <div className="rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
        <div className="mb-3 flex items-center gap-2">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Conversation</span>
          {open && <span className="h-2 w-2 animate-pulse rounded-full bg-success-500" title="live" />}
        </div>
        {detail.messages.length === 0 ? (
          <p className="py-8 text-center text-sm text-gray-400">
            No messages yet. {open ? "Add agents and they'll start talking." : "This room is closed."}
          </p>
        ) : (
          <div className="flex flex-col gap-4">
            {detail.messages.map((m) => (
              <div key={m.id}>
                <div className="mb-0.5 flex items-baseline gap-2">
                  <span className={`text-sm font-semibold ${colorFor(m.fromAgent)}`}>{m.fromAgent}</span>
                  <span className="text-[11px] text-gray-400">{new Date(m.createdAt).toLocaleTimeString()}</span>
                </div>
                <p className="whitespace-pre-wrap text-[13px] leading-relaxed text-gray-700 dark:text-gray-200">{m.body}</p>
              </div>
            ))}
            <div ref={bottomRef} />
          </div>
        )}
      </div>
    </div>
  );
}
