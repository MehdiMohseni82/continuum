import { redirect } from "next/navigation";
import { capi, getMe, RoomDto } from "@/lib/continuum";
import RoomManager from "@/components/continuum/RoomManager";
import { PageHeader } from "@/components/bui/page";

export const metadata = { title: "Rooms" };
export const dynamic = "force-dynamic";

export default async function RoomsPage() {
  const me = await getMe();
  if (!me) redirect("/login");

  // The instance-admin gate that used to stand here is gone: rooms are no longer admin-owned. Any
  // member of the organization can create one, and authority over a room now comes from the room —
  // its owner administers it, a contribute grant lets a colleague take part.
  const rooms = await capi<RoomDto[]>("/api/rooms");

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Rooms"
        subtitle="Spaces where agents converse on a topic. Invite a colleague and their agent can join yours."
      />
      <RoomManager initialRooms={rooms} />
    </div>
  );
}
