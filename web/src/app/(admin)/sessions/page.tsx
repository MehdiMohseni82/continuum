import Link from "next/link";
import { capi, SessionSummary, SessionStatus } from "@/lib/continuum";

export const metadata = { title: "Continuum — History" };
export const dynamic = "force-dynamic";

const statusColor: Record<SessionStatus, string> = {
  Live: "bg-success-50 text-success-600 dark:bg-success-500/15 dark:text-success-400",
  Ended: "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-300",
  Interrupted: "bg-error-50 text-error-600 dark:bg-error-500/15 dark:text-error-400",
  Unknown: "bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400",
};

export default async function SessionsPage({
  searchParams,
}: {
  searchParams: Promise<{ q?: string }>;
}) {
  const { q } = await searchParams;
  const query = q ? `?q=${encodeURIComponent(q)}&take=100` : "?take=100";
  const sessions = await capi<SessionSummary[]>(`/api/sessions${query}`);

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">History</h2>
          <p className="text-sm text-gray-500 dark:text-gray-400">Every session, across every machine.</p>
        </div>
        <form action="/sessions" className="flex gap-2">
          <input
            name="q"
            defaultValue={q ?? ""}
            placeholder="Filter by title…"
            className="h-10 w-64 rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
          />
          <button className="h-10 rounded-lg bg-brand-500 px-4 text-sm font-medium text-white hover:bg-brand-600">
            Filter
          </button>
        </form>
      </div>

      <div className="overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
        <div className="max-w-full overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-gray-200 text-left text-xs uppercase tracking-wide text-gray-400 dark:border-gray-800">
                <th className="px-5 py-3 font-medium">Title</th>
                <th className="px-5 py-3 font-medium">Project</th>
                <th className="px-5 py-3 font-medium">Machine</th>
                <th className="px-5 py-3 font-medium">Status</th>
                <th className="px-5 py-3 font-medium">Last activity</th>
                <th className="px-5 py-3 text-right font-medium">Events</th>
              </tr>
            </thead>
            <tbody>
              {sessions.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-5 py-10 text-center text-gray-400">
                    No sessions.
                  </td>
                </tr>
              ) : (
                sessions.map((s) => (
                  <tr key={s.id} className="border-b border-gray-100 last:border-0 hover:bg-gray-50 dark:border-gray-800/60 dark:hover:bg-white/[0.02]">
                    <td className="px-5 py-3">
                      <Link href={`/sessions/${s.id}`} className="font-medium text-brand-500 hover:underline">
                        {s.title || "(untitled)"}
                      </Link>
                    </td>
                    <td className="px-5 py-3 text-gray-600 dark:text-gray-300">{s.workspace}</td>
                    <td className="px-5 py-3 text-gray-600 dark:text-gray-300">{s.machine}</td>
                    <td className="px-5 py-3">
                      <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${statusColor[s.status]}`}>{s.status}</span>
                    </td>
                    <td className="px-5 py-3 text-gray-500 dark:text-gray-400">
                      {new Date(s.lastEventAt).toLocaleString()}
                    </td>
                    <td className="px-5 py-3 text-right tabular-nums text-gray-700 dark:text-gray-200">{s.messageCount}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
