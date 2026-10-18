import { redirect } from "next/navigation";
import { currentUser } from "@/lib/auth";
import { LoginForm } from "./LoginForm";

export default async function LoginPage() {
  if (await currentUser()) redirect("/");
  return (
    <div className="card" style={{ maxWidth: 380, margin: "40px auto" }}>
      <h1>Entrar</h1>
      <LoginForm />
    </div>
  );
}
