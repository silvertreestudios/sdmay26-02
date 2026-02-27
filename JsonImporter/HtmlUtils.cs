using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace JsonImporter
{
    /// Small helper utilities for normalizing HTML text used by the importer.
    /// Provides decoded plain text, an array of cleaned paragraphs, and context tokens extracted from @tag[bracket] occurrences.
    public static class HtmlUtils
    {
        /// Extracts a plain-text version and paragraph array from an HTML string.
        /// Additionally extracts any @tag[...] occurrences into a description context array.
        /// Behavior:
        /// - If input is null/empty -> returns plain = null, paragraphs = null and contexts = null (caller can skip adding properties).
        /// - By default the method does not populate the plain text (plain = null). Pass includePlain=true to return plain.
        /// - Any @tag[content]{...} matches will:
        ///     - record "content" into contexts (multiple allowed),
        ///     - if a trailing {curly} exists, replace the whole @tag[...] with the curly text;
        ///     - otherwise replace the @tag[...] with the last dot-delimited token from the bracket content (e.g. "A.B.C" -> "{C}"),
        ///       which will appear in the resulting paragraph text surrounded by curly braces.
        /// - Any text wrapped in <strong>...</strong> will have a colon appended (e.g. "<strong>Requirements</strong>" -> "Requirements:").
        public static void ExtractPlainAndParagraphs(string html, out string plain, out JArray paragraphs, out JArray contexts, bool includePlain = false)
        {
            // Treat null/whitespace as empty -> return nothing so callers can avoid adding properties
            if (string.IsNullOrWhiteSpace(html))
            {
                plain = null;
                paragraphs = null;
                contexts = null;
                return;
            }

            // Work on a decoded copy
            string decoded = WebUtility.HtmlDecode(html);

            // Append ":" after any <strong>...</strong> content.
            // Replace <strong>Inner</strong> with "Inner:" preserving surrounding spacing.
            decoded = Regex.Replace(decoded,
                @"<\s*strong\s*>(.*?)<\s*/\s*strong\s*>",
                m => (m.Groups[1].Value ?? string.Empty).Trim() + ":",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Extract and remove @tag[...] occurrences while preserving any trailing {curly} text.
            // Capture the inner bracket content for contexts.
            var ctxList = new List<string>();
            // Pattern: @<tag>[<bracketContent>](optional {curlyContent})
            var rx = new Regex(@"@[\w\.\-:]+\[([^\]]*)\](\{([^\}]*)\})?", RegexOptions.Compiled);
            decoded = rx.Replace(decoded, match =>
            {
                // group 1 = bracket content, group 2 = whole curly (with braces), group3 = inner curly content
                var bracket = match.Groups[1].Value;
                var curlyWhole = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;

                if (!string.IsNullOrEmpty(bracket))
                    ctxList.Add(bracket);

                // If curly content exists, keep it in place (return the curly including braces)
                if (!string.IsNullOrEmpty(curlyWhole))
                    return curlyWhole;

                // No curly content: replace with the last dot-delimited token from the bracket content,
                // wrapped in curly braces. Fallback: if no dot present, use the whole bracket content.
                var parts = (bracket ?? string.Empty).Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                var last = parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;

                // Surround the extracted token with curly braces as requested.
                return string.IsNullOrWhiteSpace(last) ? string.Empty : "{" + last.Trim() + "}";
            });

            // Paragraphs: split on closing paragraph tag and strip tags from each part
            var list = new List<string>();
            foreach (var part in Regex.Split(decoded, @"<\/p\s*>", RegexOptions.IgnoreCase))
            {
                var p = Regex.Replace(part, "<.*?>", String.Empty).Trim();
                if (!string.IsNullOrEmpty(p))
                    list.Add(p);
            }

            paragraphs = list.Count > 0 ? new JArray(list) : null;
            contexts = ctxList.Count > 0 ? new JArray(ctxList) : null;
            plain = includePlain ? Regex.Replace(decoded, "<.*?>", String.Empty).Trim() : null;
        }

        /// Convenience tuple-returning overload (does not include plain text by default).
        public static (string plain, JArray paragraphs, JArray contexts) ExtractPlainAndParagraphs(string html)
        {
            ExtractPlainAndParagraphs(html, out var plain, out var paragraphs, out var contexts);
            return (plain, paragraphs, contexts);
        }

        /// Convenience tuple-returning overload with includePlain control.
        public static (string plain, JArray paragraphs, JArray contexts) ExtractPlainAndParagraphs(string html, bool includePlain)
        {
            ExtractPlainAndParagraphs(html, out var plain, out var paragraphs, out var contexts, includePlain);
            return (plain, paragraphs, contexts);
        }
    }
}