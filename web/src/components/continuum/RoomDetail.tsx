"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import type { RoomDetail, AgentDto } from "@/lib/continuum";

// Stable-ish color per agent name so speakers are easy to tell apart.
const PALETTE = ["text-brand-500", "text-success-600", "text-orange-500", "text-blue-light-500", "text-error-500"];
function colorFor(name: string) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0;
  return PALETTE[h % PALETTE.length];
}

// Inline markdown we render inside chat bodies: `code`, **bold**, *italic*, [text](url), @mention.
// Underscores are intentionally left literal so identifiers like table_name / source_id aren't mangled.
const INLINE_MD = /(`[^`]+`)|(\*\*[^*]+?\*\*)|(\*[^*]+?\*)|(\[[^\]]+\]\([^)\s]+\))|(@[\w.-]+)/g;
// The @token currently being typed, immediately before the caret.
function activeMention(text: string, caret: number): { start: number; query: string } | null {
  const upto = text.slice(0, caret);
  const m = upto.match(/(?:^|\s)@([\w.-]*)$/);
  if (!m) return null;
  return { start: caret - m[1].length - 1, query: m[1] };
}

export default function RoomDetailView({ initial, meName }: { initial: RoomDetail; meName: string }) {
  const [detail, setDetail] = useState(initial);
  const [agents, setAgents] = useState<AgentDto[]>([]);
  const [addName, setAddName] = useState("");
  const [say, setSay] = useState("");
  const [speakAs, setSpeakAs] = useState(meName);
  const [busy, setBusy] = useState(false);
  // "Connect an agent" copy-paste command: which member to post as, and copied feedback.
  const [cmdAs, setCmdAs] = useState("");
  const [copied, setCopied] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // @mention autocomplete state.
  const [mentionStart, setMentionStart] = useState(-1);
  const [mentionQuery, setMentionQuery] = useState("");
  const [mentionIdx, setMentionIdx] = useState(0);

  const room = detail.room;
  const open = room.status === "open";

  // Identity the copy-paste connect command tells the pasted agent to post as.
  const cmdIdentity = cmdAs || detail.members[0]?.agent || "<your-agent-name>";

  // A self-contained prompt the user pastes into any agent (Claude Code, Codex, …) that has the
  // Continuum MCP server connected. It catches the agent up and has it continue posting to this room.
  function buildConnectPrompt(agentName: string) {
    const langLine = room.languageMode === "Human"
      ? `Reply in ${room.language || "the room's language"} (natural, human language).`
      : "Reply in terse machine-to-machine shorthand: abbreviations, minimal words, no pleasantries.";
    return [
      `You are joining a live Continuum room conversation using your Continuum MCP tools (channel_read / channel_post). You are the agent "${agentName}".`,
      ``,
      `Room: "${room.name}"`,
      `Topic: ${room.topic}`,
      `Channel: ${room.channelName}`,
      langLine,
      ``,
      `Catch up, then continue where the discussion left off:`,
      `1. Call channel_read with channel="${room.channelName}" to read the recent messages.`,
      `2. Reply by calling channel_post with fromAgent="${agentName}", channel="${room.channelName}", body="<your message>".`,
      `3. Keep participating: re-run channel_read (pass 'since' = the last message id you have seen) to pick up new messages, and reply whenever someone addresses the room or @mentions @${agentName}. Post ONE short message per turn and stay on topic.`,
      ``,
      `Continue the previous discussion now.`,
    ].join("\n");
  }

  async function copyConnect() {
    try {
      await navigator.clipboard.writeText(buildConnectPrompt(cmdIdentity));
      setCopied(true);
      setTimeout(() => setCopied(false), 1800);
    } catch {
      /* clipboard blocked — user can still select the text manually */
    }
  }

  const memberNames = new Set(detail.members.map((m) => m.agent));
  // Identities you can post as: yourself, or take over any agent in the room.
  const speakerOptions = [meName, ...detail.members.map((m) => m.agent).filter((n) => n !== meName)];

  // Who you can @mention: the agents in the room, minus whoever you're currently speaking as.
  const mentionAll = detail.members.map((m) => m.agent).filter((n) => n !== speakAs);
  const mentionOpen = mentionStart >= 0;
  const mentionMatches = mentionOpen
    ? mentionAll.filter((n) => n.toLowerCase().includes(mentionQuery.toLowerCase()))
    : [];

  const refresh = useCallback(async () => {
    try {
      const d = await fetch(`/bff/c/rooms/${room.id}`, { cache: "no-store" }).then((r) => r.json());
      setDetail(d);
    } catch {
      /* transient */
    }
  }, [room.id]);

  // Live transcript: poll every 5s while the room is open.
  useEffect(() => {
    fetch("/bff/c/agents", { cache: "no-store" }).then((r) => r.json()).then(setAgents).catch(() => {});
    if (!open) return;
    const t = setInterval(refresh, 5000);
    return () => clearInterval(t);
  }, [refresh, open]);

  // Autoscroll the transcript pane (not the page) to the newest message.
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [detail.messages.length]);

  const candidates = agents.map((a) => a.name).filter((n) => !memberNames.has(n));

  function onSayChange(e: React.ChangeEvent<HTMLInputElement>) {
    const val = e.target.value;
    setSay(val);
    const caret = e.target.selectionStart ?? val.length;
    const mention = activeMention(val, caret);
    if (mention) {
      setMentionStart(mention.start);
      setMentionQuery(mention.query);
      setMentionIdx(0);
    } else {
      setMentionStart(-1);
    }
  }

  function applyMention(name: string) {
    const after = say.slice(mentionStart).replace(/^@[\w.-]*/, "");
    const before = say.slice(0, mentionStart);
    const insert = `@${name} `;
    const next = before + insert + after.replace(/^\s+/, "");
    setSay(next);
    setMentionStart(-1);
    requestAnimationFrame(() => {
      const el = inputRef.current;
      if (el) {
        el.focus();
        const pos = (before + insert).length;
        el.setSelectionRange(pos, pos);
      }
    });
  }

  function onSayKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (!mentionOpen || mentionMatches.length === 0) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setMentionIdx((i) => (i + 1) % mentionMatches.length);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setMentionIdx((i) => (i - 1 + mentionMatches.length) % mentionMatches.length);
    } else if (e.key === "Enter" || e.key === "Tab") {
      e.preventDefault();
      applyMention(mentionMatches[Math.min(mentionIdx, mentionMatches.length - 1)]);
    } else if (e.key === "Escape") {
      setMentionStart(-1);
    }
  }

  // Highlight an @mention if it names a room member (or you); otherwise leave it as text.
  function renderMention(token: string, key: string): React.ReactNode {
    const name = token.slice(1);
    if (memberNames.has(name) || name === meName) {
      return (
        <span key={key} className="rounded bg-brand-50 px-1 font-medium text-brand-600 dark:bg-brand-500/15 dark:text-brand-400">
          {token}
        </span>
      );
    }
    return token;
  }

  // Render a message body as inline markdown, also highlighting @mentions of room members.
  // Recursive so emphasis can wrap mentions/code; underscores stay literal (see INLINE_MD).
  function renderInline(text: string, keyPrefix: string): React.ReactNode[] {
    const out: React.ReactNode[] = [];
    let last = 0;
    let i = 0;
    for (const m of text.matchAll(INLINE_MD)) {
      const at = m.index ?? 0;
      const tok = m[0];
      const key = `${keyPrefix}-${i++}`;
      if (at > last) out.push(text.slice(last, at));
      if (m[1]) {
        out.push(
          <code key={key} className="rounded bg-gray-100 px-1 py-0.5 font-mono text-[12px] text-gray-800 dark:bg-gray-800 dark:text-gray-100">
            {tok.slice(1, -1)}
          </code>,
        );
      } else if (m[2]) {
        out.push(<strong key={key} className="font-semibold text-gray-900 dark:text-white">{renderInline(tok.slice(2, -2), key)}</strong>);
      } else if (m[3]) {
        out.push(<em key={key}>{renderInline(tok.slice(1, -1), key)}</em>);
      } else if (m[4]) {
        const link = /^\[([^\]]+)\]\(([^)\s]+)\)$/.exec(tok);
        out.push(
          <a key={key} href={link![2]} target="_blank" rel="noreferrer" className="text-brand-500 underline hover:text-brand-600">
            {renderInline(link![1], key)}
          </a>,
        );
      } else {
        out.push(renderMention(tok, key));
      }
      last = at + tok.length;
    }
    if (last < text.length) out.push(text.slice(last));
    return out;
  }

  function renderBody(body: string) {
    return renderInline(body, "b");
  }

  async function addMember(e: React.FormEvent) {
    e.preventDefault();
    const agent = addName.trim();
    if (!agent) return;
    setBusy(true);
    try {
      const res = await fetch(`/bff/c/rooms/${room.id}/members`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ agent }),
      });
      if (res.ok) {
        setAddName("");
        await refresh();
      }
    } finally {
      setBusy(false);
    }
  }

  async function sendMessage(e: React.FormEvent) {
    e.preventDefault();
    if (mentionOpen) return; // don't submit while picking a mention
    const body = say.trim();
    if (!body) return;
    setBusy(true);
    try {
      const res = await fetch(`/bff/c/rooms/${room.id}/post`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fromAgent: speakAs, body }),
      });
      if (res.ok) {
        setSay("");
        await refresh();
      }
    } finally {
      setBusy(false);
    }
  }

  async function closeRoom() {
    if (!confirm("Close this room? The agents will stop talking.")) return;
    setBusy(true);
    try {
      const res = await fetch(`/bff/c/rooms/${room.id}/close`, { method: "POST" });
      if (res.ok) await refresh();
    } finally {
      setBusy(false);
    }
  }

  return (
    // Fill the viewport height so only the transcript scrolls, not the whole page.
    <div className="flex h-[calc(100vh-9rem)] max-w-3xl flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <Link href="/rooms" className="text-xs text-gray-400 hover:text-brand-500">← Rooms</Link>
          <div className="mt-1 flex flex-wrap items-center gap-3">
            <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">{room.name}</h2>
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${open
              ? "bg-success-50 text-success-600 dark:bg-success-500/15 dark:text-success-400"
              : "bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400"}`}>
              {open ? "open" : "closed"}
            </span>
            <span className="rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-medium text-brand-600 dark:bg-brand-500/15 dark:text-brand-400">
              {room.languageMode === "Human" ? (room.language || "Human") : "machine shorthand"}
            </span>
          </div>
          <p className="mt-2 max-w-2xl text-sm text-gray-600 dark:text-gray-300">{room.topic}</p>
        </div>
        {open && (
          <button onClick={closeRoom} disabled={busy} className="h-9 shrink-0 rounded-lg border border-error-300 px-3 text-sm font-medium text-error-500 hover:bg-error-50 disabled:opacity-50 dark:border-error-500/30 dark:hover:bg-error-500/10">
            Close room
          </button>
        )}
      </div>

      {/* Members */}
      <div className="shrink-0 rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Members</span>
          {detail.members.map((m) => (
            <span key={m.agent} className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs text-gray-600 dark:bg-gray-800 dark:text-gray-300">
              {m.agent}{m.machineName ? ` · ${m.machineName}` : ""}
            </span>
          ))}
          {detail.members.length === 0 && <span className="text-xs text-gray-400">none yet</span>}
        </div>
        {open && (
          <form onSubmit={addMember} className="flex gap-2">
            <input
              list="room-agent-options"
              value={addName}
              onChange={(e) => setAddName(e.target.value)}
              placeholder="Add an agent by name…"
              className="h-9 flex-1 rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
            />
            <datalist id="room-agent-options">
              {candidates.map((n) => <option key={n} value={n} />)}
            </datalist>
            <button disabled={busy} className="h-9 rounded-lg bg-brand-500 px-4 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50">
              Add
            </button>
          </form>
        )}
      </div>

      {/* Connect an agent — copy-paste "join this room and continue" command. */}
      <details className="group shrink-0 rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
        <summary className="flex cursor-pointer list-none items-center justify-between gap-2">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Connect an agent to this room</span>
          <span className="text-xs text-gray-400 group-open:hidden">show command ▾</span>
          <span className="hidden text-xs text-gray-400 group-open:inline">hide ▴</span>
        </summary>
        <p className="mt-2 text-xs text-gray-500 dark:text-gray-400">
          Paste this into any agent (Claude Code, Codex, …) that has the Continuum MCP server connected. It reads the recent conversation and continues posting to this room as the selected member.
        </p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <label htmlFor="connect-as" className="text-xs text-gray-500 dark:text-gray-400">Post as</label>
          {detail.members.length > 0 ? (
            <select
              id="connect-as"
              value={cmdIdentity}
              onChange={(e) => setCmdAs(e.target.value)}
              className="h-8 rounded-lg border border-gray-300 bg-transparent px-2 text-sm text-gray-700 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
            >
              {detail.members.map((m) => (
                <option key={m.agent} value={m.agent}>{m.agent}</option>
              ))}
            </select>
          ) : (
            <span className="text-xs text-gray-400">add a member first, or replace <code>{"<your-agent-name>"}</code> in the command</span>
          )}
          <button
            type="button"
            onClick={copyConnect}
            className="ml-auto h-8 rounded-lg bg-brand-500 px-3 text-xs font-medium text-white hover:bg-brand-600"
          >
            {copied ? "Copied ✓" : "Copy command"}
          </button>
        </div>
        <pre className="mt-3 max-h-56 overflow-auto whitespace-pre-wrap rounded-lg border border-gray-100 bg-gray-50 p-3 font-mono text-[12px] leading-relaxed text-gray-700 dark:border-gray-800 dark:bg-gray-900 dark:text-gray-200">
          {buildConnectPrompt(cmdIdentity)}
        </pre>
      </details>

      {/* Transcript — this card fills remaining height; only the messages list scrolls. */}
      <div className="flex min-h-0 flex-1 flex-col rounded-2xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
        <div className="mb-3 flex shrink-0 items-center gap-2">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Conversation</span>
          {open && <span className="h-2 w-2 animate-pulse rounded-full bg-success-500" title="live" />}
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto pr-1">
          {detail.messages.length === 0 ? (
            <p className="py-8 text-center text-sm text-gray-400">
              No messages yet. {open ? "Add agents and they'll start talking." : "This room is closed."}
            </p>
          ) : (
            <div className="flex flex-col gap-4">
              {detail.messages.map((m) => (
                <div key={m.id}>
                  <div className="mb-0.5 flex items-baseline gap-2">
                    <span className={`text-sm font-semibold ${colorFor(m.fromAgent)}`}>{m.fromAgent}</span>
                    <span className="text-[11px] text-gray-400">{new Date(m.createdAt).toLocaleTimeString()}</span>
                  </div>
                  <p className="whitespace-pre-wrap text-[13px] leading-relaxed text-gray-700 dark:text-gray-200">{renderBody(m.body)}</p>
                </div>
              ))}
              <div ref={bottomRef} />
            </div>
          )}
        </div>

        {open && (
          <form onSubmit={sendMessage} className="mt-4 flex shrink-0 gap-2 border-t border-gray-100 pt-4 dark:border-gray-800">
            <select
              value={speakAs}
              onChange={(e) => setSpeakAs(e.target.value)}
              title="Post as yourself, or take over an agent and speak as it"
              className="h-10 shrink-0 rounded-lg border border-gray-300 bg-transparent px-2 text-sm text-gray-700 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
            >
              {speakerOptions.map((n) => (
                <option key={n} value={n}>{n === meName ? `${n} (you)` : `as ${n}`}</option>
              ))}
            </select>
            <div className="relative flex-1">
              {/* @mention popup */}
              {mentionOpen && mentionMatches.length > 0 && (
                <ul className="absolute bottom-11 left-0 z-10 max-h-48 w-64 overflow-y-auto rounded-lg border border-gray-200 bg-white py-1 shadow-lg dark:border-gray-700 dark:bg-gray-900">
                  {mentionMatches.map((n, i) => (
                    <li key={n}>
                      <button
                        type="button"
                        onMouseDown={(e) => { e.preventDefault(); applyMention(n); }}
                        className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm ${i === mentionIdx
                          ? "bg-brand-50 text-brand-600 dark:bg-brand-500/15 dark:text-brand-400"
                          : "text-gray-700 hover:bg-gray-50 dark:text-gray-200 dark:hover:bg-white/5"}`}
                      >
                        <span className={`font-semibold ${colorFor(n)}`}>@{n}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              <input
                ref={inputRef}
                value={say}
                onChange={onSayChange}
                onKeyDown={onSayKeyDown}
                placeholder={speakAs === meName ? `Message the room as ${meName}… (use @ to mention)` : `Speaking as ${speakAs}… (use @ to mention)`}
                className="h-10 w-full rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
              />
            </div>
            <button disabled={busy || !say.trim()} className="h-10 shrink-0 rounded-lg bg-brand-500 px-4 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50">
              Send
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
