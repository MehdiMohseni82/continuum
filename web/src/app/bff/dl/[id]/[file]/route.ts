import { NextRequest } from "next/server";

// Serves a session's export.jsonl / bundle.md as a download. Uses the bearer-protected
// /api/sessions/<id>/<file> backend route (the /dl routes sit behind nginx basic-auth).
const BACKEND = process.env.CONTINUUM_BACKEND ?? "http://localhost:5000";
const TOKEN = process.env.CONTINUUM_TOKEN ?? "";

export async function GET(_req: NextRequest, ctx: { params: Promise<{ id: string; file: string }> }) {
  const { id, file } = await ctx.params;
  const res = await fetch(`${BACKEND}/api/sessions/${id}/${file}`, {
    headers: { Authorization: `Bearer ${TOKEN}` },
    cache: "no-store",
  });
  const buf = await res.arrayBuffer();
  return new Response(buf, {
    status: res.status,
    headers: {
      "Content-Type": res.headers.get("Content-Type") ?? "application/octet-stream",
      "Content-Disposition": `attachment; filename="${id}-${file}"`,
    },
  });
}
