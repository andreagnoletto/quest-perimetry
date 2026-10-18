import { chromium } from "playwright-core";
import { env } from "./env.ts";

/**
 * Prints a portal page to an A4 PDF with headless Chromium. The page is the same server-rendered
 * report the doctor sees (print stylesheet), so screen and PDF cannot drift apart. The caller's session
 * cookie is forwarded so the print page runs the same authorisation and audit as any other view.
 */
export async function printToPdf(url: string, cookie: { name: string; value: string } | null): Promise<Buffer> {
  const browser = await chromium.launch({ executablePath: env.chromiumPath, args: ["--no-sandbox"] });
  try {
    const context = await browser.newContext();
    if (cookie) await context.addCookies([{ name: cookie.name, value: cookie.value, url: new URL(url).origin }]);
    const page = await context.newPage();
    const res = await page.goto(url, { waitUntil: "networkidle" });
    if (!res || !res.ok()) throw new Error(`print page returned ${res?.status() ?? "no response"}`);
    return await page.pdf({ format: "A4", printBackground: true, preferCSSPageSize: true });
  } finally {
    await browser.close();
  }
}
