"use client";
import { useState } from "react";
import Link from "next/link";
import type { AskResponse } from "@/lib/continuum";

type Turn = { q: string; a?: AskResponse; error?: boolean };

export default function AskPage() {
  const [q, setQ] = useState("");
  const [turns, setTurns] = useState<Turn[]>([]);
  const [busy, setBusy] = useState(false);

  async function ask(e: React.FormEvent) {
    e.preventDefault();
    const question = q.trim();
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
    <div className="mx-auto flex max-w-3xl flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Ask my history</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          Answers from your own sessions &amp; memories — retrieved and reasoned over locally.
        </p>
      </div>

      {turns.length === 0 && (
        <div className="rounded-2xl border border-dashed border-gray-300 p-8 text-center text-sm text-gray-400 dark:border-gray-700">
          Try: “What did I decide about the auth token contract?” · “Which projects use pgvector?” ·
          “What was the cost driver in the scraper?”
        </div>
      )}

      <div className="flex flex-col gap-4">
        {turns.map((t, i) => (
          <div key={i} className="flex flex-col gap-3">
            <div className="self-end rounded-2xl rounded-br-sm bg-brand-500 px-4 py-2 text-sm text-white">{t.q}</div>
            {t.a ? (
              <div className="rounded-2xl rounded-bl-sm border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
                <p className="whitespace-pre-wrap text-sm leading-relaxed text-gray-800 dark:text-gray-100">{t.a.answer}</p>
                {t.a.sources.length > 0 && (
                  <div className="mt-3 flex flex-wrap gap-2 border-t border-gray-100 pt-3 dark:border-gray-800">
                    {t.a.sources.map((s, j) =>
                      s.sessionId ? (
                        <Link
                          key={j}
                          href={`/sessions/${s.sessionId}`}
                          title={s.snippet}
                          className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs text-gray-600 hover:bg-brand-50 hover:text-brand-600 dark:bg-gray-800 dark:text-gray-300"
                        >
                          {s.kind === "memory" ? "🧠" : "📄"} {s.sessionTitle || "session"}
                        </Link>
                      ) : (
                        <span key={j} title={s.snippet} className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs text-gray-500 dark:bg-gray-800 dark:text-gray-400">
                          🧠 memory
                        </span>
                      )
                    )}
                  </div>
                )}
              </div>
            ) : t.error ? (
              <div className="rounded-2xl border border-error-200 bg-error-50 p-3 text-sm text-error-600 dark:border-error-500/30 dark:bg-error-500/10">
                Couldn&apos;t answer that — the backend or model may be busy.
              </div>
            ) : (
              <div className="flex items-center gap-2 text-sm text-gray-400">
                <span className="h-2 w-2 animate-bounce rounded-full bg-brand-400" />
                thinking over your history…
              </div>
            )}
          </div>
        ))}
      </div>

      <form onSubmit={ask} className="sticky bottom-4 flex gap-2">
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder="Ask anything about your past work…"
          className="h-12 flex-1 rounded-xl border border-gray-300 bg-white px-4 text-sm text-gray-800 shadow-sm focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:bg-gray-900 dark:text-white/90"
        />
        <button
          disabled={busy}
          className="h-12 rounded-xl bg-brand-500 px-6 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50"
        >
          Ask
        </button>
      </form>
    </div>
  );
}
