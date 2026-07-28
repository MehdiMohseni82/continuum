import Link from "next/link";
import { capi, SearchHit } from "@/lib/continuum";

export const metadata = { title: "Continuum — Search" };
export const dynamic = "force-dynamic";

export default async function SearchPage({ searchParams }: { searchParams: Promise<{ q?: string }> }) {
  const { q } = await searchParams;
  const hits = q ? await capi<SearchHit[]>(`/api/search?q=${encodeURIComponent(q)}&take=100`) : [];

  return (
    <div className="flex flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Search</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">Full-text across every session on every machine.</p>
      </div>

      <form action="/search" className="flex gap-2">
        <input
          name="q"
          defaultValue={q ?? ""}
          autoFocus
          placeholder="Search everything you've ever done…"
          className="h-11 flex-1 rounded-lg border border-gray-300 bg-transparent px-4 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
        />
        <button className="h-11 rounded-lg bg-brand-500 px-5 text-sm font-medium text-white hover:bg-brand-600">Search</button>
      </form>

      {q && (
        <p className="text-sm text-gray-500 dark:text-gray-400">{hits.length} result(s) for “{q}”</p>
      )}

      <div className="flex flex-col gap-3">
        {hits.map((h) => (
          <Link
            key={h.eventId}
            href={`/sessions/${h.sessionId}`}
            className="rounded-2xl border border-gray-200 bg-white p-4 hover:border-brand-500 dark:border-gray-800 dark:bg-white/[0.03]"
          >
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="font-medium text-brand-500">{h.sessionTitle || "(untitled)"}</span>
              <span className="text-xs text-gray-400">
                {h.workspace} · {h.type} · {new Date(h.timestamp).toLocaleString()}
              </span>
            </div>
            {h.snippet && <p className="mt-2 text-sm text-gray-600 dark:text-gray-300">{h.snippet}</p>}
          </Link>
        ))}
      </div>
    </div>
  );
}
