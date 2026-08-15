import { capi, TokenStats } from "@/lib/continuum";
import { Card, tagFor } from "@/components/bui";
import { PageHeader, Section, StatStrip, Empty } from "@/components/bui/page";
import { DataTable } from "@/components/bui/table";

export const metadata = { title: "Usage" };
export const dynamic = "force-dynamic";

const fmt = (n: number) =>
  n >= 1e9 ? (n / 1e9).toFixed(1) + "B" : n >= 1e6 ? (n / 1e6).toFixed(1) + "M" : n >= 1e3 ? (n / 1e3).toFixed(1) + "K" : `${n}`;
const usd = (n: number) => "$" + n.toLocaleString(undefined, { maximumFractionDigits: n < 100 ? 2 : 0 });

export default async function UsagePage() {
  const t = await capi<TokenStats>("/api/analytics/tokens");
  const totalTokens = t.totalInput + t.totalOutput + t.totalCacheRead + t.totalCacheWrite;
  const maxDay = Math.max(1, ...t.perDay.map((d) => d.costUsd));
  const maxProj = Math.max(1, ...t.byProject.map((p) => p.costUsd));

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Usage & cost"
        subtitle="Token usage from every captured session. Cost is an estimate at pay-as-you-go API rates — on a subscription the marginal cost is effectively zero, and cache reads dominate the volume."
      />

      <StatStrip
        stats={[
          { label: "Total tokens", value: fmt(totalTokens) },
          { label: "Output", value: fmt(t.totalOutput), hint: "the generative part" },
          { label: "Cache reads", value: fmt(t.totalCacheRead), hint: "re-read context" },
          { label: "Est. cost", value: usd(t.estimatedCostUsd), hint: "if paid per token" },
        ]}
      />

      <Section title="By model">
        <DataTable
          rows={t.byModel}
          rowKey={(m) => m.model}
          empty="No token usage recorded."
          emptyHint="Usage appears once sessions with token counts have been ingested."
          columns={[
            { key: "model", header: "Model", cell: (m) => <span className="font-medium text-gray-800 dark:text-white/90">{m.model}</span> },
            { key: "in", header: "Input", numeric: true, cell: (m) => fmt(m.input) },
            { key: "out", header: "Output", numeric: true, cell: (m) => fmt(m.output) },
            { key: "cr", header: "Cache read", numeric: true, cell: (m) => fmt(m.cacheRead) },
            { key: "cw", header: "Cache write", numeric: true, cell: (m) => fmt(m.cacheWrite) },
            {
              key: "cost",
              header: "Est. cost",
              numeric: true,
              cell: (m) => <span className="font-medium text-gray-900 dark:text-white/90">{usd(m.costUsd)}</span>,
            },
          ]}
        />
      </Section>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Section title="Est. cost by project">
          <Card padded={false} className="px-4 py-3">
            {t.byProject.length === 0 ? (
              <Empty>Nothing attributed yet.</Empty>
            ) : (
              <ul className="flex flex-col gap-2">
                {t.byProject.map((p) => (
                  <li key={p.label}>
                    <div className="flex items-baseline justify-between gap-2 text-[12.5px]">
                      <span className="truncate text-gray-600 dark:text-gray-300">{p.label}</span>
                      <span className="shrink-0 font-mono tabular-nums text-gray-900 dark:text-white/90">{usd(p.costUsd)}</span>
                    </div>
                    <div className="mt-1 h-[3px] w-full rounded-full bg-line">
                      <div
                        className="h-[3px] rounded-full"
                        style={{ width: `${(p.costUsd / maxProj) * 100}%`, background: tagFor(p.label) }}
                      />
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </Section>

        <Section title="Est. cost per day · 30 days">
          <Card padded={false} className="px-4 py-3">
            {t.perDay.length === 0 ? (
              <Empty>No usage in the last 30 days.</Empty>
            ) : (
              <div className="flex h-28 items-end gap-1">
                {t.perDay.map((d) => (
                  <div key={d.label} className="flex flex-1 flex-col items-center justify-end gap-1" title={`${d.label}: ${usd(d.costUsd)}`}>
                    <div
                      className="w-full rounded-[2px] bg-accent"
                      style={{ height: `${Math.max(2, (d.costUsd / maxDay) * 100)}%` }}
                    />
                    <span className="font-mono text-[9px] text-gray-400">{d.label}</span>
                  </div>
                ))}
              </div>
            )}
          </Card>
        </Section>
      </div>
    </div>
  );
}
