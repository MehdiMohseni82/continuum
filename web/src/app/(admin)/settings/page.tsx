import { redirect } from "next/navigation";
import { capi, getMe, Pat } from "@/lib/continuum";
import TokenManager from "@/components/continuum/TokenManager";
import ChangePassword from "@/components/continuum/ChangePassword";
import { Chip } from "@/components/bui";
import { PageHeader, Section } from "@/components/bui/page";

export const metadata = { title: "Settings" };
export const dynamic = "force-dynamic";

export default async function SettingsPage() {
  const me = await getMe();
  if (!me) redirect("/login");
  const tokens = !me.isLegacy ? await capi<Pat[]>("/api/auth/tokens") : [];

  return (
    // Capped: a settings form running the full width of a 1500px canvas reads as unfinished.
    <div className="flex max-w-3xl flex-col gap-6">
      <PageHeader
        title="Settings"
        subtitle={
          me.isLegacy
            ? "You're on the legacy shared token. Sign in with your account to manage personal tokens."
            : me.email
        }
        actions={!me.isLegacy ? <Chip>{me.role}</Chip> : undefined}
      />

      <Section title="Personal access tokens">
        <p className="mb-2.5 max-w-[75ch] text-[13px] text-gray-500 dark:text-gray-400">
          One per machine. A daemon or the MCP server authenticates with a token — set it as{" "}
          <code className="rounded-chip bg-stripe px-1 py-0.5 font-mono text-[11px] shadow-hairline">CONTINUUM_TOKEN</code>.
          Sessions it ingests are attributed to you.
        </p>
        <TokenManager initialTokens={tokens} canManage={!me.isLegacy} />
      </Section>

      {!me.isLegacy && (
        <Section title="Change password">
          <ChangePassword />
        </Section>
      )}
    </div>
  );
}
