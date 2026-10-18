import { notFound } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { dateTimeBr } from "@/lib/format";
import { loadOverview, parseEye } from "@/lib/overview";
import { OverviewReport } from "@/components/OverviewReport";

export const dynamic = "force-dynamic";

/** Bare overview for printing (the PDF route opens this page in headless Chromium); landscape A4. */
export default async function PrintOverview({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ eye?: string }> }) {
  const user = await requireUser();
  const { id } = await params;
  const eye = parseEye((await searchParams).eye);
  const o = await loadOverview(id, eye, user.id, "patient.overview.print");
  if (!o) notFound();
  return (
    <>
      <style>{"@page { size: A4 landscape; margin: 10mm; }"}</style>
      <OverviewReport o={o} footerNote={`impresso por ${user.name} em ${dateTimeBr(new Date())}`} />
    </>
  );
}
