import { requireUser } from "@/lib/auth";
import { PatientForm } from "@/components/PatientForm";
import { createPatientAction } from "../actions";

export default async function NewPatient() {
  await requireUser(["admin", "doctor", "operator"]);
  return (
    <>
      <h1>Novo paciente</h1>
      <PatientForm action={createPatientAction} isNew />
    </>
  );
}
