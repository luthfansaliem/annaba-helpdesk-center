"use client";

import { useCallback } from "react";
import { messageOf } from "@/lib/api/client";
import { useToast } from "@/lib/toast";

export type RunResult<T> = { ok: true; data: T } | { ok: false; error: string; code?: string; fields?: Record<string, string> };

/** Runs a mutation, toasts success/failure, and never throws. */
export function useRun() {
  const { notify } = useToast();
  return useCallback(async <T,>(fn: () => Promise<T>, success?: string, opts: { silent?: boolean } = {}): Promise<RunResult<T>> => {
    try {
      const data = await fn();
      if (success) notify("success", success);
      return { ok: true, data };
    } catch (e) {
      const msg = messageOf(e);
      if (!opts.silent) notify("error", msg);
      return { ok: false, error: msg, code: (e as { code?: string }).code, fields: (e as { fields?: Record<string, string> }).fields };
    }
  }, [notify]);
}
