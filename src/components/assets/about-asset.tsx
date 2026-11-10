"use client";

import Link from "next/link";
import { get } from "@/lib/api/client";
import type { AssetCard } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** The asset a request is about (from a scanned label), read from `?asset=`. Unknown or retired assets are simply not offered. */
export function useAboutAsset(id: string | null) {
  const q = useQuery(() => (id ? get<AssetCard>("/api/assets/card", { id }).catch(() => null) : Promise.resolve(null)), [id]);
  return q.data && !q.data.retired ? q.data : null;
}

export function AboutAssetBanner({ asset, clearHref }: { asset: AssetCard; clearHref: string }) {
  return (
    <div className="form-alert" role="status" data-testid="about-asset">
      {t("This request is about {asset}.", { asset: `${asset.tag} · ${asset.name}` })}{asset.locationPath ? ` (${asset.locationPath})` : ""}{" "}
      <Link href={clearHref}>{t("It is not about this asset")}</Link>
    </div>
  );
}
