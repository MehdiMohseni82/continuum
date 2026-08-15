"use client";
import { useEffect, useState, useCallback } from "react";
import type { AgentDto, HandoffDto } from "@/lib/continuum";
import { Chip, TAG, tagFor } from "@/components/bui";
import { PageHeader, Section } from "@/components/bui/page";
import { DataTable } from "@/components/bui/table";

export default function AgentsPage() {
  const [agents, setAgents] = useState<AgentDto[]>([]);
  const [handoffs, setHandoffs] = useState<HandoffDto[]>([]);
  const [ok, setOk] = useState(true);
  const [loaded, setLoaded] = useState(false);

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
    } finally {
      setLoaded(true);
    }
  }, []);

  useEffect(() => {
    refresh();
    const t = setInterval(refresh, 5000);
    return () => clearInterval(t);
  }, [refresh]);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Agents & bus"
        subtitle="Registered agents and open hand-offs, refreshed every five seconds."
        actions={
          // The live indicator states its own condition rather than relying on a bare coloured dot.
          <Chip dot={ok ? TAG.green : TAG.red}>{ok ? "live" : loaded ? "offline" : "connecting"}</Chip>
        }
      />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Section title={`Agents · ${agents.length}`}>
          <DataTable
            rows={agents}
            rowKey={(a) => a.id}
            empty="No agents registered."
            emptyHint="An agent appears once it calls agent_register, or joins a room."
            columns={[
              {
                key: "name",
                header: "Agent",
                cell: (a) => (
                  <span className="flex items-center gap-2">
                    <span className="size-1.5 shrink-0 rounded-full" style={{ background: tagFor(a.name) }} />
                    <span className="font-medium text-gray-800 dark:text-white/90">{a.name}</span>
                  </span>
                ),
              },
              {
                key: "caps",
                header: "Capabilities",
                cell: (a) => <span className="text-gray-500">{a.capabilities || "—"}</span>,
              },
              {
                key: "seen",
                header: "Last seen",
                numeric: true,
                cell: (a) => new Date(a.lastSeenAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
              },
            ]}
          />
        </Section>

        <Section title={`Open hand-offs · ${handoffs.length}`}>
          <DataTable
            rows={handoffs}
            rowKey={(h) => h.id}
            empty="Nothing waiting to be picked up."
            emptyHint="An agent creates one with handoff_create when it wants another to take over."
            columns={[
              {
                key: "title",
                header: "Task",
                cell: (h) => <span className="font-medium text-gray-800 dark:text-white/90">{h.title}</span>,
              },
              { key: "from", header: "From", cell: (h) => <Chip dot={tagFor(h.fromAgent)}>{h.fromAgent}</Chip> },
              { key: "status", header: "Status", cell: (h) => <Chip dot={TAG.green}>{h.status}</Chip> },
            ]}
          />
        </Section>
      </div>
    </div>
  );
}
