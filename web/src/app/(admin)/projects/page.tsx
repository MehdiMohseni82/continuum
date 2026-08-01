import { capi, WorkspaceDto } from "@/lib/continuum";
import ProjectManager from "@/components/continuum/ProjectManager";

export const metadata = { title: "Continuum — Projects" };
export const dynamic = "force-dynamic";

export default async function ProjectsPage() {
  const workspaces = await capi<WorkspaceDto[]>("/api/workspaces");

  return (
    <div className="flex flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Projects</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">
          Give each project a friendly name. It shows up everywhere that project&apos;s sessions and memories
          appear, across every machine — renaming applies to all of its history.
        </p>
      </div>
      <ProjectManager items={workspaces} />
    </div>
  );
}
