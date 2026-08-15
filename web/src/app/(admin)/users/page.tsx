import { redirect } from "next/navigation";
import { capi, getMe, AppUser } from "@/lib/continuum";
import UserManager from "@/components/continuum/UserManager";
import { Card } from "@/components/bui";
import { PageHeader, Empty } from "@/components/bui/page";

export const metadata = { title: "Users" };
export const dynamic = "force-dynamic";

export default async function UsersPage() {
  const me = await getMe();
  if (!me) redirect("/login");

  if (me.role !== "Admin") {
    return (
      <div className="flex max-w-3xl flex-col gap-4">
        <PageHeader title="Users" />
        <Card padded={false}>
          <Empty hint="Ask an administrator to add someone to your organization.">
            Only administrators can manage accounts.
          </Empty>
        </Card>
      </div>
    );
  }

  const users = await capi<AppUser[]>("/api/users");

  return (
    <div className="flex max-w-5xl flex-col gap-4">
      <PageHeader
        title="Users"
        // The old copy said "Admins see everything", which the privacy work makes untrue: an
        // administrator manages the instance without that granting them a right to read what people
        // keep private. Saying otherwise in the interface is worse than saying nothing.
        subtitle="Everyone sees only their own history and memory unless they share it."
      />
      <UserManager initialUsers={users} meId={me.id} />
    </div>
  );
}
