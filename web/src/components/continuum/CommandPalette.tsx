"use client";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";

type Dest = { label: string; hint: string; path: string };

// Static navigation targets (mirror the sidebar).
const NAV: Dest[] = [
  { label: "Overview", hint: "dashboard", path: "/" },
  { label: "History", hint: "all sessions", path: "/sessions" },
  { label: "Ask my history", hint: "RAG chat", path: "/ask" },
  { label: "Search transcripts", hint: "full-text", path: "/search?mode=transcripts" },
  { label: "Search sessions", hint: "semantic", path: "/search?mode=sessions" },
  { label: "Memory", hint: "durable facts", path: "/memory" },
  { label: "Agents & Bus", hint: "inter-agent", path: "/agents" },
  { label: "Rooms", hint: "agent conversations", path: "/rooms" },
  { label: "Usage & cost", hint: "tokens", path: "/usage" },
  { label: "Redaction", hint: "secret scan", path: "/redaction" },
  { label: "Settings & tokens", hint: "account", path: "/settings" },
  { label: "Manage users", hint: "admin", path: "/users" },
];

export default function CommandPalette() {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  const close = useCallback(() => {
    setOpen(false);
    setQuery("");
    setActive(0);
  }, []);

  // Global ⌘K / Ctrl+K toggle.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen((o) => !o);
      } else if (e.key === "Escape") {
        setOpen(false);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  useEffect(() => {
    if (open) inputRef.current?.focus();
  }, [open]);

  // Build the result list: matching nav targets, plus "run this query" actions when text is typed.
  const results = useMemo<Dest[]>(() => {
    const q = query.trim().toLowerCase();
    const nav = NAV.filter((d) => !q || d.label.toLowerCase().includes(q) || d.hint.toLowerCase().includes(q));
    if (!q) return nav;
    const encoded = encodeURIComponent(query.trim());
    return [
      { label: `Ask “${query.trim()}”`, hint: "answer from history", path: `/ask?q=${encoded}` },
      { label: `Search sessions for “${query.trim()}”`, hint: "semantic", path: `/search?mode=sessions&q=${encoded}` },
      { label: `Search transcripts for “${query.trim()}”`, hint: "full-text", path: `/search?mode=transcripts&q=${encoded}` },
      ...nav,
    ];
  }, [query]);

  useEffect(() => {
    setActive((a) => Math.min(a, Math.max(0, results.length - 1)));
  }, [results.length]);

  const go = useCallback(
    (d: Dest | undefined) => {
      if (!d) return;
      close();
      router.push(d.path);
    },
    [close, router],
  );

  function onInputKey(e: React.KeyboardEvent) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActive((a) => Math.min(a + 1, results.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive((a) => Math.max(a - 1, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      go(results[active]);
    }
  }

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-99999 flex items-start justify-center bg-black/40 px-4 pt-[15vh] backdrop-blur-sm"
      onMouseDown={close}
    >
      <div
        className="w-full max-w-xl overflow-hidden rounded-2xl border border-gray-200 bg-white shadow-2xl dark:border-gray-800 dark:bg-gray-900"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <input
          ref={inputRef}
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            setActive(0);
          }}
          onKeyDown={onInputKey}
          placeholder="Jump to a page, or ask / search your history…"
          className="w-full border-b border-gray-100 bg-transparent px-5 py-4 text-base text-gray-800 placeholder:text-gray-400 focus:outline-none dark:border-gray-800 dark:text-white/90"
        />
        <ul className="max-h-80 overflow-y-auto py-2">
          {results.length === 0 && <li className="px-5 py-6 text-center text-sm text-gray-400">No matches.</li>}
          {results.map((d, i) => (
            <li key={d.path}>
              <button
                onMouseEnter={() => setActive(i)}
                onClick={() => go(d)}
                className={`flex w-full items-center justify-between gap-3 px-5 py-2.5 text-left text-sm ${
                  i === active ? "bg-brand-50 dark:bg-brand-500/10" : ""
                }`}
              >
                <span className="font-medium text-gray-800 dark:text-white/90">{d.label}</span>
                <span className="text-xs text-gray-400">{d.hint}</span>
              </button>
            </li>
          ))}
        </ul>
        <div className="flex items-center gap-4 border-t border-gray-100 px-5 py-2.5 text-xs text-gray-400 dark:border-gray-800">
          <span>↑↓ navigate</span>
          <span>↵ open</span>
          <span>esc close</span>
          <span className="ml-auto font-mono">⌘K</span>
        </div>
      </div>
    </div>
  );
}
