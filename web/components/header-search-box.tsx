"use client";

import React from "react";
import { Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { useTranslations } from "@/hooks/use-translations";

interface HeaderSearchBoxProps {
  value: string;
  onChange: (value: string) => void;
  visible: boolean;
  className?: string;
}

export function HeaderSearchBox({
  value,
  onChange,
  visible,
  className,
}: HeaderSearchBoxProps) {
  const t = useTranslations();

  return (
    <div
      className={cn(
        "relative hidden items-center transition-all duration-250 ease-in-out sm:flex",
        visible
          ? "opacity-100 translate-x-0 pointer-events-auto"
          : "opacity-0 translate-x-2 pointer-events-none",
        className
      )}
      aria-hidden={!visible}
    >
      <Search className="absolute left-2.5 h-4 w-4 text-muted-foreground" />
      <Input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={t("home.searchPlaceholder") || "Search repositories..."}
        maxLength={100}
        className="h-8 w-48 pl-8 text-sm md:w-56"
        tabIndex={visible ? 0 : -1}
      />
    </div>
  );
}
