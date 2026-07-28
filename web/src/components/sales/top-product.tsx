import {
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableRow,
} from "../ui/table";
import Badge from "../ui/badge/Badge";

interface Product {
  id: number;
  image: string;
  name: string;
  variants: string;
  category: string;
  sales: number;
  earnings: string;
  stocks: number;
  status: "In Stock" | "Low Stock" | "Out of Stock";
}

const tableData: Product[] = [
  {
    id: 1,
    image: "./images/product-01/product-01.jpg",
    name: "Classic Denim Jacket",
    variants: "3 Sizes",
    category: "Jacket",
    sales: 500,
    earnings: "$89.99",
    stocks: 100,
    status: "In Stock",
  },
  {
    id: 2,
    image: "/images/product-01/product-02.jpg",
    name: "Slim Fit Chinos",
    variants: "4 Colors",
    category: "Pants",
    sales: 500,
    earnings: "$49.99",
    stocks: 80,
    status: "In Stock",
  },
  {
    id: 3,
    image: "/images/product-01/product-03.jpg",
    name: "Organic Cotton T-Shirt",
    variants: "5 Colors",
    category: "T-Shirt",
    sales: 220,
    earnings: "$25.00",
    stocks: 30,
    status: "Low Stock",
  },
  {
    id: 4,
    image: "/images/product-01/product-04.jpg",
    name: "Leather Ankle Boots",
    variants: "6 Sizes",
    category: "Footwear",
    sales: 875,
    earnings: "$120.00",
    stocks: 120,
    status: "Low Stock",
  },
  {
    id: 5,
    image: "/images/product-01/product-05.jpg",
    name: "Classic Denim Jacket",
    variants: "3 Sizes",
    category: "Jacket",
    sales: 500,
    earnings: "$89.99",
    stocks: 100,
    status: "In Stock",
  },
];

const statusColorMap: Record<
  Product["status"],
  "success" | "error" | "warning"
> = {
  "In Stock": "success",
  "Low Stock": "error",
  "Out of Stock": "warning",
};

export default function TopProductTable() {
  return (
    <div className="rounded-2xl border border-gray-200 bg-white dark:border-gray-800 dark:bg-white/3">
      <div className="flex items-center justify-between px-6 py-4">
        <div>
          <h3 className="text-lg font-semibold text-gray-800 dark:text-white/90">
            Top Products
          </h3>
        </div>
        <div className="flex gap-3">
          <button className="text-theme-sm shadow-theme-xs inline-flex h-10 items-center gap-2 rounded-lg border border-gray-300 bg-white px-4 py-2.5 font-medium text-gray-700 hover:bg-gray-50 hover:text-gray-800 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-400 dark:hover:bg-white/[0.03] dark:hover:text-gray-200">
            <svg
              className="fill-white stroke-current dark:fill-gray-800"
              width="20"
              height="20"
              viewBox="0 0 20 20"
              fill="none"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path
                d="M2.29004 5.90393H17.7067"
                stroke=""
                strokeWidth="1.5"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
              <path
                d="M17.7075 14.0961H2.29085"
                stroke=""
                strokeWidth="1.5"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
              <path
                d="M12.0826 3.33331C13.5024 3.33331 14.6534 4.48431 14.6534 5.90414C14.6534 7.32398 13.5024 8.47498 12.0826 8.47498C10.6627 8.47498 9.51172 7.32398 9.51172 5.90415C9.51172 4.48432 10.6627 3.33331 12.0826 3.33331Z"
                fill=""
                stroke=""
                strokeWidth="1.5"
              />
              <path
                d="M7.91745 11.525C6.49762 11.525 5.34662 12.676 5.34662 14.0959C5.34661 15.5157 6.49762 16.6667 7.91745 16.6667C9.33728 16.6667 10.4883 15.5157 10.4883 14.0959C10.4883 12.676 9.33728 11.525 7.91745 11.525Z"
                fill=""
                stroke=""
                strokeWidth="1.5"
              />
            </svg>
            Filter
          </button>
          <button className="text-theme-sm shadow-theme-xs inline-flex h-10 items-center gap-2 rounded-lg border border-gray-300 bg-white px-4 py-2.5 font-medium text-gray-700 hover:bg-gray-50 hover:text-gray-800 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-400 dark:hover:bg-white/[0.03] dark:hover:text-gray-200">
            See All
          </button>
        </div>
      </div>
      <div className="px-5 pb-5">
        <div className="custom-scrollbar max-w-full overflow-x-auto rounded-xl border border-gray-100 dark:border-gray-800">
          <Table>
            <TableHeader className="border-b border-gray-100 dark:border-gray-800">
              <TableRow>
                {[
                  "Product Name",
                  "Product ID",
                  "Sales",
                  "Earnings",
                  "Stocks",
                  "Status",
                ].map((col) => (
                  <TableCell
                    key={col}
                    isHeader
                    className="px-5 py-3 whitespace-nowrap text-left"
                  >
                    <p className="text-theme-xs font-medium text-gray-500 dark:text-gray-400">
                      {col}
                    </p>
                  </TableCell>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody className="divide-y divide-gray-100 py-3 dark:divide-gray-800">
              {tableData.map((product) => (
                <TableRow key={product.id}>
                  <TableCell className="px-5 py-2 whitespace-nowrap">
                    <div className="flex items-center gap-3">
                      <div className="size-12.5 overflow-hidden rounded-md">
                        <img src={product.image} alt={product.name} />
                      </div>
                      <div>
                        <p className="text-theme-sm font-medium text-gray-800 dark:text-white/90">
                          {product.name}
                        </p>
                        <span className="text-theme-xs text-gray-500 dark:text-gray-400">
                          {product.variants}
                        </span>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell className="px-5 py-2 whitespace-nowrap">
                    <p className="text-theme-sm text-gray-500 dark:text-gray-400">
                      {product.category}
                    </p>
                  </TableCell>
                  <TableCell className="px-5 py-2 whitespace-nowrap">
                    <p className="text-theme-sm text-gray-500 dark:text-gray-400">
                      {product.sales}
                    </p>
                  </TableCell>
                  <TableCell className="px-5 py-2 whitespace-nowrap">
                    <p className="text-theme-sm text-gray-500 dark:text-gray-400">
                      {product.earnings}
                    </p>
                  </TableCell>
                  <TableCell className="px-5 py-2 whitespace-nowrap">
                    <p className="text-theme-sm text-gray-500 dark:text-gray-400">
                      {product.stocks}
                    </p>
                  </TableCell>
                  <TableCell className="px-5 py-2 whitespace-nowrap">
                    <Badge color={statusColorMap[product.status]} size="sm">
                      {product.status}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </div>
    </div>
  );
}
