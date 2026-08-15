import { capi, WorkspaceDto } from "@/lib/continuum";
import ProjectManager from "@/components/continuum/ProjectManager";
import { PageHeader } from "@/components/bui/page";

export const metadata = { title: "Continuum — Projects" };
export const dynamic = "force-dynamic";

export default async function ProjectsPage() {
  const workspaces = await capi<WorkspaceDto[]>("/api/workspaces");

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Projects"
        subtitle="Give each project a friendly name — it appears everywhere that project's sessions and memories do, across every machine, and applies to all of its history."
      />
      <ProjectManager items={workspaces} />
    </div>
  );
}
