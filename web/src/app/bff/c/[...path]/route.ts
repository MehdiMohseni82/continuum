import { NextRequest } from "next/server";

// Proxies /bff/c/<path> -> <backend>/api/<path>, forwarding the caller's session cookie so client
// mutations act as the logged-in user. No legacy-token fallback: unauthenticated calls 401.
const BACKEND = process.env.CONTINUUM_BACKEND ?? "http://localhost:5000";

async function handler(req: NextRequest, ctx: { params: Promise<{ path: string[] }> }) {
  const { path } = await ctx.params;
  const url = `${BACKEND}/api/${path.join("/")}${req.nextUrl.search}`;
  const method = req.method;
  const body = method === "GET" || method === "HEAD" ? undefined : await req.text();

  const headers: Record<string, string> = { "Content-Type": "application/json" };
  const cookie = req.headers.get("cookie");
  if (cookie) headers["Cookie"] = cookie;

  const res = await fetch(url, { method, headers, body, cache: "no-store" });

  const text = await res.text();
  const out = new Response(text, {
    status: res.status,
    headers: { "Content-Type": res.headers.get("Content-Type") ?? "application/json" },
  });
  // Pass through any Set-Cookie (e.g. logout clearing the session).
  const setCookie = res.headers.get("set-cookie");
  if (setCookie) out.headers.set("set-cookie", setCookie);
  return out;
}

export { handler as GET, handler as POST, handler as DELETE, handler as PUT, handler as PATCH };
