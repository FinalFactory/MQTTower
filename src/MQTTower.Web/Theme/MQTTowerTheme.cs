using MudBlazor;

namespace MQTTower.Web.Theme;

public static class MQTTowerTheme
{
    /// <summary>Primary brand green (#0FF197) — MQTT “signal” accent.</summary>
    public const string PrimaryGreen = "#0FF197";

    /// <summary>Secondary cyan — icon signal arcs, charts, secondary highlights.</summary>
    public const string SecondaryCyan = "#00E5FF";

    /// <summary>Tower / structural neutral from brand icon.</summary>
    public const string TowerGray = "#6B7580";

    public static readonly MudTheme Instance = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = PrimaryGreen,
            Secondary = SecondaryCyan,
            Background = "#0D1117",
            Surface = "#161B22",
            AppbarBackground = "#12171E",
            DrawerBackground = "#12171E",
            DrawerText = "#E6EDF3",
            TextPrimary = "#E6EDF3",
            TextSecondary = "#8B949E",
            ActionDefault = "#8B949E",
            ActionDisabled = "#484F58",
            Divider = "#21262D",
            DividerLight = "#21262D",
            TableLines = "#21262D",
            LinesDefault = "#30363D",
            Dark = "#010409",
            Black = "#010409",
        },
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "260px",
            DrawerMiniWidthLeft = "56px",
        },
    };
}
