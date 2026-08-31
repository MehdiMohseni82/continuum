"use client";
import { useState } from "react";
import Link from "next/link";
import type { RoomDto, LanguageMode, RoomProposal, WorkspaceDto } from "@/lib/continuum";
import DraftRoomChat from "./DraftRoomChat";
import { Card, Chip, TAG } from "@/components/bui";
import { Empty, Section } from "@/components/bui/page";
import { Input, Select, Textarea, Field, FormRow } from "@/components/bui/form";

export default function RoomManager({
  initialRooms,
  workspaces = [],
}: {
  initialRooms: RoomDto[];
  workspaces?: WorkspaceDto[];
}) {
  const [rooms, setRooms] = useState(initialRooms);
  const [open, setOpen] = useState(initialRooms.length === 0);
  const [mode, setMode] = useState<"draft" | "write">("draft");
  const [form, setForm] = useState({
    name: "", topic: "", languageMode: "Human" as LanguageMode, language: "English", systemPrompt: "",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function useDraft(p: RoomProposal) {
    // The done-criteria is folded into the system prompt rather than dropped: the prompt is the only
    // text an agent reads when it joins, so a finish line kept anywhere else is a finish line the
    // agents never see.
    const prompt = p.doneCriteria
      ? `${p.systemPrompt}\n\nDone when: ${p.doneCriteria}`
      : p.systemPrompt;

    setForm({
      name: p.name,
      topic: p.topic,
      languageMode: p.languageMode,
      language: p.language || "English",
      systemPrompt: prompt,
    });
    setMode("write");
    setOpen(true);
  }

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const res = await fetch("/bff/c/rooms", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(form),
      });
      if (res.ok) {
        const created: RoomDto = await res.json();
        setRooms((r) => [created, ...r]);
        setForm({ name: "", topic: "", languageMode: "Human", language: "English", systemPrompt: "" });
        setOpen(false);
      } else {
        setError((await res.text()) || "Could not create room.");
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Section
        title={`Rooms · ${rooms.length}`}
        actions={
          <button
            onClick={() => { setOpen((o) => !o); setMode("draft"); }}
            className="rounded-control bg-accent px-3 py-1.5 text-[13px] font-medium text-white hover:bg-accent-ink"
          >
            {open ? "Cancel" : "New room"}
          </button>
        }
      >
        {/*
          The form used to sit permanently open above the list, three full-width boxes deep, so the
          rooms themselves started below the fold. It now folds away unless there is nothing to show.
        */}
        {open && (
          <Card className="mb-3 flex flex-col gap-3">
            {/*
              Drafting used to sit behind a second, quieter button beside "New room". Nobody found it:
              the obvious action opened the same four empty boxes it always had, so the feature read as
              missing. It is now the first tab of the one flow, and the default — describing what you
              want is a better starting point than an empty Name field, and writing it yourself is
              always one click away.
            */}
            <div className="flex items-center gap-1 border-b border-line pb-2">
              {(["draft", "write"] as const).map((m) => (
                <button
                  key={m}
                  type="button"
                  onClick={() => setMode(m)}
                  className={
                    mode === m
                      ? "rounded-control bg-stripe px-3 py-1.5 text-[13px] font-medium text-gray-800 dark:text-white/90"
                      : "rounded-control px-3 py-1.5 text-[13px] text-gray-500 hover:bg-stripe dark:text-gray-400"
                  }
                >
                  {m === "draft" ? "Draft from a spec" : "Write it yourself"}
                </button>
              ))}
            </div>

            {mode === "draft" && (
              <DraftRoomChat
                workspaces={workspaces}
                onAccept={useDraft}
                onWriteInstead={(seed) => {
                  // Keep whatever the draft established rather than resetting to empty boxes.
                  setForm((f) => ({ ...f, name: seed.name || f.name, topic: seed.topic || f.topic }));
                  setMode("write");
                }}
              />
            )}

            {mode === "write" && (
            <form onSubmit={create} className="flex flex-col gap-3">
              <FormRow>
                <Field label="Name" className="flex-1 min-w-[240px]">
                  <Input
                    size="full"
                    value={form.name}
                    onChange={(e) => setForm({ ...form, name: e.target.value })}
                    placeholder="e.g. Ingest parser design"
                    required
                  />
                </Field>
                <Field label="Speak">
                  <Select
                    value={form.languageMode}
                    onChange={(e) => setForm({ ...form, languageMode: e.target.value as LanguageMode })}
                  >
                    <option value="Human">Human language</option>
                    <option value="Shorthand">Machine shorthand</option>
                  </Select>
                </Field>
                {form.languageMode === "Human" && (
                  <Field label="Language">
                    <Input
                      size="sm"
                      value={form.language}
                      onChange={(e) => setForm({ ...form, language: e.target.value })}
                      placeholder="English"
                    />
                  </Field>
                )}
              </FormRow>

              <Field label="Topic" hint="What the agents are here to settle.">
                <Textarea
                  value={form.topic}
                  onChange={(e) => setForm({ ...form, topic: e.target.value })}
                  required
                  rows={2}
                />
              </Field>

              <Field
                label="System prompt"
                hint="Optional. Standing framing fed to each agent as it joins — its role, the goal, the rules of engagement."
              >
                <Textarea
                  value={form.systemPrompt}
                  onChange={(e) => setForm({ ...form, systemPrompt: e.target.value })}
                  rows={3}
                />
              </Field>

              <div className="flex items-center gap-3">
                <button
                  disabled={busy}
                  className="rounded-control bg-accent px-3 py-1.5 text-[13px] font-medium text-white hover:bg-accent-ink disabled:opacity-50"
                >
                  {busy ? "Creating…" : "Create room"}
                </button>
                {error && <p className="text-[12px] text-[#ee6572]">{error}</p>}
              </div>
            </form>
            )}
          </Card>
        )}

        {rooms.length === 0 ? (
          <Card padded={false}>
            <Empty hint="A room is where two agents — yours and a colleague's — work something out in the open.">
              No rooms yet.
            </Empty>
          </Card>
        ) : (
          <Card padded={false} className="divide-y divide-line">
            {rooms.map((r) => {
              const isOpen = r.status === "open";
              return (
                <Link key={r.id} href={`/rooms/${r.id}`} className="block px-4 py-3 hover:bg-stripe">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium text-gray-800 dark:text-white/90">{r.name}</span>
                    <Chip dot={isOpen ? TAG.green : undefined}>{isOpen ? "open" : "closed"}</Chip>
                    <Chip>{r.languageMode === "Human" ? r.language || "Human" : "shorthand"}</Chip>
                    <span className="ml-auto font-mono text-[11px] text-gray-400">
                      {r.memberCount} agents · {r.messageCount} msgs
                      {r.totalTokens > 0 && ` · ${(r.totalTokens / 1000).toFixed(1)}k tok`}
                    </span>
                  </div>
                  <p className="mt-1 line-clamp-2 max-w-[90ch] text-[13px] text-gray-600 dark:text-gray-300">{r.topic}</p>
                </Link>
              );
            })}
          </Card>
        )}
      </Section>
    </div>
  );
}
