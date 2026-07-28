import Link from "next/link";
import { capi, RedactionHit } from "@/lib/continuum";

export const metadata = { title: "Continuum — Redaction" };
export const dynamic = "force-dynamic";

export default async function RedactionPage() {
  const hits = await capi<RedactionHit[]>(`/api/redaction/scan?scanLimit=5000`);

  return (
    <div className="flex flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Redaction review</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          Secrets found in captured transcripts. Memory is redacted automatically; the raw archive is not.
        </p>
      </div>

      {hits.length === 0 ? (
        <p className="py-10 text-center text-success-600">✓ No secrets detected in the scanned transcripts.</p>
      ) : (
        <>
          <p className="text-sm text-error-600">⚠ {hits.length} event(s) contain likely secrets.</p>
          <div className="flex flex-col gap-3">
            {hits.map((h) => (
              <div key={h.eventId} className="rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
                <div className="mb-2 flex flex-wrap items-center gap-2">
                  {h.labels.map((l) => (
                    <span key={l} className="rounded-full bg-error-50 px-2.5 py-0.5 text-xs font-medium text-error-600 dark:bg-error-500/15 dark:text-error-400">
                      {l}
                    </span>
                  ))}
                  <Link href={`/sessions/${h.sessionId}`} className="text-xs text-gray-400 hover:text-brand-500">
                    {h.sessionTitle || "(untitled)"}
                  </Link>
                </div>
                <pre className="whitespace-pre-wrap break-words font-mono text-[13px] text-gray-700 dark:text-gray-200">{h.snippet}</pre>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
