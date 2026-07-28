"use client";
import { useState } from "react";
import type { EventDto } from "@/lib/continuum";

const LIMIT = 700;

export default function TranscriptEvent({ e }: { e: EventDto }) {
  const [open, setOpen] = useState(false);
  const text = e.text ?? "";
  const long = text.length > LIMIT;
  const shown = open || !long ? text : text.slice(0, LIMIT) + " …";
  const role = e.role ?? e.type;
  const isUser = role === "user";

  return (
    <div className={`rounded-2xl border border-gray-200 p-4 dark:border-gray-800 ${isUser ? "bg-brand-50/60 dark:bg-brand-500/[0.06]" : "bg-white dark:bg-white/[0.03]"}`}>
      <div className="mb-2 flex items-center justify-between text-xs uppercase tracking-wide text-gray-400">
        <span className="font-semibold text-gray-600 dark:text-gray-300">{role}</span>
        <span>{new Date(e.timestamp).toLocaleTimeString()}</span>
      </div>
      {text && (
        <pre className="whitespace-pre-wrap break-words font-mono text-[13px] leading-relaxed text-gray-700 dark:text-gray-200">
          {shown}
        </pre>
      )}
      {long && (
        <button
          onClick={() => setOpen((o) => !o)}
          className="mt-2 text-xs font-medium text-brand-500 hover:underline"
        >
          {open ? "Show less" : `Show more (${(text.length / 1000).toFixed(1)}k chars)`}
        </button>
      )}
    </div>
  );
}
