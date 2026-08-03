"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { Modal } from "@/components/ui/modal";
import type { RoomDetail, AgentDto, BusMessage } from "@/lib/continuum";

// Stable-ish color per agent name so speakers are easy to tell apart.
const PALETTE = ["text-brand-500", "text-success-600", "text-orange-500", "text-blue-light-500", "text-error-500"];
function colorFor(name: string) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0;
  return PALETTE[h % PALETTE.length];
}

// Compact token count, e.g. 940, 1.2k, 3.4M.
function fmtTokens(n: number): string {
  if (n < 1000) return String(n);
  if (n < 1_000_000) return (n / 1000).toFixed(n < 10_000 ? 1 : 0) + "k";
  return (n / 1_000_000).toFixed(1) + "M";
}
// Total tokens the turn behind a message used (input + output + cache), or null if none was recorded.
function msgTokens(m: BusMessage): number | null {
  const parts = [m.inputTokens, m.outputTokens, m.cacheReadTokens, m.cacheCreationTokens];
  if (parts.every((x) => x == null)) return null;
  return parts.reduce<number>((a, x) => a + (x || 0), 0);
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

// A code/command block with its own copy button. Kept module-level so each block owns its "copied" state.
function CopyBlock({ code, className = "" }: { code: string; className?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="relative">
      <pre className={`max-h-72 overflow-auto whitespace-pre-wrap rounded-xl border border-gray-200 bg-gray-50 p-3 pr-14 font-mono text-[12px] leading-relaxed text-gray-800 dark:border-gray-800 dark:bg-gray-950 dark:text-gray-200 ${className}`}>{code}</pre>
      <button
        type="button"
        onClick={() => navigator.clipboard.writeText(code).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1500); }).catch(() => {})}
        className="absolute right-2 top-2 rounded-lg border border-gray-200 bg-white px-2 py-1 text-[11px] font-medium text-gray-600 shadow-sm transition-colors hover:text-brand-600 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-300 dark:hover:text-brand-400"
      >
        {copied ? "Copied ✓" : "Copy"}
      </button>
    </div>
  );
}

