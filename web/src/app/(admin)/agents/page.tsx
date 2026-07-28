"use client";
import { useEffect, useState, useCallback } from "react";
import type { AgentDto, HandoffDto } from "@/lib/continuum";

export default function AgentsPage() {
  const [agents, setAgents] = useState<AgentDto[]>([]);
  const [handoffs, setHandoffs] = useState<HandoffDto[]>([]);
  const [ok, setOk] = useState(true);

  const refresh = useCallback(async () => {
    try {
      const [a, h] = await Promise.all([
        fetch("/bff/c/agents", { cache: "no-store" }).then((r) => r.json()),
        fetch("/bff/c/handoffs?status=open", { cache: "no-store" }).then((r) => r.json()),
      ]);
      setAgents(a);
      setHandoffs(h);
      setOk(true);
    } catch {
      setOk(false);
    }
  }, []);

  useEffect(() => {
    refresh();
    const t = setInterval(refresh, 5000);
    return () => clearInterval(t);
  }, [refresh]);

  return (
    <div className="flex flex-col gap-5">
      <div className="flex items-center gap-2">
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Agents &amp; Bus</h2>
        <span className={`h-2.5 w-2.5 rounded-full ${ok ? "animate-pulse bg-success-500" : "bg-error-500"}`} title={ok ? "live" : "offline"} />
      </div>
      <p className="-mt-3 text-sm text-gray-500 dark:text-gray-400">Registered agents and open hand-offs, refreshed live.</p>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
          <h3 className="mb-4 text-base font-semibold text-gray-800 dark:text-white/90">Agents ({agents.length})</h3>
          {agents.length === 0 ? (
            <p className="text-sm text-gray-400">None registered.</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {agents.map((a) => (
                <li key={a.id} className="text-sm">
                  <span className="font-medium text-gray-800 dark:text-white/90">{a.name}</span>
                  {a.capabilities && <span className="text-gray-500 dark:text-gray-400"> — {a.capabilities}</span>}
                  <span className="ml-2 text-xs text-gray-400">{new Date(a.lastSeenAt).toLocaleTimeString()}</span>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
          <h3 className="mb-4 text-base font-semibold text-gray-800 dark:text-white/90">Open hand-offs ({handoffs.length})</h3>
          {handoffs.length === 0 ? (
            <p className="text-sm text-gray-400">None open.</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {handoffs.map((h) => (
                <li key={h.id} className="text-sm">
                  <span className="rounded-full bg-success-50 px-2 py-0.5 text-xs font-medium text-success-600 dark:bg-success-500/15 dark:text-success-400">
                    {h.status}
                  </span>{" "}
                  <span className="font-medium text-gray-800 dark:text-white/90">{h.title}</span>
                  <span className="text-gray-500 dark:text-gray-400"> from {h.fromAgent}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}
