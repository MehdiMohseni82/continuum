"use client";
import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import Image from "next/image";
import { usePathname } from "next/navigation";
import { useSidebar } from "../../../context/SidebarContext";
import {
  AiIcon,
  CartIcon,
  TaskIcon,
  ListIcon,
  TableIcon,
  PageIcon,
  HorizontaLDots,
  PlusAltIcon,
  MinusAltIcon,
  GridIcon,
  UserCircleIcon,
  CalenderIcon,
} from "@/icons";

export default function SidebarOne() {
  const { isExpanded, isMobileOpen, isHovered, setIsHovered } = useSidebar();
  const pathname = usePathname();

  const [selected, setSelected] = useState<string>("Dashboard");
  const [subSelected, setSubSelected] = useState<string>("");

  const isActive = useCallback((path: string) => pathname === path, [pathname]);

  const handleMenuToggle = (name: string) => {
    setSelected((prev) => (prev === name ? "" : name));
  };

  const handleSubMenuToggle = (name: string) => {
    setSubSelected((prev) => (prev === name ? "" : name));
  };

  const showContent = isExpanded || isHovered || isMobileOpen;

  // Auto-expand active menu when sidebar is visible, close all when collapsed
  useEffect(() => {
    if (!showContent) {
      setSelected("");
      setSubSelected("");
      return;
    }

    const path = pathname;

    // Dashboard group
    if (
      [
        "/",
        "/analytics",
        "/marketing",
        "/stocks",
        "/saas",
        "/logistics",
        "/sales",
        "/ai",
      ].includes(path)
    ) {
      setSelected("Dashboard");
    } else if (
      [
        "/inventory-management",
        "/product-development",
        "/finance",
        "/human-resources",
        "/supply-chain",
      ].includes(path)
    ) {
      setSelected("Dashboard");
      setSubSelected("CRM");
    }
    // AI Assistant group
    else if (
      [
        "/text-generator",
        "/image-generator",
        "/code-generator",
        "/video-generator",
      ].includes(path)
    ) {
      setSelected("AI");
    }
    // E-commerce group
    else if (
      [
        "/products-list",
        "/add-product",
        "/billing",
        "/transactions",
        "/single-transaction",
      ].includes(path)
    ) {
      setSelected("E-commerce");
    } else if (
      ["/invoices", "/single-invoice", "/create-invoice"].includes(path)
    ) {
      setSelected("E-commerce");
      setSubSelected("Invoices");
    }
    // Task group
    else if (["/task-list", "/task-kanban"].includes(path)) {
      setSelected("Task");
    }
    // Forms group
    else if (["/form-elements", "/form-layout"].includes(path)) {
      setSelected("Forms");
    }
    // Tables group
    else if (["/basic-tables", "/data-tables"].includes(path)) {
      setSelected("Tables");
    }
    // Pages group
    else if (
      [
        "/file-manager",
        "/pricing-tables",
        "/faq",
        "/api-keys",
        "/integrations",
        "/blank",
        "/coming-soon",
        "/maintenance",
        "/success",
      ].includes(path)
    ) {
      setSelected("Pages");
    } else if (["/error-404", "/error-500", "/error-503"].includes(path)) {
      setSelected("Pages");
      setSubSelected("ErrorPages");
    }
  }, [showContent, pathname]);

  return (
    <aside
      className={`fixed flex flex-col top-0 px-5 left-0 bg-white dark:bg-gray-900 h-screen transition-all duration-300 ease-in-out z-50 border-r border-gray-200 dark:border-gray-800
        ${
          isExpanded || isMobileOpen
            ? "w-[290px]"
            : isHovered
              ? "w-[290px]"
              : "w-[90px]"
        }
        ${isMobileOpen ? "translate-x-0" : "-translate-x-full"}
        xl:translate-x-0`}
      onMouseEnter={() => !isExpanded && setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      {/* SIDEBAR HEADER */}
      <div
        className={`sidebar-header shrink-0 flex items-center gap-2 pt-8 pb-7 ${
          !isExpanded && !isHovered ? "xl:justify-center" : "justify-between"
        }`}
      >
        <Link href="#">
          {showContent ? (
            <>
              <Image
                className="dark:hidden"
                src="/images/logo/logo.svg"
                alt="Logo"
                width={160}
                height={32}
                priority
                style={{ width: "auto", height: "auto" }}
              />
              <Image
                className="hidden dark:block"
                src="/images/logo/logo-dark.svg"
                alt="Logo"
                width={160}
                height={32}
                priority
                style={{ width: "auto", height: "auto" }}
              />
            </>
          ) : (
            <Image
              src="/images/logo/logo-icon.svg"
              alt="Logo"
              width={32}
              height={32}
              priority
              style={{ width: "auto", height: "auto" }}
            />
          )}
        </Link>
      </div>
      {/* /SIDEBAR HEADER */}

      <div className="flex-1 min-h-0 overflow-y-auto pb-4 duration-300 ease-linear no-scrollbar">
        <nav>
          {/* Menu Group */}
          <div>
            <h3 className="mb-4 text-xs leading-[20px] text-gray-400 uppercase">
              {showContent ? (
                "MENU"
              ) : (
                <HorizontaLDots className="size-6 mx-auto" />
              )}
            </h3>

            <ul className="mb-6 flex flex-col gap-1">
              {/* ===== Dashboard ===== */}
              <li>
                <button
                  onClick={() => handleMenuToggle("Dashboard")}
                  className={`menu-item group ${
                    selected === "Dashboard"
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  } cursor-pointer w-full`}
                >
                  <GridIcon
                    className={
                      selected === "Dashboard"
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && (
                    <span className="menu-item-text">Dashboard</span>
                  )}
                  {showContent &&
                    (selected === "Dashboard" ? (
                      <MinusAltIcon className="ml-auto w-5 h-5 text-brand-500" />
                    ) : (
                      <PlusAltIcon className="ml-auto w-5 h-5" />
                    ))}
                </button>

                {/* Dashboard Dropdown */}
                <div
                  className={`menu-accordion ${selected === "Dashboard" ? "open" : ""} ${!showContent ? "hidden" : ""}`}
                >
                  <div>
                    <ul className="menu-dropdown mt-2 ml-6 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Ecommerce
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/analytics")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Analytics
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/marketing")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Marketing
                        </Link>
                      </li>
                      {/* CRM nested submenu */}
                      <li>
                        <button
                          onClick={() => handleSubMenuToggle("CRM")}
                          className={`menu-dropdown-item group flex items-center justify-between w-full ${
                            isActive("/crm")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          CRM
                          {subSelected === "CRM" ? (
                            <MinusAltIcon className="w-5 h-5 text-brand-500" />
                          ) : (
                            <PlusAltIcon className="w-5 h-5" />
                          )}
                        </button>
                        <div
                          className={`menu-accordion ${
                            subSelected === "CRM" ? "open" : ""
                          }`}
                        >
                          <div>
                            <ul className="mt-2 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/inventory-management")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  Inventory Management
                                </Link>
                              </li>
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/product-development")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  Product Development
                                </Link>
                              </li>
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/finance")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  Finance
                                </Link>
                              </li>
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/human-resources")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  Human Resources
                                </Link>
                              </li>
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/supply-chain")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  Supply Chain
                                </Link>
                              </li>
                            </ul>
                          </div>
                        </div>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/stocks")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Stocks
                          <span className="absolute right-3 flex items-center gap-1">
                            <span className="menu-dropdown-badge menu-dropdown-badge-inactive">
                              New
                            </span>
                          </span>
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/saas")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          SaaS
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/logistics")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Logistics
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="/sales"
                          className={`menu-dropdown-item group ${
                            isActive("/sales")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Sales
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="/ai"
                          className={`menu-dropdown-item group ${
                            isActive("/ai")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          AI
                        </Link>
                      </li>
                    </ul>
                  </div>
                </div>
              </li>
              {/* ===== /Dashboard ===== */}

              {/* ===== AI Assistant ===== */}
              <li>
                <button
                  onClick={() => handleMenuToggle("AI")}
                  className={`menu-item group ${
                    selected === "AI"
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  } cursor-pointer w-full`}
                >
                  <AiIcon
                    className={
                      selected === "AI"
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && (
                    <span className="menu-item-text">AI Assistant</span>
                  )}
                  {showContent &&
                    (selected === "AI" ? (
                      <MinusAltIcon className="ml-auto w-5 h-5 text-brand-500" />
                    ) : (
                      <PlusAltIcon className="ml-auto w-5 h-5" />
                    ))}
                </button>

                <div
                  className={`menu-accordion ${selected === "AI" ? "open" : ""} ${!showContent ? "hidden" : ""}`}
                >
                  <div>
                    <ul className="menu-dropdown mt-2 ml-6 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/text-generator")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Text Generator
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/image-generator")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Image Generator
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/code-generator")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Code Generator
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/video-generator")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Video Generator
                        </Link>
                      </li>
                    </ul>
                  </div>
                </div>
              </li>
              {/* ===== /AI Assistant ===== */}

              {/* ===== E-commerce ===== */}
              <li>
                <button
                  onClick={() => handleMenuToggle("E-commerce")}
                  className={`menu-item group ${
                    selected === "E-commerce"
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  } cursor-pointer w-full`}
                >
                  <CartIcon
                    className={
                      selected === "E-commerce"
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && (
                    <span className="menu-item-text">E-commerce</span>
                  )}
                  {showContent &&
                    (selected === "E-commerce" ? (
                      <MinusAltIcon className="ml-auto w-5 h-5 text-brand-500" />
                    ) : (
                      <PlusAltIcon className="ml-auto w-5 h-5" />
                    ))}
                </button>

                <div
                  className={`menu-accordion ${selected === "E-commerce" ? "open" : ""} ${!showContent ? "hidden" : ""}`}
                >
                  <div>
                    <ul className="menu-dropdown mt-2 ml-6 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/products-list")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Products
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/add-product")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Add Product
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/billing")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Billing
                        </Link>
                      </li>
                      {/* Invoices nested submenu */}
                      <li>
                        <button
                          onClick={() => handleSubMenuToggle("Invoices")}
                          className={`menu-dropdown-item group flex items-center justify-between w-full ${
                            isActive("/invoices")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Invoices
                          {subSelected === "Invoices" ? (
                            <MinusAltIcon className="w-4 h-4 text-brand-500" />
                          ) : (
                            <PlusAltIcon className="w-4 h-4" />
                          )}
                        </button>
                        <div
                          className={`menu-accordion ${
                            subSelected === "Invoices" ? "open" : ""
                          }`}
                        >
                          <div>
                            <ul className="mt-2 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/invoices")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  Invoices List
                                </Link>
                              </li>
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/single-invoice")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  Single Invoice
                                </Link>
                              </li>
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/create-invoice")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  Create Invoice
                                </Link>
                              </li>
                            </ul>
                          </div>
                        </div>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/transactions")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Transactions
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/single-transaction")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Single Transaction
                        </Link>
                      </li>
                    </ul>
                  </div>
                </div>
              </li>
              {/* ===== /E-commerce ===== */}

              {/* ===== Calendar ===== */}
              <li>
                <Link
                  href="#"
                  className={`menu-item group ${
                    isActive("/calendar")
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  }`}
                >
                  <CalenderIcon
                    className={
                      isActive("/calendar")
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && (
                    <span className="menu-item-text">Calendar</span>
                  )}
                </Link>
              </li>
              {/* ===== /Calendar ===== */}

              {/* ===== User Profile ===== */}
              <li>
                <Link
                  href="#"
                  className={`menu-item group ${
                    isActive("/profile")
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  }`}
                >
                  <UserCircleIcon
                    className={
                      isActive("/profile")
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && (
                    <span className="menu-item-text">User Profile</span>
                  )}
                </Link>
              </li>
              {/* ===== /User Profile ===== */}

              {/* ===== Task ===== */}
              <li>
                <button
                  onClick={() => handleMenuToggle("Task")}
                  className={`menu-item group ${
                    selected === "Task"
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  } cursor-pointer w-full`}
                >
                  <TaskIcon
                    className={
                      selected === "Task"
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && <span className="menu-item-text">Task</span>}
                  {showContent &&
                    (selected === "Task" ? (
                      <MinusAltIcon className="ml-auto w-5 h-5 text-brand-500" />
                    ) : (
                      <PlusAltIcon className="ml-auto w-5 h-5" />
                    ))}
                </button>

                <div
                  className={`menu-accordion ${selected === "Task" ? "open" : ""} ${!showContent ? "hidden" : ""}`}
                >
                  <div>
                    <ul className="menu-dropdown mt-2 ml-6 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/task-list")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          List
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/task-kanban")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Kanban
                        </Link>
                      </li>
                    </ul>
                  </div>
                </div>
              </li>
              {/* ===== /Task ===== */}

              {/* ===== Forms ===== */}
              <li>
                <button
                  onClick={() => handleMenuToggle("Forms")}
                  className={`menu-item group ${
                    selected === "Forms"
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  } cursor-pointer w-full`}
                >
                  <ListIcon
                    className={
                      selected === "Forms"
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && <span className="menu-item-text">Forms</span>}
                  {showContent &&
                    (selected === "Forms" ? (
                      <MinusAltIcon className="ml-auto w-5 h-5 text-brand-500" />
                    ) : (
                      <PlusAltIcon className="ml-auto w-5 h-5" />
                    ))}
                </button>

                <div
                  className={`menu-accordion ${selected === "Forms" ? "open" : ""} ${!showContent ? "hidden" : ""}`}
                >
                  <div>
                    <ul className="menu-dropdown mt-2 ml-6 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/form-elements")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Form Elements
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/form-layout")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Form Layout
                        </Link>
                      </li>
                    </ul>
                  </div>
                </div>
              </li>
              {/* ===== /Forms ===== */}

              {/* ===== Tables ===== */}
              <li>
                <button
                  onClick={() => handleMenuToggle("Tables")}
                  className={`menu-item group ${
                    selected === "Tables"
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  } cursor-pointer w-full`}
                >
                  <TableIcon
                    className={
                      selected === "Tables"
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && (
                    <span className="menu-item-text">Tables</span>
                  )}
                  {showContent &&
                    (selected === "Tables" ? (
                      <MinusAltIcon className="ml-auto w-5 h-5 text-brand-500" />
                    ) : (
                      <PlusAltIcon className="ml-auto w-5 h-5" />
                    ))}
                </button>

                <div
                  className={`menu-accordion ${selected === "Tables" ? "open" : ""} ${!showContent ? "hidden" : ""}`}
                >
                  <div>
                    <ul className="menu-dropdown mt-2 ml-6 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/basic-tables")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Basic Tables
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/data-tables")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Data Tables
                        </Link>
                      </li>
                    </ul>
                  </div>
                </div>
              </li>
              {/* ===== /Tables ===== */}

              {/* ===== Pages ===== */}
              <li>
                <button
                  onClick={() => handleMenuToggle("Pages")}
                  className={`menu-item group ${
                    selected === "Pages"
                      ? "menu-item-active"
                      : "menu-item-inactive"
                  } cursor-pointer w-full`}
                >
                  <PageIcon
                    className={
                      selected === "Pages"
                        ? "menu-item-icon-active"
                        : "menu-item-icon-inactive"
                    }
                    width="24"
                    height="24"
                  />
                  {showContent && <span className="menu-item-text">Pages</span>}
                  {showContent &&
                    (selected === "Pages" ? (
                      <MinusAltIcon className="ml-auto w-5 h-5 text-brand-500" />
                    ) : (
                      <PlusAltIcon className="ml-auto w-5 h-5" />
                    ))}
                </button>

                <div
                  className={`menu-accordion ${selected === "Pages" ? "open" : ""} ${!showContent ? "hidden" : ""}`}
                >
                  <div>
                    <ul className="menu-dropdown mt-2 ml-6 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/file-manager")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          File Manager
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/pricing-tables")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Pricing Tables
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/faq")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          FAQ
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/api-keys")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          API Keys
                          <span className="absolute right-3 flex items-center gap-1">
                            <span className="menu-dropdown-badge menu-dropdown-badge-inactive">
                              New
                            </span>
                          </span>
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/integrations")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Integrations
                          <span className="absolute right-3 flex items-center gap-1">
                            <span className="menu-dropdown-badge menu-dropdown-badge-inactive">
                              New
                            </span>
                          </span>
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/blank")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Blank Page
                        </Link>
                      </li>
                      {/* Error Pages nested submenu */}
                      <li>
                        <button
                          onClick={() => handleSubMenuToggle("ErrorPages")}
                          className={`menu-dropdown-item group flex items-center justify-between w-full ${
                            isActive("/error-404") ||
                            isActive("/error-500") ||
                            isActive("/error-503")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Error Pages
                          {subSelected === "ErrorPages" ? (
                            <MinusAltIcon className="w-4 h-4 text-brand-500" />
                          ) : (
                            <PlusAltIcon className="w-4 h-4" />
                          )}
                        </button>
                        <div
                          className={`menu-accordion ${
                            subSelected === "ErrorPages" ? "open" : ""
                          }`}
                        >
                          <div>
                            <ul className="mt-2 flex flex-col gap-1 border-l border-gray-200 pl-4 dark:border-gray-800">
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/error-404")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  404 Error
                                </Link>
                              </li>
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/error-500")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  500 Error
                                </Link>
                              </li>
                              <li>
                                <Link
                                  href="#"
                                  className={`menu-dropdown-item group ${
                                    isActive("/error-503")
                                      ? "menu-dropdown-item-active"
                                      : "menu-dropdown-item-inactive"
                                  }`}
                                >
                                  503 Error
                                </Link>
                              </li>
                            </ul>
                          </div>
                        </div>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/coming-soon")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Coming Soon
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/maintenance")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Maintenance
                        </Link>
                      </li>
                      <li>
                        <Link
                          href="#"
                          className={`menu-dropdown-item group ${
                            isActive("/success")
                              ? "menu-dropdown-item-active"
                              : "menu-dropdown-item-inactive"
                          }`}
                        >
                          Success
                        </Link>
                      </li>
                    </ul>
                  </div>
                </div>
              </li>
              {/* ===== /Pages ===== */}
            </ul>
          </div>
        </nav>
      </div>
    </aside>
  );
}
