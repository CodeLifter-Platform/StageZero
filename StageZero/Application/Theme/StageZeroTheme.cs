using MudBlazor;

namespace StageZero.Application.Theme;

// ═══════════════════════════════════════════════════════════════
// TOKEN DICTIONARY
// ═══════════════════════════════════════════════════════════════
//
// StageZero's port of the CodeLifter token dictionary
// (Platform-Standards/design/tokens.md). Values are copied verbatim from
// that table — change tokens.md first, then this file. Views never name a
// hex value: they use MudBlazor Color.* / --mud-palette-* variables, which
// this file feeds.
//
// Accent family: StageZero teal, locked in
// Platform-Standards/design/app-accents.md (dark #14b8a6, light #0f766e).
// Light canvas is the harness "light-warm" scope.

public sealed record StageZeroTokens(
    string Bg, string Titlebar, string Panel, string Card, string Elev,
    string Bd, string Bd2, string Bd3,
    string Tx, string Tx2, string Tx3,
    string Acc, string Acc2, string AccTx, string OnAcc,
    string Add, string Rem, string Warn)
{
    public static readonly StageZeroTokens Dark = new(
        Bg: "#0a0a0f", Titlebar: "#12121a", Panel: "#0e0e15", Card: "#12121a", Elev: "#1a1a28",
        Bd: "#1e1e2e", Bd2: "#1a1a28", Bd3: "#2a2a40",
        Tx: "#e8e8ed", Tx2: "#8888a0", Tx3: "#66667e",
        Acc: "#14b8a6", Acc2: "#2dd4bf", AccTx: "#2dd4bf", OnAcc: "#0a0a0f",
        Add: "#34d399", Rem: "#f2555a", Warn: "#f5b544");

    public static readonly StageZeroTokens Light = new(
        Bg: "#f6f4ee", Titlebar: "#f1ece1", Panel: "#faf8f2", Card: "#fffefb", Elev: "#fffefb",
        Bd: "#e8e2d6", Bd2: "#eee9de", Bd3: "#dcd4c4",
        Tx: "#1c1a17", Tx2: "#6b6459", Tx3: "#8f887b",
        Acc: "#0f766e", Acc2: "#0f766e", AccTx: "#115e59", OnAcc: "#fffefb",
        Add: "#0f9d6b", Rem: "#d1373d", Warn: "#b5790a");
}

// ═══════════════════════════════════════════════════════════════
// MUDBLAZOR THEME
// ═══════════════════════════════════════════════════════════════
//
// Maps the dictionary onto MudBlazor's palette slots. MudBlazor's extra
// color slots are pinned to existing roles rather than given new hues:
// Secondary/Tertiary/Dark are neutral (Tx2), Info is the accent.

public static class StageZeroTheme
{
    public const string SansFont = "Inter";
    public const string MonoFont = "JetBrains Mono";

    public static readonly MudTheme Theme = new()
    {
        PaletteDark = Apply(new PaletteDark(), StageZeroTokens.Dark),
        PaletteLight = Apply(new PaletteLight(), StageZeroTokens.Light),
        Typography = new Typography
        {
            Default = new Default { FontFamily = [SansFont, "system-ui", "sans-serif"] },
        },
    };

    private static T Apply<T>(T p, StageZeroTokens t) where T : Palette
    {
        p.Primary = t.Acc;
        p.PrimaryContrastText = t.OnAcc;
        p.Secondary = t.Tx2;
        p.SecondaryContrastText = t.Bg;
        p.Tertiary = t.Tx2;
        p.TertiaryContrastText = t.Bg;
        p.Info = t.Acc;
        p.InfoContrastText = t.OnAcc;
        p.Success = t.Add;
        p.SuccessContrastText = t.Bg;
        p.Warning = t.Warn;
        p.WarningContrastText = t.Bg;
        p.Error = t.Rem;
        p.ErrorContrastText = t.Bg;
        p.Dark = t.Tx2;
        p.DarkContrastText = t.Bg;

        p.Background = t.Bg;
        p.BackgroundGray = t.Panel;
        p.Surface = t.Card;
        p.AppbarBackground = t.Titlebar;
        p.AppbarText = t.Tx;
        p.DrawerBackground = t.Panel;
        p.DrawerText = t.Tx2;
        p.DrawerIcon = t.Tx2;

        p.TextPrimary = t.Tx;
        p.TextSecondary = t.Tx2;
        p.TextDisabled = t.Tx3;
        p.ActionDefault = t.Tx2;
        p.ActionDisabled = t.Tx3;
        p.ActionDisabledBackground = t.Bd2;

        p.LinesDefault = t.Bd;
        p.LinesInputs = t.Bd3;
        p.TableLines = t.Bd;
        p.TableHover = t.Bd2;
        p.TableStriped = t.Panel;
        p.Divider = t.Bd;
        p.DividerLight = t.Bd2;
        return p;
    }
}
