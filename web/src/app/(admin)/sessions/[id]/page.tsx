import Link from "next/link";
import { capi, SessionDetail } from "@/lib/continuum";
import TranscriptEvent from "@/components/continuum/TranscriptEvent";
import { Card, Chip, Label, tagFor } from "@/components/bui";

export const dynamic = "force-dynamic";

export default async function SessionDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  let detail: SessionDetail | null = null;
  try {
    detail = await capi<SessionDetail>(`/api/sessions/${id}?take=500`);
  } catch {
    detail = null;
  }

  if (!detail) return <p className="text-gray-400">Session not found.</p>;

  const s = detail.session;

  return (
    <div className="flex flex-col gap-4">
      <div>
        <Link href="/sessions" className="text-sm text-gray-500 hover:text-accent-ink">
          ← History
        </Link>
        <h2 className="mt-2 text-2xl font-semibold tracking-[-0.01em] text-gray-800 dark:text-white/90">
          {s.title || "(untitled session)"}
        </h2>
        {/* Facts about the session read better as chips than as a run-on line of separators. */}
        <div className="mt-2.5 flex flex-wrap items-center gap-1.5">
          <Chip dot={tagFor(s.workspace)}>{s.workspace}</Chip>
          <Chip dot={tagFor(s.machine)}>{s.machine}</Chip>
          <Chip className="font-mono">{s.messageCount} events</Chip>
          <Chip className="font-mono">{new Date(s.startedAt).toLocaleDateString()}</Chip>
        </div>
      </div>

      <Card className="flex flex-wrap items-center gap-3">
        <div className="min-w-0 flex-1">
          <Label>Resume on another machine</Label>
          <code className="mt-1.5 block truncate font-mono text-[13px] text-gray-700 dark:text-gray-200">
            claude --resume {s.id}
          </code>
        </div>
        <div className="flex shrink-0 flex-wrap gap-2">
          <a
            href={`/bff/dl/${s.id}/export.jsonl`}
            className="rounded-control bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-ink"
          >
            Transcript .jsonl
          </a>
          <a
            href={`/bff/dl/${s.id}/bundle.md`}
            className="rounded-control bg-surface px-3 py-1.5 text-sm font-medium text-gray-800 shadow-btn hover:bg-stripe dark:text-white/90"
          >
            Hand-off .md
          </a>
        </div>
      </Card>

      {/* One card holding the whole conversation, rather than one card per turn. */}
      <Card padded={false} className="px-5 py-1">
        {detail.events.map((e) => (
          <TranscriptEvent key={e.id} e={e} />
        ))}
      </Card>
    </div>
  );
}
