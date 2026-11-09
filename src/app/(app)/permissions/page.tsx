"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { DataTable, type Column } from "@/components/data/data-table";
import { FilterBar, SearchBox } from "@/components/data/filter-bar";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { PermissionItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

export default function PermissionsPage() {
  const q = useQuery(() => get<PermissionItem[]>("/api/permissions"), []); const [s, setS] = useState("");
  const rows = (q.data ?? []).filter((p) => (p.key + p.description + p.module).toLowerCase().includes(s.toLowerCase()));
  const cols: Column<PermissionItem>[] = [
    { key: "k", header: t("Permission"), cell: (p) => <><code>{p.key}</code> {p.critical && <Badge tone="red" dot={false}>{t("critical")}</Badge>}</> },
    { key: "m", header: t("Module"), className: "hide-sm", cell: (p) => p.module },
    { key: "d", header: t("Description"), cell: (p) => p.description },
    { key: "r", header: t("Granted to"), className: "hide-md", cell: (p) => p.grantedTo.join(", ") || "—" },
  ];
  return (
    <Require perm="permissions.read">
      <PageHeader title={t("Permissions")} description={t("The fixed permission catalog. Permissions are defined in code and assigned to roles.")} />
      <Panel>
        <FilterBar><SearchBox label={t("Search permissions")} placeholder={t("Search permissions…")} value={s} onChange={setS} /></FilterBar>
        <DataTable label={t("Permissions")} columns={cols} rows={rows} rowKey={(p) => p.key} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} emptyMessage={t("No permissions match.")} />
      </Panel>
    </Require>
  );
}
