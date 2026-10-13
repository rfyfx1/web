namespace Rahbar.Core.Language;

public enum TextScript { Unknown, Latin, Arabic, Devanagari, Other }

/// <summary>Works out which writing system a piece of text mostly uses.</summary>
public static class ScriptDetector
{
    public static TextScript Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TextScript.Unknown;

        int latin = 0, arabic = 0, devanagari = 0, other = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch)) continue;
            if (ch <= 'z' && ch >= 'A' || ch is >= 'À' and <= 'ɏ') latin++;
            else if (ch is >= '؀' and <= 'ۿ' || ch is >= 'ݐ' and <= 'ݿ'
                     || ch is >= 'ﭐ' and <= '﷿' || ch is >= 'ﹰ' and <= '﻿') arabic++;
            else if (ch is >= 'ऀ' and <= 'ॿ') devanagari++;
            else other++;
        }

        var max = Math.Max(Math.Max(latin, arabic), Math.Max(devanagari, other));
        if (max == 0) return TextScript.Unknown;
        if (max == arabic) return TextScript.Arabic;
        if (max == devanagari) return TextScript.Devanagari;
        if (max == latin) return TextScript.Latin;
        return TextScript.Other;
    }
}
