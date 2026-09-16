#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Extensions.Logging;
using Monocle;
using TowerFall;

namespace TF8PlayerFortRise;

[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.CreateRollcall))]
internal static class MainMenuRollcallPatch
{
    private static void Postfix(MainMenu __instance)
    {
        if (!TF8Runtime.IsEightPlayerFreeForAll)
            return;

        for (int playerIndex = TF8Runtime.VanillaPlayers; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            TFGame.Players[playerIndex] = false;
            TFGame.CoOpCrowns[playerIndex] = false;
            __instance.Add(new RollcallElement(playerIndex));
        }
    }
}

// Team Deathmatch reaches this screen after the eight-player rollcall.  The
// vanilla menu only creates four selectors and places them too far apart for
// eight rows.  Keep its TeamSelector input/tween behaviour, but instantiate
// all active slots in a compact vertical arrangement.
[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.CreateTeamSelect))]
internal static class MainMenuEightPlayerTeamSelectPatch
{
    private static bool Prefix(MainMenu __instance)
    {
        if (!TF8Runtime.HasExtendedPlayer)
            return true;

        TeamBanner blueBanner = new(new Vector2(64f, 76f), new Vector2(-40f, 50f), "teamA2x");
        TeamBanner redBanner = new(new Vector2(256f, 76f), new Vector2(360f, 50f), "teamB2x");
        __instance.Add<TeamBanner>(blueBanner, redBanner);
        __instance.Add(new ReadyBanner());

        for (int playerIndex = 0; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (!TFGame.Players[playerIndex])
                continue;

            // Eight labels fit between the team banners and the lower edge of
            // TowerFall's 320x240 logical canvas without overlapping.
            Vector2 position = new(160f, 108f + playerIndex * 16f);
            Vector2 tweenFrom = new(160f, position.Y - 10f);
            tweenFrom.X = playerIndex % 2 == 0 ? -40f : 360f;
            __instance.Add(new TeamSelector(position, tweenFrom, playerIndex));
        }

        __instance.ToStartSelected = null;
        __instance.BackState = MainMenu.MenuState.VersusOptions;
        __instance.TweenBGCameraToY(3);
        return false;
    }
}

[HarmonyPatch(typeof(RollcallElement), "get_MaxPlayers")]
internal static class RollcallMaximumPatch
{
    private static bool Prefix(ref int __result)
    {
        if (!TF8Runtime.IsEightPlayerFreeForAll)
            return true;

        __result = TF8Runtime.MaxPlayers;
        return false;
    }
}

[HarmonyPatch(typeof(RollcallElement), nameof(RollcallElement.GetPosition))]
internal static class RollcallPositionPatch
{
    private static bool Prefix(int playerIndex, ref Vector2 __result)
    {
        if (!TF8Runtime.IsEightPlayerFreeForAll)
            return true;

        __result = TF8RollcallLayout.GetPosition(playerIndex);
        return false;
    }
}

[HarmonyPatch(typeof(RollcallElement), nameof(RollcallElement.GetTweenSource))]
internal static class RollcallTweenSourcePatch
{
    private static bool Prefix(int playerIndex, ref Vector2 __result)
    {
        if (!TF8Runtime.IsEightPlayerFreeForAll)
            return true;

        Vector2 position = RollcallElement.GetPosition(playerIndex);
        // Wider Set brings the top row in from the left and the lower row in
        // from the right. The larger travel distance is purely visual and
        // does not change selection order or input ownership.
        __result = position + Vector2.UnitX * (playerIndex < 4 ? -420f : 420f);
        return false;
    }
}

[HarmonyPatch(typeof(RollcallElement), nameof(RollcallElement.Render))]
internal static class RollcallCompactControlsPatch
{
    private static readonly FieldInfo? PlayerIndexField =
        AccessTools.Field(typeof(RollcallElement), "playerIndex");

    private static bool Prefix(RollcallElement __instance)
    {
        TF8RollcallLayout.ApplyControlLayout(__instance);
        if (!TF8RollcallLayout.IsActive)
            return true;

        // The stock renderer reserves a tall card and draws its confirm icon
        // beneath it.  Render the existing components directly instead, then
        // place the controller indicator and P# label at the card's top-left.
        foreach (Component component in __instance.Components)
        {
            if (component.Visible)
                component.Render();
        }

        if (PlayerIndexField?.GetValue(__instance) is int playerIndex)
        {
            // Wider Set anchors the player label and controller details to the
            // upper-left corner of each 60x60 portrait.  Keeping that visual
            // treatment makes the eight-player grid immediately readable
            // without changing any of this mod's rollcall behaviour.
            Draw.OutlineTextCentered(
                TFGame.Font,
                "P" + (playerIndex + 1),
                __instance.Position + new Vector2(15f, -40f),
                ArcherData.GetColorA(playerIndex),
                Color.Black,
                2f);

            PlayerInput[]? playerInputs = TFGame.PlayerInputs;
            if (playerInputs is not null && playerIndex >= 0 && playerIndex < playerInputs.Length &&
                playerInputs[playerIndex] is PlayerInput input)
            {
                Color nameColor = ArcherData.Archers[__instance.CharacterIndex].ColorA;
                Draw.OutlineTextCentered(
                    TFGame.Font,
                    input.Name,
                    __instance.Position + new Vector2(-15f, -15f),
                    nameColor,
                    Color.Black);
            }
        }

        return false;
    }
}

[HarmonyPatch(typeof(ArcherPortrait), nameof(ArcherPortrait.Render))]
internal static class RollcallCompactPortraitPatch
{
    private static readonly FieldInfo? OffsetField = AccessTools.Field(typeof(ArcherPortrait), "offset");
    private static readonly FieldInfo? JoinedField = AccessTools.Field(typeof(ArcherPortrait), "joined");
    private static readonly FieldInfo? PlayerIndexField = AccessTools.Field(typeof(RollcallElement), "playerIndex");
    private static readonly FieldInfo? PortraitField = AccessTools.Field(typeof(ArcherPortrait), "portrait");
    private static readonly FieldInfo? PortraitAltField = AccessTools.Field(typeof(ArcherPortrait), "portraitAlt");
    private static readonly FieldInfo? FlipEaseField = AccessTools.Field(typeof(ArcherPortrait), "flipEase");
    private static readonly FieldInfo? GemField = AccessTools.Field(typeof(ArcherPortrait), "gem");
    private static readonly FieldInfo? WigglerField = AccessTools.Field(typeof(ArcherPortrait), "wiggler");
    private static readonly FieldInfo? GemWigglerField = AccessTools.Field(typeof(ArcherPortrait), "gemWiggler");
    private static readonly FieldInfo? LastMoveField = AccessTools.Field(typeof(ArcherPortrait), "lastMove");
    private static readonly FieldInfo? LastShakeField = AccessTools.Field(typeof(ArcherPortrait), "lastShake");

