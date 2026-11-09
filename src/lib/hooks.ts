"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "./api/client";

export type Query<T> = { data?: T; error?: ApiError | Error; loading: boolean; reload: () => void; setData: (d: T) => void };

/** Minimal data hook: loads on mount and whenever `deps` change; ignores stale responses; `reload()` refetches. */
export function useQuery<T>(fetcher: () => Promise<T>, deps: unknown[] = []): Query<T> {
  const [state, setState] = useState<{ data?: T; error?: ApiError | Error; loading: boolean }>({ loading: true });
  const [tick, setTick] = useState(0);
  const seq = useRef(0);
  const fn = useRef(fetcher); fn.current = fetcher;
  useEffect(() => {
    const mine = ++seq.current;
    setState((s) => ({ ...s, loading: true, error: undefined }));
    fn.current().then((data) => { if (seq.current === mine) setState({ data, loading: false }); }, (error) => { if (seq.current === mine) setState((s) => ({ data: s.data, error, loading: false })); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);
  const reload = useCallback(() => setTick((t) => t + 1), []);
  const setData = useCallback((d: T) => setState({ data: d, loading: false }), []);
  return { ...state, reload, setData };
}

export function useDebounced<T>(v: T, ms = 250) {
  const [d, setD] = useState(v);
  useEffect(() => { const t = setTimeout(() => setD(v), ms); return () => clearTimeout(t); }, [v, ms]);
  return d;
}
