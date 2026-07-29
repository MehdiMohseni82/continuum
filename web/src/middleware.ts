import { NextRequest, NextResponse } from "next/server";

// UI auth gate: the browser must have a Continuum session cookie to reach any page. Without one we
// send them to /login. Machine clients (daemon/MCP) hit /api directly through nginx and never pass
// through here; /bff/* is excluded so client components can probe /auth/me while logged out.
export function middleware(req: NextRequest) {
  if (req.cookies.has("continuum_session")) return NextResponse.next();
  const url = req.nextUrl.clone();
  url.pathname = "/login";
  url.search = "";
  return NextResponse.redirect(url);
}

export const config = {
  // Everything except: the login page, the API, the BFF proxy, Next internals, and static files.
  matcher: ["/((?!login|api|bff|_next/static|_next/image|favicon.ico|images|.*\\.).*)"],
};
