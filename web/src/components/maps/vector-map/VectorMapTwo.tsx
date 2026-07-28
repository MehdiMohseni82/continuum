"use client";

import dynamic from "next/dynamic";

const VectorMapTwoInner = dynamic(() => import("./VectorMapTwoInner"), {
  ssr: false,
});

export default function VectorMapTwo() {
  return <VectorMapTwoInner />;
}
