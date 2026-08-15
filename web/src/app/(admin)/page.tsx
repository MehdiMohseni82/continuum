import { capi, Analytics, BackupStatus, BusMessage } from "@/lib/continuum";
import { Card, tagFor } from "@/components/bui";
import { PageHeader, Section, StatStrip, Empty } from "@/components/bui/page";

export const metadata = { title: "Continuum — Overview" };
export const dynamic = "force-dynamic";

async function tryGet<T>(path: string): Promise<T | null> {
  try {
    return await capi<T>(path);
  } catch {
    return null; // 204 (no digest yet), or backups not configured in this environment
  }
}

function fmtBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let v = n / 1024, i = 0;
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
  return `${v.toFixed(1)} ${units[i]}`;
}

/**
 * A breakdown as rows, not as a card.
 *
 * These used to be four separate cards holding a bar chart each, so four empty ones meant four
 * 130px boxes containing an em-dash. Rows inside one card cost a line when empty.
 */
function Breakdown({ title, rows }: { title: string; rows: { label: string; count: number }[] }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  return (
    <div className="min-w-0 px-4 py-3">
      <div className="font-mono text-[10px] uppercase tracking-[0.1em] text-gray-400">{title}</div>
      {rows.length === 0 ? (
        <p className="mt-2 text-[12px] text-gray-400">Nothing yet</p>
      ) : (
        <ul className="mt-2 flex flex-col gap-1.5">
          {rows.slice(0, 5).map((r) => (
            <li key={r.label}>
              <div className="flex items-baseline justify-between gap-2 text-[12px]">
                <span className="truncate text-gray-600 dark:text-gray-300">{r.label}</span>
                <span className="shrink-0 font-mono tabular-nums text-gray-500">{r.count.toLocaleString()}</span>
              </div>
              <div className="mt-1 h-[3px] w-full rounded-full bg-line">
                <div
                  className="h-[3px] rounded-full"
                  style={{ width: `${(r.count / max) * 100}%`, background: tagFor(r.label) }}
                />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export default async function OverviewPage() {
  const [a, digest, backups] = await Promise.all([
    capi<Analytics>("/api/analytics"),
    tryGet<BusMessage>("/api/digest/latest"),
    tryGet<BackupStatus>("/api/backups"),
  ]);
  const maxDay = Math.max(1, ...a.eventsPerDay.map((d) => d.count));
  const quiet = a.sessions === 0 && a.events === 0;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Overview" subtitle="Everything captured across your machines." />

      <StatStrip
        stats={[
          { label: "Sessions", value: a.sessions },
          { label: "Events", value: a.events },
          { label: "Memories", value: a.memories },
          { label: "Agents", value: a.agents },
          { label: "Hand-offs", value: a.handoffs },
        ]}
      />

      {/* Activity and breakdowns share a row: neither deserves the full width on its own. */}
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-[1.6fr_1fr]">
        <Section title="Events per day · last 14">
          <Card padded={false} className="px-4 py-3">
            {a.eventsPerDay.length === 0 ? (
              <Empty hint={quiet ? "Start a Claude Code session and the daemon will send it here." : undefined}>
                No events in the last two weeks.
              </Empty>
            ) : (
              <div className="flex h-28 items-end gap-1.5">
                {a.eventsPerDay.map((d) => (
                  <div key={d.label} className="flex flex-1 flex-col items-center justify-end gap-1.5" title={`${d.label}: ${d.count}`}>
                    <div
                      className="w-full rounded-[2px] bg-accent"
                      style={{ height: `${Math.max(2, (d.count / maxDay) * 100)}%` }}
                    />
                    <span className="font-mono text-[9px] text-gray-400">{d.label}</span>
                  </div>
                ))}
              </div>
            )}
          </Card>
        </Section>

        <Section title="Breakdown">
          <Card padded={false} className="grid grid-cols-2 divide-x divide-y divide-line overflow-hidden">
            <Breakdown title="By machine" rows={a.sessionsByMachine} />
            <Breakdown title="By status" rows={a.sessionsByStatus} />
            <Breakdown title="Top projects" rows={a.topWorkspaces} />
            <Breakdown title="Memory by type" rows={a.memoriesByType} />
          </Card>
        </Section>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Section title="Daily digest">
          <Card padded={false} className="px-4 py-3">
            {digest ? (
              <>
                <div className="mb-2 font-mono text-[10px] text-gray-400">
                  {new Date(digest.createdAt).toLocaleString()}
                </div>
                <p className="whitespace-pre-line text-[13px] leading-relaxed text-gray-600 dark:text-gray-300">
                  {digest.body}
                </p>
              </>
            ) : (
              <Empty hint="The first one lands within a day.">No digest posted yet.</Empty>
            )}
          </Card>
        </Section>

        <Section title="Database backups">
          <Card padded={false} className="px-4 py-3">
            {backups?.configured ? (
              <>
                <div className="flex flex-wrap gap-x-8 gap-y-2">
                  {[
                    { k: "Dumps", v: String(backups.count) },
                    { k: "Total", v: fmtBytes(backups.totalBytes) },
                    { k: "Latest", v: backups.latestAt ? new Date(backups.latestAt).toLocaleDateString() : "—" },
                  ].map((x) => (
                    <div key={x.k}>
                      <div className="font-mono text-[10px] uppercase tracking-[0.1em] text-gray-400">{x.k}</div>
                      <div className="mt-0.5 font-mono text-[15px] tabular-nums text-gray-900 dark:text-white/90">{x.v}</div>
                    </div>
                  ))}
                </div>
                {backups.recent.length > 0 && (
                  <ul className="mt-3 flex flex-col gap-1 border-t border-line pt-2.5">
                    {backups.recent.slice(0, 3).map((f) => (
                      <li key={f.name} className="flex justify-between gap-2 font-mono text-[11px] text-gray-500 dark:text-gray-400">
                        <span className="truncate">{f.name}</span>
                        <span className="shrink-0 tabular-nums">{fmtBytes(f.sizeBytes)}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </>
            ) : (
              <Empty>Backup sidecar not reporting.</Empty>
            )}
          </Card>
        </Section>
      </div>
    </div>
  );
}
