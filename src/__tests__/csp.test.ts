import { describe, expect, it } from "vitest";
import { buildCsp } from "@/middleware";

describe("buildCsp", () => {
  it("allows scripts only from this origin and the request nonce", () => {
    const csp = buildCsp("abc123", false);
    expect(csp).toContain("script-src 'self' 'nonce-abc123' 'strict-dynamic'");
    expect(csp).not.toContain("unsafe-eval");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("object-src 'none'");
  });
  it("adds unsafe-eval only in development", () => {
    expect(buildCsp("n", true)).toContain("'unsafe-eval'");
  });
});
