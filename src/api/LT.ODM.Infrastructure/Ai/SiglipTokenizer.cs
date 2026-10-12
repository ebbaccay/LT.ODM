using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LT.ODM.Infrastructure.Ai;

/// <summary>
/// The text tokenizer of SigLIP models (Marqo-FashionSigLIP, timm ViT-B-16-SigLIP), read from their Hugging Face
/// tokenizer.json, giving the same ids as the Python <c>tokenizers</c> library with open_clip's "canonicalize" cleaning:
/// <list type="number">
/// <item>open_clip clean: "_" to space, ASCII punctuation removed, lower case, whitespace collapsed;</item>
/// <item>normalizer: SentencePiece precompiled character map (NFKC-like), then runs of spaces to one;</item>
/// <item>pre-tokenizer: Metaspace (space to "▁", "▁" in front, split before each "▁");</item>
/// <item>model: Unigram (best-scoring split by Viterbi; unknown characters fused into one unk token);</item>
/// <item>post-processor: "&lt;/s&gt;" appended; truncated and padded to the context length with the pad id.</item>
/// </list>
/// Only the configuration these models use is supported; anything else is refused when the file is loaded.
/// </summary>
public sealed partial class SiglipTokenizer
{
    private const char Space = '▁';
    private const double UnkPenalty = 10.0;

    private readonly Dictionary<string, (int Id, double Score)> _pieces;
    private readonly int _maxPieceLength;
    private readonly int _unkId;
    private readonly double _unkScore;
    private readonly int _eosId;
    private readonly int _padId;
    private readonly PrecompiledCharsMap? _charsMap;

    public int ContextLength { get; }

    private SiglipTokenizer(Dictionary<string, (int, double)> pieces, int unkId, double minScore, int eosId, int padId, int contextLength, PrecompiledCharsMap? charsMap)
    {
        _pieces = pieces;
        _maxPieceLength = pieces.Keys.Max(k => k.Length);
        _unkId = unkId;
        _unkScore = minScore - UnkPenalty;
        _eosId = eosId;
        _padId = padId;
        ContextLength = contextLength;
        _charsMap = charsMap;
    }

    /// <summary>Reads tokenizer.json (Unigram model, Metaspace pre-tokenizer, &lt;/s&gt; post-processing, fixed padding).</summary>
    public static SiglipTokenizer Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var model = root.GetProperty("model");
        if (model.GetProperty("type").GetString() != "Unigram") throw new NotSupportedException("Only Unigram tokenizers are supported.");

        var pieces = new Dictionary<string, (int, double)>(StringComparer.Ordinal);
        var minScore = double.MaxValue;
        var id = 0;
        foreach (var entry in model.GetProperty("vocab").EnumerateArray())
        {
            var piece = entry[0].GetString()!;
            var score = entry[1].GetDouble();
            pieces.TryAdd(piece, (id, score));
            minScore = Math.Min(minScore, score);
            id++;
        }
        var unkId = model.GetProperty("unk_id").GetInt32();

        PrecompiledCharsMap? charsMap = null;
        if (root.TryGetProperty("normalizer", out var normalizer) && normalizer.ValueKind == JsonValueKind.Object)
            foreach (var n in normalizer.GetProperty("normalizers").EnumerateArray())
                if (n.GetProperty("type").GetString() == "Precompiled")
                    charsMap = new PrecompiledCharsMap(Convert.FromBase64String(n.GetProperty("precompiled_charsmap").GetString()!));

