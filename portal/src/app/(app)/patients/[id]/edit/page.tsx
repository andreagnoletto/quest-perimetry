import { notFound } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { view } from "@/lib/patients";
import { PatientForm } from "@/components/PatientForm";
import { updatePatientAction } from "../../actions";

export default async function EditPatient({ params }: { params: Promise<{ id: string }> }) {
  const user = await requireUser(["admin", "doctor", "operator"]);
  const { id } = await params;
  const row = await db.patient.findUnique({ where: { id } });
  if (!row) notFound();
  const p = view(row);
  await audit(user.id, "patient.view", "patient", id, "edit form");
  return (
    <>
      <h1>Editar paciente {p.code}</h1>
      <PatientForm action={updatePatientAction.bind(null, id)} defaults={p} isNew={false} />
    </>
  );
}
