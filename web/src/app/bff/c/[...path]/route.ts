import { NextRequest } from "next/server";

// Proxies /bff/c/<path> -> <backend>/api/<path>, injecting the bearer token server-side.
const BACKEND = process.env.CONTINUUM_BACKEND ?? "http://localhost:5000";
const TOKEN = process.env.CONTINUUM_TOKEN ?? "";

async function handler(req: NextRequest, ctx: { params: Promise<{ path: string[] }> }) {
  const { path } = await ctx.params;
  const url = `${BACKEND}/api/${path.join("/")}${req.nextUrl.search}`;
  const method = req.method;
  const body = method === "GET" || method === "HEAD" ? undefined : await req.text();

  const res = await fetch(url, {
    method,
    headers: { Authorization: `Bearer ${TOKEN}`, "Content-Type": "application/json" },
    body,
    cache: "no-store",
  });

  const text = await res.text();
  return new Response(text, {
    status: res.status,
    headers: { "Content-Type": res.headers.get("Content-Type") ?? "application/json" },
  });
}

export { handler as GET, handler as POST, handler as DELETE, handler as PUT, handler as PATCH };
