using System.Collections.Generic;
using Perimetry.Core.Analysis;

namespace Perimetry.Core.Localization
{
    /// <summary>Minimal i18n table (pt-BR default, en-US prepared). UI layers may replace it with their own resource system.</summary>
    public static class Strings
    {
        public const string PtBr = "pt-BR";
        public const string EnUs = "en-US";

        private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
        {
            [PtBr] = new Dictionary<string, string>
            {
                ["ght." + GhtResult.WithinNormalLimits] = "Dentro dos limites normais",
                ["ght." + GhtResult.Borderline] = "Limítrofe",
                ["ght." + GhtResult.OutsideNormalLimits] = "Fora dos limites normais",
                ["ght." + GhtResult.GeneralReduction] = "Sensibilidade geral reduzida",
                ["ght." + GhtResult.BorderlineAndGeneralReduction] = "Limítrofe / Sensibilidade geral reduzida",
                ["ght." + GhtResult.AbnormallyHigh] = "Sensibilidade anormalmente alta",
                ["ght." + GhtResult.NotApplicable] = "Não se aplica",
                ["fixationLosses"] = "Perdas de fixação",
                ["falsePositives"] = "Falsos positivos",
                ["falseNegatives"] = "Falsos negativos",
                ["duration"] = "Duração do exame",
                ["examDone"] = "Exame concluído. Aguarde o médico.",
            },
            [EnUs] = new Dictionary<string, string>
            {
                ["ght." + GhtResult.WithinNormalLimits] = "Within normal limits",
                ["ght." + GhtResult.Borderline] = "Borderline",
                ["ght." + GhtResult.OutsideNormalLimits] = "Outside normal limits",
                ["ght." + GhtResult.GeneralReduction] = "General reduction of sensitivity",
                ["ght." + GhtResult.BorderlineAndGeneralReduction] = "Borderline / General reduction of sensitivity",
                ["ght." + GhtResult.AbnormallyHigh] = "Abnormally high sensitivity",
                ["ght." + GhtResult.NotApplicable] = "Not applicable",
                ["fixationLosses"] = "Fixation losses",
                ["falsePositives"] = "False positive errors",
                ["falseNegatives"] = "False negative errors",
                ["duration"] = "Test duration",
                ["examDone"] = "Test complete. Please wait for the doctor.",
            },
        };

        public static string Get(string key, string culture = PtBr) =>
            Table.TryGetValue(culture, out var t) && t.TryGetValue(key, out var v) ? v : key;

        public static string Ght(GhtResult r, string culture = PtBr) => Get("ght." + r, culture);
    }
}
