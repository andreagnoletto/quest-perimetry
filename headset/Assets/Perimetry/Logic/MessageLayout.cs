using System;
using System.Collections.Generic;
using System.Text;

namespace Perimetry.Headset.Logic
{
    /// <summary>
    /// Size and wrapping of the patient message text (config/headset.json presentation). The text is drawn on a
    /// plane at <c>messageDistanceM</c>; what matters to the patient is the angular size, so the line height is
    /// given in degrees and converted to the renderer's character size here, and long sentences are wrapped so
    /// that no line is wider than <c>messageMaxWidthDeg</c> (the lens is sharp only near its centre).
    /// </summary>
    public static class MessageLayout
    {
        /// <summary>Average advance of a Latin character as a fraction of the line height (sans-serif, mixed case).</summary>
        public const double CharWidthFactor = 0.55;

        /// <summary>World-space height of one text line at the message distance.</summary>
        public static double LineHeightM(double lineHeightDeg, double distanceM)
        {
            if (lineHeightDeg <= 0 || distanceM <= 0) throw new ArgumentOutOfRangeException(nameof(lineHeightDeg));
            return 2 * distanceM * Math.Tan(lineHeightDeg * Math.PI / 360);
        }

        /// <summary>
        /// Unity TextMesh character size that gives the wanted line height: a TextMesh line is
        /// characterSize × fontSize / 10 world units high.
        /// </summary>
        public static double CharacterSize(double lineHeightDeg, double distanceM, int fontSize)
        {
            if (fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
            return LineHeightM(lineHeightDeg, distanceM) * 10.0 / fontSize;
        }

        /// <summary>Characters that fit in <paramref name="maxWidthDeg"/> at the given line height (at least 8).</summary>
        public static int MaxCharsPerLine(double lineHeightDeg, double maxWidthDeg)
        {
            if (lineHeightDeg <= 0) throw new ArgumentOutOfRangeException(nameof(lineHeightDeg));
            if (maxWidthDeg <= 0) return int.MaxValue;
            return Math.Max(8, (int)Math.Floor(maxWidthDeg / (lineHeightDeg * CharWidthFactor)));
        }

        /// <summary>
        /// Word-wraps <paramref name="text"/> so that no line exceeds <paramref name="maxChars"/>; existing line
        /// breaks are kept (they separate paragraphs). A single word longer than the limit stays on its own line.
        /// </summary>
        public static string Wrap(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return text ?? "";
            var sb = new StringBuilder();
            string[] paragraphs = text.Replace("\r\n", "\n").Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0) sb.Append('\n');
                int lineLen = 0;
                foreach (string word in paragraphs[p].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (lineLen > 0 && lineLen + 1 + word.Length > maxChars) { sb.Append('\n'); lineLen = 0; }
                    if (lineLen > 0) { sb.Append(' '); lineLen++; }
                    sb.Append(word);
                    lineLen += word.Length;
                }
            }
            return sb.ToString();
        }

        /// <summary>Lines of the wrapped text (for tests and the operator log).</summary>
        public static IReadOnlyList<string> Lines(string wrapped) => (wrapped ?? "").Split('\n');
    }
}
