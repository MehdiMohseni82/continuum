"use client";
import { useRef, useState } from "react";
import type { RoomDraftResponse, RoomDraftTurn, RoomProposal, WorkspaceDto } from "@/lib/continuum";
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
}: {
  workspaces: WorkspaceDto[];
  onAccept: (p: RoomProposal) => void;
}) {
  const [turns, setTurns] = useState<RoomDraftTurn[]>([]);
  const [input, setInput] = useState("");
  const [spec, setSpec] = useState<string | null>(null);
  const [specName, setSpecName] = useState<string | null>(null);
  const [workspaceId, setWorkspaceId] = useState("");
  const [proposal, setProposal] = useState<RoomProposal | null>(null);
  const [model, setModel] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  async function attach(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setError(null);
    try {
      const text = await file.text();
      setSpec(text);
      setSpecName(`${file.name} · ${Math.round(text.length / 1000)}k chars`);
    } catch {
      setError("Could not read that file. Paste the text instead.");
    }
    e.target.value = "";
  }

  async function send(e?: React.FormEvent) {
    e?.preventDefault();
    const text = input.trim();
    // The first turn can be the document alone — there may be nothing to add to it.
    if (!text && !(spec && turns.length === 0)) return;

    const next: RoomDraftTurn[] = text ? [...turns, { role: "user", text }] : [...turns];
    setTurns(next);
    setInput("");
    setBusy(true);
    setError(null);

    try {
      const res = await fetch("/bff/c/rooms/draft", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          // The spec rides along every turn: the server holds no session, so the conversation is
          // whatever the browser replays.
          spec,
          history: next,
          workspaceId: workspaceId || null,
        }),
      });
      if (!res.ok) {
        setError((await res.text()) || "The drafting service did not respond.");
        return;
      }
      const data: RoomDraftResponse = await res.json();
      setModel(data.model);
      setTurns([...next, { role: "assistant", text: data.reply }]);
      if (data.proposal) setProposal(data.proposal);
    } catch {
      setError("The drafting service could not be reached.");
    } finally {
      setBusy(false);
    }
  }

  const empty = turns.length === 0;

  return (
    <Card className="mb-3 flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[13px] font-medium text-gray-800 dark:text-white/90">Draft from a specification</span>
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
        {busy && <p className="text-[12px] text-gray-400">Drafting…</p>}
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
        <div className="flex items-center gap-3">
          <button
            disabled={busy}
            className="rounded-control bg-accent px-3 py-1.5 text-[13px] font-medium text-white hover:bg-accent-ink disabled:opacity-50"
          >
            {busy ? "Drafting…" : empty ? "Draft a room" : "Send"}
          </button>
          {error && <p className="text-[12px] text-[#ee6572]">{error}</p>}
        </div>
      </form>
    </Card>
  );
}