        var padding = root.GetProperty("padding");
        var padId = padding.GetProperty("pad_id").GetInt32();
        var length = padding.GetProperty("strategy").GetProperty("Fixed").GetInt32();
        var eosId = pieces.TryGetValue("</s>", out var eos) ? eos.Item1 : throw new NotSupportedException("The tokenizer has no </s> token.");
        return new SiglipTokenizer(pieces, unkId, minScore, eosId, padId, length, charsMap);
    }

    /// <summary>ContextLength ids: the text's pieces, "&lt;/s&gt;", then padding.</summary>
    public long[] Encode(string text)
    {
        var ids = new List<long>(ContextLength);
        foreach (var word in PreTokenize(Normalize(Canonicalize(text))))
            foreach (var pieceId in Viterbi(word))
            {
                if (ids.Count == ContextLength - 1) break;
                ids.Add(pieceId);
            }
        ids.Add(_eosId);
        while (ids.Count < ContextLength) ids.Add(_padId);
        return ids.ToArray();
    }

    // ----- Steps -----

    /// <summary>open_clip canonicalize_text: "_" to space, string.punctuation removed, lower case, whitespace collapsed.</summary>
    public static string Canonicalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Replace('_', ' '))
            if (!(c is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(c))) sb.Append(c);   // Python's string.punctuation
        var lower = sb.ToString().ToLowerInvariant();
        return string.Join(' ', lower.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();

    private string Normalize(string text) => Spaces().Replace(_charsMap?.Normalize(text) ?? text, " ");

    /// <summary>Metaspace with prepend "always" and split: "▁navy▁hooded" -> "▁navy", "▁hooded".</summary>
    private static IEnumerable<string> PreTokenize(string text)
    {
        if (text.Length == 0) yield break;
        var s = text.Replace(' ', Space);
        if (s[0] != Space) s = Space + s;
        var start = 0;
        for (var i = 1; i <= s.Length; i++)
            if (i == s.Length || s[i] == Space)
            {
                yield return s[start..i];
                start = i;
            }
    }

    /// <summary>Best split of one word by summed piece scores; characters no piece covers become one fused unk.</summary>
    private List<int> Viterbi(string word)
    {
        // Positions are text elements' UTF-16 offsets; work in chars but never split a surrogate pair.
        var n = word.Length;
        var best = new double[n + 1];
        var from = new int[n + 1];
        var pieceAt = new int[n + 1];
        Array.Fill(best, double.NegativeInfinity);
        best[0] = 0;
        for (var start = 0; start < n; start++)
        {
            if (double.IsNegativeInfinity(best[start])) continue;
            var charLen = char.IsHighSurrogate(word[start]) && start + 1 < n ? 2 : 1;
            var single = false;
            for (var len = 1; len <= _maxPieceLength && start + len <= n; len++)
            {
                if (!_pieces.TryGetValue(word.Substring(start, len), out var p)) continue;
                var score = best[start] + p.Score;
                if (score > best[start + len])
                {
                    best[start + len] = score;
                    from[start + len] = start;
                    pieceAt[start + len] = p.Id;
                }
                if (len == charLen) single = true;
            }
            if (!single)
            {
                var score = best[start] + _unkScore;
                if (score > best[start + charLen])
                {
                    best[start + charLen] = score;
                    from[start + charLen] = start;
                    pieceAt[start + charLen] = _unkId;
                }
            }
        }

        var ids = new List<int>();
        for (var end = n; end > 0; end = from[end]) ids.Add(pieceAt[end]);
        ids.Reverse();
        // fuse_unk: consecutive unknowns are one token.
        var fused = new List<int>(ids.Count);
        foreach (var id in ids)
            if (!(id == _unkId && fused.Count > 0 && fused[^1] == _unkId)) fused.Add(id);
        return fused;
    }

    /// <summary>
    /// SentencePiece's precompiled normalisation map: a darts-clone double-array trie over UTF-8 bytes whose values point
    /// into a block of zero-terminated replacement strings. Applied per grapheme (whole grapheme first when under 6
    /// bytes, else per character), like the Rust spm_precompiled crate used by Hugging Face tokenizers.
    /// </summary>
    private sealed class PrecompiledCharsMap
    {
        private readonly uint[] _trie;
        private readonly byte[] _normalized;

        public PrecompiledCharsMap(byte[] data)
        {
            var trieSize = (int)BitConverter.ToUInt32(data, 0);
            _trie = new uint[trieSize / 4];
            Buffer.BlockCopy(data, 4, _trie, 0, trieSize);
            _normalized = data[(4 + trieSize)..];
        }

        public string Normalize(string text)
        {
            var sb = new StringBuilder(text.Length);
            var e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
            {
                var grapheme = (string)e.Current;
                if (Encoding.UTF8.GetByteCount(grapheme) < 6 && Transform(grapheme) is { } whole)
                {
                    sb.Append(whole);
                    continue;
                }
                for (var i = 0; i < grapheme.Length; i++)
                {
                    var len = char.IsHighSurrogate(grapheme[i]) && i + 1 < grapheme.Length ? 2 : 1;
                    var part = grapheme.Substring(i, len);
                    sb.Append(Transform(part) ?? part);
                    i += len - 1;
                }
            }
            return sb.ToString();
        }

        private string? Transform(string chunk)
        {
            if (FirstPrefixValue(Encoding.UTF8.GetBytes(chunk)) is not { } index) return null;
            var end = index;
            while (end < _normalized.Length && _normalized[end] != 0) end++;
            return Encoding.UTF8.GetString(_normalized, index, end - index);
        }

        /// <summary>The value of the shortest key in the trie that is a prefix of <paramref name="key"/> (darts-clone commonPrefixSearch, first result).</summary>
        private int? FirstPrefixValue(byte[] key)
        {
            static uint Offset(uint unit) => (unit >> 10) << (int)((unit & (1u << 9)) >> 6);
            static bool HasLeaf(uint unit) => ((unit >> 8) & 1) == 1;
            static uint Label(uint unit) => unit & ((1u << 31) | 0xFF);
            static int Value(uint unit) => (int)(unit & ((1u << 31) - 1));

            uint node = 0;
            node ^= Offset(_trie[0]);
            foreach (var c in key)
            {
                node ^= c;
                if (node >= _trie.Length) return null;
                var unit = _trie[node];
                if (Label(unit) != c) return null;
                node ^= Offset(unit);
                if (HasLeaf(unit)) return Value(_trie[node]);
            }
            return null;
        }
    }
}