    private static bool Prefix(ArcherPortrait __instance)
    {
        if (!TF8RollcallLayout.IsActive || __instance.Entity is not RollcallElement element ||
            PlayerIndexField?.GetValue(element) is not int playerIndex ||
            OffsetField?.GetValue(__instance) is not Vector2 offset ||
            JoinedField?.GetValue(__instance) is not bool joined ||
            PortraitField?.GetValue(__instance) is not Image portrait ||
            PortraitAltField?.GetValue(__instance) is not Image portraitAlt ||
            FlipEaseField?.GetValue(__instance) is not float flipEase ||
            GemField?.GetValue(__instance) is not Sprite<string> gem ||
            WigglerField?.GetValue(__instance) is not Wiggler wiggler ||
            GemWigglerField?.GetValue(__instance) is not Wiggler gemWiggler ||
            LastMoveField?.GetValue(__instance) is not int lastMove ||
            LastShakeField?.GetValue(__instance) is not Vector2 lastShake)
        {
            return true;
        }

        // An empty slot has no usable controller or keyboard input. Its
        // character image remains visible so the whole 4x2 selection grid is
        // readable, but it has no coloured frame or gem. The native "no
        // control" icon continues to communicate why it cannot be selected.
        PlayerInput[]? inputs = TFGame.PlayerInputs;
        bool hasAvailableInput = inputs is not null &&
                                 playerIndex >= 0 &&
                                 playerIndex < inputs.Length &&
                                 inputs[playerIndex] is not null;

        // The native selection portraits are 60x120.  Draw the face portion
        // of that existing art as a 60x60 crop, rather than replacing it with
        // the different portrait used on the results screen. This preserves
        // joined/not-joined states and each character's alternate selection.
        Vector2 center = __instance.Entity.Position + offset + lastShake;
        float cardSize = TF8RollcallLayout.CardSize;
        float halfCard = cardSize / 2f;
        // These are player-slot colours, not archer colours. They deliberately
        // match the P1-P8 labels in the round/death summary.
        Color border = ArcherData.GetColorA(playerIndex);
        // Match Wider Set's muted unjoined portraits. Joined portraits keep
        // their original bright colour and the TF8 player-colour frame.
        Color portraitColor = joined ? Color.White : new Color(0.55f, 0.55f, 0.55f);
        Image activePortrait = portrait;
        float flipScaleX = 1f;
        if (!joined)
        {
            if (flipEase < 0.5f)
            {
                activePortrait = portraitAlt;
                flipScaleX = MathHelper.Lerp(1f, 0f, flipEase * 2f);
            }
            else
            {
                flipScaleX = MathHelper.Lerp(0f, 1f, (flipEase - 0.5f) * 2f);
            }
        }

        Rectangle faceCrop = GetFaceCrop(activePortrait.ClipRect, __instance.CharacterIndex);
        // Wider Set uses the native 60x60 source crop at its original scale.
        // The four-pixel player-colour frame remains visible around it.
        float faceScale = cardSize / faceCrop.Width;
        Vector2 faceScaleVector = new(
            faceScale * (1f + wiggler.Value * 0.05f) * flipScaleX,
            faceScale * (1f - wiggler.Value * 0.05f));

        if (hasAvailableInput)
        {
            Draw.Rect(center.X - halfCard - 2f, center.Y - halfCard - 2f, cardSize + 4f, cardSize + 4f, border);
            Draw.Rect(center.X - halfCard, center.Y - halfCard, cardSize, cardSize, Color.Black);
        }
        Draw.SpriteBatch.Draw(
            activePortrait.Texture.Texture2D,
            center.Floor(),
            faceCrop,
            portraitColor,
            0f,
            new Vector2(faceCrop.Width / 2f, faceCrop.Height / 2f),
            faceScaleVector,
            Microsoft.Xna.Framework.Graphics.SpriteEffects.None,
            0f);

        gem.Visible = hasAvailableInput;
        if (!hasAvailableInput)
            return false;

        gem.Position = offset + lastShake + Vector2.UnitY * 30f;
        gem.Scale = Vector2.One * (joined
            ? 1.5f + 0.2f * gemWiggler.Value
            : 1f + 0.2f * gemWiggler.Value);
        gem.Rotation = (joined ? 45f : 20f) * lastMove * (MathF.PI / 180f) * gemWiggler.Value;
        gem.DrawOutline();
        gem.Render();

        return false;
    }

    private static Rectangle GetFaceCrop(Rectangle portraitRect, int characterIndex)
    {
        // Wider Set's compact portrait logic crops from Y=10 and has one
        // built-in correction for archer index 6 (an additional 50px down).
        // The stock data in this installation does not define per-archer
        // Wide*Offset values, so these are the complete offsets it actually
        // supplies. Clamp them for third-party square portraits.
        int size = Math.Min(portraitRect.Width, portraitRect.Height);
        int sourceX = portraitRect.X;
        int sourceY = portraitRect.Y + 10 + (characterIndex == 6 ? 50 : 0);
        int maxY = portraitRect.Bottom - size;
        sourceY = Math.Clamp(sourceY, portraitRect.Y, Math.Max(portraitRect.Y, maxY));
        return new Rectangle(sourceX, sourceY, size, size);
    }
}

internal static class TF8RollcallLayout
{
    // These are Wider Set's compact rollcall measurements, kept inside
    // TowerFall's standard 320x240 menu instead of enabling its wide world.
    internal const float CardSize = 60f;

    private static readonly FieldInfo? ControlIconPositionField =
        AccessTools.Field(typeof(RollcallElement), "ControlIconPos");
    private static readonly FieldInfo? RightArrowField =
        AccessTools.Field(typeof(RollcallElement), "rightArrow");
    private static readonly FieldInfo? LeftArrowField =
        AccessTools.Field(typeof(RollcallElement), "leftArrow");
    private static readonly FieldInfo? ControlIconField =
        AccessTools.Field(typeof(RollcallElement), "controlIcon");

