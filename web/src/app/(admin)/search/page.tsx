import Link from "next/link";
import { capi, SearchHit, SessionSearchHit, WorkspaceDto } from "@/lib/continuum";
import { Card, Chip, tagFor } from "@/components/bui";
import { PageHeader, Empty } from "@/components/bui/page";
import { Input, Select } from "@/components/bui/form";

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
    q && semantic
      ? capi<SessionSearchHit[]>(`/api/sessions/semantic?q=${encodeURIComponent(q)}&take=30`)
      : Promise.resolve<SessionSearchHit[]>([]),
    !semantic ? capi<WorkspaceDto[]>("/api/workspaces") : Promise.resolve<WorkspaceDto[]>([]),
  ]);

  const count = semantic ? sessions.length : events.length;

  /** Two search modes, so they read as a segmented control rather than as two buttons. */
  const tab = (m: string, label: string) => {
    const active = (m === "sessions") === semantic;
    return (
      <Link
        href={`/search?mode=${m}${q ? `&q=${encodeURIComponent(q)}` : ""}`}
        className={`rounded-control px-2.5 py-1 text-[12.5px] font-medium transition-colors ${
          active ? "bg-surface text-gray-900 shadow-btn dark:text-white/90" : "text-gray-500 hover:text-gray-800 dark:hover:text-white/90"
        }`}
      >
        {label}
      </Link>
    );
  };

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Search"
        subtitle={
          semantic
            ? "Find sessions by meaning, over the summaries Claude writes for each one."
            : "Full-text across every transcript you've captured."
        }
        actions={<div className="flex gap-0.5 rounded-control bg-stripe p-0.5">{tab("transcripts", "Transcripts")}{tab("sessions", "Semantic")}</div>}
      />

      <form action="/search" className="flex flex-wrap items-center gap-2">
        <input type="hidden" name="mode" value={semantic ? "sessions" : "transcripts"} />
        <Input
          name="q"
          size="lg"
          defaultValue={q ?? ""}
          autoFocus
          placeholder={semantic ? "Describe the session you're looking for…" : "Search everything you've ever done…"}
        />
        <button className="h-8 rounded-control bg-accent px-3 text-[13px] font-medium text-white hover:bg-accent-ink">
          Search
        </button>

        {!semantic && (
          <>
            <Select name="workspaceId" defaultValue={workspaceId ?? ""}>
              <option value="">All projects</option>
              {workspaces.map((w) => (
                <option key={w.id} value={w.id}>{w.displayName} ({w.sessionCount})</option>
              ))}
            </Select>
            <Select name="type" size="sm" defaultValue={type ?? ""}>
              <option value="">All types</option>
              {EVENT_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
            </Select>
            <Select name="sinceDays" size="sm" defaultValue={sinceDays ?? ""}>
              {SINCE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </Select>
          </>
        )}
      </form>

      {q && (
        <p className="font-mono text-[11px] text-gray-400">
          {count} {count === 1 ? "result" : "results"} for “{q}”
        </p>
      )}

      {!q ? (
        <Card padded={false}>
          <Empty hint={semantic ? "Semantic search matches meaning, so describe the session rather than guessing its words." : "Full-text search covers every message, tool call and result."}>
            Type something to search.
          </Empty>
        </Card>
      ) : count === 0 ? (
        <Card padded={false}>
          <Empty hint={semantic ? "Try describing it differently — this matches meaning, not keywords." : "Try fewer words, or widen the filters above."}>
            Nothing found for “{q}”.
          </Empty>
        </Card>
      ) : (
        <Card padded={false} className="divide-y divide-line">
          {semantic
            ? sessions.map((s) => (
                <Link key={s.id} href={`/sessions/${s.id}`} className="block px-4 py-3 hover:bg-stripe">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium text-gray-800 dark:text-white/90">{s.title || "(untitled)"}</span>
                    <Chip dot={tagFor(s.workspace)}>{s.workspace}</Chip>
                    <span className="ml-auto font-mono text-[11px] text-gray-400">
                      {new Date(s.lastEventAt).toLocaleDateString()}
                      {s.score != null && ` · match ${s.score.toFixed(2)}`}
                    </span>
                  </div>
                  {s.summary && <p className="mt-1 max-w-[95ch] text-[13px] text-gray-600 dark:text-gray-300">{s.summary}</p>}
                </Link>
              ))
            : events.map((h) => (
                <Link key={h.eventId} href={`/sessions/${h.sessionId}`} className="block px-4 py-3 hover:bg-stripe">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium text-gray-800 dark:text-white/90">{h.sessionTitle || "(untitled)"}</span>
                    <Chip dot={tagFor(h.workspace)}>{h.workspace}</Chip>
                    <Chip>{h.type}</Chip>
                    <span className="ml-auto font-mono text-[11px] text-gray-400">
                      {new Date(h.timestamp).toLocaleDateString()}
                    </span>
                  </div>
                  {h.snippet && <p className="mt-1 max-w-[95ch] text-[13px] text-gray-600 dark:text-gray-300">{h.snippet}</p>}
                </Link>
              ))}
        </Card>
      )}
    </div>
  );
}
