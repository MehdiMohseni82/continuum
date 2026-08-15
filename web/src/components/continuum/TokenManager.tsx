"use client";
import { useState } from "react";
import type { Pat, PatCreated } from "@/lib/continuum";
import { Card, Chip, TAG } from "@/components/bui";
import { Empty } from "@/components/bui/page";
import { Input, Field } from "@/components/bui/form";

export default function TokenManager({ initialTokens, canManage }: { initialTokens: Pat[]; canManage: boolean }) {
  const [tokens, setTokens] = useState(initialTokens);
  const [name, setName] = useState("");
  const [expiresDays, setExpiresDays] = useState("");
  const [busy, setBusy] = useState(false);
  const [justCreated, setJustCreated] = useState<PatCreated | null>(null);
  const [copied, setCopied] = useState(false);

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      const res = await fetch("/bff/c/auth/tokens", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: name.trim() || "token", expiresDays: expiresDays ? Number(expiresDays) : null }),
      });
      if (res.ok) {
        const created: PatCreated = await res.json();
        setJustCreated(created);
        setCopied(false);
        setName("");
        setExpiresDays("");
        setTokens((t) => [
          {
            id: created.id, name: created.name, prefix: created.prefix, createdAt: created.createdAt,
            lastUsedAt: null, revokedAt: null, expiresAt: created.expiresAt,
          },
          ...t,
        ]);
      }
    } finally {
      setBusy(false);
    }
  }

  async function revoke(id: string) {
    if (!confirm("Revoke this token? Any machine using it will stop syncing.")) return;
    const res = await fetch(`/bff/c/auth/tokens/${id}`, { method: "DELETE" });
    if (res.ok) {
      setTokens((t) => t.map((x) => (x.id === id ? { ...x, revokedAt: new Date().toISOString() } : x)));
    }
  }

  if (!canManage) {
    return (
      <Card padded={false}>
        <Empty hint="The legacy shared token can't own personal tokens.">
          Sign in with an account to create tokens.
        </Empty>
      </Card>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      {/*
        The one moment on this page that cannot be repeated: the raw token is shown once and never
        again. It gets its own emphasis and says so plainly, rather than being a green info box.
      */}
      {justCreated && (
        <Card className="border-l-2 border-l-[#25a878]">
          <p className="mb-2 text-[13px] font-medium text-gray-900 dark:text-white/90">
            Copy this now — it is never shown again.
          </p>
          <div className="flex items-center gap-2">
            <code className="min-w-0 flex-1 overflow-x-auto rounded-control bg-stripe px-2.5 py-1.5 font-mono text-[12px] text-gray-800 shadow-inset-field dark:text-gray-200">
              {justCreated.token}
            </code>
            <button
              onClick={() => { navigator.clipboard.writeText(justCreated.token); setCopied(true); }}
              className="shrink-0 rounded-control bg-accent px-3 py-1.5 text-[12px] font-medium text-white hover:bg-accent-ink"
            >
              {copied ? "Copied" : "Copy"}
            </button>
          </div>
          <p className="mt-2 font-mono text-[11px] text-gray-500 dark:text-gray-400">
            Set it on that machine as CONTINUUM_TOKEN.
          </p>
        </Card>
      )}

      <form onSubmit={create} className="flex flex-wrap items-end gap-2.5">
        <Field label="Name">
          <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. laptop" />
        </Field>
        <Field label="Expires (days)">
          <Input
            size="sm"
            value={expiresDays}
            onChange={(e) => setExpiresDays(e.target.value.replace(/[^0-9]/g, ""))}
            placeholder="never"
            inputMode="numeric"
          />
        </Field>
        <button
          disabled={busy}
          className="h-8 rounded-control bg-accent px-3 text-[13px] font-medium text-white hover:bg-accent-ink disabled:opacity-50"
        >
          {busy ? "…" : "Create token"}
        </button>
      </form>

      <Card padded={false} className="divide-y divide-line">
        {tokens.length === 0 ? (
          <Empty hint="A daemon or MCP client needs one to sync.">No tokens yet.</Empty>
        ) : (
          tokens.map((t) => {
            const revoked = !!t.revokedAt;
            return (
              <div key={t.id} className="flex items-center justify-between gap-3 px-4 py-2.5">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className={`text-[13px] font-medium ${revoked ? "text-gray-400 line-through" : "text-gray-800 dark:text-white/90"}`}>
                      {t.name}
                    </span>
                    <span className="font-mono text-[11px] text-gray-400">{t.prefix}…</span>
                    {revoked && <Chip dot={TAG.red}>revoked</Chip>}
                  </div>
                  <span className="font-mono text-[11px] text-gray-400">
                    created {new Date(t.createdAt).toLocaleDateString()}
                    {t.lastUsedAt ? ` · last used ${new Date(t.lastUsedAt).toLocaleDateString()}` : " · never used"}
                    {t.expiresAt ? ` · expires ${new Date(t.expiresAt).toLocaleDateString()}` : ""}
                  </span>
                </div>
                {!revoked && (
                  <button
                    onClick={() => revoke(t.id)}
                    className="shrink-0 text-[12px] font-medium text-gray-400 hover:text-[#ee6572]"
                  >
                    Revoke
                  </button>
                )}
              </div>
            );
          })
        )}
      </Card>
    </div>
  );
}
