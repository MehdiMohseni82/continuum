import { capi, getMe, AppUser } from "@/lib/continuum";
import UserManager from "@/components/continuum/UserManager";

export const metadata = { title: "Users" };
export const dynamic = "force-dynamic";

export default async function UsersPage() {
  const me = await getMe();
  if (!me || me.role !== "Admin") {
    return (
      <div className="flex flex-col gap-2">
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Users</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">Only admins can manage users.</p>
      </div>
    );
  }

  const users = await capi<AppUser[]>("/api/users");

  return (
    <div className="flex max-w-4xl flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Users</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          Each person sees only their own history and memory unless they share it. Admins see everything.
        </p>
      </div>
      <UserManager initialUsers={users} meId={me.id} />
    </div>
  );
}
