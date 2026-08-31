"use client";
import { useRef, useState } from "react";
import type { RoomDraftJob, RoomDraftTurn, RoomProposal, WorkspaceDto } from "@/lib/continuum";
import { Card, Chip } from "@/components/bui";
import { Field, Select, Textarea } from "@/components/bui/form";

/**
 * Draft a room by talking about the specification instead of filling in four boxes.
 *
 * The panel never creates anything. It produces a proposal, and accepting one loads it into the
 * ordinary create form — same fields, same button — so the last word is always an edit you made, not
 * a model's. That also means there is no second creation path to keep in step with the first.
 */
export default function DraftRoomChat({
  workspaces,
  onAccept,
  onWriteInstead,
}: {
  workspaces: WorkspaceDto[];
  onAccept: (p: RoomProposal) => void;
  /** Leave for the manual form, carrying whatever the draft got as far as. */
  onWriteInstead: (seed: { name: string; topic: string }) => void;
}) {
  const [turns, setTurns] = useState<RoomDraftTurn[]>([]);
  const [input, setInput] = useState("");
  const [spec, setSpec] = useState<string | null>(null);
  const [specName, setSpecName] = useState<string | null>(null);
  const [workspaceId, setWorkspaceId] = useState("");
  const [proposal, setProposal] = useState<RoomProposal | null>(null);
  const [model, setModel] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [waited, setWaited] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  async function attach(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setError(null);
    try {
      const text = await file.text();
      // A .docx or .pdf reads as binary noise here, which the model then dutifully hallucinates
      // around. Detect it and say so, rather than drafting from garbage.
      const sample = text.slice(0, 4000);
      // eslint-disable-next-line no-control-regex
      const binary = /[\u0000-\u0008\u000E-\u001F]/.test(sample);
      if (binary || !text.trim()) {
        setError(
          `${file.name} isn't plain text, so it can't be read here. Export it as Markdown or ` +
            `plain text, or copy the text into the box below.`,
        );
        return;
      }
      setSpec(text);
      setSpecName(`${file.name} · ${Math.round(text.length / 1000)}k chars`);
    } catch {
      setError("Could not read that file. Paste the text instead.");
    }
    e.target.value = "";
  }

  async function send(e?: React.FormEvent, requireProposal = false) {
    e?.preventDefault();
    const text = input.trim();

    // Pressing send with nothing to say used to return silently — a dead button with no explanation.
    // A document alone is a valid first turn, and "propose it now" needs no text at all.
    if (!text && !spec && !requireProposal && turns.length === 0) {
      setError("Attach a document or describe what the room should settle.");
      return;
    }

    const next: RoomDraftTurn[] = text ? [...turns, { role: "user", text }] : [...turns];
    setTurns(next);
    setInput("");
    setBusy(true);
    setError(null);

    setWaited(0);
    const tick = setInterval(() => setWaited((w) => w + 1), 1000);

    try {
      // Start a job rather than waiting on one long request. A draft can take minutes and Cloudflare
      // cuts every request at 100 seconds — which is exactly the 524 this replaces.
      const started = await fetch("/bff/c/rooms/draft", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          // The spec rides along every turn: the server holds no session, so the conversation is
          // whatever the browser replays.
          spec,
          history: next,
          workspaceId: workspaceId || null,
          requireProposal,
        }),
      });
      if (!started.ok) {
        setError(describe(await started.text()));
        return;
      }
      const { jobId }: RoomDraftJob = await started.json();

      // Poll until it settles. The server gives up at six minutes; stop a little after that so a
      // wedged model surfaces as a message rather than a spinner that never ends.
      const deadline = Date.now() + 7 * 60_000;
      for (;;) {
        if (Date.now() > deadline) {
          setError("Drafting timed out. Try a shorter specification.");
          return;
        }
        await new Promise((r) => setTimeout(r, 2000));

        const polled = await fetch(`/bff/c/rooms/draft/${jobId}`, { cache: "no-store" });
        if (!polled.ok) {
          setError(describe(await polled.text()));
          return;
        }
        const job: RoomDraftJob = await polled.json();

        if (job.status === "failed") {
          setError(job.error || "Drafting failed.");
          return;
        }
        if (job.status === "done" && job.result) {
          const data = job.result;
          setModel(data.model);
          setTurns([...next, { role: "assistant", text: data.reply }]);
          if (data.proposal) setProposal(data.proposal);
          return;
        }
      }
    } catch {
      setError("The drafting service could not be reached.");
    } finally {
      clearInterval(tick);
      setBusy(false);
    }
  }

  /**
   * Server errors reach here as whatever the proxy chose to send, which for a gateway timeout is a
   * full HTML page. Dumping that into the error line is how a 524 came to fill the panel with markup.
   */
  function describe(body: string) {
    const text = body.replace(/<[^>]*>/g, " ").replace(/\s+/g, " ").trim();
    if (!text) return "The drafting service did not respond.";
    return text.length > 200 ? text.slice(0, 200) + "…" : text;
  }

  const empty = turns.length === 0;

  /**
   * What to carry into the manual form when leaving the draft. Not clever — the document's first
   * heading and the first thing asked for — but it beats handing back four empty boxes after a
   * conversation, which is what abandoning the draft used to cost.
   */
  function seedFromDraft() {
    const heading = spec
      ?.split("\n")
      .find((l) => l.trim().startsWith("#"))
      ?.replace(/^#+\s*/, "")
      .trim();
    const asked = turns.find((t) => t.role === "user")?.text.trim();
    return { name: heading || "", topic: asked || "" };
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        {model && <Chip>{model}</Chip>}
        <div className="ml-auto flex items-center gap-2">
          <Select value={workspaceId} onChange={(e) => setWorkspaceId(e.target.value)}>
            <option value="">No project context</option>
            {workspaces.map((w) => (
              <option key={w.id} value={w.id}>
                {w.displayName}
              </option>
            ))}
          </Select>
          <button
            type="button"
            onClick={() => fileRef.current?.click()}
            className="rounded-control border border-line px-3 py-1.5 text-[13px] hover:bg-stripe"
          >
            Attach document
          </button>
          <input
            ref={fileRef}
            type="file"
            accept=".md,.txt,.markdown,.rst,.adoc,text/*"
            onChange={attach}
            className="hidden"
          />
        </div>
      </div>

      {specName && (
        <div className="flex items-center gap-2 text-[12px] text-gray-500 dark:text-gray-400">
          <Chip>{specName}</Chip>
          <button type="button" onClick={() => { setSpec(null); setSpecName(null); }} className="hover:underline">
            remove
          </button>
        </div>
      )}

      <div className="flex max-h-[420px] flex-col gap-3 overflow-y-auto">
        {empty ? (
          <p className="text-[13px] text-gray-500 dark:text-gray-400">
            Attach or paste your specification and say what you want out of it. Picking a project also
            lets the draft draw on what Continuum already remembers about it.
          </p>
        ) : (
          turns.map((t, i) => (
            <div key={i} className={t.role === "user" ? "self-end max-w-[85%]" : "max-w-[95%]"}>
              <div
                className={
                  t.role === "user"
                    ? "rounded-control bg-accent px-3 py-2 text-[13px] text-white"
                    : "whitespace-pre-wrap text-[13px] text-gray-700 dark:text-gray-200"
                }
              >
                {t.text}
              </div>
            </div>
          ))
        )}
        {busy && (
          <p className="text-[12px] text-gray-400">
            Drafting… {waited}s{waited > 45 && " — a local model on a long document takes a while"}
          </p>
        )}
      </div>

      {proposal && (
        <Card className="flex flex-col gap-2 bg-stripe">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-medium text-gray-800 dark:text-white/90">{proposal.name}</span>
            {proposal.agents.map((a) => (
              <Chip key={a.name}>
                {a.name} · {a.role}
                {a.write ? " · write" : ""}
              </Chip>
            ))}
          </div>
          <p className="text-[13px] text-gray-600 dark:text-gray-300">{proposal.topic}</p>
          {proposal.doneCriteria && (
            <p className="text-[12px] text-gray-500 dark:text-gray-400">
              <span className="font-medium">Done when:</span> {proposal.doneCriteria}
            </p>
          )}
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => onAccept(proposal)}
              className="rounded-control bg-accent px-3 py-1.5 text-[13px] font-medium text-white hover:bg-accent-ink"
            >
              Use this draft
            </button>
            <span className="text-[12px] text-gray-500 dark:text-gray-400">
              Loads it into the form below, where you edit it before creating.
            </span>
          </div>
        </Card>
      )}

      <form onSubmit={send} className="flex flex-col gap-2">
        <Field label={empty ? "What should this room settle?" : "Reply"}>
          <Textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              // Enter sends; Shift+Enter is a newline. A drafting reply is usually one line.
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                void send();
              }
            }}
            rows={2}
            placeholder={
              empty
                ? "e.g. scope this to the ledger/adapter contract only — two agents"
                : "Tell it what to change…"
            }
          />
        </Field>
        {/*
          There must always be a way to reach a room. The model — a 7B one especially — will happily
          ask questions for several turns without ever committing to a proposal, and while it does,
          nothing on screen creates anything. So: overrule it, or leave and write it by hand. Both
          carry across what the draft has established.
        */}
        <div className="flex flex-wrap items-center gap-3">
          <button
            disabled={busy}
            className="rounded-control bg-accent px-3 py-1.5 text-[13px] font-medium text-white hover:bg-accent-ink disabled:opacity-50"
          >
            {busy ? "Drafting…" : empty ? "Draft a room" : "Send"}
          </button>

          {!proposal && !empty && (
            <button
              type="button"
              disabled={busy}
              onClick={() => void send(undefined, true)}
              className="rounded-control border border-line px-3 py-1.5 text-[13px] hover:bg-stripe disabled:opacity-50"
            >
              Propose the room now
            </button>
          )}

          <button
            type="button"
            onClick={() => onWriteInstead(seedFromDraft())}
            className="text-[12px] text-gray-500 hover:underline dark:text-gray-400"
          >
            Write it myself instead
          </button>

          {error && <p className="text-[12px] text-[#ee6572]">{error}</p>}
        </div>
      </form>
    </div>
  );
}
