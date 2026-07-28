import { capi, Analytics, BackupStatus, BusMessage } from "@/lib/continuum";

export const metadata = { title: "Continuum — Overview" };
export const dynamic = "force-dynamic";

async function tryGet<T>(path: string): Promise<T | null> {
  try {
    return await capi<T>(path);
  } catch {
    return null; // 204 (no digest yet) or backups not configured in this environment
  }
}

function fmtBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let v = n / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(1)} ${units[i]}`;
}

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
  const [a, digest, backups] = await Promise.all([
    capi<Analytics>("/api/analytics"),
    tryGet<BusMessage>("/api/digest/latest"),
    tryGet<BackupStatus>("/api/backups"),
  ]);
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

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
          <div className="mb-3 flex items-center justify-between">
            <h3 className="text-base font-semibold text-gray-800 dark:text-white/90">Daily digest</h3>
            {digest && (
              <span className="text-xs text-gray-400">{new Date(digest.createdAt).toLocaleString()}</span>
            )}
          </div>
          {digest ? (
            <p className="whitespace-pre-line text-sm leading-relaxed text-gray-600 dark:text-gray-300">{digest.body}</p>
          ) : (
            <p className="text-sm text-gray-400">No digest posted yet — the first one lands within a day.</p>
          )}
        </div>

        <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
          <h3 className="mb-3 text-base font-semibold text-gray-800 dark:text-white/90">Database backups</h3>
          {backups?.configured ? (
            <div className="flex flex-col gap-3">
              <div className="flex gap-6">
                <div>
                  <span className="text-sm text-gray-500 dark:text-gray-400">Dumps</span>
                  <p className="text-2xl font-bold tabular-nums text-gray-800 dark:text-white/90">{backups.count}</p>
                </div>
                <div>
                  <span className="text-sm text-gray-500 dark:text-gray-400">Total size</span>
                  <p className="text-2xl font-bold tabular-nums text-gray-800 dark:text-white/90">{fmtBytes(backups.totalBytes)}</p>
                </div>
                <div>
                  <span className="text-sm text-gray-500 dark:text-gray-400">Latest</span>
                  <p className="text-sm font-medium text-gray-800 dark:text-white/90">
                    {backups.latestAt ? new Date(backups.latestAt).toLocaleString() : "—"}
                  </p>
                </div>
              </div>
              {backups.recent.length > 0 && (
                <ul className="flex flex-col gap-1 border-t border-gray-100 pt-3 dark:border-gray-800">
                  {backups.recent.slice(0, 3).map((f) => (
                    <li key={f.name} className="flex justify-between font-mono text-xs text-gray-500 dark:text-gray-400">
                      <span className="truncate pr-2">{f.name}</span>
                      <span className="tabular-nums">{fmtBytes(f.sizeBytes)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ) : (
            <p className="text-sm text-gray-400">Backup sidecar not reporting yet.</p>
          )}
        </div>
      </div>
    </div>
  );
}
