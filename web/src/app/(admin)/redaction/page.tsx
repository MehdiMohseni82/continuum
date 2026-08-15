import Link from "next/link";
import { capi, RedactionHit } from "@/lib/continuum";
import { Card, Chip, TAG } from "@/components/bui";
import { PageHeader, Empty } from "@/components/bui/page";

export const metadata = { title: "Continuum — Redaction" };
export const dynamic = "force-dynamic";

export default async function RedactionPage() {
  const hits = await capi<RedactionHit[]>(`/api/redaction/scan?scanLimit=5000`);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Redaction review"
        subtitle="Secrets found in captured transcripts. Memory is redacted automatically; the raw archive is not."
        actions={
          // This is the one screen where colour should mean danger rather than category.
          <Chip dot={hits.length === 0 ? TAG.green : TAG.red}>
            {hits.length === 0 ? "clean" : `${hits.length} to review`}
          </Chip>
        }
      />

      {hits.length === 0 ? (
        <Card padded={false}>
          <Empty hint="Scanned the 5,000 most recent events. Memory is always redacted before storage; this checks the raw archive.">
            No secrets detected.
          </Empty>
        </Card>
      ) : (
        <Card padded={false} className="divide-y divide-line">
          {hits.map((h) => (
            <article key={h.eventId} className="px-4 py-3">
              <div className="mb-1.5 flex flex-wrap items-center gap-2">
                {h.labels.map((l) => (
                  <Chip key={l} dot={TAG.red}>{l}</Chip>
                ))}
                <Link
                  href={`/sessions/${h.sessionId}`}
                  className="ml-auto text-[12px] text-gray-400 hover:text-accent-ink"
                >
                  {h.sessionTitle || "(untitled session)"} →
                </Link>
              </div>
              <pre className="max-w-[100ch] overflow-x-auto whitespace-pre-wrap break-words rounded-control bg-stripe p-2.5 font-mono text-[12px] leading-relaxed text-gray-700 shadow-hairline dark:text-gray-200">
                {h.snippet}
              </pre>
            </article>
          ))}
        </Card>
      )}
    </div>
  );
}
