// Server-side Continuum API client. The bearer token stays on the server — client components
// go through the /bff/c/* proxy route instead of calling the backend directly.

export const CONTINUUM_BACKEND = process.env.CONTINUUM_BACKEND ?? "http://localhost:5000";
const TOKEN = process.env.CONTINUUM_TOKEN ?? "";

export async function capi<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${CONTINUUM_BACKEND}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${TOKEN}`,
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
    },
    cache: "no-store",
  });
  if (!res.ok) throw new Error(`Continuum ${path} -> ${res.status}`);
  return res.json() as Promise<T>;
}

// ---- types (mirror the backend DTOs; JSON is camelCase) ----
export type CountByLabel = { label: string; count: number };

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

export type MemoryType = "User" | "Feedback" | "Project" | "Reference";
export type MemoryDto = {
  id: string;
  type: MemoryType;
  content: string;
  salience: number;
  pinned: boolean;
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

export type ModelUsage = { model: string; input: number; output: number; cacheRead: number; cacheWrite: number; costUsd: number };
export type LabeledCost = { label: string; costUsd: number; tokens: number };
export type TokenStats = {
  totalInput: number; totalOutput: number; totalCacheRead: number; totalCacheWrite: number;
  estimatedCostUsd: number; byModel: ModelUsage[]; byProject: LabeledCost[]; perDay: LabeledCost[];
};
