import Link from "next/link";
import { capi, SearchHit, SessionSearchHit, WorkspaceDto } from "@/lib/continuum";

export const metadata = { title: "Search" };
export const dynamic = "force-dynamic";

const EVENT_TYPES = ["user", "assistant", "tool_use", "tool_result", "summary"];
const SINCE_OPTIONS = [
  { value: "", label: "Any time" },
  { value: "1", label: "Last 24h" },
  { value: "7", label: "Last 7 days" },
  { value: "30", label: "Last 30 days" },
  { value: "90", label: "Last 90 days" },
];

type Params = { q?: string; mode?: string; workspaceId?: string; type?: string; sinceDays?: string };

export default async function SearchPage({ searchParams }: { searchParams: Promise<Params> }) {
  const { q, mode, workspaceId, type, sinceDays } = await searchParams;
  const semantic = mode === "sessions";

  const searchQs = new URLSearchParams({ q: q ?? "", take: "100" });
  if (workspaceId) searchQs.set("workspaceId", workspaceId);
  if (type) searchQs.set("type", type);
  if (sinceDays) searchQs.set("sinceDays", sinceDays);

  const [events, sessions, workspaces] = await Promise.all([
    q && !semantic ? capi<SearchHit[]>(`/api/search?${searchQs}`) : Promise.resolve<SearchHit[]>([]),
    q && semantic ? capi<SessionSearchHit[]>(`/api/sessions/semantic?q=${encodeURIComponent(q)}&take=30`) : Promise.resolve<SessionSearchHit[]>([]),
    !semantic ? capi<WorkspaceDto[]>("/api/workspaces") : Promise.resolve<WorkspaceDto[]>([]),
  ]);

  const selectCls =
    "h-11 rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-700 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-gray-200 dark:bg-gray-900";

  const tab = (m: string, label: string) => {
    const active = (m === "sessions") === semantic;
    const href = `/search?mode=${m}${q ? `&q=${encodeURIComponent(q)}` : ""}`;
    return (
      <Link
        href={href}
        className={`rounded-lg px-3 py-1.5 text-sm font-medium ${active ? "bg-brand-500 text-white" : "text-gray-500 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-gray-800"}`}
      >
        {label}
      </Link>
    );
  };

  return (
    <div className="flex flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Search</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          {semantic ? "Find sessions by meaning (over auto-written summaries)." : "Full-text across every transcript."}
        </p>
      </div>

      <div className="flex gap-1">
        {tab("transcripts", "Transcripts")}
        {tab("sessions", "Sessions (semantic)")}
      </div>

      <form action="/search" className="flex flex-col gap-2">
        <input type="hidden" name="mode" value={semantic ? "sessions" : "transcripts"} />
        <div className="flex gap-2">
          <input
            name="q"
            defaultValue={q ?? ""}
            autoFocus
            placeholder={semantic ? "Describe the session you're looking for…" : "Search everything you've ever done…"}
            className="h-11 flex-1 rounded-lg border border-gray-300 bg-transparent px-4 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
          />
          <button className="h-11 rounded-lg bg-brand-500 px-5 text-sm font-medium text-white hover:bg-brand-600">Search</button>
        </div>
        {!semantic && (
          <div className="flex flex-wrap gap-2">
            <select name="workspaceId" defaultValue={workspaceId ?? ""} className={selectCls}>
              <option value="">All workspaces</option>
              {workspaces.map((w) => (
                <option key={w.id} value={w.id}>{w.displayName} ({w.sessionCount})</option>
              ))}
            </select>
            <select name="type" defaultValue={type ?? ""} className={selectCls}>
              <option value="">All event types</option>
              {EVENT_TYPES.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
            <select name="sinceDays" defaultValue={sinceDays ?? ""} className={selectCls}>
              {SINCE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          </div>
        )}
      </form>

      {q && <p className="text-sm text-gray-500 dark:text-gray-400">{(semantic ? sessions.length : events.length)} result(s) for “{q}”</p>}

      {semantic ? (
        <div className="flex flex-col gap-3">
          {sessions.map((s) => (
            <Link key={s.id} href={`/sessions/${s.id}`} className="rounded-2xl border border-gray-200 bg-white p-4 hover:border-brand-500 dark:border-gray-800 dark:bg-white/[0.03]">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-medium text-brand-500">{s.title || "(untitled)"}</span>
                <span className="text-xs text-gray-400">
                  {s.workspace} · {s.machine} · {new Date(s.lastEventAt).toLocaleDateString()}
                  {s.score != null && ` · match ${s.score.toFixed(2)}`}
                </span>
              </div>
              {s.summary && <p className="mt-2 text-sm text-gray-600 dark:text-gray-300">{s.summary}</p>}
            </Link>
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {events.map((h) => (
            <Link key={h.eventId} href={`/sessions/${h.sessionId}`} className="rounded-2xl border border-gray-200 bg-white p-4 hover:border-brand-500 dark:border-gray-800 dark:bg-white/[0.03]">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-medium text-brand-500">{h.sessionTitle || "(untitled)"}</span>
                <span className="text-xs text-gray-400">{h.workspace} · {h.type} · {new Date(h.timestamp).toLocaleString()}</span>
              </div>
              {h.snippet && <p className="mt-2 text-sm text-gray-600 dark:text-gray-300">{h.snippet}</p>}
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