    internal static bool IsActive => TF8Runtime.IsEightPlayerFreeForAll;

    internal static Vector2 GetPosition(int playerIndex)
    {
        int column = playerIndex % 4;
        int row = playerIndex / 4;
        return new Vector2(55f + column * 70f, 75f + row * 90f);
    }

    internal static void ApplyControlLayout(RollcallElement element)
    {
        bool compact = IsActive;
        Vector2 controlPosition = compact ? new Vector2(-15f, -30f) : new Vector2(0f, 90f);
        ControlIconPositionField?.SetValue(null, controlPosition);
        if (ControlIconField?.GetValue(element) is Image controlIcon)
            controlIcon.Position = controlPosition;

        // Match Wider Set's visual grouping: the arrows sit on either side of
        // the gem beneath the portrait rather than beside the portrait itself.
        float arrowY = compact ? 30f : 60f;
        if (RightArrowField?.GetValue(element) is Image rightArrow)
        {
            rightArrow.Y = arrowY;
            if (compact)
                rightArrow.X = 20f;
        }
        if (LeftArrowField?.GetValue(element) is Image leftArrow)
        {
            leftArrow.Y = arrowY;
            if (compact)
                leftArrow.X = -19f;
        }
    }
}

// RollcallElement sets a gamepad light colour unconditionally, including P5-P8.
// Without the external launcher FNA's SDL backend owns only four physical slots,
// so calling its light-bar API for an extended slot throws before the menu opens.
// LEDs are cosmetic; the launcher path still gives all input functionality.
[HarmonyPatch(typeof(Microsoft.Xna.Framework.Input.GamePad), "SetLightBarEXT")]
internal static class TF8ExtendedLightBarPatch
{
    private static bool Prefix(PlayerIndex playerIndex)
    {
        return !TF8Runtime.Enabled || (int)playerIndex < TF8Runtime.VanillaPlayers;
    }
}

[HarmonyPatch(typeof(RollcallElement), "NotJoinedUpdate")]
internal static class RollcallBackPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(MatchSettings), "get_PlayerLimit")]
internal static class VersusPlayerLimitPatch
{
    private static bool Prefix(MatchSettings __instance, ref int __result)
    {
        if (!TF8Runtime.Enabled || __instance.SoloMode)
            return true;

        __result = TF8Runtime.MaxPlayers;
        return false;
    }
}

// MatchTeams is a sealed-size vanilla object with four public fields.  A
// ConditionalWeakTable lets a FortRise mod attach slots P5-P8 to each existing
// MatchTeams instance without replacing the game's type or serialised state.
internal sealed class TF8ExtraTeamSlots
{
    internal TF8ExtraTeamSlots(Allegiance start)
    {
        Values = new[] { start, start, start, start };
    }

    internal Allegiance[] Values { get; }
}

internal static class TF8Teams
{
    private static readonly ConditionalWeakTable<MatchTeams, TF8ExtraTeamSlots> ExtraSlots = new();

    internal static void Initialize(MatchTeams teams, Allegiance start)
    {
        ExtraSlots.Remove(teams);
        ExtraSlots.Add(teams, new TF8ExtraTeamSlots(start));
    }

    internal static Allegiance Get(MatchTeams teams, int playerIndex)
    {
        return ExtraSlots.GetValue(teams, _ => new TF8ExtraTeamSlots(Allegiance.Neutral))
            .Values[playerIndex - TF8Runtime.VanillaPlayers];
    }

    internal static void Set(MatchTeams teams, int playerIndex, Allegiance allegiance)
    {
        ExtraSlots.GetValue(teams, _ => new TF8ExtraTeamSlots(Allegiance.Neutral))
            .Values[playerIndex - TF8Runtime.VanillaPlayers] = allegiance;
    }

    internal static bool IsExtraSlot(int playerIndex) =>
        playerIndex >= TF8Runtime.VanillaPlayers && playerIndex < TF8Runtime.MaxPlayers;
}

[HarmonyPatch(typeof(MatchTeams), MethodType.Constructor, new[] { typeof(Allegiance) })]
internal static class TF8MatchTeamsConstructorPatch
{
    private static void Postfix(MatchTeams __instance, Allegiance start)
    {
        if (TF8Runtime.Enabled)
            TF8Teams.Initialize(__instance, start);
    }
}

[HarmonyPatch(typeof(MatchTeams), "get_Item")]
internal static class TF8MatchTeamsIndexerGetPatch
{
    private static bool Prefix(MatchTeams __instance, int index, ref Allegiance __result)
    {
        if (!TF8Runtime.Enabled || !TF8Teams.IsExtraSlot(index))
            return true;

        __result = TF8Teams.Get(__instance, index);
        return false;
    }
}

[HarmonyPatch(typeof(MatchTeams), "set_Item")]
internal static class TF8MatchTeamsIndexerSetPatch
{
    private static bool Prefix(MatchTeams __instance, int index, Allegiance value)
    {
        if (!TF8Runtime.Enabled || !TF8Teams.IsExtraSlot(index))
            return true;

        TF8Teams.Set(__instance, index, value);
        return false;
    }
}

[HarmonyPatch(typeof(MatchTeams), nameof(MatchTeams.GetAmountOfPlayersOfAllegiance))]
internal static class TF8MatchTeamsAmountPatch
{
    private static bool Prefix(MatchTeams __instance, Allegiance allegiance, ref int __result)
    {
        if (!TF8Runtime.Enabled)
            return true;

        int amount = 0;
        for (int playerIndex = 0; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (TFGame.Players[playerIndex] && __instance[playerIndex] == allegiance)
                amount++;
        }

        __result = amount;
        return false;
    }
}

[HarmonyPatch(typeof(MatchTeams), nameof(MatchTeams.GetPlayersOfAllegiance))]
internal static class TF8MatchTeamsPlayersPatch
{
    private static bool Prefix(MatchTeams __instance, Allegiance allegiance, ref List<int> __result)
    {
        if (!TF8Runtime.Enabled)
            return true;

        List<int> players = [];
        for (int playerIndex = 0; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (TFGame.Players[playerIndex] && __instance[playerIndex] == allegiance)
                players.Add(playerIndex);
        }

        __result = players;
        return false;
    }
}

