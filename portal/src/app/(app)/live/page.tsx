import { requireUser } from "@/lib/auth";
import { listSessions } from "@/lib/live";
import { LiveList } from "./LiveList";

export const dynamic = "force-dynamic";

export default async function LivePage() {
  await requireUser();
  const sessions = await listSessions();
  return (
    <>
      <h1>Ao vivo</h1>
      <p className="muted">Sessões que o headset está transmitindo agora (um envio por segundo pela rede local) e as últimas encerradas. Abra uma sessão para acompanhar o mapa, a confiabilidade e pausar ou encerrar o exame.</p>
      <LiveList initial={sessions} />
    </>
  );
}
