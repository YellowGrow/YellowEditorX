namespace YellowEditorX.Core;

public static class CardTypes
{
    public const long Monster = 0x1, Spell = 0x2, Trap = 0x4;
    public const long Normal = 0x10, Effect = 0x20, Fusion = 0x40, Ritual = 0x80;
    public const long Spirit = 0x200, Union = 0x400, Gemini = 0x800, Tuner = 0x1000;
    public const long Synchro = 0x2000, Token = 0x4000, QuickPlay = 0x10000, Continuous = 0x20000;
    public const long Equip = 0x40000, Field = 0x80000, Counter = 0x100000, Flip = 0x200000;
    public const long Toon = 0x400000, Xyz = 0x800000, Pendulum = 0x1000000, SpSummon = 0x2000000;
    public const long Link = 0x4000000;
    /// <summary>기프트 (custom): a spell sub-type.</summary>
    public const long Gift = 0x10000000;
    /// <summary>키 (custom). The unlocked card uses the next code with the locked card as alias, but has its own script.</summary>
    public const long Key = 0x8;
    /// <summary>키-해금됨 (custom): the unlocked card of a 키; it is always a 키 too.</summary>
    public const long Unlocked = 0x8000;

    public const long Access = 0x8000000, Hijack = 0x20000000, Pairing = 0x40000000;
    /// <summary>The orica kinds in the order the card list names them.</summary>
    public static readonly long[] CustomOrder = { Access, Pairing, Hijack, Gift };

    public const long MainTypes = Monster | Spell | Trap;
    public static readonly long[] SpellSubs = { QuickPlay, Continuous, Ritual, Equip, Field, Gift };
    public static readonly long[] TrapSubs = { Continuous, Counter };

    public static long MainOf(long type) =>
        (type & Monster) != 0 ? Monster : (type & Spell) != 0 ? Spell : (type & Trap) != 0 ? Trap : 0;

    /// <summary>Link marker bits laid out as the 3x3 grid (index 4 is the center).</summary>
    public static readonly long[] MarkerGrid = { 0x40, 0x80, 0x100, 0x8, 0, 0x20, 0x1, 0x2, 0x4 };
    public static readonly string[] MarkerArrows = { "↖", "↑", "↗", "←", "", "→", "↙", "↓", "↘" };

    public static int PopCount(long v) => System.Numerics.BitOperations.PopCount((ulong)v);
}