[HarmonyPatch(typeof(MatchTeams), "get_ProperlyAssigned")]
internal static class TF8MatchTeamsProperlyAssignedPatch
{
    private static bool Prefix(MatchTeams __instance, ref bool __result)
    {
        if (!TF8Runtime.Enabled)
            return true;

        bool blue = false;
        bool red = false;
        for (int playerIndex = 0; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (!TFGame.Players[playerIndex])
                continue;

            Allegiance team = __instance[playerIndex];
            if (team == Allegiance.Neutral)
            {
                __result = false;
                return false;
            }

            blue |= team == Allegiance.Blue;
            red |= team == Allegiance.Red;
        }

        __result = blue && red;
        return false;
    }
}

[HarmonyPatch(typeof(MatchTeams), "get_HasEvenTeams")]
internal static class TF8MatchTeamsEvenPatch
{
    private static bool Prefix(MatchTeams __instance, ref bool __result)
    {
        if (!TF8Runtime.Enabled)
            return true;

        int blue = __instance.GetAmountOfPlayersOfAllegiance(Allegiance.Blue);
        int red = __instance.GetAmountOfPlayersOfAllegiance(Allegiance.Red);
        __result = blue == red;
        return false;
    }
}

[HarmonyPatch(typeof(MatchSettings), "get_CanStartWithTeams")]
internal static class TF8CanStartWithTeamsPatch
{
    private static bool Prefix(MatchSettings __instance, ref bool __result)
    {
        if (!TF8Runtime.Enabled || __instance.Teams is null)
            return true;

        __result = __instance.Teams.ProperlyAssigned;
        return false;
    }
}

[HarmonyPatch(typeof(MatchSettings), nameof(MatchSettings.GetMaxTeamSize))]
internal static class TF8MaxTeamSizePatch
{
    private static bool Prefix(MatchSettings __instance, ref int __result)
    {
        if (!TF8Runtime.Enabled || !__instance.TeamMode || __instance.Teams is null)
            return true;

        int blue = __instance.Teams.GetAmountOfPlayersOfAllegiance(Allegiance.Blue);
        int red = __instance.Teams.GetAmountOfPlayersOfAllegiance(Allegiance.Red);
        __result = Math.Max(blue, red);
        return false;
    }
}

[HarmonyPatch(typeof(MatchSettings), nameof(MatchSettings.GetPlayerTeamSize))]
internal static class TF8PlayerTeamSizePatch
{
    private static bool Prefix(MatchSettings __instance, int playerIndex, ref int __result)
    {
        if (!TF8Runtime.Enabled || !__instance.TeamMode || __instance.Teams is null)
            return true;

        __result = __instance.Teams.GetAmountOfPlayersOfAllegiance(__instance.Teams[playerIndex]);
        return false;
    }
}

[HarmonyPatch(typeof(MatchSettings), nameof(MatchSettings.GetTeamMismatch))]
internal static class TF8TeamMismatchPatch
{
    private static bool Prefix(MatchSettings __instance, Allegiance team, ref int __result)
    {
        if (!TF8Runtime.Enabled || __instance.Teams is null)
            return true;

        int selected = __instance.Teams.GetAmountOfPlayersOfAllegiance(team);
        int opposing = 0;
        for (int playerIndex = 0; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (TFGame.Players[playerIndex] && __instance.Teams[playerIndex] != team)
                opposing++;
        }

        __result = selected - opposing;
        return false;
    }
}

[HarmonyPatch(typeof(Session), MethodType.Constructor, new Type[] { typeof(MatchSettings) })]
internal static class SessionConstructorPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Session), nameof(Session.EndRound))]
internal static class SessionEndRoundPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(VersusLevelSystem), "GenLevels")]
internal static class VersusLevelGenerationPatch
{
    private static readonly FieldInfo? LevelsField =
        AccessTools.Field(typeof(VersusLevelSystem), "levels");
    private static readonly MethodInfo? ThemeSetter =
        AccessTools.PropertySetter(typeof(LevelSystem), nameof(LevelSystem.Theme));

    private static bool Prefix(VersusLevelSystem __instance, MatchSettings matchSettings)
    {
        if (!TF8Runtime.Enabled)
            return true;

        VersusTowerData? selectedTower = __instance.VersusTowerData;
        if (selectedTower is null)
            return true;

        bool eightPlayerTeams = matchSettings.TeamMode && TF8Runtime.HasExtendedPlayer;

        // VersusLevelData only records the smaller of TeamSpawnA/B.  The
        // shipped eight-player maps are FFA-first and commonly have 0 or 3
        // dedicated team spawns, while all compatible maps have eight
        // PlayerSpawn entries.  Select by that real capacity here; the team
        // spawn patch below turns those positions into safe team starts.
        if (eightPlayerTeams && LevelsField is not null)
        {
            List<string> selectedCompatible = GetCompatiblePaths(selectedTower, teamMode: true);
            if (selectedCompatible.Count > 0)
            {
                LevelsField.SetValue(__instance, selectedCompatible);
                selectedCompatible.Shuffle(new Random());
                return false;
            }
        }

        // Let FortRise keep its normal shuffle and FixedFirst behavior whenever
        // the selected tower has a map compatible with the current match.
        List<string> compatibleLevels = selectedTower.GetLevels(matchSettings);
        if (compatibleLevels.Count > 0)
            return true;

        List<string> fallback = FindEightPlayerLevels(matchSettings, selectedTower, out VersusTowerData? sourceTower);
        if (fallback.Count == 0 || sourceTower is null || LevelsField is null)
        {
            TF8Runtime.Logger?.LogError(
                "TF8PlayerFortRise found no compatible Versus levels for tower {tower} and {players} players.",
                selectedTower.LevelID, TFGame.PlayerAmount);
            return true;
        }

        // A custom tower can be registered with a different id while using the
        // same art.  If we fall back to such a tower, also use the exact theme
        // that belongs to its XML maps.  Keeping the previous theme here is
        // what produced the red X / missing-tile placeholders in six-to-eight
        // player maps.
        if (!ReferenceEquals(sourceTower.Theme, selectedTower.Theme))
        {
            if (ThemeSetter is null)
            {
                TF8Runtime.Logger?.LogError(
                    "TF8PlayerFortRise cannot switch to the compatible theme for fallback tower {tower}.",
                    sourceTower.LevelID);
                return true;
            }

            ThemeSetter.Invoke(__instance, new object[] { sourceTower.Theme });
        }

        LevelsField.SetValue(__instance, fallback);
        TF8Runtime.Logger?.LogWarning(
            "Selected tower {tower} has no map for {players} players; using {count} compatible maps from {fallbackTower}.",
            selectedTower.LevelID, TFGame.PlayerAmount, fallback.Count, sourceTower.LevelID);

        // The original method would now shuffle the list and apply FixedFirst.
        // Re-running it would recreate the empty list, so this prefix performs
        // the harmless part here and skips the original implementation.
        fallback.Shuffle(new Random());
        return false;
    }

