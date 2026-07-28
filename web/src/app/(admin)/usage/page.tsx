import { capi, TokenStats } from "@/lib/continuum";

export const metadata = { title: "Usage" };
export const dynamic = "force-dynamic";

const fmt = (n: number) => (n >= 1e9 ? (n / 1e9).toFixed(1) + "B" : n >= 1e6 ? (n / 1e6).toFixed(1) + "M" : n >= 1e3 ? (n / 1e3).toFixed(1) + "K" : `${n}`);
const usd = (n: number) => "$" + n.toLocaleString(undefined, { maximumFractionDigits: 0 });

function Tile({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
      <span className="text-sm text-gray-500 dark:text-gray-400">{label}</span>
      <h4 className="mt-2 text-3xl font-bold text-gray-800 tabular-nums dark:text-white/90">{value}</h4>
      {sub && <span className="text-xs text-gray-400">{sub}</span>}
    </div>
  );
}

export default async function UsagePage() {
  const t = await capi<TokenStats>("/api/analytics/tokens");
  const totalTokens = t.totalInput + t.totalOutput + t.totalCacheRead + t.totalCacheWrite;
  const maxDay = Math.max(1, ...t.perDay.map((d) => d.costUsd));
  const maxProj = Math.max(1, ...t.byProject.map((p) => p.costUsd));

  return (
    <div className="flex flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Usage &amp; cost</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          Token usage from every captured session. Cost is an <strong>estimate at API pay-as-you-go rates</strong> —
          on a subscription the marginal cost is effectively $0, and cache reads dominate the volume.
        </p>
      </div>

      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <Tile label="Total tokens" value={fmt(totalTokens)} />
        <Tile label="Output tokens" value={fmt(t.totalOutput)} sub="the generative part" />
        <Tile label="Cache reads" value={fmt(t.totalCacheRead)} sub="cached context re-reads" />
        <Tile label="Est. cost (API rates)" value={usd(t.estimatedCostUsd)} sub="if paid per-token" />
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
        <h3 className="mb-4 text-base font-semibold text-gray-800 dark:text-white/90">By model</h3>
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-gray-200 text-left text-xs uppercase tracking-wide text-gray-400 dark:border-gray-800">
                <th className="px-3 py-2 font-medium">Model</th>
                <th className="px-3 py-2 text-right font-medium">Input</th>
                <th className="px-3 py-2 text-right font-medium">Output</th>
                <th className="px-3 py-2 text-right font-medium">Cache read</th>
                <th className="px-3 py-2 text-right font-medium">Cache write</th>
                <th className="px-3 py-2 text-right font-medium">Est. cost</th>
              </tr>
            </thead>
            <tbody>
              {t.byModel.map((m) => (
                <tr key={m.model} className="border-b border-gray-100 last:border-0 dark:border-gray-800/60">
                  <td className="px-3 py-2 font-medium text-gray-800 dark:text-white/90">{m.model}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-600 dark:text-gray-300">{fmt(m.input)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-600 dark:text-gray-300">{fmt(m.output)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-600 dark:text-gray-300">{fmt(m.cacheRead)}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-600 dark:text-gray-300">{fmt(m.cacheWrite)}</td>
                  <td className="px-3 py-2 text-right tabular-nums font-medium text-gray-800 dark:text-white/90">{usd(m.costUsd)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
          <h3 className="mb-4 text-base font-semibold text-gray-800 dark:text-white/90">Est. cost by project</h3>
          {t.byProject.length === 0 ? (
            <p className="text-sm text-gray-400">—</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {t.byProject.map((p) => (
                <li key={p.label} className="text-sm">
                  <div className="mb-1 flex justify-between text-gray-600 dark:text-gray-300">
                    <span className="truncate pr-2">{p.label}</span>
                    <span className="tabular-nums text-gray-800 dark:text-white/90">{usd(p.costUsd)}</span>
                  </div>
                  <div className="h-1.5 w-full rounded-full bg-gray-100 dark:bg-gray-800">
                    <div className="h-1.5 rounded-full bg-brand-500" style={{ width: `${(p.costUsd / maxProj) * 100}%` }} />
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
          <h3 className="mb-4 text-base font-semibold text-gray-800 dark:text-white/90">Est. cost per day (30d)</h3>
          {t.perDay.length === 0 ? (
            <p className="text-sm text-gray-400">No recent usage.</p>
          ) : (
            <div className="flex h-40 items-end gap-1.5">
              {t.perDay.map((d) => (
                <div key={d.label} className="flex flex-1 flex-col items-center justify-end gap-1" title={`${d.label}: ${usd(d.costUsd)}`}>
                  <div className="w-2/3 rounded-t bg-brand-500" style={{ height: `${(d.costUsd / maxDay) * 100}%` }} />
                  <span className="text-[9px] text-gray-400">{d.label}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
