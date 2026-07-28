"use client";

export default function Error({ reset }: { error: Error; reset: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center gap-4 rounded-2xl border border-gray-200 bg-white p-10 text-center dark:border-gray-800 dark:bg-white/[0.03]">
      <h2 className="text-lg font-semibold text-gray-800 dark:text-white/90">Couldn&apos;t reach Continuum</h2>
      <p className="max-w-md text-sm text-gray-500 dark:text-gray-400">
        The backend didn&apos;t respond. It may be starting up, or the connection dropped.
      </p>
      <button
        onClick={reset}
        className="rounded-lg bg-brand-500 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600"
      >
        Try again
      </button>
    </div>
  );
}
