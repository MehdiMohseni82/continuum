import { redirect } from "next/navigation";
import { capi, getMe, Pat } from "@/lib/continuum";
import TokenManager from "@/components/continuum/TokenManager";
import ChangePassword from "@/components/continuum/ChangePassword";

export const metadata = { title: "Settings" };
export const dynamic = "force-dynamic";

export default async function SettingsPage() {
  const me = await getMe();
  if (!me) redirect("/login");
  const tokens = me && !me.isLegacy ? await capi<Pat[]>("/api/auth/tokens") : [];

  return (
    <div className="flex max-w-3xl flex-col gap-8">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Settings</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          {me?.isLegacy
            ? "You're on the legacy shared token. Sign in with your account to manage personal tokens."
            : `Signed in as ${me?.email} · ${me?.role}`}
        </p>
      </div>

      <section>
        <h3 className="mb-1 text-base font-semibold text-gray-800 dark:text-white/90">Personal access tokens</h3>
        <p className="mb-4 text-sm text-gray-500 dark:text-gray-400">
          One per machine. A daemon or the MCP server authenticates with a token — set it as{" "}
          <code className="rounded bg-gray-100 px-1 py-0.5 text-xs dark:bg-gray-800">CONTINUUM_TOKEN</code>. Sessions it
          ingests are attributed to you.
        </p>
        <TokenManager initialTokens={tokens} canManage={!!me && !me.isLegacy} />
      </section>

      {me && !me.isLegacy && (
        <section>
          <h3 className="mb-1 text-base font-semibold text-gray-800 dark:text-white/90">Change password</h3>
          <ChangePassword />
        </section>
      )}
    </div>
  );
}
