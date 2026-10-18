using System.Collections.Generic;
using Perimetry.Core.Geometry;

namespace Perimetry.Headset.Logic
{
    /// <summary>
    /// Patient-facing text (pt-BR default, en-US ready). Each key is also the name of the optional voice clip
    /// (Resources/Perimetry/Voice/{culture}/{key}); clips are not shipped yet.
    /// Never contains results: the patient only ever sees neutral instructions.
    /// </summary>
    public static class PatientMessages
    {
        public const string PtBr = "pt-BR";
        public const string EnUs = "en-US";

        private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
        {
            [PtBr] = new Dictionary<string, string>
            {
                ["welcome"] = "Bem-vindo(a) ao exame de campo visual.\n\nAperte o botão do controle para começar.",
                ["setup.order"] = "Operador: pedido de exame\n\n{0}\n\nDirecional: alternar   ·   Segure: confirmar",
                ["setup.order.item"] = "{0} · {1} · {2} anos",
                ["setup.order.none"] = "sem pedido (manual)",
                ["setup.preset"] = "Operador: escolha o exame\n\n{0}\n\nToque: próximo   ·   Segure: confirmar",
                ["setup.age"] = "Operador: idade do paciente\n\n{0} anos\n\nDirecional: ajustar   ·   Segure: confirmar",
                ["instructions"] = "Durante o exame, olhe SEMPRE para o losango amarelo no centro.\n\nPequenas luzes vão aparecer em vários lugares, algumas bem fracas.\nAperte o botão sempre que vir uma luz, sem tirar os olhos do centro.\n\nSe precisar descansar, segure o botão apertado.\n\nAperte para continuar.",
                ["instructions.oculokinetic"] = "Durante o exame, o losango amarelo vai mudar de lugar de vez em quando.\nSiga-o com os olhos e olhe SEMPRE para ele.\n\nPequenas luzes vão aparecer ao redor, algumas bem fracas.\nAperte o botão sempre que vir uma luz, sem tirar os olhos do losango.\n\nSe precisar descansar, segure o botão apertado.\n\nAperte para continuar.",
                ["instructions.forcedChoice"] = "Durante o exame, olhe SEMPRE para o losango amarelo no centro.\n\nA cada vez, o losango vai piscar duas vezes. Uma pequena luz aparece em algum lugar logo depois de UMA das piscadas.\nAperte o botão no momento da piscada em que viu a luz. Se não tiver certeza, escolha mesmo assim.\n\nSe precisar descansar, segure o botão apertado.\n\nAperte para continuar.",
                ["eye.intro"] = "Agora vamos testar o olho {0}.\nO outro olho ficará no escuro.\n\nAperte para continuar.",
                ["eye.OD"] = "direito",
                ["eye.OS"] = "esquerdo",
                ["check.sharpness"] = "O losango amarelo está nítido?\n\nSe estiver borrado, ajuste o headset (distância entre as lentes e posição) ou chame o médico.\n\nAperte quando estiver nítido.",
                ["check.occlusion.intro"] = "Verificação: quando apertar o botão, feche o olho {0} por alguns segundos.\nSe enxergar QUALQUER luz com o olho aberto, aperte o botão.\n\nAperte para iniciar.",
                ["check.occlusion.window"] = "",
                ["check.occlusion.failed"] = "A verificação de oclusão falhou.\nPor favor, chame o médico.\n\nOperador: segure o botão para repetir.",
                ["training.intro"] = "Vamos treinar. Olhe para o losango amarelo e aperte quando vir uma luz.\n\nAperte para começar o treino.",
                ["training.hit"] = "Muito bem! Você viu a luz.",
                ["training.miss"] = "Havia uma luz. Aperte sempre que vir uma luz, mesmo fraca.",
                ["training.falseAlarm"] = "Desta vez não havia luz. Aperte só quando vir uma luz.",
                ["training.correctRejection"] = "Correto: desta vez não havia luz.",
                ["training.repeat"] = "Vamos treinar mais um pouco.\n\nAperte para continuar.",
                ["exam.ready"] = "Olhe para o losango amarelo e aperte quando vir uma luz.\nSegure o botão para pausar.\n\nAperte para começar.",
                ["exam.ready.foveal"] = "Primeiro, olhe para o centro, entre os quatro pontos amarelos.\nAperte quando vir uma luz bem no meio deles.\nDepois o losango volta ao centro e o exame continua.\nSegure o botão para pausar.\n\nAperte para começar.",
                ["exam.ready.oculokinetic"] = "Siga o losango amarelo com os olhos e aperte quando vir uma luz.\nSegure o botão para pausar.\n\nAperte para começar.",
                ["exam.ready.forcedChoice"] = "Olhe para o losango amarelo. Ele vai piscar duas vezes; aperte na piscada em que viu a luz.\nSegure o botão para pausar.\n\nAperte para começar.",
                ["pause"] = "Exame pausado.\n\nDescanse. Quando estiver pronto(a), olhe para o losango amarelo e aperte o botão.",
                ["pause.headsetRemoved"] = "Exame pausado.\n\nRecoloque o headset e aperte o botão para continuar.",
                ["betweenEyes"] = "Primeira parte concluída. Descanse um pouco.\n\nAperte quando estiver pronto(a).",
                ["saving"] = "Aguarde...",
                ["done"] = "Exame concluído. Aguarde o médico.",
                ["aborted"] = "Exame interrompido. Aguarde o médico.",
                ["error.save"] = "Erro ao gravar o exame. Chame o médico.",
            },
            [EnUs] = new Dictionary<string, string>
            {
                ["welcome"] = "Welcome to the visual field test.\n\nPress the controller button to start.",
                ["setup.order"] = "Operator: exam order\n\n{0}\n\nThumbstick: switch   ·   Hold: confirm",
                ["setup.order.item"] = "{0} · {1} · {2} years",
                ["setup.order.none"] = "no order (manual)",
                ["setup.preset"] = "Operator: choose the test\n\n{0}\n\nTap: next   ·   Hold: confirm",
                ["setup.age"] = "Operator: patient age\n\n{0} years\n\nThumbstick: adjust   ·   Hold: confirm",
                ["instructions"] = "During the test, ALWAYS look at the yellow diamond in the centre.\n\nSmall lights will appear in many places, some very dim.\nPress the button whenever you see a light, without moving your eyes from the centre.\n\nIf you need a rest, hold the button down.\n\nPress to continue.",
                ["instructions.oculokinetic"] = "During the test, the yellow diamond will move to a new place from time to time.\nFollow it with your eyes and ALWAYS look at it.\n\nSmall lights will appear around it, some very dim.\nPress the button whenever you see a light, without moving your eyes from the diamond.\n\nIf you need a rest, hold the button down.\n\nPress to continue.",
                ["instructions.forcedChoice"] = "During the test, ALWAYS look at the yellow diamond in the centre.\n\nEach time, the diamond blinks twice. A small light appears somewhere right after ONE of the blinks.\nPress the button at the blink in which you saw the light. If you are not sure, choose anyway.\n\nIf you need a rest, hold the button down.\n\nPress to continue.",
                ["eye.intro"] = "Now we will test your {0} eye.\nThe other eye will be in the dark.\n\nPress to continue.",
                ["eye.OD"] = "right",
                ["eye.OS"] = "left",
                ["check.sharpness"] = "Is the yellow diamond sharp?\n\nIf it is blurred, adjust the headset (lens spacing and position) or call the doctor.\n\nPress when it is sharp.",
                ["check.occlusion.intro"] = "Check: after you press, close your {0} eye for a few seconds.\nIf you see ANY light with the open eye, press the button.\n\nPress to start.",
                ["check.occlusion.window"] = "",
                ["check.occlusion.failed"] = "The occlusion check failed.\nPlease call the doctor.\n\nOperator: hold the button to repeat.",
                ["training.intro"] = "Let's practise. Look at the yellow diamond and press when you see a light.\n\nPress to start the practice.",
                ["training.hit"] = "Well done! You saw the light.",
                ["training.miss"] = "There was a light. Press whenever you see a light, even a dim one.",
                ["training.falseAlarm"] = "There was no light this time. Press only when you see a light.",
                ["training.correctRejection"] = "Correct: there was no light this time.",
                ["training.repeat"] = "Let's practise a little more.\n\nPress to continue.",
                ["exam.ready"] = "Look at the yellow diamond and press when you see a light.\nHold the button to pause.\n\nPress to start.",
                ["exam.ready.foveal"] = "First, look at the centre, between the four yellow dots.\nPress when you see a light right in the middle of them.\nThen the diamond returns to the centre and the test goes on.\nHold the button to pause.\n\nPress to start.",
                ["exam.ready.oculokinetic"] = "Follow the yellow diamond with your eyes and press when you see a light.\nHold the button to pause.\n\nPress to start.",
                ["exam.ready.forcedChoice"] = "Look at the yellow diamond. It blinks twice; press at the blink in which you saw the light.\nHold the button to pause.\n\nPress to start.",
                ["pause"] = "Test paused.\n\nRest. When you are ready, look at the yellow diamond and press the button.",
                ["pause.headsetRemoved"] = "Test paused.\n\nPut the headset back on and press the button to continue.",
                ["betweenEyes"] = "First part complete. Take a short rest.\n\nPress when you are ready.",
                ["saving"] = "Please wait...",
                ["done"] = "Test complete. Please wait for the doctor.",
                ["aborted"] = "Test stopped. Please wait for the doctor.",
                ["error.save"] = "Could not save the test. Please call the doctor.",
            },
        };

        public static bool Has(string key, string culture = PtBr) =>
            Table.TryGetValue(culture, out var t) && t.ContainsKey(key);

        public static string Get(string key, string culture = PtBr, params object[] args)
        {
            if (!Table.TryGetValue(culture, out var t) || !t.TryGetValue(key, out var v))
                if (!Table[PtBr].TryGetValue(key, out v)) return key;
            return args == null || args.Length == 0 ? v : string.Format(v, args);
        }

        public static string EyeName(Eye eye, string culture) => Get("eye." + eye, culture);

        public static IEnumerable<string> Keys(string culture) => Table[culture].Keys;
    }
}
