"use client";
import { useState } from "react";
import Link from "next/link";
import type { AskResponse } from "@/lib/continuum";
import { Card, Chip, TAG } from "@/components/bui";
import { PageHeader, Empty } from "@/components/bui/page";

type Turn = { q: string; a?: AskResponse; error?: boolean };

const EXAMPLES = [
  "What did I decide about the auth token contract?",
  "Which projects use pgvector?",
  "What was the cost driver in the scraper?",
];

export default function AskPage() {
  const [q, setQ] = useState("");
  const [turns, setTurns] = useState<Turn[]>([]);
  const [busy, setBusy] = useState(false);

  async function send(question: string) {
    if (!question || busy) return;
    setQ("");
    setBusy(true);
    setTurns((t) => [...t, { q: question }]);
    try {
      const res = await fetch("/bff/c/ask", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ question }),
      });
      const a: AskResponse = await res.json();
      setTurns((t) => t.map((x, i) => (i === t.length - 1 ? { ...x, a } : x)));
    } catch {
      setTurns((t) => t.map((x, i) => (i === t.length - 1 ? { ...x, error: true } : x)));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4">
      <PageHeader
        title="Ask my history"
        subtitle="Answers drawn from your own sessions and memories, retrieved and reasoned over on your own server."
      />

      {turns.length === 0 ? (
        <Card padded={false}>
          <Empty hint="Answers cite the sessions and memories they came from, so you can check them.">
            Ask anything about your past work.
          </Empty>
          {/* Examples are clickable rather than decorative — the fastest way to learn what it can do. */}
          <div className="flex flex-wrap justify-center gap-1.5 px-4 pb-4">
            {EXAMPLES.map((e) => (
              <button
                key={e}
                onClick={() => send(e)}
                className="rounded-chip bg-stripe px-2.5 py-1 text-left text-[12px] text-gray-600 shadow-hairline hover:text-accent-ink dark:text-gray-300"
              >
                {e}
              </button>
            ))}
          </div>
        </Card>
      ) : (
        <div className="flex flex-col gap-3">
          {turns.map((t, i) => (
            <div key={i} className="flex flex-col gap-2">
              <div className="self-end rounded-card rounded-br-sm bg-accent px-3 py-1.5 text-[13px] text-white">
                {t.q}
              </div>

              {t.a ? (
                <Card className="rounded-bl-sm">
                  <p className="whitespace-pre-wrap text-[13px] leading-relaxed text-gray-800 dark:text-gray-100">
                    {t.a.answer}
                  </p>
                  {t.a.sources.length > 0 && (
                    <div className="mt-3 flex flex-wrap gap-1.5 border-t border-line pt-2.5">
                      {t.a.sources.map((s, j) =>
                        s.sessionId ? (
                          <Link key={j} href={`/sessions/${s.sessionId}`} title={s.snippet}>
                            <Chip dot={s.kind === "memory" ? TAG.violet : TAG.blue}>
                              {s.sessionTitle || "session"}
                            </Chip>
                          </Link>
                        ) : (
                          <span key={j} title={s.snippet}>
                            <Chip dot={TAG.violet}>memory</Chip>
                          </span>
                        ),
                      )}
                    </div>
                  )}
                </Card>
              ) : t.error ? (
                <Card className="border-l-2 border-l-[#ee6572]">
                  <p className="text-[13px] text-gray-700 dark:text-gray-200">
                    Couldn&apos;t answer that.
                  </p>
                  <p className="mt-1 text-[12px] text-gray-400">
                    The model runs on your own server and is slow under load — worth retrying.
                  </p>
                </Card>
              ) : (
                <div className="flex items-center gap-2 px-1 text-[12px] text-gray-400">
                  <span className="size-1.5 animate-pulse rounded-full bg-accent" />
                  Reading your history…
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      <form
        onSubmit={(e) => { e.preventDefault(); send(q.trim()); }}
        className="sticky bottom-4 flex gap-2"
      >
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder="Ask anything about your past work…"
          className="h-9 flex-1 rounded-control bg-surface px-3 text-[13px] text-gray-800 shadow-btn placeholder:text-gray-400 focus:outline-none focus:shadow-[0_0_0_1px_var(--bui-accent)] dark:text-white/90"
        />
        <button
          disabled={busy}
          className="h-9 shrink-0 rounded-control bg-accent px-4 text-[13px] font-medium text-white hover:bg-accent-ink disabled:opacity-50"
        >
          {busy ? "…" : "Ask"}
        </button>
      </form>
    </div>
  );
}
