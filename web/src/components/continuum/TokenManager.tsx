"use client";
import { useState } from "react";
import type { Pat, PatCreated } from "@/lib/continuum";

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
          { id: created.id, name: created.name, prefix: created.prefix, createdAt: created.createdAt, lastUsedAt: null, revokedAt: null, expiresAt: created.expiresAt },
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
    if (res.ok) setTokens((t) => t.map((x) => (x.id === id ? { ...x, revokedAt: new Date().toISOString() } : x)));
  }

  if (!canManage) {
    return <p className="rounded-xl border border-dashed border-gray-300 p-4 text-sm text-gray-400 dark:border-gray-700">Sign in with an account to create tokens.</p>;
  }

  return (
    <div className="flex flex-col gap-4">
      {justCreated && (
        <div className="rounded-xl border border-success-300 bg-success-50 p-4 dark:border-success-500/30 dark:bg-success-500/10">
          <p className="mb-2 text-sm font-medium text-success-700 dark:text-success-400">
            Copy your token now — it won’t be shown again.
          </p>
          <div className="flex items-center gap-2">
            <code className="flex-1 overflow-x-auto rounded-lg bg-white px-3 py-2 font-mono text-xs text-gray-800 dark:bg-gray-900 dark:text-gray-200">
              {justCreated.token}
            </code>
            <button
              onClick={() => { navigator.clipboard.writeText(justCreated.token); setCopied(true); }}
              className="shrink-0 rounded-lg bg-brand-500 px-3 py-2 text-xs font-medium text-white hover:bg-brand-600"
            >
              {copied ? "Copied" : "Copy"}
            </button>
          </div>
          <p className="mt-2 font-mono text-xs text-gray-500 dark:text-gray-400">CONTINUUM_TOKEN={justCreated.token.slice(0, 12)}…</p>
        </div>
      )}

      <form onSubmit={create} className="flex flex-wrap items-end gap-2">
        <div className="flex-1">
          <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Name (e.g. machine)</label>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="laptop"
            className="h-10 w-full rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
          />
        </div>
        <div className="w-28">
          <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Expires (days)</label>
          <input
            value={expiresDays}
            onChange={(e) => setExpiresDays(e.target.value.replace(/\D/g, ""))}
            placeholder="never"
            inputMode="numeric"
            className="h-10 w-full rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
          />
        </div>
        <button disabled={busy} className="h-10 rounded-lg bg-brand-500 px-4 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50">
          {busy ? "…" : "Create token"}
        </button>
      </form>

      <div className="flex flex-col divide-y divide-gray-100 rounded-xl border border-gray-200 dark:divide-gray-800 dark:border-gray-800">
        {tokens.length === 0 && <p className="p-4 text-sm text-gray-400">No tokens yet.</p>}
        {tokens.map((t) => {
          const revoked = !!t.revokedAt;
          return (
            <div key={t.id} className="flex items-center justify-between gap-3 p-4">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <span className={`text-sm font-medium ${revoked ? "text-gray-400 line-through" : "text-gray-800 dark:text-white/90"}`}>{t.name}</span>
                  <span className="font-mono text-xs text-gray-400">{t.prefix}…</span>
                  {revoked && <span className="text-xs text-error-500">revoked</span>}
                </div>
                <span className="text-xs text-gray-400">
                  created {new Date(t.createdAt).toLocaleDateString()}
                  {t.lastUsedAt ? ` · last used ${new Date(t.lastUsedAt).toLocaleDateString()}` : " · never used"}
                  {t.expiresAt ? ` · expires ${new Date(t.expiresAt).toLocaleDateString()}` : ""}
                </span>
              </div>
              {!revoked && (
                <button onClick={() => revoke(t.id)} className="shrink-0 text-xs font-medium text-gray-400 hover:text-error-500">
                  Revoke
                </button>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
