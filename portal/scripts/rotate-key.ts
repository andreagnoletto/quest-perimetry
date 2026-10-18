// Rotates PERIMETRY_DATA_KEY: re-encrypts every encrypted column and recomputes the blind indexes with the new key.
// Dry run by default (reports what would change); `--apply` writes, in ONE transaction, so a failure leaves the
// database on the old key. Runbook: portal/SECURITY.md.
//
//   PERIMETRY_DATA_KEY=<current, base64> PERIMETRY_DATA_KEY_NEW=<new, base64> node scripts/rotate-key.ts [--apply]
//
// Afterwards set PERIMETRY_DATA_KEY=<new> in the portal's environment and restart it. Anonymised patients hold
// markers instead of ciphertext and are skipped. Research study IDs (HMAC of the patient id) change with the key.
import { PrismaClient } from "@prisma/client";
import { blindIndex, decrypt, decryptOpt, encrypt, encryptOpt } from "../src/lib/crypto.ts";

function key(name: string): Buffer {
  const k = Buffer.from(process.env[name] ?? "", "base64");
  if (k.length !== 32) { console.error(`${name} must be 32 bytes, base64`); process.exit(2); }
  return k;
}

const oldKey = key("PERIMETRY_DATA_KEY");
const newKey = key("PERIMETRY_DATA_KEY_NEW");
if (oldKey.equals(newKey)) { console.error("the new key equals the current one"); process.exit(2); }
const apply = process.argv.includes("--apply");

const db = new PrismaClient();
try {
  const patients = await db.patient.findMany({ where: { anonymizedAt: null } });
  const devices = await db.device.findMany({ where: { hmacKeyEnc: { not: null } } });
  const skipped = await db.patient.count({ where: { anonymizedAt: { not: null } } });

  // decrypt everything first: a wrong current key fails here, before any write
  const patientUpdates = patients.map((p) => {
    const internalId = decrypt(p.internalIdEnc, oldKey), name = decrypt(p.nameEnc, oldKey);
    return {
      id: p.id,
      data: {
        internalIdEnc: encrypt(internalId, newKey), internalIdIdx: blindIndex(internalId, "internalId", newKey),
        nameEnc: encrypt(name, newKey), nameIdx: blindIndex(name, "name", newKey),
        birthDateEnc: encrypt(decrypt(p.birthDateEnc, oldKey), newKey),
        sexEnc: encryptOpt(decryptOpt(p.sexEnc, oldKey), newKey),
        refractionEnc: encryptOpt(decryptOpt(p.refractionEnc, oldKey), newKey),
        diagnosisEnc: encryptOpt(decryptOpt(p.diagnosisEnc, oldKey), newKey),
      },
    };
  });
  const deviceUpdates = devices.map((d) => ({ id: d.id, data: { hmacKeyEnc: encrypt(decrypt(d.hmacKeyEnc!, oldKey), newKey) } }));

  console.log(`${patientUpdates.length} pacientes, ${deviceUpdates.length} chaves de headset a recriptografar; ${skipped} pacientes anonimizados ignorados`);
  if (!apply) {
    console.log("simulação: nada gravado (use --apply para aplicar)");
  } else {
    await db.$transaction(async (tx) => {
      for (const u of patientUpdates) await tx.patient.update({ where: { id: u.id }, data: u.data });
      for (const u of deviceUpdates) await tx.device.update({ where: { id: u.id }, data: u.data });
      await tx.accessLog.create({ data: { action: "key.rotate", entityType: "user", detail: `${patientUpdates.length} pacientes, ${deviceUpdates.length} headsets, via script` } });
    }, { timeout: 10 * 60_000, maxWait: 30_000 });
    // read back one row with the new key
    const check = patientUpdates[0] ? await db.patient.findUnique({ where: { id: patientUpdates[0].id } }) : null;
    if (check) decrypt(check.nameEnc, newKey);
    console.log("ok: chave trocada. Agora defina PERIMETRY_DATA_KEY com a chave nova e reinicie o portal.");
  }
} catch (e) {
  console.error(e instanceof Error ? e.message : e);
  process.exitCode = 1;
} finally {
  await db.$disconnect();
}
