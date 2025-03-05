using System;
using System.Collections.Frozen;
using System.Linq;

namespace DataCollection;

static class Constants
{
    public const string ACTUAL_CURATED_DOIS = """
        10.1145/3597503.3639076
        10.1145/3597503.3639087
        10.1145/3597503.3639229
        10.1145/3597503.3623343
        10.1145/3597503.3639210
        10.1145/3597503.3623300
        10.1145/3597503.3639165
        10.1145/3597503.3623344
        10.1145/3597503.3639112
        10.1145/3597503.3623315
        10.48550/arXiv.2401.05112
        10.1145/3597503.3639172
        10.1145/3597503.3639158
        10.1145/3597503.3639118
        10.1145/3597503.3639200
        10.1145/3597503.3623321
        10.1145/3597503.3639117
        10.1145/3597503.3639132
        10.1145/3597503.3639121
        10.1145/3597503.3639102
        """;

    public static string[] ACTUAL_CURATED_DOIS_ARRAY = ACTUAL_CURATED_DOIS
        .Split("\n", StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .ToArray();

    public static FrozenDictionary<string, bool> ACTUAL_CURATED_DOIS_DICT =
        ACTUAL_CURATED_DOIS_ARRAY.ToDictionary(p => p, _ => true).ToFrozenDictionary();
}
