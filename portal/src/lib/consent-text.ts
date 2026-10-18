// Consent texts shown on the patient form and recorded on the patient (Patient.consentVersion). Bump the version
// whenever the wording changes so the access log and the patient record say which text was agreed to.
// This is the clinic's working text for the research prototype; it is not a reviewed legal instrument.

export const CONSENT_VERSION = "2026-10-18.1";

export const CONSENT_TEXT =
  "O paciente foi informado de que o exame é feito com um protótipo de perimetria em headset de realidade virtual, " +
  "para pesquisa ou uso sob supervisão médica, não registrado na ANVISA; de que os dados de identificação ficam " +
  "criptografados no portal da clínica e nunca vão para o headset, que recebe só um código; de que todo acesso ao " +
  "seu registro fica no log; e de que pode pedir a cópia dos seus dados ou a anonimização do registro a qualquer " +
  "momento (LGPD, art. 18).";

export const RESEARCH_CONSENT_TEXT =
  "Autoriza o uso dos exames, de forma pseudonimizada (sem nome, ID interno, nascimento ou datas), em pesquisa " +
  "científica da clínica, inclusive na construção do banco normativo do aparelho. Pode ser retirada a qualquer momento.";
