"use client";

import dynamic from "next/dynamic";

const VectorMapOneInner = dynamic(() => import("./VectorMapOneInner"), {
  ssr: false,
});

export default function VectorMapOne() {
  return <VectorMapOneInner />;
}
