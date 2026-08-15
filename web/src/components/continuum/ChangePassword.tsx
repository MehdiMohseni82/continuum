"use client";
import { useState } from "react";
import { Input } from "@/components/bui/form";

export default function ChangePassword() {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setMsg(null);
    try {
      const res = await fetch("/bff/c/auth/password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ currentPassword: current, newPassword: next }),
      });
      if (res.ok) {
        setMsg({ ok: true, text: "Password changed." });
        setCurrent("");
        setNext("");
      } else if (res.status === 401) {
        setMsg({ ok: false, text: "Current password is wrong." });
      } else {
        setMsg({ ok: false, text: (await res.text()) || "Could not change password." });
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="flex max-w-sm flex-col gap-3">
      <Input
        type="password"
        value={current}
        onChange={(e) => setCurrent(e.target.value)}
        placeholder="Current password"
        required
      />
      <Input
        type="password"
        value={next}
        onChange={(e) => setNext(e.target.value)}
        placeholder="New password (min 8 chars)"
        minLength={8}
        required
      />
      {msg && <p className={`text-[12px] ${msg.ok ? "text-[#25a878]" : "text-[#ee6572]"}`}>{msg.text}</p>}
      <button disabled={busy} className="h-8 w-fit rounded-control bg-accent px-3 text-[13px] font-medium text-white hover:bg-accent-ink disabled:opacity-50">
        {busy ? "…" : "Update password"}
      </button>
    </form>
  );
}
