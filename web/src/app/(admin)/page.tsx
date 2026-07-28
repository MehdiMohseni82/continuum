import { capi, Analytics } from "@/lib/continuum";

export const metadata = { title: "Continuum — Overview" };
export const dynamic = "force-dynamic";

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
      <span className="text-sm text-gray-500 dark:text-gray-400">{label}</span>
      <h4 className="mt-2 text-3xl font-bold text-gray-800 tabular-nums dark:text-white/90">
        {value.toLocaleString()}
      </h4>
    </div>
  );
}

function Breakdown({ title, rows }: { title: string; rows: { label: string; count: number }[] }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  return (
    <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
      <h3 className="mb-4 text-base font-semibold text-gray-800 dark:text-white/90">{title}</h3>
      {rows.length === 0 ? (
        <p className="text-sm text-gray-400">—</p>
      ) : (
        <ul className="flex flex-col gap-3">
          {rows.map((r) => (
            <li key={r.label} className="text-sm">
              <div className="mb-1 flex justify-between text-gray-600 dark:text-gray-300">
                <span className="truncate pr-2">{r.label}</span>
                <span className="tabular-nums text-gray-800 dark:text-white/90">{r.count.toLocaleString()}</span>
              </div>
              <div className="h-1.5 w-full rounded-full bg-gray-100 dark:bg-gray-800">
                <div className="h-1.5 rounded-full bg-brand-500" style={{ width: `${(r.count / max) * 100}%` }} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export default async function OverviewPage() {
  const a = await capi<Analytics>("/api/analytics");
  const maxDay = Math.max(1, ...a.eventsPerDay.map((d) => d.count));

  return (
    <div className="flex flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Overview</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">Everything captured across your machines.</p>
      </div>

      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-5">
        <Stat label="Sessions" value={a.sessions} />
        <Stat label="Events" value={a.events} />
        <Stat label="Memories" value={a.memories} />
        <Stat label="Agents" value={a.agents} />
        <Stat label="Hand-offs" value={a.handoffs} />
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
        <h3 className="mb-4 text-base font-semibold text-gray-800 dark:text-white/90">Events per day (last 14)</h3>
        {a.eventsPerDay.length === 0 ? (
          <p className="text-sm text-gray-400">No recent events.</p>
        ) : (
          <div className="flex h-40 items-end gap-2">
            {a.eventsPerDay.map((d) => (
              <div key={d.label} className="flex flex-1 flex-col items-center justify-end gap-2" title={`${d.label}: ${d.count}`}>
                <div className="w-2/3 rounded-t bg-brand-500" style={{ height: `${(d.count / maxDay) * 100}%` }} />
                <span className="text-[10px] text-gray-400">{d.label}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Breakdown title="By machine" rows={a.sessionsByMachine} />
        <Breakdown title="By status" rows={a.sessionsByStatus} />
        <Breakdown title="Top projects" rows={a.topWorkspaces} />
        <Breakdown title="Memory by type" rows={a.memoriesByType} />
      </div>
    </div>
  );
}
