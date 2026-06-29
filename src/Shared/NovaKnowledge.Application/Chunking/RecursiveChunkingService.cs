namespace NovaKnowledge.Application.Chunking;

public class RecursiveChunkingService : Interfaces.IChunkingService
{
    private static readonly char[] SentenceEndings = ['.', '!', '?', '\n'];

    public IReadOnlyList<string> ChunkText(string text, string strategy, int chunkSize = 512, int overlap = 128)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return strategy.ToLowerInvariant() switch
        {
            "fixed" => ChunkFixed(text, chunkSize, overlap),
            "semantic" => ChunkRecursive(text, chunkSize), // semantic benchmark delegated to Python; recursive in prod
            _ => ChunkRecursive(text, chunkSize)
        };
    }

    private static List<string> ChunkRecursive(string text, int maxChars)
    {
        var chunks = new List<string>();
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new System.Text.StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (current.Length + paragraph.Length + 2 <= maxChars)
            {
                if (current.Length > 0) current.Append("\n\n");
                current.Append(paragraph);
                continue;
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (paragraph.Length <= maxChars)
            {
                current.Append(paragraph);
                continue;
            }

            foreach (var sentence in SplitSentences(paragraph))
            {
                if (current.Length + sentence.Length + 1 <= maxChars)
                {
                    if (current.Length > 0) current.Append(' ');
                    current.Append(sentence);
                }
                else
                {
                    if (current.Length > 0)
                    {
                        chunks.Add(current.ToString());
                        current.Clear();
                    }

                    if (sentence.Length <= maxChars)
                    {
                        current.Append(sentence);
                    }
                    else
                    {
                        for (var i = 0; i < sentence.Length; i += maxChars)
                        {
                            chunks.Add(sentence.Substring(i, Math.Min(maxChars, sentence.Length - i)));
                        }
                    }
                }
            }
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }

    private static List<string> ChunkFixed(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        var step = Math.Max(1, chunkSize - overlap);

        for (var i = 0; i < text.Length; i += step)
        {
            var length = Math.Min(chunkSize, text.Length - i);
            chunks.Add(text.Substring(i, length));
            if (i + length >= text.Length) break;
        }

        return chunks;
    }

    private static IEnumerable<string> SplitSentences(string paragraph)
    {
        var start = 0;
        for (var i = 0; i < paragraph.Length; i++)
        {
            if (!SentenceEndings.Contains(paragraph[i])) continue;
            var sentence = paragraph[start..(i + 1)].Trim();
            if (sentence.Length > 0) yield return sentence;
            start = i + 1;
        }

        if (start < paragraph.Length)
        {
            var remainder = paragraph[start..].Trim();
            if (remainder.Length > 0) yield return remainder;
        }
    }
}
