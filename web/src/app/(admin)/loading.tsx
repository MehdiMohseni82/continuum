export default function Loading() {
  return (
    <div className="flex flex-col gap-5">
      <div className="h-8 w-48 animate-pulse rounded-lg bg-gray-100 dark:bg-gray-800" />
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-5">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="h-24 animate-pulse rounded-2xl bg-gray-100 dark:bg-gray-800" />
        ))}
      </div>
      <div className="h-56 animate-pulse rounded-2xl bg-gray-100 dark:bg-gray-800" />
    </div>
  );
}
