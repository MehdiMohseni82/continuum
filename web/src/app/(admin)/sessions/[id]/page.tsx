import Link from "next/link";
import { capi, SessionDetail } from "@/lib/continuum";
import TranscriptEvent from "@/components/continuum/TranscriptEvent";

export const dynamic = "force-dynamic";

export default async function SessionDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  let detail: SessionDetail | null = null;
  try {
    detail = await capi<SessionDetail>(`/api/sessions/${id}?take=500`);
  } catch {
    detail = null;
  }

  if (!detail) {
    return <p className="text-gray-400">Session not found.</p>;
  }

  const s = detail.session;
  return (
    <div className="flex flex-col gap-5">
      <div>
        <Link href="/sessions" className="text-sm text-gray-500 hover:text-brand-500">← History</Link>
        <h2 className="mt-2 text-2xl font-bold text-gray-800 dark:text-white/90">{s.title || "(untitled session)"}</h2>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          {s.workspace} · {s.machine} · {s.messageCount} events · {new Date(s.startedAt).toLocaleString()}
        </p>
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 dark:border-gray-800 dark:bg-white/[0.03]">
        <p className="mb-2 text-sm text-gray-500 dark:text-gray-400">Resume on another machine:</p>
        <pre className="mb-3 overflow-x-auto rounded-lg bg-gray-900 px-4 py-3 text-sm text-gray-100">claude --resume {s.id}</pre>
        <div className="flex flex-wrap gap-2">
          <a href={`/bff/dl/${s.id}/export.jsonl`} className="rounded-lg bg-brand-500 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600">
            ⬇ transcript (.jsonl)
          </a>
          <a href={`/bff/dl/${s.id}/bundle.md`} className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:border-brand-500 dark:border-gray-700 dark:text-gray-200">
            ⬇ hand-off (.md)
          </a>
        </div>
      </div>

      <div className="flex flex-col gap-3">
        {detail.events.map((e) => (
          <TranscriptEvent key={e.id} e={e} />
        ))}
      </div>
    </div>
  );
}