    private static List<string> FindEightPlayerLevels(
        MatchSettings matchSettings,
        VersusTowerData selectedTower,
        out VersusTowerData? sourceTower)
    {
        sourceTower = null;
        IEnumerable<VersusTowerData> towers = EnumerateTowers(selectedTower);
        bool teamMode = matchSettings.Mode == Modes.TeamDeathmatch;

        // First favor the exact theme id.  Then allow themes that genuinely
        // share all visual assets; this covers FortRise's cloned/modded tower
        // metadata without mixing tilesets.
        foreach (bool exactThemeOnly in new[] { true, false })
        {
            foreach (VersusTowerData tower in towers)
            {
                bool sameTheme = string.Equals(tower.Theme.ID, selectedTower.Theme.ID, StringComparison.Ordinal);
                bool sameVisualAssets = string.Equals(tower.Theme.Tileset, selectedTower.Theme.Tileset, StringComparison.Ordinal) &&
                                        string.Equals(tower.Theme.BGTileset, selectedTower.Theme.BGTileset, StringComparison.Ordinal) &&
                                        string.Equals(tower.Theme.BackgroundID, selectedTower.Theme.BackgroundID, StringComparison.Ordinal);

                if (exactThemeOnly ? !sameTheme : (!sameVisualAssets || sameTheme))
                    continue;

                List<string> compatible = GetCompatiblePaths(tower, teamMode);
                if (compatible.Count == 0)
                    continue;

                sourceTower = tower;
                return compatible;
            }
        }

        // Last resort: always choose an actual eight-player map rather than
        // letting FortRise index an empty list.  The caller swaps the level
        // system theme to this tower's matching theme before XML is loaded.
        foreach (VersusTowerData tower in towers)
        {
            List<string> compatible = GetCompatiblePaths(tower, teamMode);

            if (compatible.Count > 0)
            {
                sourceTower = tower;
                return compatible;
            }
        }

        return [];
    }

    private static List<string> GetCompatiblePaths(VersusTowerData tower, bool teamMode)
    {
        IEnumerable<VersusLevelData> levels = tower.Levels ?? [];
        return levels
            .Where(level => !string.IsNullOrWhiteSpace(level.Path))
            .Where(level => level.PlayerSpawns >= (teamMode
                ? TFGame.PlayerAmount
                : TF8Runtime.MaxPlayers))
            .Select(level => level.Path)
            .ToList();
    }

    private static IEnumerable<VersusTowerData> EnumerateTowers(VersusTowerData selectedTower)
    {
        yield return selectedTower;

        if (GameData.VersusTowers is null)
            yield break;

        // Prefer the tower set registered by this mod, then inspect the rest.
        foreach (VersusTowerData tower in GameData.VersusTowers
                     .Where(tower => tower is not null && !ReferenceEquals(tower, selectedTower))
                     .OrderByDescending(tower =>
                         tower.TowerSet?.Contains("8 Players Classic", StringComparison.OrdinalIgnoreCase) == true))
        {
            yield return tower;
        }
    }
}

[HarmonyPatch(typeof(RoundLogic), MethodType.Constructor, new Type[] { typeof(Session), typeof(bool) })]
internal static class RoundLogicConstructorPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(RoundLogic), "SpawnPlayersFFA")]
internal static class RoundLogicSpawnFfaPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(RoundLogic), "SpawnPlayersTeams")]
internal static class RoundLogicSpawnTeamsPatch
{
    private static bool Prefix(RoundLogic __instance)
    {
        Session session = __instance.Session;
        if (!TF8Runtime.HasExtendedPlayer || !session.MatchSettings.TeamMode ||
            session.MatchSettings.Teams is null)
        {
            return true;
        }

        MatchTeams teams = session.MatchSettings.Teams;
        List<Vector2> allSpawns = [];
        List<Vector2> blueSpawns = [];
        List<Vector2> redSpawns = [];

        AddUnique(allSpawns, session.CurrentLevel.GetXMLPositions("TeamSpawnA"));
        AddUnique(allSpawns, session.CurrentLevel.GetXMLPositions("TeamSpawnB"));
        AddUnique(allSpawns, session.CurrentLevel.GetXMLPositions("TeamSpawn"));
        AddUnique(allSpawns, session.CurrentLevel.GetXMLPositions("PlayerSpawn"));

        AddUnique(blueSpawns, session.CurrentLevel.GetXMLPositions("TeamSpawnA"));
        AddUnique(redSpawns, session.CurrentLevel.GetXMLPositions("TeamSpawnB"));

        AddSideSpawns(session.CurrentLevel.GetXMLPositions("TeamSpawn"), blueSpawns, redSpawns);
        AddSideSpawns(session.CurrentLevel.GetXMLPositions("PlayerSpawn"), blueSpawns, redSpawns);

        int seed = new Random().Next();
        blueSpawns.Sort(SortLeft);
        redSpawns.Sort(SortRight);
        blueSpawns.Shuffle(new Random(seed));
        redSpawns.Shuffle(new Random(seed));

        List<int> bluePlayers = GetPlayersToSpawn(session, teams, Allegiance.Blue);
        List<int> redPlayers = GetPlayersToSpawn(session, teams, Allegiance.Red);
        if (allSpawns.Count < bluePlayers.Count + redPlayers.Count)
        {
            TF8Runtime.Logger?.LogError(
                "TF8PlayerFortRise map has {spawns} usable starts for {players} Team Deathmatch players.",
                allSpawns.Count, bluePlayers.Count + redPlayers.Count);
            return false;
        }

        HashSet<Vector2> usedSpawns = [];
        SpawnTeam(session, teams, bluePlayers, blueSpawns, allSpawns, usedSpawns);
        SpawnTeam(session, teams, redPlayers, redSpawns, allSpawns, usedSpawns);
        return false;
    }

