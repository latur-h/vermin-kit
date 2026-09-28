namespace VerminKit;

static class KitLook
{
    public static Font Ui { get; } = new("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
    public static Font Notes { get; } = new("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Point);
    public static Font Button { get; } = new("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
    public static Font LoadoutName { get; } = new("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Point);
    public static Font Overlay { get; } = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    public static Font Caption { get; } = new("Georgia", 13f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font NotesCaption { get; } = new("Georgia", 12f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font PickerCaption { get; } = new("Georgia", 10f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font CardTitle { get; } = new("Georgia", 12f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font CardPower { get; } = new("Georgia", 20f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font Level { get; } = new("Georgia", 11f, FontStyle.Bold, GraphicsUnit.Point);

    public static Color Ink { get; } = Color.FromArgb(236, 226, 210);
    public static Color Hint { get; } = Color.FromArgb(186, 170, 148);
    public static Color Paper { get; } = Color.FromArgb(28, 18, 12);
    public static Color Field { get; } = Color.FromArgb(22, 14, 10);
    public static Color Frame { get; } = Color.FromArgb(168, 118, 48);
    public static Color CardInk { get; } = Color.FromArgb(214, 64, 52);
    public static Color Shade { get; } = Color.FromArgb(18, 12, 8);

    public static Color ButtonIdle { get; } = Color.FromArgb(32, 22, 16);
    public static Color ButtonHot { get; } = Color.FromArgb(58, 38, 22);
    public static Color ButtonChosen { get; } = Color.FromArgb(92, 48, 18);
    public static Color ButtonBorder { get; } = Color.FromArgb(128, 92, 48);
    public static Color ButtonChosenBorder { get; } = Color.FromArgb(214, 154, 58);
    public static Color ButtonDim { get; } = Color.FromArgb(120, 110, 100);

    public static Color CardTitleColor { get; } = Color.FromArgb(196, 48, 40);
    public static Color CardSubtitle { get; } = Color.FromArgb(236, 232, 226);
    public static Color CardPowerColor { get; } = Color.FromArgb(245, 242, 236);
    public static Color CardLabel { get; } = Color.FromArgb(168, 160, 150);
    public static Color CardProperty { get; } = Color.FromArgb(176, 198, 232);
    public static Color CardPlaceholder { get; } = Color.FromArgb(128, 116, 104);
    public static Color CardTrait { get; } = Color.FromArgb(214, 122, 48);
    public static Color CardBody { get; } = Color.FromArgb(214, 208, 198);
    public static Color CardKeyword { get; } = Color.FromArgb(96, 176, 72);
    public static Color CardBorder { get; } = Color.FromArgb(118, 74, 38);
    public static Color CardHover { get; } = Color.FromArgb(48, 255, 220, 170);
    public static Color CardShade { get; } = Color.FromArgb(168, 12, 6, 4);

    public static Color TalentIdleBorder { get; } = Color.FromArgb(92, 68, 36);
    public static Color TalentSelectedBorder { get; } = Color.FromArgb(214, 146, 48);
    public static Color TalentIdleFill { get; } = Color.FromArgb(14, 10, 8);
    public static Color TalentSelectedFill { get; } = Color.FromArgb(42, 22, 10);
    public static Color TalentDetail { get; } = Color.FromArgb(176, 168, 156);
    public static Color LevelRing { get; } = Color.FromArgb(168, 118, 48);
    public static Color LevelInner { get; } = Color.FromArgb(28, 16, 8);
    public static Color LevelNumber { get; } = Color.FromArgb(232, 196, 120);

    public static Color ScrollTrack { get; } = Color.FromArgb(28, 18, 12);
    public static Color ScrollThumb { get; } = Color.FromArgb(168, 118, 48);
    public static Color ScrollThumbHot { get; } = Color.FromArgb(214, 164, 78);
    public static Color PickSelected { get; } = Color.FromArgb(84, 42, 16);
    public static Color PickSelectedText { get; } = Color.FromArgb(255, 228, 176);
    public static Color TalentCaption { get; } = Color.FromArgb(196, 112, 42);
    public static Color Link { get; } = Color.FromArgb(126, 196, 255);

    public static Color StatusOn { get; } = Color.FromArgb(190, 230, 190);
    public static Color StatusOff { get; } = Color.FromArgb(255, 160, 140);
    public static Color SettingsHint { get; } = Color.FromArgb(180, 180, 180);
    public static Color SettingsError { get; } = Color.FromArgb(255, 150, 130);
    public static Color Chrome { get; } = Color.FromArgb(28, 28, 28);
    public static Color ChromeText { get; } = Color.FromArgb(235, 235, 235);
}
