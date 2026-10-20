using System.Linq;
using NUnit.Framework;
using Perimetry.Headset.Logic;

namespace Perimetry.Headset.Logic.Tests
{
    /// <summary>Patient message text: angular size and word wrapping (product owner 2026-10-20: larger, closer to the target).</summary>
    public class MessageLayoutTests
    {
        [Test]
        public void CharacterSize_GivesTheRequestedAngularLineHeight()
        {
            // 2° at 1.5 m = 2 * 1.5 * tan(1°) = 0.05236 m per line; TextMesh line = characterSize * fontSize / 10.
            double size = MessageLayout.CharacterSize(2.0, 1.5, 64);
            Assert.That(size * 64 / 10, Is.EqualTo(0.05236).Within(1e-4));
            Assert.That(MessageLayout.LineHeightM(1.0, 1.5) * 2, Is.EqualTo(MessageLayout.LineHeightM(2.0, 1.5)).Within(1e-4), "small-angle linear");
        }

        [Test]
        public void MaxCharsPerLine_FollowsWidthOverLineHeight()
        {
            Assert.That(MessageLayout.MaxCharsPerLine(2.0, 40), Is.EqualTo(36));
            Assert.That(MessageLayout.MaxCharsPerLine(2.0, 0), Is.EqualTo(int.MaxValue), "0 = no wrapping");
            Assert.That(MessageLayout.MaxCharsPerLine(10.0, 5), Is.EqualTo(8), "never narrower than 8 characters");
        }

        [Test]
        public void Wrap_BreaksAtWords_KeepsParagraphs_AndLeavesLongWordsWhole()
        {
            string wrapped = MessageLayout.Wrap("Olhe para o losango amarelo.\nAperte o botão sempre que vir a luz.", 20);
            Assert.That(MessageLayout.Lines(wrapped), Is.EqualTo(new[] { "Olhe para o losango", "amarelo.", "Aperte o botão", "sempre que vir a", "luz." }));
            Assert.That(MessageLayout.Wrap("supercalifragilistic x", 8), Is.EqualTo("supercalifragilistic\nx"));
            Assert.That(MessageLayout.Wrap("", 8), Is.EqualTo(""));
            Assert.That(MessageLayout.Wrap("a b", 0), Is.EqualTo("a b"), "no limit");
        }

        [Test]
        public void EveryPatientMessage_FitsTheConfiguredWidth()
        {
            var h = Env.Headset;
            Assert.That(h.Presentation.MessageLineHeightDeg, Is.EqualTo(1.8));
            Assert.That(h.Presentation.MessageOffsetDeg, Is.EqualTo(-3.5));
            Assert.That(h.Presentation.MessageMaxWidthDeg, Is.EqualTo(48.0));
            int max = MessageLayout.MaxCharsPerLine(h.Presentation.MessageLineHeightDeg, h.Presentation.MessageMaxWidthDeg);
            foreach (string text in PatientMessages.Keys("pt-BR").Select(k => PatientMessages.Get(k, "pt-BR")).Concat(PatientMessages.Keys("en-US").Select(k => PatientMessages.Get(k, "en-US"))))
            {
                string[] lines = MessageLayout.Lines(MessageLayout.Wrap(text, max)).ToArray();
                Assert.That(lines.Where(l => l.Length > max && l.Contains(' ')), Is.Empty, text);
                // 14 lines x 1.8 deg x 1.1 spacing = 28 deg: the longest instruction screen ends about 31 deg below the target, inside the Quest 3 field of view.
                Assert.That(lines.Length, Is.LessThanOrEqualTo(14), "whole message stays inside the field of view: " + text);
            }
        }
    }
}
