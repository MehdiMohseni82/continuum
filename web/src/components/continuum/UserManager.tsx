"use client";
import { useState } from "react";
import type { AppUser } from "@/lib/continuum";
import { Card, Avatar, Chip, TAG } from "@/components/bui";
import { Input, Select, Field } from "@/components/bui/form";

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

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <form onSubmit={create} className="flex flex-wrap items-end gap-2.5">
          <Field label="Email">
            <Input value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} type="email" placeholder="sara@example.com" required />
          </Field>
          <Field label="Name">
            <Input value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} placeholder="Sara Meier" required />
          </Field>
          <Field label="Temporary password">
            <Input value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} type="password" minLength={8} required />
          </Field>
          <Field label="Role">
            <Select size="sm" value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}>
              <option value="Member">Member</option>
              <option value="Admin">Admin</option>
            </Select>
          </Field>
          <button disabled={busy} className="h-8 rounded-control bg-accent px-3 text-[13px] font-medium text-white hover:bg-accent-ink disabled:opacity-50">
            {busy ? "…" : "Add user"}
          </button>
          {error && <p className="w-full text-[12px] text-[#ee6572]">{error}</p>}
        </form>
      </Card>

      <Card padded={false} className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="border-b border-line text-left">
            <tr>
              <th className="px-3 py-2 font-mono text-[10px] font-normal uppercase tracking-[0.09em] text-gray-400">User</th>
              <th className="px-3 py-2 font-mono text-[10px] font-normal uppercase tracking-[0.09em] text-gray-400">Role</th>
              <th className="px-3 py-2 font-mono text-[10px] font-normal uppercase tracking-[0.09em] text-gray-400">Last login</th>
              <th className="px-3 py-2 font-mono text-[10px] font-normal uppercase tracking-[0.09em] text-gray-400">Status</th>
              <th className="px-3 py-2 font-mono text-[10px] font-normal uppercase tracking-[0.09em] text-gray-400"></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {users.map((u) => {
              const isBootstrap = u.id === DEFAULT_OWNER;
              return (
                <tr key={u.id}>
                  <td className="px-3 py-2">
                    <div className="flex items-center gap-2.5">
                      <Avatar name={u.displayName || u.email} />
                      <div className="min-w-0">
                        <div className="text-[13px] font-medium text-gray-800 dark:text-white/90">
                          {u.displayName}{u.id === meId ? " · you" : ""}
                        </div>
                        <div className="font-mono text-[11px] text-gray-400">{u.email}</div>
                      </div>
                    </div>
                  </td>
                  <td className="px-3 py-2">
                    <select
                      value={u.role}
                      disabled={isBootstrap}
                      onChange={(e) => patch(u.id, { role: e.target.value })}
                      className="h-7 rounded-control bg-stripe px-2 text-[12px] text-gray-700 shadow-inset-field disabled:opacity-50 dark:text-gray-200"
                    >
                      <option value="Member">Member</option>
                      <option value="Admin">Admin</option>
                    </select>
                  </td>
                  <td className="px-3 py-2 font-mono text-[11px] text-gray-500 dark:text-gray-400">{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleDateString() : "—"}</td>
                  <td className="px-3 py-2">
                    {u.disabled
                      ? <Chip dot={TAG.red}>disabled</Chip>
                      : <Chip dot={TAG.green}>active</Chip>}
                  </td>
                  <td className="px-3 py-2">
                    {resetFor === u.id ? (
                      <div className="flex items-center gap-2">
                        <input type="password" value={resetPw} onChange={(e) => setResetPw(e.target.value)} placeholder="new password" className="h-7 rounded-control bg-stripe px-2 text-[12px] shadow-inset-field" />
                        <button onClick={() => resetPassword(u.id)} className="text-[12px] font-medium text-accent-ink">Save</button>
                        <button onClick={() => { setResetFor(null); setResetPw(""); }} className="text-[12px] text-gray-400">Cancel</button>
                      </div>
                    ) : (
                      <div className="flex items-center gap-3">
                        <button onClick={() => { setResetFor(u.id); setResetPw(""); }} className="text-[12px] font-medium text-gray-400 hover:text-accent-ink">Reset password</button>
                        {!isBootstrap && (
                          <button onClick={() => patch(u.id, { disabled: !u.disabled })} className="text-[12px] font-medium text-gray-400 hover:text-[#ee6572]">
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
      </Card>
    </div>
  );
}
