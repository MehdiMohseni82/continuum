import type { ReactNode } from "react";
import { Card } from "./index";
import { Empty } from "./page";

/**
 * One table for every list in the app.
 *
 * History, Memory, Projects, Agents and Users each hand-rolled their own — five implementations of
 * the same thing, drifting apart at every edit. Consistency here is structural rather than a matter
 * of discipline: a list looks like the others because it cannot easily look different.
 */
export type Column<T> = {
  key: string;
  header: string;
  /** Rendered cell. Keep it to one line; the row is 36px. */
  cell: (row: T) => ReactNode;
  /** Right-align and use tabular figures — for counts, sizes, costs. */
  numeric?: boolean;
  width?: string;
};

export function DataTable<T>({
  columns, rows, rowKey, empty, emptyHint, footer,
}: {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  /** What to say when there is nothing — never leave this to a dash. */
  empty: string;
  emptyHint?: string;
  footer?: ReactNode;
}) {
  return (
    <Card padded={false} className="overflow-hidden">
      <div className="max-w-full overflow-x-auto">
        <table className="min-w-full text-[13px]">
          <thead>
            <tr className="border-b border-line">
              {columns.map((c) => (
                <th
                  key={c.key}
                  style={c.width ? { width: c.width } : undefined}
                  className={`whitespace-nowrap px-3 py-2 font-mono text-[10px] font-normal uppercase tracking-[0.09em] text-gray-400 ${
                    c.numeric ? "text-right" : "text-left"
                  }`}
                >
                  {c.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="p-0">
                  <Empty hint={emptyHint}>{empty}</Empty>
                </td>
              </tr>
            ) : (
              rows.map((r) => (
                <tr key={rowKey(r)} className="border-b border-line last:border-0 hover:bg-stripe">
                  {columns.map((c) => (
                    <td
                      key={c.key}
                      className={`px-3 py-2 align-middle ${
                        c.numeric
                          ? "text-right font-mono tabular-nums text-gray-600 dark:text-gray-300"
                          : "text-gray-600 dark:text-gray-300"
                      }`}
                    >
                      {c.cell(r)}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      {footer && <div className="border-t border-line px-3 py-2 text-[12px] text-gray-400">{footer}</div>}
    </Card>
  );
}

/** Placeholder rows while a list loads — shaped like the table it replaces, not a spinner. */
export function TableSkeleton({ columns = 4, rows = 6 }: { columns?: number; rows?: number }) {
  return (
    <Card padded={false} className="overflow-hidden">
      <div className="divide-y divide-line">
        {Array.from({ length: rows }).map((_, i) => (
          <div key={i} className="flex gap-3 px-3 py-2.5">
            {Array.from({ length: columns }).map((_, j) => (
              <div
                key={j}
                className="h-3 animate-pulse rounded-chip bg-line"
                style={{ width: j === 0 ? "34%" : "14%" }}
              />
            ))}
          </div>
        ))}
      </div>
    </Card>
  );
}

/**
 * Something failed.
 *
 * Server components previously had no error boundary at all, so a failed fetch rendered a blank
 * frame — indistinguishable from a slow one, or from genuinely having no data.
 */
export function ErrorState({ what, detail }: { what: string; detail?: string }) {
  return (
    <Card className="border-l-2 border-l-[#ee6572]">
      <p className="text-[13px] font-medium text-gray-800 dark:text-white/90">{what}</p>
      {detail && <p className="mt-1 font-mono text-[11px] text-gray-500 dark:text-gray-400">{detail}</p>}
    </Card>
  );
}
