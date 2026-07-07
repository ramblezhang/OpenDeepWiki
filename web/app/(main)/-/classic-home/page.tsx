import type { Metadata } from "next";
import { ClassicHome } from "@/components/home/classic-home";

export const metadata: Metadata = {
  robots: {
    index: false,
    follow: false,
  },
};

export default function ClassicHomePage() {
  return <ClassicHome />;
}