    private static List<int> GetPlayersToSpawn(Session session, MatchTeams teams, Allegiance team)
    {
        List<int> players = [];
        for (int playerIndex = 0; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (session.ShouldSpawn(playerIndex) && teams[playerIndex] == team)
                players.Add(playerIndex);
        }

        return players;
    }

    private static void SpawnTeam(
        Session session,
        MatchTeams teams,
        IEnumerable<int> playerIndexes,
        List<Vector2> preferredSpawns,
        List<Vector2> allSpawns,
        HashSet<Vector2> usedSpawns)
    {
        foreach (int playerIndex in playerIndexes)
        {
            if (!TryTakeSpawn(preferredSpawns, allSpawns, usedSpawns, out Vector2 position))
            {
                TF8Runtime.Logger?.LogError(
                    "TF8PlayerFortRise could not allocate a Team Deathmatch spawn for P{player}.",
                    playerIndex + 1);
                continue;
            }

            Allegiance allegiance = teams[playerIndex];
            Player player = new(
                playerIndex,
                position + Vector2.UnitY * 2f,
                allegiance,
                allegiance,
                session.GetPlayerInventory(playerIndex),
                session.GetSpawnHatState(playerIndex),
                frozen: true,
                flash: true,
                indicator: true);
            session.CurrentLevel.Add(player);
        }
    }

    private static bool TryTakeSpawn(
        IEnumerable<Vector2> preferredSpawns,
        IEnumerable<Vector2> allSpawns,
        HashSet<Vector2> usedSpawns,
        out Vector2 position)
    {
        foreach (Vector2 candidate in preferredSpawns)
        {
            if (usedSpawns.Add(candidate))
            {
                position = candidate;
                return true;
            }
        }

        foreach (Vector2 candidate in allSpawns)
        {
            if (usedSpawns.Add(candidate))
            {
                position = candidate;
                return true;
            }
        }

        position = default;
        return false;
    }

    private static void AddSideSpawns(
        IEnumerable<Vector2> source,
        List<Vector2> blueSpawns,
        List<Vector2> redSpawns)
    {
        foreach (Vector2 position in source)
        {
            if (position.X <= 160f)
                AddUnique(blueSpawns, position);
            else
                AddUnique(redSpawns, position);
        }
    }

    private static void AddUnique(List<Vector2> target, IEnumerable<Vector2> source)
    {
        foreach (Vector2 position in source)
            AddUnique(target, position);
    }

    private static void AddUnique(List<Vector2> target, Vector2 position)
    {
        if (!target.Contains(position))
            target.Add(position);
    }

    private static int SortLeft(Vector2 a, Vector2 b) =>
        a.Y != b.Y ? a.Y.CompareTo(b.Y) : b.X.CompareTo(a.X);

    private static int SortRight(Vector2 a, Vector2 b) =>
        a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
}

[HarmonyPatch(typeof(RoundLogic), "FinalKillTeams")]
internal static class RoundLogicFinalKillTeamsPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Level), MethodType.Constructor, new Type[] { typeof(Session), typeof(XmlElement) })]
internal static class LevelControllerStatePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ExpandLevelControllerAttachedFlags(instructions);
}

[HarmonyPatch(typeof(Level), "HandlePausing")]
internal static class LevelPausingPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Level), "Update")]
internal static class LevelControllerUpdatePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Player), nameof(Player.ResolvePlayerCollisions))]
internal static class PlayerCollisionCapacityPatch
{
    private static void Prefix()
    {
        FieldInfo? field = AccessTools.Field(typeof(Player), "wasColliders");
        if (field?.GetValue(null) is not Collider[] colliders || colliders.Length >= TF8Runtime.MaxPlayers)
            return;

        Array.Resize(ref colliders, TF8Runtime.MaxPlayers);
        field.SetValue(null, colliders);
    }
}

[HarmonyPatch(typeof(TreasureSpawner), nameof(TreasureSpawner.GetPickupsForTreasureDraft))]
internal static class TreasureDraftPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(TreasureSpawner), "CanSpawnAnotherChest")]
internal static class TreasureChestLimitPatch
{
    private static bool Prefix(TreasureSpawner __instance, int alreadySpawnedAmount, ref bool __result)
    {
        if (!TF8Runtime.HasExtendedPlayer)
            return true;

        int chancesIndex = Math.Clamp(TFGame.PlayerAmount - 2, 0, TreasureSpawner.ChestChances.Length - 1);
        float[] chances = TreasureSpawner.ChestChances[chancesIndex];
        if (alreadySpawnedAmount >= chances.Length)
        {
            __result = false;
            return false;
        }

        MatchVariants? variants = __instance.Session.MatchSettings.Variants;
        if (variants is not null && (bool)variants.MaxTreasure)
        {
            __result = true;
            return false;
        }

        __result = __instance.Random.NextDouble() < chances[alreadySpawnedAmount];
        return false;
    }
}

internal static class VariantConstructorPatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetDeclaredConstructors(typeof(Variant));

    private static void Postfix(Variant __instance) => TF8Runtime.EnsureVariantPlayerValues(__instance);
}

[HarmonyPatch(typeof(Variant), "get_Value")]
internal static class VariantValueGetPatch
{
    private static void Prefix(Variant __instance) => TF8Runtime.EnsureVariantPlayerValues(__instance);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Variant), "set_Value")]
internal static class VariantValueSetPatch
{
    private static void Prefix(Variant __instance) => TF8Runtime.EnsureVariantPlayerValues(__instance);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Variant), "get_AllTrue")]
internal static class VariantAllTruePatch
{
    private static void Prefix(Variant __instance) => TF8Runtime.EnsureVariantPlayerValues(__instance);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Variant), "get_Players")]
internal static class VariantPlayersPatch
{
    private static void Prefix(Variant __instance) => TF8Runtime.EnsureVariantPlayerValues(__instance);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Variant), nameof(Variant.Clean))]
internal static class VariantCleanPatch
{
    private static void Prefix(Variant __instance) => TF8Runtime.EnsureVariantPlayerValues(__instance);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(Variant), "get_Item")]
