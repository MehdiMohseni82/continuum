import { capi, MemoryDto } from "@/lib/continuum";
import MemoryList from "@/components/continuum/MemoryList";

export const metadata = { title: "Memory" };
export const dynamic = "force-dynamic";

export default async function MemoryPage({ searchParams }: { searchParams: Promise<{ q?: string }> }) {
  const { q } = await searchParams;
  const items = q
    ? await capi<MemoryDto[]>(`/api/memory/search?q=${encodeURIComponent(q)}&take=25`)
    : await capi<MemoryDto[]>(`/api/memory?take=100`);

  return (
    <div className="flex flex-col gap-5">
      <div>
        <h2 className="text-2xl font-bold text-gray-800 dark:text-white/90">Memory</h2>
        <p className="text-sm text-gray-500 dark:text-gray-400">The durable facts Claude can recall (secrets redacted).</p>
      </div>

      <form action="/memory" className="flex gap-2">
        <input
          name="q"
          defaultValue={q ?? ""}
          placeholder="Semantic recall — what do I know about…"
          className="h-11 flex-1 rounded-lg border border-gray-300 bg-transparent px-4 text-sm text-gray-800 focus:border-brand-500 focus:outline-none dark:border-gray-700 dark:text-white/90"
        />
        <button className="h-11 rounded-lg bg-brand-500 px-5 text-sm font-medium text-white hover:bg-brand-600">Recall</button>
      </form>

      <MemoryList
        items={items}
        emptyText={q ? `Nothing recalled for “${q}”.` : "No memories yet — Claude saves them via the memory_save tool."}
      />
    </div>
  );
}
