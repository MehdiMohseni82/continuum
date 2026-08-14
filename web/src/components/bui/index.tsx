/**
 * Beautiful UI primitives.
 *
 * Deliberately thin: elevation, radius and colour all come from tokens in globals.css, so a component
 * here is mostly a name for a decision rather than a pile of utility classes. That is the point — the
 * old pages each re-specified `rounded-2xl border border-gray-200 dark:border-gray-800`, which is how
 * fourteen pages drifted into fourteen slightly different cards.
 */
import type { ReactNode } from "react";

const cx = (...parts: (string | false | null | undefined)[]) => parts.filter(Boolean).join(" ");

/** Categorical palette, for anything that is a *kind* rather than a state. */
export const TAG = {
  cyan: "#16a6c7",
  green: "#25a878",
  blue: "#3f78ff",
  lime: "#92b72d",
  violet: "#9a5cff",
  pink: "#c84f9d",
  red: "#ee6572",
  amber: "#f09a2f",
} as const;
export type TagColor = keyof typeof TAG;

/** Stable colour for a label, so the same project or agent keeps its hue everywhere. */
export function tagFor(seed: string): string {
  const keys = Object.keys(TAG) as TagColor[];
  let h = 0;
  for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) >>> 0;
  return TAG[keys[h % keys.length]];
}

export function Card({ children, className, padded = true }: { children: ReactNode; className?: string; padded?: boolean }) {
  return <div className={cx("rounded-card bg-surface shadow-card", padded && "p-4", className)}>{children}</div>;
}

export function Chip({
  children, dot, tone = "plain", className, title,
}: {
  children: ReactNode;
  /** A colour from TAG, to mark a kind. */
  dot?: string;
  tone?: "plain" | "accent";
  className?: string;
  title?: string;
}) {
  return (
    <span
      title={title}
      className={cx(
        "inline-flex items-center gap-1.5 rounded-chip px-2 py-0.5 text-xs whitespace-nowrap",
        tone === "accent"
          ? "bg-accent-tint text-accent-ink"
          : "bg-surface text-gray-600 shadow-hairline dark:text-gray-300",
        className,
      )}
    >
      {dot && <span className="size-1.5 shrink-0 rounded-full" style={{ background: dot }} />}
      {children}
    </span>
  );
}

export function Button({
  children, variant = "default", type = "button", ...rest
}: {
  children: ReactNode;
  variant?: "default" | "primary" | "quiet";
} & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      type={type}
      {...rest}
      className={cx(
        "rounded-control px-3 py-1.5 text-sm font-medium transition-colors disabled:opacity-40",
        variant === "primary" && "bg-accent text-white hover:bg-accent-ink",
        variant === "default" && "bg-surface text-gray-800 shadow-btn hover:bg-stripe dark:text-white/90",
        variant === "quiet" && "text-accent-ink hover:bg-accent-tint",
        rest.className,
      )}
    />
  );
}

export function Field(props: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      {...props}
      className={cx(
        "rounded-control bg-stripe px-3 py-1.5 text-sm text-gray-800 shadow-inset-field",
        "placeholder:text-gray-400 focus:outline-none focus:shadow-[0_0_0_1px_var(--bui-accent)]",
        "dark:text-white/90",
        props.className,
      )}
    />
  );
}

/** A section label — mono micro-type, the system's quietest voice. */
export function Label({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cx("font-mono text-[10px] uppercase tracking-[0.1em] text-gray-400", className)}>
      {children}
    </div>
  );
}

export function Avatar({ name, size = 22 }: { name: string; size?: number }) {
  const initials = name.trim().split(/\s+/).slice(0, 2).map((w) => w[0]?.toUpperCase() ?? "").join("") || "?";
  return (
    <span
      title={name}
      className="inline-grid shrink-0 place-items-center rounded-full font-mono font-semibold text-white"
      style={{ width: size, height: size, fontSize: size * 0.42, background: tagFor(name) }}
    >
      {initials}
    </span>
  );
}