internal static class VariantIndexerGetPatch
{
    private static void Prefix(Variant __instance) => TF8Runtime.EnsureVariantPlayerValues(__instance);
}

[HarmonyPatch(typeof(Variant), "set_Item")]
internal static class VariantIndexerSetPatch
{
    private static void Prefix(Variant __instance) => TF8Runtime.EnsureVariantPlayerValues(__instance);
}

[HarmonyPatch(typeof(VersusAwards), nameof(VersusAwards.GetAwards))]
internal static class VersusAwardsGetPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(VersusAwards), "AssignAward")]
internal static class VersusAwardsAssignPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(SessionStats), nameof(SessionStats.RegisterArcherPlays))]
internal static class SessionStatsPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(VersusRoundResults), MethodType.Constructor, new[] { typeof(Session), typeof(List<EventLog>) })]
internal static class VersusRoundResultsEightPlayerLabelsPatch
{
    private static readonly FieldInfo? CrownsField =
        AccessTools.Field(typeof(VersusRoundResults), "crowns");

    private static void Postfix(VersusRoundResults __instance, Session session)
    {
        if (!TF8Runtime.HasExtendedPlayer)
            return;

        __instance.Add(new Coroutine(AddExtraPlayerLabels(__instance, session)));
    }

    private static IEnumerator AddExtraPlayerLabels(VersusRoundResults results, Session session)
    {
        // The native sequence creates the score rows and its four labels on
        // its first update. Wait for that frame before adding P5-P8.
        yield return 1;

        int pointWidth = session.MatchSettings.Mode == Modes.HeadHunters ? 12 : 10;
        int maxTeamSize = session.MatchSettings.GetMaxTeamSize();
        int rowStart = 160 - (session.MatchSettings.GoalScore * pointWidth + 25 * maxTeamSize) / 2 + 25 * maxTeamSize;
        int labelX = rowStart - 12;

        if (session.MatchSettings.TeamMode)
        {
            // Team Deathmatch has two score rows. The vanilla code lays P1-P4
            // horizontally to the left of each row and then puts the crown
            // after the last label. Continue that exact layout for P5-P8 and
            // move each crown so no player label is obscured.
            int[] teamCounts = new int[2];
            for (int playerIndex = 0; playerIndex < TF8Runtime.VanillaPlayers; playerIndex++)
            {
                if (TFGame.Players[playerIndex])
                    teamCounts[session.GetScoreIndex(playerIndex)]++;
            }

            for (int playerIndex = TF8Runtime.VanillaPlayers; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
            {
                if (!TFGame.Players[playerIndex])
                    continue;

                int team = session.GetScoreIndex(playerIndex);
                results.Add(new Text(
                    TFGame.Font,
                    "P" + (playerIndex + 1),
                    new Vector2(labelX - teamCounts[team] * 25f, 81f + team * 20f),
                    ArcherData.GetColorA(playerIndex),
                    Text.HorizontalAlign.Right)
                {
                    Scale = Vector2.One * 2f
                });
                teamCounts[team]++;
            }

            if (CrownsField?.GetValue(results) is Image[] crowns)
            {
                for (int team = 0; team < crowns.Length && team < teamCounts.Length; team++)
                {
                    crowns[team].Position = new Vector2(
                        labelX - teamCounts[team] * 25f - crowns[team].Width / 2f,
                        80f + team * 20f);
                }
            }

            yield break;
        }

        // Free-for-all uses one score/death row per player. Move it upward so
        // P8 has the same breathing room as P1, then append P5-P8.
        results.Position = new Vector2(0f, -10f);

        for (int playerIndex = TF8Runtime.VanillaPlayers; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (!TFGame.Players[playerIndex])
                continue;

            results.Add(new Text(
                TFGame.Font,
                "P" + (playerIndex + 1),
                new Vector2(labelX, 81f + playerIndex * 20f),
                ArcherData.GetColorA(playerIndex),
                Text.HorizontalAlign.Right)
            {
                Scale = Vector2.One * 2f
            });
        }
    }
}

[HarmonyPatch(typeof(VersusMatchResults), MethodType.Constructor, new Type[] { typeof(Session), typeof(VersusRoundResults) })]
internal static class VersusMatchResultsPatch
{
    private static readonly FieldInfo? SessionField = AccessTools.Field(typeof(VersusMatchResults), "session");
    private static readonly FieldInfo? RoundResultsField = AccessTools.Field(typeof(VersusMatchResults), "roundResults");
    private static readonly FieldInfo? PlayerResultsField = AccessTools.Field(typeof(VersusMatchResults), "playerResults");
    private static readonly MethodInfo? TagsSetter = AccessTools.PropertySetter(typeof(Entity), nameof(Entity.Tags));
    private static readonly FieldInfo? TagsField = AccessTools.Field(typeof(Entity), "<Tags>k__BackingField");

    private static bool Prefix(VersusMatchResults __instance, Session session, VersusRoundResults roundResults)
    {
        if (!TF8Runtime.HasExtendedPlayer)
            return true;

        if (SessionField is null || RoundResultsField is null || PlayerResultsField is null)
            return true;

        // Returning false from a constructor prefix also skips HUD's base
        // constructor. Recreate the Entity state it supplies. In particular,
        // an uninitialized Entity starts with Active/Visible set to false:
        // its alarms and coroutines never run, leaving the result screen
        // frozen with portraits only.
        __instance.Active = true;
        __instance.Visible = true;
        __instance.Collidable = true;
        __instance.LayerIndex = 3;
        if (__instance.Tags is null)
        {
            List<GameTags> tags = [];
            if (TagsSetter is not null)
                TagsSetter.Invoke(__instance, new object[] { tags });
            else
                TagsField?.SetValue(__instance, tags);
        }

        SessionField.SetValue(__instance, session);
        RoundResultsField.SetValue(__instance, roundResults);
        __instance.Position = new Vector2(0f, 240f);

        int winner = session.GetWinner();
        for (int playerIndex = 0; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (!TFGame.Players[playerIndex])
                continue;

            bool won = winner == session.GetScoreIndex(playerIndex);
            session.MatchStats[playerIndex].Won = won;
            if (won)
            {
                SaveData.Instance.Stats.Wins[TFGame.Characters[playerIndex]]++;
                SessionStats.RegisterArcherWin(playerIndex);
            }
        }

        List<AwardInfo>[] awards = VersusAwards.GetAwards(session.MatchSettings, session.MatchStats);
        SessionStats.RegisterArcherPlays();

        List<VersusPlayerMatchResults> results = [];
        for (int playerIndex = 0; playerIndex < TF8Runtime.MaxPlayers; playerIndex++)
        {
            if (!TFGame.Players[playerIndex])
                continue;

            int displayIndex = results.Count;
            Vector2 to = TF8MatchResultsScroller.GetCardPosition(displayIndex);
            Vector2 from = TF8MatchResultsScroller.GetEntryPosition(displayIndex, to);
            var result = new VersusPlayerMatchResults(session, __instance, playerIndex, from, to, awards[playerIndex]);
            session.CurrentLevel.Add(result);
            results.Add(result);
        }

        PlayerResultsField.SetValue(__instance, results);
        __instance.Add(new TF8MatchResultsScroller(results));
        if (session.MatchSettings.LevelSystem?.Procedural == true)
            session.CurrentLevel.Add(new VersusSeedDisplay(session.MatchSettings.RandomSeedIcons));

        return false;
    }
}

// Each native result card is roughly eighty logical pixels wide.  Keeping the
// original four-card spacing and moving the entire strip gives eight players a
// readable, continuous horizontal scroller instead of overlapping cards.
internal sealed class TF8MatchResultsScroller : Component
{
    private const int VisibleCards = 4;
    private const float CardSpacing = 80f;
    private const float CardY = 120f;
    private const int ScrollFrames = 12;

