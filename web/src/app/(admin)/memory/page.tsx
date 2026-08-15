import { capi, MemoryDto } from "@/lib/continuum";
import MemoryList from "@/components/continuum/MemoryList";
import { PageHeader } from "@/components/bui/page";
import { Input } from "@/components/bui/form";

export const metadata = { title: "Memory" };
export const dynamic = "force-dynamic";

export default async function MemoryPage({ searchParams }: { searchParams: Promise<{ q?: string }> }) {
  const { q } = await searchParams;
  const items = q
    ? await capi<MemoryDto[]>(`/api/memory/search?q=${encodeURIComponent(q)}&take=25`)
    : await capi<MemoryDto[]>(`/api/memory?take=100`);

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Memory"
        subtitle="The durable facts Claude can recall. Secrets are redacted before anything is stored."
        actions={
          // Capped rather than full-width: this used to span the whole canvas at 48px tall.
          <form action="/memory" className="flex items-center gap-2">
            <Input name="q" size="lg" defaultValue={q ?? ""} placeholder="Semantic recall — what do I know about…" />
            <button className="h-8 shrink-0 rounded-control bg-accent px-3 text-[13px] font-medium text-white hover:bg-accent-ink">
              Recall
            </button>
          </form>
        }
      />

      <MemoryList
        items={items}
        emptyText={q ? `Nothing recalled for “${q}”.` : "No memories yet."}
        emptyHint={
          q
            ? "Try fewer or broader words — recall is semantic, not keyword matching."
            : "Claude writes them with the memory_save tool, and the extraction worker distils them from idle sessions."
        }
      />
    </div>
  );
}
