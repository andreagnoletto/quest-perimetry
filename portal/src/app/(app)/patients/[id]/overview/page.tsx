import Link from "next/link";
import { notFound } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { loadOverview, parseEye } from "@/lib/overview";
import { OverviewReport } from "@/components/OverviewReport";

export const dynamic = "force-dynamic";

/** Phase 6: progression overview of one eye (series of maps + MD / VFI trend), printable as PDF. */
export default async function OverviewPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ eye?: string }> }) {
  const user = await requireUser();
  const { id } = await params;
  const eye = parseEye((await searchParams).eye);
  const o = await loadOverview(id, eye, user.id, "patient.overview");
  if (!o) notFound();
  const other = eye === "OD" ? "OS" : "OD";
  return (
    <>
      <div className="no-print">
        <p><Link href={`/patients/${id}`}>← {o.patient.name}</Link></p>
        <div className="actions">
          <a className="button" href={`/patients/${id}/overview/pdf?eye=${eye}`}>PDF (A4 paisagem)</a>
          <Link className="button secondary" href={`/patients/${id}/overview?eye=${other}`}>Ver {other}</Link>
        </div>
      </div>
      <OverviewReport o={o} />
    </>
  );
}
