import Link from "next/link";
import { capi, SessionSummary, SessionStatus } from "@/lib/continuum";
import { Card, Chip, TAG, tagFor } from "@/components/bui";

export const metadata = { title: "Continuum — History" };
export const dynamic = "force-dynamic";

// Status is a state, so it takes a semantic colour rather than one from the categorical palette.
const STATUS_DOT: Record<SessionStatus, string> = {
  Live: TAG.green,
  Ended: "#9ca3af",
  Interrupted: TAG.red,
  Unknown: "#9ca3af",
};

/** "3h ago" reads faster down a column than a full locale timestamp. */
function ago(iso: string): string {
  const mins = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.round(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  const days = Math.round(hrs / 24);
  return days < 30 ? `${days}d ago` : new Date(iso).toLocaleDateString();
}

export default async function SessionsPage({
  searchParams,
}: {
  searchParams: Promise<{ q?: string }>;
}) {
  const { q } = await searchParams;
  const query = q ? `?q=${encodeURIComponent(q)}&take=100` : "?take=100";
  const sessions = await capi<SessionSummary[]>(`/api/sessions${query}`);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-2xl font-semibold tracking-[-0.01em] text-gray-800 dark:text-white/90">History</h2>
          <p className="mt-0.5 text-sm text-gray-500 dark:text-gray-400">
            Every session, across every machine.
          </p>
        </div>
        <form action="/sessions" className="flex gap-2">
          <input
            name="q"
            defaultValue={q ?? ""}
            placeholder="Filter by title…"
            className="w-56 rounded-control bg-stripe px-3 py-1.5 text-sm text-gray-800 shadow-inset-field placeholder:text-gray-400 focus:outline-none focus:shadow-[0_0_0_1px_var(--bui-accent)] dark:text-white/90"
          />
          <button className="rounded-control bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-ink">
            Filter
          </button>
        </form>
      </div>

      <Card padded={false} className="overflow-hidden">
        <div className="max-w-full overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-line text-left">
                {["Title", "Project", "Machine", "Status", "Last activity"].map((h) => (
                  <th key={h} className="px-4 py-2.5 font-mono text-[10px] font-normal uppercase tracking-[0.09em] text-gray-400">
                    {h}
                  </th>
                ))}
                <th className="px-4 py-2.5 text-right font-mono text-[10px] font-normal uppercase tracking-[0.09em] text-gray-400">
                  Events
                </th>
              </tr>
            </thead>
            <tbody>
              {sessions.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-4 py-12 text-center text-gray-400">
                    No sessions yet.
                  </td>
                </tr>
              ) : (
                sessions.map((s) => (
                  <tr key={s.id} className="border-b border-line last:border-0 hover:bg-stripe">
                    <td className="px-4 py-2.5">
                      <Link
                        href={`/sessions/${s.id}`}
                        className="font-medium text-gray-800 hover:text-accent-ink dark:text-white/90"
                      >
                        {s.title || "(untitled)"}
                      </Link>
                    </td>
                    {/* A stable hue per project and machine, so the eye groups rows without reading them. */}
                    <td className="px-4 py-2.5"><Chip dot={tagFor(s.workspace)}>{s.workspace}</Chip></td>
                    <td className="px-4 py-2.5"><Chip dot={tagFor(s.machine)}>{s.machine}</Chip></td>
                    <td className="px-4 py-2.5"><Chip dot={STATUS_DOT[s.status]}>{s.status}</Chip></td>
                    <td className="px-4 py-2.5 font-mono text-[12px] text-gray-500 dark:text-gray-400">
                      {ago(s.lastEventAt)}
                    </td>
                    <td className="px-4 py-2.5 text-right font-mono text-[12px] tabular-nums text-gray-600 dark:text-gray-300">
                      {s.messageCount}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}