    private readonly List<VersusPlayerMatchResults> playerResults;
    private MenuButtonGuide? guide;
    private int scrollIndex;
    private int inputDelay;
    private bool shown;

    internal TF8MatchResultsScroller(List<VersusPlayerMatchResults> playerResults)
        : base(active: true, visible: false)
    {
        this.playerResults = playerResults;
    }

    internal static Vector2 GetCardPosition(int displayIndex) =>
        new(40f + displayIndex * CardSpacing, CardY);

    internal static Vector2 GetEntryPosition(int displayIndex, Vector2 target)
    {
        // Preserve TowerFall's familiar fly-in for the initial four cards.
        // The remaining cards begin off-screen at their eventual strip slots.
        return displayIndex switch
        {
            0 => new Vector2(-160f, CardY),
            1 => new Vector2(-80f, CardY),
            2 => new Vector2(400f, CardY),
            3 => new Vector2(480f, CardY),
            _ => target
        };
    }

    public override void Added()
    {
        base.Added();

        guide = new MenuButtonGuide(0)
        {
            // VersusMatchResults itself remains at Y=240; cancel that offset
            // so the navigation hint is placed at the bottom of the screen.
            Position = new Vector2(160f, -16f),
            Visible = false
        };
        guide.SetDetails(MenuButtonGuide.ButtonModes.Arrows, "MOVER P1-P4");
        Entity.Add(guide);
    }

    internal void Show()
    {
        shown = true;
        Active = true;
        scrollIndex = 0;
        inputDelay = 30;
        UpdateGuide();
        if (guide is not null)
            guide.Visible = true;
    }

    internal void Hide()
    {
        shown = false;
        Active = false;
        if (guide is not null)
            guide.Visible = false;
    }

    public override void Update()
    {
        base.Update();
        if (!shown)
            return;

        if (inputDelay > 0)
        {
            inputDelay--;
            return;
        }

        if (MenuInput.Left)
            Scroll(-1);
        else if (MenuInput.Right)
            Scroll(1);
    }

    private void Scroll(int direction)
    {
        int maxScroll = Math.Max(0, playerResults.Count - VisibleCards);
        int nextScrollIndex = Math.Clamp(scrollIndex + direction, 0, maxScroll);
        if (nextScrollIndex == scrollIndex)
            return;

        float displacement = -direction * CardSpacing;
        foreach (VersusPlayerMatchResults result in playerResults)
        {
            Vector2 from = result.Position;
            Vector2 to = from + Vector2.UnitX * displacement;
            Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeOut, ScrollFrames, start: true);
            tween.OnUpdate = t => result.Position = Calc.Round(Vector2.Lerp(from, to, t.Eased));
            result.Add(tween);
        }

        scrollIndex = nextScrollIndex;
        inputDelay = ScrollFrames;
        UpdateGuide();
        Sounds.ui_moveVersus.Play();
    }

    private void UpdateGuide()
    {
        if (guide is null)
            return;

        int firstPlayer = scrollIndex + 1;
        int lastPlayer = Math.Min(scrollIndex + VisibleCards, playerResults.Count);
        guide.SetDetails(MenuButtonGuide.ButtonModes.Arrows, $"MOVER P{firstPlayer}-P{lastPlayer}");
    }
}

[HarmonyPatch(typeof(VersusMatchResults), nameof(VersusMatchResults.TweenIn))]
internal static class VersusMatchResultsScrollShowPatch
{
    private static void Postfix(VersusMatchResults __instance)
    {
        if (TF8Runtime.HasExtendedPlayer)
            __instance.GetFirst<TF8MatchResultsScroller>()?.Show();
    }
}

[HarmonyPatch(typeof(VersusMatchResults), nameof(VersusMatchResults.TweenOut))]
internal static class VersusMatchResultsScrollHidePatch
{
    private static void Prefix(VersusMatchResults __instance)
    {
        if (TF8Runtime.HasExtendedPlayer)
            __instance.GetFirst<TF8MatchResultsScroller>()?.Hide();
    }
}

// VersusPlayerMatchResults.Sequence uses the card's absolute X coordinate as
// the stereo pan position when it reveals an award.  Native cards live within
// 0..320, but the extra cards are intentionally off-screen until the player
// scrolls to them.  FNA validates SoundEffectInstance.Pan and throws if the
// converted value goes outside -1..1, which aborts the whole results sequence
// before it can mark itself as finished.  Keep every SFX coordinate in the
// coordinate system SFX.Play expects while the eight-player mode is active.
[HarmonyPatch(typeof(SFX), nameof(SFX.Play), new Type[] { typeof(float), typeof(float) })]
internal static class TF8SoundPanBoundsPatch
{
    private static void Prefix(ref float panX)
    {
        if (TF8Runtime.HasExtendedPlayer)
            panX = Math.Clamp(panX, 0f, 320f);
    }
}
