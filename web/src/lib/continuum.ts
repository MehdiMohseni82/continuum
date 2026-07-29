// Server-side Continuum API client. Forwards the logged-in user's session cookie so SSR pages act
// as that user; falls back to the legacy shared token (→ bootstrap admin) when nobody is logged in.
// Client components go through the /bff/c/* proxy, which forwards the cookie the same way.

import { cookies } from "next/headers";
import { redirect } from "next/navigation";

export const CONTINUUM_BACKEND = process.env.CONTINUUM_BACKEND ?? "http://localhost:5000";
export const SESSION_COOKIE = "continuum_session";

// The browser UI authenticates the *person* by their session cookie only — never the legacy shared
// token. (That token is for machine clients hitting /api directly.) No cookie → the backend 401s and
// we send them to /login, so the UI is gated even with nginx basic-auth removed.
async function authHeaders(): Promise<Record<string, string>> {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  const session = (await cookies()).get(SESSION_COOKIE)?.value;
  if (session) headers.Cookie = `${SESSION_COOKIE}=${session}`;
  return headers;
}

export async function capi<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${CONTINUUM_BACKEND}${path}`, {
    ...init,
    headers: { ...(await authHeaders()), ...(init?.headers ?? {}) },
    cache: "no-store",
  });
  if (res.status === 401) redirect("/login");
  if (!res.ok) throw new Error(`Continuum ${path} -> ${res.status}`);
  return res.json() as Promise<T>;
}

export type Me = { id: string; email: string; displayName: string; role: "Member" | "Admin"; isLegacy: boolean; mustChangePassword: boolean };

/// Fetch the current principal without redirecting (null when unauthenticated).
export async function getMe(): Promise<Me | null> {
  const session = (await cookies()).get(SESSION_COOKIE)?.value;
  if (!session) return null;
  const res = await fetch(`${CONTINUUM_BACKEND}/api/auth/me`, {
    headers: { "Content-Type": "application/json", Cookie: `${SESSION_COOKIE}=${session}` },
    cache: "no-store",
  });
  return res.ok ? ((await res.json()) as Me) : null;
}

// ---- types (mirror the backend DTOs; JSON is camelCase) ----
export type CountByLabel = { label: string; count: number };
export type Pat = { id: string; name: string; prefix: string; createdAt: string; lastUsedAt: string | null; revokedAt: string | null; expiresAt: string | null };
export type PatCreated = { id: string; name: string; token: string; prefix: string; createdAt: string; expiresAt: string | null };
export type AppUser = { id: string; email: string; displayName: string; role: "Member" | "Admin"; disabled: boolean; createdAt: string; lastLoginAt: string | null };

export type Analytics = {
  sessions: number;
  events: number;
  memories: number;
  agents: number;
  handoffs: number;
  sessionsByMachine: CountByLabel[];
  sessionsByStatus: CountByLabel[];
  topWorkspaces: CountByLabel[];
  memoriesByType: CountByLabel[];
  eventsPerDay: CountByLabel[];
};

export type SessionStatus = "Live" | "Ended" | "Interrupted" | "Unknown";

export type SessionSummary = {
  id: string;
  title: string | null;
  workspace: string;
  machine: string;
  status: SessionStatus;
  startedAt: string;
  lastEventAt: string;
  messageCount: number;
};

export type EventDto = {
  id: number;
  uuid: string;
  type: string;
  role: string | null;
  timestamp: string;
  text: string | null;
};

export type SessionDetail = { session: SessionSummary; events: EventDto[] };

export type SearchHit = {
  sessionId: string;
  sessionTitle: string | null;
  workspace: string;
  eventId: number;
  type: string;
  timestamp: string;
  snippet: string | null;
};

export type SessionSearchHit = {
  id: string; title: string | null; workspace: string; machine: string;
  summary: string | null; lastEventAt: string; messageCount: number; score: number | null;
};

export type MemoryType = "User" | "Feedback" | "Project" | "Reference";
export type MemoryDto = {
  id: string;
  type: MemoryType;
  content: string;
  salience: number;
  pinned: boolean;
  shared: boolean;
  workspaceId: string | null;
  createdAt: string;
  score: number | null;
};

export type WorkspaceDto = { id: string; projectKey: string; displayName: string; sessionCount: number };

export type AgentDto = { id: string; name: string; machineName: string | null; capabilities: string | null; lastSeenAt: string };
export type HandoffDto = {
  id: string; fromAgent: string; claimedBy: string | null; title: string; task: string;
  contextRef: string | null; status: string; createdAt: string; claimedAt: string | null;
};

export type RedactionHit = {
  sessionId: string; sessionTitle: string | null; eventId: number; labels: string[]; snippet: string;
};

export type NotificationDto = {
  id: string; kind: "message" | "handoff"; title: string; detail: string;
  timestamp: string; severity: "info" | "warning";
};

export type RagSource = { kind: "memory" | "event"; sessionId: string | null; sessionTitle: string | null; snippet: string };
export type AskResponse = { answer: string; sources: RagSource[] };

export type BackupFile = { name: string; sizeBytes: number; createdAt: string };
export type BackupStatus = {
  configured: boolean; directory: string; count: number; totalBytes: number;
  latestAt: string | null; recent: BackupFile[];
};
export type BusMessage = { id: number; fromAgent: string; toAgent: string | null; channel: string | null; body: string; createdAt: string };

export type LanguageMode = "Shorthand" | "Human";
export type RoomDto = {
  id: string; name: string; topic: string; languageMode: LanguageMode; language: string | null;
  status: string; channelName: string; createdAt: string; closedAt: string | null;
  memberCount: number; messageCount: number; lastActivityAt: string | null;
};
export type RoomMemberDto = { agent: string; machineName: string | null; joinedAt: string };
export type RoomDetail = { room: RoomDto; members: RoomMemberDto[]; messages: BusMessage[] };

export type ModelUsage = { model: string; input: number; output: number; cacheRead: number; cacheWrite: number; costUsd: number };
export type LabeledCost = { label: string; costUsd: number; tokens: number };
export type TokenStats = {
  totalInput: number; totalOutput: number; totalCacheRead: number; totalCacheWrite: number;
  estimatedCostUsd: number; byModel: ModelUsage[]; byProject: LabeledCost[]; perDay: LabeledCost[];
};
