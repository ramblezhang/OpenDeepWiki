import type { MetadataRoute } from "next";
import { absoluteUrl } from "@/lib/repo-seo";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: "*",
        allow: "/",
        disallow: [
          "/admin/",
          "/api/",
          "/auth",
          "/oauth/",
          "/-/classic-home",
          "/private",
          "/settings",
          "/share/",
        ],
      },
    ],
    sitemap: absoluteUrl("/sitemap.xml"),
  };
}
