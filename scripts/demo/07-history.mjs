/**
 * 07 — last step, after every module: things that can only be right once all the others have run.
 *  - budgets follow the real work-order costs (the stock module's calibrateBudgets).
 */
import { calibrateBudgets } from "./04-stock.mjs";
export const name = "history";

export async function run(d) {
  await d.soft("budget calibration", async () => { await calibrateBudgets(d); d.log("  budgets calibrated to work-order costs"); });
}
