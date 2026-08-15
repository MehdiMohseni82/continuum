import type { ReactNode } from "react";

/**
 * Form controls at the 32px scale.
 *
 * Every page previously wrote its own `h-10 w-full rounded-lg border …`, which is how Memory ended up
 * with a recall field spanning the entire 1500px canvas. These default to a readable measure and have
 * to be widened on purpose rather than by accident.
 */
const cx = (...p: (string | false | null | undefined)[]) => p.filter(Boolean).join(" ");

const base =
  "h-8 rounded-control bg-stripe px-2.5 text-[13px] text-gray-800 shadow-inset-field " +
  "placeholder:text-gray-400 focus:outline-none focus:shadow-[0_0_0_1px_var(--bui-accent)] " +
  "disabled:opacity-50 dark:text-white/90";

/** Sensible widths by role, so nothing has to reach for `w-full` to look deliberate. */
const WIDTH = {
  sm: "w-40",
  md: "w-64",
  lg: "w-[420px] max-w-full",
  full: "w-full",
} as const;

export function Input({
  size = "md", className, ...rest
}: { size?: keyof typeof WIDTH } & React.InputHTMLAttributes<HTMLInputElement>) {
  return <input {...rest} className={cx(base, WIDTH[size], className)} />;
}

export function Select({
  size = "md", className, children, ...rest
}: { size?: keyof typeof WIDTH } & React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select {...rest} className={cx(base, WIDTH[size], "pr-7", className)}>
      {children}
    </select>
  );
}

export function Textarea({
  className, rows = 3, ...rest
}: React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      {...rest}
      rows={rows}
      className={cx(base.replace("h-8 ", ""), "w-full py-2 leading-relaxed", className)}
    />
  );
}

/** A labelled control. The label is mono micro-type, matching section headings. */
export function Field({
  label, hint, children, className,
}: { label: string; hint?: string; children: ReactNode; className?: string }) {
  return (
    <label className={cx("flex flex-col gap-1", className)}>
      <span className="font-mono text-[10px] uppercase tracking-[0.1em] text-gray-400">{label}</span>
      {children}
      {hint && <span className="text-[11px] text-gray-400">{hint}</span>}
    </label>
  );
}

/** A row of controls that wraps rather than overflowing. */
export function FormRow({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx("flex flex-wrap items-end gap-2.5", className)}>{children}</div>;
}
