"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useSidebar } from "../context/SidebarContext";
import {
  BoxCubeIcon, ChatIcon, FolderIcon, GridIcon, ListIcon,
  PageIcon, PieChartIcon, PlugInIcon, UserCircleIcon,
} from "../icons/index";

/**
 * Continuum's sidebar.
 *
 * Replaces the template's version, which carried ~300 lines of submenu, badge and dropdown machinery
 * for a nav that has never had a single sub-item. What it lacked instead was structure: eleven
 * equally-weighted entries in one flat column, which is a list to scroll rather than a menu to scan.
 *
 * Grouping does the work. Three headings sort the same destinations into what you're doing — your own
 * material, the things shared with colleagues, and running the instance — and give the collaboration
 * features somewhere to belong rather than being appended to the bottom.
 */
type Item = { name: string; path: string; icon: React.ReactNode; badge?: number };

const GROUPS: { label: string; items: Item[] }[] = [
  {
    label: "My work",
    items: [
      { icon: <GridIcon />, name: "Overview", path: "/" },
      { icon: <ListIcon />, name: "History", path: "/sessions" },
      { icon: <FolderIcon />, name: "Projects", path: "/projects" },
      { icon: <BoxCubeIcon />, name: "Memory", path: "/memory" },
      { icon: <ChatIcon />, name: "Ask my history", path: "/ask" },
      { icon: <PageIcon />, name: "Search", path: "/search" },
    ],
  },
  {
    label: "Team",
    items: [
      { icon: <ChatIcon />, name: "Rooms", path: "/rooms" },
      { icon: <UserCircleIcon />, name: "Agents & bus", path: "/agents" },
    ],
  },
  {
    label: "Operations",
    items: [
      { icon: <PieChartIcon />, name: "Usage & cost", path: "/usage" },
      { icon: <PlugInIcon />, name: "Redaction", path: "/redaction" },
      { icon: <UserCircleIcon />, name: "Settings & tokens", path: "/settings" },
    ],
  },
];

export default function AppSidebar() {
  const { isExpanded, isMobileOpen, isHovered, setIsHovered } = useSidebar();
  const pathname = usePathname();
  const wide = isExpanded || isHovered || isMobileOpen;

  return (
    <aside
      onMouseEnter={() => !isExpanded && setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      className={`fixed top-0 left-0 z-50 flex h-screen flex-col border-r border-line bg-stripe px-3 py-4
        transition-all duration-300 ease-out
        ${wide ? "w-[218px]" : "w-[86px]"}
        ${isMobileOpen ? "translate-x-0" : "-translate-x-full"} xl:translate-x-0`}
    >
      <Link href="/" className="mb-2 flex items-center gap-2.5 px-2 py-1">
        <span className="grid size-6 shrink-0 place-items-center rounded-chip bg-accent font-mono text-[11px] font-semibold text-white">
          C
        </span>
        {wide && (
          <span className="truncate text-[14px] font-semibold tracking-[-0.01em] text-gray-800 dark:text-white/90">
            Continuum
          </span>
        )}
      </Link>

      <nav className="no-scrollbar flex-1 overflow-y-auto">
        {GROUPS.map((group) => (
          <div key={group.label}>
            {/* At the narrow width a heading would only be noise, so it becomes a rule instead. */}
            {wide ? (
              <div className="px-2 pt-4 pb-1.5 font-mono text-[10px] uppercase tracking-[0.1em] text-gray-400">
                {group.label}
              </div>
            ) : (
              <div className="mx-2 my-3 border-t border-line" />
            )}

            {group.items.map((item) => {
              const active = pathname === item.path;
              return (
                <Link
                  key={item.path}
                  href={item.path}
                  title={wide ? undefined : item.name}
                  className={`mb-0.5 flex items-center gap-2.5 rounded-control px-2 py-[5px] text-[12.5px] transition-colors
                    ${active
                      ? "bg-accent-tint font-medium text-accent-ink"
                      : "text-gray-600 hover:bg-surface hover:text-gray-900 dark:text-gray-400 dark:hover:text-white/90"}
                    ${wide ? "" : "justify-center"}`}
                >
                  <span className={`size-[15px] shrink-0 ${active ? "text-accent-ink" : ""}`}>{item.icon}</span>
                  {wide && <span className="truncate">{item.name}</span>}
                  {wide && item.badge ? (
                    <span className="ml-auto rounded-full bg-accent px-1.5 font-mono text-[10px] leading-4 text-white">
                      {item.badge}
                    </span>
                  ) : null}
                </Link>
              );
            })}
          </div>
        ))}
      </nav>
    </aside>
  );
}
