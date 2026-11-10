import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Markdown } from "@/components/kb/markdown";

describe("knowledge-base Markdown", () => {
  it("renders headings, lists, emphasis, code and quotes", () => {
    const { container } = render(<Markdown text={"# Title\n\nSome **bold** and *soft* text with `code`.\n\n- one\n- two\n\n1. first\n2. second\n\n> careful\n\n```\nraw <b>\n```"} />);
    expect(screen.getByRole("heading", { level: 2, name: "Title" })).toBeInTheDocument();
    expect(container.querySelector("strong")?.textContent).toBe("bold");
    expect(container.querySelector("em")?.textContent).toBe("soft");
    expect(container.querySelectorAll("ul li")).toHaveLength(2);
    expect(container.querySelectorAll("ol li")).toHaveLength(2);
    expect(container.querySelector("blockquote")?.textContent).toContain("careful");
    expect(container.querySelector("pre")?.textContent).toBe("raw <b>");
    expect(container.querySelector("pre b")).toBeNull();
  });

  it("never turns text into markup or unsafe links", () => {
    const { container } = render(<Markdown text={'<script>alert(1)</script> <img src=x onerror=alert(1)>\n\n[bad](javascript:alert(1)) [good](https://example.com) [here](/kb) ![pic](https://evil.example/x.png)'} />);
    expect(container.querySelector("script")).toBeNull();
    expect(container.querySelector("img")).toBeNull();
    expect(container.textContent).toContain("<script>alert(1)</script>");
    const links = [...container.querySelectorAll("a")].map((a) => a.getAttribute("href"));
    expect(links).toEqual(["https://example.com", "/kb"]);
    expect(screen.getByRole("link", { name: "good" })).toHaveAttribute("rel", "noreferrer noopener");
    expect(container.textContent).toContain("[pic]");
  });

  it("shows the article's own images through the file URL", () => {
    const id = "0193a4a2-1111-7222-8333-944455556666";
    render(<Markdown text={`![Panel diagram](file:${id})`} fileUrl={(f) => `/api/kb/articles/a/files/${f}`} />);
    expect(screen.getByRole("img", { name: "Panel diagram" })).toHaveAttribute("src", `/api/kb/articles/a/files/${id}`);
  });
});
