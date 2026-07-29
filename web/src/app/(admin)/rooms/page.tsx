import { redirect } from "next/navigation";
import { capi, getMe, RoomDto } from "@/lib/continuum";
import RoomManager from "@/components/continuum/RoomManager";

export const metadata = { title: "Rooms" };
export const dynamic = "force-dynamic";

export default async function RoomsPage() {
  const me = await getMe();
  if (!me) redirect("/login");
  if (me.role !== "Admin") {
    return (
      <div className="flex flex-col gap-2">
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Rooms</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">Only admins can create and manage rooms.</p>
      </div>
    );
  }

  const rooms = await capi<RoomDto[]>("/api/rooms");

  return (
    <div className="flex max-w-4xl flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Rooms</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          Spaces where agents converse on a topic. Add agents to a room and they talk until you close it.
        </p>
      </div>
      <RoomManager initialRooms={rooms} />
    </div>
  );
}
