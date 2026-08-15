import type { ReactNode } from "react";

/**
 * Page furniture.
 *
 * Every page previously invented its own header — a 30px bold title, a grey subtitle, whatever gap
 * felt right — and every card its own padding. The result was fourteen pages with no shared rhythm,
 * which is most of what "looks like a template" actually means.
 *
 * The scale here is deliberately tighter than the template's. A 30px title and 48px inputs make a
 * demo; a working instrument runs closer to 20px and 32px, which also lets far more fit on screen
 * without feeling cramped.
 */

export function PageHeader({
  title, subtitle, actions,
}: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0">
        <h1 className="text-[20px] font-semibold leading-tight tracking-[-0.01em] text-gray-900 dark:text-white/90">
          {title}
        </h1>
        {subtitle && (
          <p className="mt-0.5 text-[13px] text-gray-500 dark:text-gray-400">{subtitle}</p>
        )}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  );
}

/** A titled region. The label is mono micro-type so it reads as structure, not as content. */
export function Section({
  title, actions, children, className,
}: { title?: string; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={className}>
      {(title || actions) && (
        <div className="mb-2 flex items-center justify-between gap-3">
          {title && (
            <h2 className="font-mono text-[10px] uppercase tracking-[0.11em] text-gray-400">{title}</h2>
          )}
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}

/**
 * A run of figures.
 *
 * Replaces five separate cards each holding one number. Those cost 150px of height apiece to say
 * "0", and gave five equally-weighted boxes where the eye wanted one glance. As a single strip the
 * same facts take one row and read as a set, which is what they are.
 */
export function StatStrip({ stats }: { stats: { label: string; value: number | string; hint?: string }[] }) {
  return (
    <div className="grid grid-cols-2 divide-x divide-y divide-line overflow-hidden rounded-card bg-surface shadow-card sm:grid-cols-3 lg:grid-cols-5 lg:divide-y-0">
      {stats.map((s) => (
        <div key={s.label} className="px-4 py-3">
          <div className="font-mono text-[10px] uppercase tracking-[0.1em] text-gray-400">{s.label}</div>
          <div className="mt-1 font-mono text-[22px] font-medium leading-none tabular-nums text-gray-900 dark:text-white/90">
            {typeof s.value === "number" ? s.value.toLocaleString() : s.value}
          </div>
          {s.hint && <div className="mt-1 text-[11px] text-gray-400">{s.hint}</div>}
        </div>
      ))}
    </div>
  );
}

/**
 * An empty state that says something.
 *
 * The template's version was a bare em-dash floating in a 130px box, which tells you nothing about
 * whether the thing is broken, still loading, or simply hasn't happened yet.
 */
export function Empty({ children, hint }: { children: ReactNode; hint?: string }) {
  return (
    <div className="px-4 py-6 text-center">
      <p className="text-[13px] text-gray-500 dark:text-gray-400">{children}</p>
      {hint && <p className="mt-1 text-[12px] text-gray-400 dark:text-gray-500">{hint}</p>}
    </div>
  );
}
