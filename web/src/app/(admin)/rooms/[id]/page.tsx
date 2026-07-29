import { redirect } from "next/navigation";
import { capi, getMe, RoomDetail } from "@/lib/continuum";
import RoomDetailView from "@/components/continuum/RoomDetail";

export const metadata = { title: "Room" };
export const dynamic = "force-dynamic";

export default async function RoomPage({ params }: { params: Promise<{ id: string }> }) {
  const me = await getMe();
  if (!me) redirect("/login");
  if (me.role !== "Admin") {
    return <p className="text-sm text-gray-500 dark:text-gray-400">Only admins can view rooms.</p>;
  }

  const { id } = await params;
  let detail: RoomDetail | null = null;
  try {
    detail = await capi<RoomDetail>(`/api/rooms/${id}`);
  } catch {
    detail = null;
  }
  if (!detail) return <p className="text-sm text-gray-500 dark:text-gray-400">Room not found.</p>;

  return <RoomDetailView initial={detail} />;
}
