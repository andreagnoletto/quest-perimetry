import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Quest Perimetry — portal",
  description: "Portal do médico. Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA.",
  robots: { index: false, follow: false },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="pt-BR">
      <body>{children}</body>
    </html>
  );
}
