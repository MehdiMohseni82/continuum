"use client";

import dynamic from "next/dynamic";

const VectorMapThreeInner = dynamic(() => import("./VectorMapThreeInner"), {
  ssr: false,
});

export default function VectorMapThree() {
  return <VectorMapThreeInner />;
}
