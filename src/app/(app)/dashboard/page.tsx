"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { AgentHome } from "@/components/home/agent-home";
import { RequesterHome } from "@/components/home/requester-home";
import { useAuth } from "@/lib/auth";

/** R2-11: a vendor's person has no home page; theirs is their company's work orders. */
function VendorHome() {
  const router = useRouter();
  useEffect(() => { router.replace("/work-orders"); }, [router]);
  return null;
}

/**
 * Home (docs/52). What a person sees first depends on what they do: someone who works the service desk gets their queue, the backlog and how the
 * desk is doing; everyone else gets "what do you need?", the popular services and their own open requests. The account and security overview that
 * used to live here is under People & access → Access overview.
 */
export default function HomePage() {
  const { user, can } = useAuth();
  if (!user) return null;
  if (user.vendorName) return <VendorHome />;
  return can("tickets.read_team") ? <AgentHome /> : <RequesterHome />;
}