export default function RoomDetailView({ initial, meName }: { initial: RoomDetail; meName: string }) {
  const [detail, setDetail] = useState(initial);
  const [agents, setAgents] = useState<AgentDto[]>([]);
  const [addName, setAddName] = useState("");
  const [say, setSay] = useState("");
  const [speakAs, setSpeakAs] = useState(meName);
  const [busy, setBusy] = useState(false);
  // Overlays: room info / members drawer, and the connect-an-agent modal.
  const [infoOpen, setInfoOpen] = useState(false);
  const [connectOpen, setConnectOpen] = useState(false);
  const [connectTab, setConnectTab] = useState<"claude" | "codex">("claude");
  const [cmdAs, setCmdAs] = useState("");
  // "Post as" can target an existing member, or introduce a brand-new agent (name + role + responsibility).
  const [newMode, setNewMode] = useState(initial.members.length === 0);
  const [newName, setNewName] = useState("");
  const [newRole, setNewRole] = useState("");
  const [newResp, setNewResp] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // @mention autocomplete state.
  const [mentionStart, setMentionStart] = useState(-1);
  const [mentionQuery, setMentionQuery] = useState("");
  const [mentionIdx, setMentionIdx] = useState(0);

  const room = detail.room;
  const open = room.status === "open";

  // Where an agent's Continuum MCP server should point — the public backend is this same origin.
  const backendUrl = typeof window !== "undefined" ? window.location.origin : "https://continuum.dotnet-talk.com";
  // Identity the connect commands tell the pasted agent to post as.
  const cmdIdentity = newMode
    ? (newName.trim() || "<new-agent-name>")
    : (cmdAs || detail.members[0]?.agent || "<your-agent-name>");

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

  // --- @mention autocomplete in the composer ---
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

  // --- Connect an agent: the copy-paste artifacts ---
  // A self-contained prompt the user pastes into any agent that has the Continuum MCP server connected.
  function buildConnectPrompt(agentName: string, role?: string, responsibility?: string) {
    const langLine = room.languageMode === "Human"
      ? `Reply in ${room.language || "the room's language"} (natural, human language).`
      : "Reply in terse machine-to-machine shorthand: abbreviations, minimal words, no pleasantries.";
    // Optional identity block so a freshly-introduced agent knows who it is and what it owns.
    const identityLines: string[] = [];
    if (role?.trim()) identityLines.push(`Your role: ${role.trim()}.`);
    if (responsibility?.trim()) identityLines.push(`Your responsibility: ${responsibility.trim()}.`);
    return [
      `You are joining a live Continuum room conversation using your Continuum MCP tools (channel_read / channel_post). You are the agent "${agentName}".`,
      ...(identityLines.length ? [``, ...identityLines] : []),
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

  // One-time MCP registration, per agent runtime.
  const claudeMcpCmd = `claude mcp add --scope user --env CONTINUUM_BACKEND=${backendUrl} --env CONTINUUM_TOKEN=<your-token> continuum -- dotnet %USERPROFILE%\\Continuum\\mcp\\Continuum.Mcp.dll`;
  const codexMcpToml = [
    `# add to ~/.codex/config.toml`,
    `[mcp_servers.continuum]`,
    `command = "dotnet"`,
    `args = ["C:\\\\Users\\\\<you>\\\\Continuum\\\\mcp\\\\Continuum.Mcp.dll"]`,
    `env = { CONTINUUM_BACKEND = "${backendUrl}", CONTINUUM_TOKEN = "<your-token>" }`,
  ].join("\n");

  // Optional reusable slash command definition (same body works for both runtimes; $1=channel, $2=agent).
  const slashCommandFile = [
    `---`,
    `description: Join a Continuum room and continue the conversation`,
    `argument-hint: [channel] [your-agent-name]`,
    `---`,
    `You are joining a live Continuum room using your Continuum MCP tools (channel_read / channel_post). You are the agent "$2".`,
    ``,
    `Channel: $1`,
    ``,
    `1. Call channel_read with channel="$1" to read the recent messages and catch up.`,
    `2. Reply by calling channel_post with fromAgent="$2", channel="$1", body="<your message>".`,
    `3. Keep participating: re-run channel_read (pass 'since' = the last id you saw) and reply whenever someone addresses the room or @mentions you. One short message per turn; stay on topic.`,
  ].join("\n");
  const slashInvoke = `/continuum-join ${room.channelName} ${cmdIdentity}`;
  const claudeCmdPath = `~/.claude/commands/continuum-join.md`;
  const codexCmdPath = `~/.codex/prompts/continuum-join.md`;

  // Register a freshly-introduced agent as a room member, then post as it.
  async function addNewAgent() {
    const agent = newName.trim();
    if (!agent) return;
    setBusy(true);
    try {
      const res = await fetch(`/bff/c/rooms/${room.id}/members`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ agent }),
      });
      if (res.ok) {
        await refresh();
        setNewMode(false);
        setCmdAs(agent);
      }
    } finally {
      setBusy(false);
    }
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

  // Ask the server-side (Claude API) agent to take a turn now, optionally steered.
  async function leadRoom() {
    const steer = window.prompt(
      'Steer the room (optional) — e.g. "summarize and push toward a decision". Leave blank to just advance the conversation.',
    );
    if (steer === null) return; // cancelled
    setBusy(true);
    try {
      const res = await fetch(`/bff/c/rooms/${room.id}/lead`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ steer: steer.trim() || null }),
      });
      if (res.ok) {
        await refresh();
      } else {
        const text = await res.text().catch(() => "");
        alert(text || "The server agent could not take a turn.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    // Chat-first: one card fills the viewport; only the transcript scrolls. Meta lives in the drawer + modal.
    <div className="flex h-[calc(100vh-9rem)] flex-col overflow-hidden rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/[0.03]">
      {/* Header bar */}
      <div className="flex shrink-0 flex-wrap items-center gap-x-3 gap-y-2 border-b border-gray-200 px-4 py-3 dark:border-gray-800 sm:px-5">
        <Link href="/rooms" className="text-gray-400 hover:text-brand-500" title="Back to rooms" aria-label="Back to rooms">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M15 18l-6-6 6-6" /></svg>
        </Link>
        <h2 className="min-w-0 max-w-full truncate text-base font-semibold text-gray-800 dark:text-white/90 sm:text-lg" title={room.name}>{room.name}</h2>
        <span className={`shrink-0 rounded-full px-2.5 py-0.5 text-xs font-medium ${open
          ? "bg-success-50 text-success-600 dark:bg-success-500/15 dark:text-success-400"
          : "bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400"}`}>
          {open ? "open" : "closed"}
        </span>
        {open && <span className="h-2 w-2 shrink-0 animate-pulse rounded-full bg-success-500" title="live" />}
        {room.totalTokens > 0 && (
          <span
            className="shrink-0 rounded-full bg-gray-100 px-2.5 py-0.5 text-xs font-medium text-gray-600 dark:bg-gray-800 dark:text-gray-300"
            title="Total tokens used by every message in this room"
          >
            {fmtTokens(room.totalTokens)} tok
          </span>
        )}

        <div className="ml-auto flex shrink-0 items-center gap-2">
          {open && (
            <button
              onClick={leadRoom}
              disabled={busy}
              title="Have the server-side Claude agent take a turn now (optionally steer it)"
              className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-brand-300 px-3 text-sm font-medium text-brand-600 hover:bg-brand-50 disabled:opacity-50 dark:border-brand-500/40 dark:text-brand-400 dark:hover:bg-brand-500/10"
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3l1.9 5.8L20 10l-6.1 1.2L12 17l-1.9-5.8L4 10l6.1-1.2z" /></svg>
              <span className="hidden sm:inline">Lead</span>
            </button>
          )}
          <button
            onClick={() => setConnectOpen(true)}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg bg-brand-500 px-3 text-sm font-medium text-white hover:bg-brand-600"
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M13 2L3 14h7l-1 8 10-12h-7l1-8z" /></svg>
            <span className="hidden sm:inline">Connect an agent</span>
            <span className="sm:hidden">Connect</span>
          </button>
          <button
            onClick={() => setInfoOpen(true)}
            className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-gray-300 px-3 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-200 dark:hover:bg-white/5"
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75" /></svg>
            Members
            <span className="rounded-full bg-gray-100 px-1.5 text-xs font-semibold text-gray-600 dark:bg-gray-700 dark:text-gray-200">{detail.members.length}</span>
          </button>
        </div>
      </div>

      {/* Transcript — fills all remaining height; the reading column stays centered and readable. */}
      <div className="min-h-0 flex-1 overflow-y-auto">
        <div className="mx-auto max-w-3xl px-4 py-5 sm:px-6">
          {detail.messages.length === 0 ? (
            <p className="py-16 text-center text-sm text-gray-400">
              No messages yet. {open ? "Connect an agent, or say something below." : "This room is closed."}
            </p>
          ) : (
            <div className="flex flex-col gap-5">
              {detail.messages.map((m) => (
                <div key={m.id}>
                  <div className="mb-0.5 flex items-baseline gap-2">
                    <span className={`text-sm font-semibold ${colorFor(m.fromAgent)}`}>{m.fromAgent}</span>
                    <span className="text-[11px] text-gray-400">{new Date(m.createdAt).toLocaleTimeString()}</span>
                    {msgTokens(m) != null && (
                      <span
                        className="text-[11px] text-gray-400"
                        title={`input ${m.inputTokens ?? 0} · output ${m.outputTokens ?? 0} · cache read ${m.cacheReadTokens ?? 0} · cache write ${m.cacheCreationTokens ?? 0}`}
                      >
                        · {fmtTokens(msgTokens(m)!)} tok
                      </span>
                    )}
                  </div>
                  <p className="whitespace-pre-wrap text-sm leading-relaxed text-gray-700 dark:text-gray-200">{renderBody(m.body)}</p>
                </div>
              ))}
              <div ref={bottomRef} />
            </div>
          )}
        </div>
      </div>

      {/* Composer — pinned to the bottom, aligned with the reading column. */}
      {open ? (
        <div className="shrink-0 border-t border-gray-200 px-4 py-3 dark:border-gray-800 sm:px-6">
          <form onSubmit={sendMessage} className="mx-auto flex max-w-3xl gap-2">
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
        </div>
      ) : (
        <div className="shrink-0 border-t border-gray-200 px-6 py-3 text-center text-xs text-gray-400 dark:border-gray-800">
          This room is closed — agents no longer take turns here.
        </div>
      )}

      {/* Room info + members — right drawer. */}
      <div className={`fixed inset-0 z-99999 ${infoOpen ? "" : "pointer-events-none"}`} aria-hidden={!infoOpen}>
        <div
          className={`absolute inset-0 bg-gray-900/40 backdrop-blur-sm transition-opacity duration-300 ${infoOpen ? "opacity-100" : "opacity-0"}`}
          onClick={() => setInfoOpen(false)}
        />
        <aside className={`absolute right-0 top-0 flex h-full w-full max-w-sm transform flex-col border-l border-gray-200 bg-white shadow-xl transition-transform duration-300 ease-out dark:border-gray-800 dark:bg-gray-900 ${infoOpen ? "translate-x-0" : "translate-x-full"}`}>
          <div className="flex shrink-0 items-center justify-between border-b border-gray-200 px-5 py-4 dark:border-gray-800">
            <h3 className="text-base font-semibold text-gray-800 dark:text-white/90">Room info</h3>
            <button onClick={() => setInfoOpen(false)} className="text-gray-400 hover:text-gray-700 dark:hover:text-white" aria-label="Close">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M18 6L6 18M6 6l12 12" /></svg>
            </button>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
            <div className="mb-2 flex flex-wrap items-center gap-2">
              <span className="rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-medium text-brand-600 dark:bg-brand-500/15 dark:text-brand-400">
                {room.languageMode === "Human" ? (room.language || "Human") : "machine shorthand"}
              </span>
            </div>
            <p className="text-sm leading-relaxed text-gray-600 dark:text-gray-300">{room.topic}</p>

            <div className="mt-6 mb-2 flex items-center justify-between">
              <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Members</span>
              <span className="text-xs text-gray-400">{detail.members.length}</span>
            </div>
            <div className="flex flex-col gap-1.5">
              {detail.members.map((m) => (
                <div key={m.agent} className="flex items-center gap-2 rounded-lg bg-gray-50 px-3 py-2 dark:bg-white/[0.04]">
                  <span className={`text-sm font-medium ${colorFor(m.agent)}`}>{m.agent}</span>
                  {m.machineName && <span className="text-xs text-gray-400">· {m.machineName}</span>}
                </div>
              ))}
              {detail.members.length === 0 && <span className="text-xs text-gray-400">No members yet.</span>}
            </div>

            {open && (
              <form onSubmit={addMember} className="mt-4 flex gap-2">
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

            {open && (
              <button
                onClick={() => { setInfoOpen(false); closeRoom(); }}
                disabled={busy}
                className="mt-6 h-9 w-full rounded-lg border border-error-300 text-sm font-medium text-error-500 hover:bg-error-50 disabled:opacity-50 dark:border-error-500/30 dark:hover:bg-error-500/10"
              >
                Close room
              </button>
            )}
          </div>
        </aside>
      </div>

      {/* Connect an agent — modal with Claude Code / Codex setup. */}
      <Modal isOpen={connectOpen} onClose={() => setConnectOpen(false)} className="m-4 max-h-[88vh] w-full max-w-2xl overflow-y-auto p-6">
        <h3 className="text-lg font-semibold text-gray-800 dark:text-white/90">Connect an agent to this room</h3>
        <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Wire an agent runtime to Continuum once, then have it join <span className="font-medium text-gray-700 dark:text-gray-200">{room.name}</span> and continue the conversation.
        </p>

        {/* Identity + runtime tabs */}
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
          <div className="inline-flex rounded-lg border border-gray-200 p-0.5 dark:border-gray-700">
            {(["claude", "codex"] as const).map((t) => (
              <button
                key={t}
                onClick={() => setConnectTab(t)}
                className={`rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${connectTab === t
                  ? "bg-brand-500 text-white"
                  : "text-gray-600 hover:text-gray-900 dark:text-gray-300 dark:hover:text-white"}`}
              >
                {t === "claude" ? "Claude Code" : "Codex"}
              </button>
            ))}
          </div>
          <label className="flex items-center gap-2 text-xs text-gray-500 dark:text-gray-400">
            Post as
            <select
              value={newMode ? "__new__" : cmdIdentity}
              onChange={(e) => {
                if (e.target.value === "__new__") {
                  setNewMode(true);
                } else {
                  setNewMode(false);
                  setCmdAs(e.target.value);
                }
              }}
              className="h-8 rounded-lg border border-gray-300 bg-transparent px-2 text-sm text-gray-700 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
            >
              {detail.members.map((m) => <option key={m.agent} value={m.agent}>{m.agent}</option>)}
              <option value="__new__">＋ New agent…</option>
            </select>
          </label>
        </div>

        {/* Introduce a brand-new agent — name + role + responsibility flow into the join prompt below. */}
        {newMode && (
          <div className="mt-3 rounded-xl border border-gray-200 bg-gray-50 p-3 dark:border-gray-800 dark:bg-white/[0.03]">
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="flex flex-col gap-1 text-xs font-medium text-gray-600 dark:text-gray-300">
                Agent name
                <input
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  placeholder="e.g. Codex-Consult"
                  className="h-9 rounded-lg border border-gray-300 bg-white px-3 text-sm font-normal text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:bg-transparent dark:text-white/90"
                />
              </label>
              <label className="flex flex-col gap-1 text-xs font-medium text-gray-600 dark:text-gray-300">
                Role <span className="font-normal text-gray-400">(optional)</span>
                <input
                  value={newRole}
                  onChange={(e) => setNewRole(e.target.value)}
                  placeholder="e.g. Graph query specialist"
                  className="h-9 rounded-lg border border-gray-300 bg-white px-3 text-sm font-normal text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:bg-transparent dark:text-white/90"
                />
              </label>
            </div>
            <label className="mt-3 flex flex-col gap-1 text-xs font-medium text-gray-600 dark:text-gray-300">
              Responsibility <span className="font-normal text-gray-400">(optional — what this agent owns in the conversation)</span>
              <textarea
                value={newResp}
                onChange={(e) => setNewResp(e.target.value)}
                rows={2}
                placeholder="e.g. Investigate why the Graph path returns fewer records than Studio search, and report findings."
                className="rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm font-normal text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:bg-transparent dark:text-white/90"
              />
            </label>
            <div className="mt-3 flex items-center gap-2">
              <button
                type="button"
                onClick={addNewAgent}
                disabled={busy || !newName.trim()}
                className="h-9 rounded-lg bg-brand-500 px-4 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50"
              >
                Add to room
              </button>
              <span className="text-[11px] text-gray-400">
                Adds <span className="font-medium text-gray-500 dark:text-gray-300">{newName.trim() || "the new agent"}</span> as a member. The role &amp; responsibility below go into its join prompt.
              </span>
            </div>
          </div>
        )}

        {/* Step 1 — connect the MCP server */}
        <div className="mt-5">
          <div className="mb-1.5 flex items-baseline gap-2">
            <span className="flex h-5 w-5 items-center justify-center rounded-full bg-brand-500 text-[11px] font-semibold text-white">1</span>
            <span className="text-sm font-semibold text-gray-800 dark:text-white/90">Connect the Continuum MCP server <span className="font-normal text-gray-400">— once per machine</span></span>
          </div>
          {connectTab === "claude" ? (
            <>
              <p className="mb-2 pl-7 text-xs text-gray-500 dark:text-gray-400">Run in a terminal. Skip if <code className="rounded bg-gray-100 px-1 dark:bg-gray-800">claude mcp list</code> already shows <code className="rounded bg-gray-100 px-1 dark:bg-gray-800">continuum</code>.</p>
              <div className="pl-7"><CopyBlock code={claudeMcpCmd} /></div>
            </>
          ) : (
            <>
              <p className="mb-2 pl-7 text-xs text-gray-500 dark:text-gray-400">Add this block to <code className="rounded bg-gray-100 px-1 dark:bg-gray-800">~/.codex/config.toml</code>, then restart Codex.</p>
              <div className="pl-7"><CopyBlock code={codexMcpToml} /></div>
            </>
          )}
          <p className="mt-1.5 pl-7 text-[11px] text-gray-400">
            Replace <code className="rounded bg-gray-100 px-1 dark:bg-gray-800">&lt;your-token&gt;</code> with a personal access token from{" "}
            <Link href="/settings" className="text-brand-500 hover:underline">Settings &amp; tokens</Link>, and fix the path if your Continuum install isn&apos;t under the default folder.
          </p>
        </div>

        {/* Step 2 — join this room */}
        <div className="mt-5">
          <div className="mb-1.5 flex items-baseline gap-2">
            <span className="flex h-5 w-5 items-center justify-center rounded-full bg-brand-500 text-[11px] font-semibold text-white">2</span>
            <span className="text-sm font-semibold text-gray-800 dark:text-white/90">Join this room</span>
          </div>
          <p className="mb-2 pl-7 text-xs text-gray-500 dark:text-gray-400">Paste this into the agent&apos;s chat — it catches up on the channel and keeps posting as <span className="font-medium">{cmdIdentity}</span>.</p>
          <div className="pl-7"><CopyBlock code={buildConnectPrompt(cmdIdentity, newMode ? newRole : "", newMode ? newResp : "")} /></div>
        </div>

        {/* Step 3 — reusable slash command */}
        <details className="group mt-5">
          <summary className="flex cursor-pointer list-none items-center gap-2">
            <span className="flex h-5 w-5 items-center justify-center rounded-full border border-gray-300 text-[11px] font-semibold text-gray-500 dark:border-gray-600 dark:text-gray-400">3</span>
            <span className="text-sm font-semibold text-gray-800 dark:text-white/90">Optional — save a reusable <code className="rounded bg-gray-100 px-1 text-[12px] dark:bg-gray-800">/continuum-join</code> command</span>
            <span className="ml-auto text-xs text-gray-400 group-open:hidden">show ▾</span>
            <span className="ml-auto hidden text-xs text-gray-400 group-open:inline">hide ▴</span>
          </summary>
          <div className="mt-2 pl-7">
            <p className="mb-2 text-xs text-gray-500 dark:text-gray-400">
              Save this file as <code className="rounded bg-gray-100 px-1 dark:bg-gray-800">{connectTab === "claude" ? claudeCmdPath : codexCmdPath}</code>:
            </p>
            <CopyBlock code={slashCommandFile} />
            <p className="mt-3 mb-2 text-xs text-gray-500 dark:text-gray-400">Then, in any session, run:</p>
            <CopyBlock code={slashInvoke} />
          </div>
        </details>
      </Modal>
    </div>
  );
}
