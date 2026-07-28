"use client";
import { useState } from "react";
import type { AppUser } from "@/lib/continuum";

const DEFAULT_OWNER = "00000000-0000-0000-0000-000000000001";

export default function UserManager({ initialUsers, meId }: { initialUsers: AppUser[]; meId: string }) {
  const [users, setUsers] = useState(initialUsers);
  const [form, setForm] = useState({ email: "", displayName: "", password: "", role: "Member" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resetFor, setResetFor] = useState<string | null>(null);
  const [resetPw, setResetPw] = useState("");

  async function patch(id: string, body: { disabled?: boolean; role?: string }) {
    const res = await fetch(`/bff/c/users/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    if (res.ok) {
      const updated: AppUser = await res.json();
      setUsers((u) => u.map((x) => (x.id === id ? updated : x)));
    }
  }

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const res = await fetch("/bff/c/users", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(form),
      });
      if (res.ok) {
        const created: AppUser = await res.json();
        setUsers((u) => [...u, created]);
        setForm({ email: "", displayName: "", password: "", role: "Member" });
      } else {
        setError((await res.text()) || "Could not create user.");
      }
    } finally {
      setBusy(false);
    }
  }

  async function resetPassword(id: string) {
    if (resetPw.length < 8) { setError("Password must be at least 8 characters."); return; }
    const res = await fetch(`/bff/c/users/${id}/reset-password`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ newPassword: resetPw }),
    });
    if (res.ok) { setResetFor(null); setResetPw(""); setError(null); }
  }

  const inputCls = "h-10 rounded-lg border border-gray-300 bg-transparent px-3 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90";

  return (
    <div className="flex flex-col gap-6">
      <form onSubmit={create} className="flex flex-wrap items-end gap-2 rounded-2xl border border-gray-200 p-4 dark:border-gray-800">
        <input value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} type="email" placeholder="email" required className={`${inputCls} flex-1`} />
        <input value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} placeholder="name" required className={`${inputCls} flex-1`} />
        <input value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} type="password" placeholder="temp password" minLength={8} required className={inputCls} />
        <select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })} className={inputCls}>
          <option value="Member">Member</option>
          <option value="Admin">Admin</option>
        </select>
        <button disabled={busy} className="h-10 rounded-lg bg-brand-500 px-4 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50">
          {busy ? "…" : "Invite user"}
        </button>
        {error && <p className="w-full text-sm text-error-500">{error}</p>}
      </form>

      <div className="overflow-x-auto rounded-2xl border border-gray-200 dark:border-gray-800">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 text-left text-xs uppercase text-gray-500 dark:bg-white/[0.02] dark:text-gray-400">
            <tr>
              <th className="px-4 py-3 font-medium">User</th>
              <th className="px-4 py-3 font-medium">Role</th>
              <th className="px-4 py-3 font-medium">Last login</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium"></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100 dark:divide-gray-800">
            {users.map((u) => {
              const isBootstrap = u.id === DEFAULT_OWNER;
              return (
                <tr key={u.id}>
                  <td className="px-4 py-3">
                    <div className="font-medium text-gray-800 dark:text-white/90">{u.displayName}</div>
                    <div className="text-xs text-gray-400">{u.email}{u.id === meId ? " · you" : ""}</div>
                  </td>
                  <td className="px-4 py-3">
                    <select
                      value={u.role}
                      disabled={isBootstrap}
                      onChange={(e) => patch(u.id, { role: e.target.value })}
                      className="rounded-lg border border-gray-300 bg-transparent px-2 py-1 text-xs text-gray-700 disabled:opacity-50 dark:border-gray-700 dark:text-gray-200"
                    >
                      <option value="Member">Member</option>
                      <option value="Admin">Admin</option>
                    </select>
                  </td>
                  <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleDateString() : "—"}</td>
                  <td className="px-4 py-3">
                    {u.disabled
                      ? <span className="rounded-full bg-error-50 px-2 py-0.5 text-xs text-error-600 dark:bg-error-500/15 dark:text-error-400">disabled</span>
                      : <span className="rounded-full bg-success-50 px-2 py-0.5 text-xs text-success-600 dark:bg-success-500/15 dark:text-success-400">active</span>}
                  </td>
                  <td className="px-4 py-3">
                    {resetFor === u.id ? (
                      <div className="flex items-center gap-2">
                        <input type="password" value={resetPw} onChange={(e) => setResetPw(e.target.value)} placeholder="new password" className="h-8 rounded border border-gray-300 px-2 text-xs dark:border-gray-700 dark:bg-transparent" />
                        <button onClick={() => resetPassword(u.id)} className="text-xs font-medium text-brand-500">Save</button>
                        <button onClick={() => { setResetFor(null); setResetPw(""); }} className="text-xs text-gray-400">Cancel</button>
                      </div>
                    ) : (
                      <div className="flex items-center gap-3">
                        <button onClick={() => { setResetFor(u.id); setResetPw(""); }} className="text-xs font-medium text-gray-400 hover:text-brand-500">Reset password</button>
                        {!isBootstrap && (
                          <button onClick={() => patch(u.id, { disabled: !u.disabled })} className="text-xs font-medium text-gray-400 hover:text-error-500">
                            {u.disabled ? "Enable" : "Disable"}
                          </button>
                        )}
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
