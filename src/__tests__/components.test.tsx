import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Toast } from "@/components/ui/toast";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { ConfirmDialog } from "@/components/data/confirm-dialog";
import { DonutChart } from "@/components/charts/donut-chart";

describe("ui primitives", () => {
  it("Button applies variant classes and fires onClick", async () => {
    const fn = vi.fn();
    render(<Button variant="danger-outline" onClick={fn}>Delete</Button>);
    const b = screen.getByRole("button", { name: "Delete" });
    expect(b).toHaveClass("btn", "danger-o");
    await userEvent.click(b);
    expect(fn).toHaveBeenCalledOnce();
  });

  it("Badge renders tone", () => {
    render(<Badge tone="green">Active</Badge>);
    expect(screen.getByText("Active")).toHaveClass("pill", "green");
  });

  it("Tabs switches the selected tab", async () => {
    function T() { const [v, setV] = useState("a"); return <Tabs items={[{ key: "a", label: "A" }, { key: "b", label: "B" }]} value={v} onChange={setV} />; }
    render(<T />);
    await userEvent.click(screen.getByRole("tab", { name: "B" }));
    expect(screen.getByRole("tab", { name: "B" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "A" })).toHaveAttribute("aria-selected", "false");
  });

  it("Toast can be dismissed", async () => {
    const fn = vi.fn();
    render(<Toast tone="error" onDismiss={fn}>Oops</Toast>);
    await userEvent.click(screen.getByRole("button", { name: "Dismiss" }));
    expect(fn).toHaveBeenCalled();
  });

  it("Field shows label, required mark and error", () => {
    render(<Field label="Email" htmlFor="e" required error="Bad"><TextInput id="e" invalid /></Field>);
    expect(screen.getByLabelText(/Email/)).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent("Bad");
  });
});

describe("data components", () => {
  const rows = [{ id: "1", n: "Ann" }, { id: "2", n: "Bob" }];
  const cols = [{ key: "n", header: "Name", cell: (r: (typeof rows)[number]) => r.n }];

  it("DataTable renders rows, supports select-all and empty state", async () => {
    const onSel = vi.fn();
    const { rerender } = render(<DataTable rows={rows} columns={cols} rowKey={(r) => r.id} selectable selected={[]} onSelectedChange={onSel} />);
    expect(screen.getByText("Ann")).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText("Select all rows"));
    expect(onSel).toHaveBeenCalledWith(["1", "2"]);
    rerender(<DataTable rows={[]} columns={cols} rowKey={(r: { id: string }) => r.id} emptyMessage="Nothing" />);
    expect(screen.getByText("Nothing")).toBeInTheDocument();
  });

  it("Pagination navigates and disables edges", async () => {
    const fn = vi.fn();
    render(<Pagination page={1} pageCount={10} onPageChange={fn} pageSize={10} summary="s" />);
    expect(screen.getByLabelText("Previous page")).toBeDisabled();
    await userEvent.click(screen.getByRole("button", { name: "3" }));
    expect(fn).toHaveBeenCalledWith(3);
  });

  it("ConfirmDialog confirms and cancels", async () => {
    const ok = vi.fn(), no = vi.fn();
    render(<ConfirmDialog title="Delete?" onConfirm={ok} onCancel={no}>Sure?</ConfirmDialog>);
    await userEvent.click(screen.getByRole("button", { name: /Delete$/ }));
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(ok).toHaveBeenCalledOnce(); expect(no).toHaveBeenCalledOnce();
  });

  it("DonutChart shows the total", () => {
    render(<DonutChart segments={[{ label: "a", value: 3, color: "#000" }, { label: "b", value: 2, color: "#111" }]} centerLabel="Total" />);
    expect(screen.getByText("5")).toBeInTheDocument();
  });
});

import { OtpInput } from "@/components/ui/otp-input";
describe("OtpInput", () => {
  it("spreads a pasted/autofilled code across the boxes", async () => {
    const fn = vi.fn();
    render(<OtpInput value="" onChange={fn} />);
    await userEvent.click(screen.getByLabelText("Digit 1"));
    await userEvent.paste("123456");
    expect(fn).toHaveBeenLastCalledWith("123456");
  });
  it("accepts a single digit and ignores non-digits", async () => {
    const fn = vi.fn();
    render(<OtpInput value="" onChange={fn} />);
    await userEvent.type(screen.getByLabelText("Digit 1"), "a");
    expect(fn).not.toHaveBeenCalledWith("a");
  });
});
