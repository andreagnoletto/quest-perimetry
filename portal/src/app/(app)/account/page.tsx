import { requireUser } from "@/lib/auth";
import { env } from "@/lib/env";
import { MIN_PASSWORD_LENGTH } from "@/lib/security-policy";
import { PasswordForm } from "./PasswordForm";

export default async function Account() {
  const user = await requireUser();
  return (
    <>
      <h1>Minha conta</h1>
      <div className="card">
        <p>{user.name} · {user.email} · perfil <b>{user.role}</b></p>
        <p className="muted small">A sessão expira após {env.sessionTtlHours} h ou {env.sessionIdleMinutes} min sem uso. A conta é bloqueada por 15 min após 10 senhas erradas seguidas.</p>
      </div>
      <h2>Trocar senha</h2>
      <PasswordForm minLength={MIN_PASSWORD_LENGTH} />
    </>
  );
}
