import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { view } from "@/lib/patients";
import { ImportForm } from "./ImportForm";

export default async function ImportPage() {
  const user = await requireUser(["admin", "doctor", "operator"]);
  const [devices, patients] = await Promise.all([
    db.device.findMany({ orderBy: { createdAt: "asc" } }),
    db.patient.findMany({ orderBy: { createdAt: "desc" }, take: 300 }),
  ]);
  await audit(user.id, "patient.list", "patient", undefined, `${patients.length} pacientes (importação)`);
  return (
    <>
      <h1>Importar exames do headset</h1>
      <ImportForm
        devices={devices.map((d) => ({ id: d.id, label: `${d.label ?? d.model ?? "headset"} (${d.serial.slice(0, 12)})${d.hmacKeyEnc ? "" : " — sem chave"}` }))}
        patients={patients.map(view).map((p) => ({ id: p.id, label: `${p.name} — ${p.code}` }))}
      />
    </>
  );
}
