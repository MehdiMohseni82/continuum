"use client";
import { useState } from "react";
import type { EventDto } from "@/lib/continuum";
import { TAG } from "@/components/bui";

const LIMIT = 1400;

/** Roles that are the conversation itself, versus machinery around it. */
const SPOKEN = new Set(["user", "assistant"]);

const ROLE_TONE: Record<string, string> = {
  user: TAG.blue,
  assistant: TAG.violet,
  tool_use: TAG.amber,
  tool_result: TAG.cyan,
  system: TAG.lime,
};

/**
 * One turn of a transcript.
 *
 * The previous version wrapped every event — prose, code and tool output alike — in a bordered card
 * full of monospace, so a conversation read as a stack of identical boxes and the actual words were
 * the hardest thing in it to read. Here the transcript is set as a conversation: who spoke in a quiet
 * mono gutter, what they said in the body face at a real measure, and mono kept for the things that
 * genuinely are code. Machinery collapses to a single line you can open.
 */
export default function TranscriptEvent({ e }: { e: EventDto }) {
  const [open, setOpen] = useState(false);

  const text = e.text ?? "";
  const role = (e.role ?? e.type ?? "event").toLowerCase();
  const spoken = SPOKEN.has(role);
  const tone = ROLE_TONE[role] ?? TAG.green;
  const time = new Date(e.timestamp).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

  // Tool traffic is folded away by default: it is usually long, rarely what you came to read, and
  // always available one click down.
  if (!spoken) {
    return (
      <div className="grid grid-cols-[68px_1fr] gap-4 border-t border-line py-2.5 first:border-t-0">
        <div className="pt-0.5 text-right font-mono text-[11px] text-gray-400">{time}</div>
        <button
          onClick={() => setOpen((o) => !o)}
          className="flex w-full items-center gap-2 rounded-control bg-stripe px-2.5 py-1.5 text-left text-xs text-gray-500 shadow-hairline hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
        >
          <span className="size-1.5 shrink-0 rounded-full" style={{ background: tone }} />
          <span className="font-mono">{role}</span>
          {text && <span className="truncate opacity-70">{text.slice(0, 90)}</span>}
          <span className="ml-auto shrink-0 opacity-60">{open ? "▴" : "▾"}</span>
        </button>
        {open && text && (
          <pre className="col-start-2 mt-1.5 overflow-x-auto rounded-control bg-stripe p-3 font-mono text-[12px] leading-relaxed text-gray-600 shadow-hairline dark:text-gray-300">
            {text}
          </pre>
        )}
      </div>
    );
  }

  const long = text.length > LIMIT;
  const shown = open || !long ? text : text.slice(0, LIMIT) + " …";

  return (
    <div className="grid grid-cols-[68px_1fr] gap-4 border-t border-line py-3.5 first:border-t-0">
      <div className="pt-0.5 text-right font-mono text-[11px] text-gray-400">
        <span className="block font-medium" style={{ color: tone }}>
          {role === "user" ? "you" : "claude"}
        </span>
        {time}
      </div>
      <div className="min-w-0 max-w-[72ch] text-[14px] leading-[1.62] text-gray-700 dark:text-gray-200">
        <p className="whitespace-pre-wrap break-words">{shown}</p>
        {long && (
          <button
            onClick={() => setOpen((o) => !o)}
            className="mt-1.5 text-xs font-medium text-accent-ink hover:underline"
          >
            {open ? "Show less" : `Show all ${(text.length / 1000).toFixed(1)}k characters`}
          </button>
        )}
      </div>
    </div>
  );
}
