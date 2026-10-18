import Link from "next/link";
import { currentUser } from "@/lib/auth";
import { DISCLAIMER } from "@/lib/strings";
import { logoutAction } from "./login/actions";

export const dynamic = "force-dynamic";

export default async function AppLayout({ children }: { children: React.ReactNode }) {
  const user = await currentUser();
  return (
    <>
      <div className="disclaimer">{DISCLAIMER}</div>
      {user && (
        <header className="topbar">
          <Link href="/" className="brand">Quest Perimetry</Link>
          <nav>
            <Link href="/patients">Pacientes</Link>
            <Link href="/exams/import">Importar exames</Link>
            <Link href="/exams">Exames</Link>
            <Link href="/devices">Headsets e calibração</Link>
            {user.role === "admin" && <Link href="/research">Exportação para pesquisa</Link>}
            {user.role === "admin" && <Link href="/audit">Log de acesso</Link>}
          </nav>
          <form action={logoutAction} className="user">
            <span>{user.name} ({roleName[user.role]})</span>
            <button className="link" type="submit">Sair</button>
          </form>
        </header>
      )}
      <main>{children}</main>
    </>
  );
}

const roleName = { admin: "administrador", doctor: "médico", operator: "operador" } as const;
