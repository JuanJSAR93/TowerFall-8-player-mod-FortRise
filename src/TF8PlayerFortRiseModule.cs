#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using FortRise;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Monocle;
using TowerFall;

namespace TF8PlayerFortRise;

/// <summary>
/// Runtime port of the old Cecil-based TF-8-Player patch.  It deliberately
/// activates automatically when FortRise loads the mod. The --tf8players argument
/// remains supported by the included launcher for eight physical controller slots.
/// </summary>
public sealed class TF8PlayerFortRiseModule : Mod
{
    // El mod debe funcionar también cuando FortRise se inicia sin argumentos.
    private bool requested = true;

    public TF8PlayerFortRiseModule(IModContent content, IModuleContext context, ILogger logger)
        : base(content, context, logger)
    {
        Environment.SetEnvironmentVariable("FNA_GAMEPAD_NUM_GAMEPADS", "8");

        OnLoad = Activate;
        OnInitialize = _ =>
        {
            if (TF8Runtime.Enabled)
                TF8Runtime.ReconfigureInputs();
        };
    }

    public override void ParseArgs(string[] args)
    {
        // --tf8players se mantiene como argumento compatible, pero ya no es obligatorio.
        // FortRise puede entregar null cuando no se especifican argumentos.
        requested = true;
    }

    private void Activate(IModuleContext context)
    {
        if (!requested)
        {
            Logger.LogInformation("TF8PlayerFortRise is installed but inactive. Start with Launch-8Players.cmd.");
            return;
        }

        TF8Runtime.Enabled = true;
        TF8Runtime.Logger = Logger;
        context.Harmony.PatchAll(Assembly.GetExecutingAssembly());
        TF8Runtime.ExpandCoreState();
        TF8Runtime.ExpandTreasureChances();
        TF8Runtime.ExpandTeamStartArrows();
        TF8Runtime.RegisterClassicMaps(ModContent, context);
        TF8Runtime.RegisterDarkWorldMaps(ModContent, context);

        // OnLoad happens before FNA/SDL and PlayerInput are initialized.  Calling
        // AssignInputs here produces a harmless (but confusing) logged exception.
        // OnInitialize below runs after the game input layer exists and performs
        // the actual eight-controller refresh.
        TF8Runtime.EnsureGamepadConfigurations();

        Logger.LogInformation(
            "TF8PlayerFortRise activated: Versus FFA and Team Deathmatch support up to {maxPlayers} players.",
            TF8Runtime.MaxPlayers);
    }
}

internal static class TF8Runtime
{
    internal const int VanillaPlayers = 4;
    internal const int MaxPlayers = 8;

    internal static bool Enabled { get; set; }
    internal static ILogger? Logger { get; set; }
    private static readonly HashSet<string> GenericJoystickMappings = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsEightPlayerFreeForAll
    {
        get
        {
            if (!Enabled || MainMenu.RollcallMode != MainMenu.RollcallModes.Versus)
                return false;

            // The rollcall is shared by free-for-all and Team Deathmatch.  It
            // must expose all eight slots before the player reaches the team
            // assignment screen, regardless of the selected Versus mode.
            return true;
        }
    }

    // PlayerAmount is a count, not the highest occupied slot. For example,
    // P1 + P2 + P5 reports three players. Any feature that accesses slot
    // P5-P8 must therefore use this predicate instead of PlayerAmount > 4.
    internal static bool HasExtendedPlayer
    {
        get
        {
            if (!Enabled || TFGame.Players is null)
                return false;

            int lastSlot = Math.Min(TFGame.Players.Length, MaxPlayers);
            for (int playerIndex = VanillaPlayers; playerIndex < lastSlot; playerIndex++)
            {
                if (TFGame.Players[playerIndex])
                    return true;
            }

            return false;
        }
    }

    internal static int GetFnaGamepadSlots()
    {
        Type? gamePad = AccessTools.TypeByName("Microsoft.Xna.Framework.Input.GamePad");
        FieldInfo? field = gamePad is null ? null : AccessTools.Field(gamePad, "GAMEPAD_COUNT");
        return field?.GetValue(null) is int count ? count : 0;
    }

    internal static void ExpandCoreState()
    {
        TFGame.Players = CopyToCapacity(TFGame.Players);
        TFGame.Characters = CopyToCapacity(TFGame.Characters);
        TFGame.CoOpCrowns = CopyToCapacity(TFGame.CoOpCrowns);
        TFGame.AltSelect = CopyToCapacity(TFGame.AltSelect);
        TFGame.PlayerInputs = CopyToCapacity(TFGame.PlayerInputs ?? Array.Empty<PlayerInput>());

        for (int i = VanillaPlayers; i < MaxPlayers; i++)
        {
            if (TFGame.Characters[i] == 0)
                TFGame.Characters[i] = i;
        }
    }

    internal static void EnsureGamepadConfigurations()
    {
        SaveData? saveData = SaveData.Instance;
        if (saveData is null)
            return;

        GamepadConfig[] configs = saveData.Gamepad ?? Array.Empty<GamepadConfig>();
        if (configs.Length >= MaxPlayers)
            return;

        int previousLength = configs.Length;
        Array.Resize(ref configs, MaxPlayers);
        for (int index = previousLength; index < configs.Length; index++)
            configs[index] = GamepadConfig.GetDefault();

        // Old or incomplete saves can also contain null entries in the first
        // four slots; XGamepadInput requires a usable config for every slot.
        for (int index = 0; index < configs.Length; index++)
        {
            if (configs[index] is null)
                configs[index] = GamepadConfig.GetDefault();
        }

        saveData.Gamepad = configs;
        Logger?.LogInformation(
            "TF8PlayerFortRise expanded saved gamepad configurations from {previousLength} to {newLength}.",
            previousLength, configs.Length);
    }

    internal static void ExpandTreasureChances()
    {
        TreasureSpawner.ChestChances =
        [
            [0.9f, 0.9f, 0.2f, 0.1f],
            [0.9f, 0.9f, 0.8f, 0.2f, 0.1f],
            [0.9f, 0.9f, 0.6f, 0.8f, 0.2f, 0.1f],
            [0.9f, 0.9f, 0.9f, 0.6f, 0.8f, 0.2f, 0.1f],
            [0.9f, 0.9f, 0.9f, 0.9f, 0.6f, 0.8f, 0.2f, 0.1f]
        ];
    }

    internal static void ExpandTeamStartArrows()
    {
        // Vanilla only has entries for 1v1 through 3v3. Team Deathmatch can
        // now reach 4v4 (and uneven teams), and Session indexes this table by
        // team size minus one when it creates player inventories.
        int[] existing = Session.TeamStartArrows ?? Array.Empty<int>();
        if (existing.Length >= MaxPlayers)
            return;

        int[] expanded = new int[MaxPlayers];
        for (int index = 0; index < expanded.Length; index++)
        {
            expanded[index] = index < existing.Length
                ? existing[index]
                : 2;
        }

        Session.TeamStartArrows = expanded;
    }

    internal static void ReconfigureInputs()
    {
        if (!Enabled)
            return;

        try
        {
            EnsureGamepadConfigurations();
            RefreshFnaGamepads();
            AccessTools.Method(typeof(MInput), "UpdateJoysticks")?.Invoke(null, null);
            PlayerInput.AssignInputs();
        }
        catch (Exception exception)
        {
            Exception detail = exception is TargetInvocationException { InnerException: not null } invocation
                ? invocation.InnerException
                : exception;
            Logger?.LogError(detail, "TF8PlayerFortRise could not reconfigure player inputs.");
        }
    }

    internal static void RefreshFnaGamepads()
    {
        Type? sdlType = AccessTools.TypeByName("SDL3.SDL");
        Type? platformType = AccessTools.TypeByName("Microsoft.Xna.Framework.SDL3_FNAPlatform");
        MethodInfo? getGamepads = sdlType is null
            ? null
            : AccessTools.Method(sdlType, "SDL_GetGamepads", new[] { typeof(int).MakeByRefType() });
        MethodInfo? free = sdlType is null ? null : AccessTools.Method(sdlType, "SDL_free", new[] { typeof(nint) });
        MethodInfo? addInstance = platformType is null
            ? null
            : AccessTools.Method(platformType, "INTERNAL_AddInstance", new[] { typeof(uint) });

        if (getGamepads is null || free is null || addInstance is null)
        {
            Logger?.LogWarning("TF8PlayerFortRise could not access FNA's SDL gamepad enumeration.");
            return;
        }

        EnableRawInput(sdlType!);
        RegisterGenericJoystickMappings(sdlType!, free);

        object[] countArguments = [0];
        nint gamepads = 0;
        try
        {
            gamepads = getGamepads.Invoke(null, countArguments) is nint result ? result : 0;
            int count = countArguments[0] is int value ? value : 0;
            for (int index = 0; index < count; index++)
            {
                uint instanceId = unchecked((uint)Marshal.ReadInt32(gamepads, index * sizeof(uint)));
                addInstance.Invoke(null, new object[] { instanceId });
            }

            Logger?.LogInformation("TF8PlayerFortRise refreshed {count} SDL gamepads before assigning inputs.", count);
        }
        finally
        {
            if (gamepads != 0)
                free.Invoke(null, new object[] { gamepads });
        }
    }

    private static void EnableRawInput(Type sdlType)
    {
        MethodInfo? setHint = AccessTools.Method(
            sdlType, "SDL_SetHint", new[] { typeof(string), typeof(string) });
        if (setHint is null)
        {
            Logger?.LogWarning("TF8PlayerFortRise could not access SDL_SetHint.");
            return;
        }

        try
        {
            // FortRise exposes these same switches in FortRise.settings.json.
            // Set them here too so the experiment does not depend on load order.
            setHint.Invoke(null, new object[] { "SDL_JOYSTICK_RAWINPUT", "1" });
            setHint.Invoke(null, new object[] { "SDL_JOYSTICK_RAWINPUT_CORRELATE_XINPUT", "0" });
            setHint.Invoke(null, new object[] { "SDL_JOYSTICK_DIRECTINPUT", "1" });
            setHint.Invoke(null, new object[] { "SDL_XINPUT_ENABLED", "0" });
            setHint.Invoke(null, new object[] { "SDL_JOYSTICK_GAMEINPUT", "1" });
            setHint.Invoke(null, new object[] { "SDL_JOYSTICK_THREAD", "1" });
            Logger?.LogInformation(
                "TF8PlayerFortRise enabled SDL DirectInput-only discovery for the eight-player test.");
        }
        catch (Exception exception)
        {
            Logger?.LogWarning(exception, "TF8PlayerFortRise could not enable SDL Raw Input.");
        }
    }

    private static void RegisterGenericJoystickMappings(Type sdlType, MethodInfo free)
    {
        MethodInfo? getJoysticks = AccessTools.Method(
            sdlType, "SDL_GetJoysticks", new[] { typeof(int).MakeByRefType() });
        MethodInfo? isGamepad = AccessTools.Method(sdlType, "SDL_IsGamepad", new[] { typeof(uint) });
        MethodInfo? getGuid = AccessTools.Method(sdlType, "SDL_GetJoystickGUIDForID", new[] { typeof(uint) });
        MethodInfo? getName = AccessTools.Method(sdlType, "SDL_GetJoystickNameForID", new[] { typeof(uint) });
        MethodInfo? addMapping = AccessTools.Method(sdlType, "SDL_AddGamepadMapping", new[] { typeof(string) });

        if (getJoysticks is null || isGamepad is null || getGuid is null || getName is null || addMapping is null)
        {
            Logger?.LogWarning("TF8PlayerFortRise could not access SDL's raw joystick mapping API.");
            return;
        }

        object[] countArguments = [0];
        nint joystickIds = 0;
        try
        {
            joystickIds = getJoysticks.Invoke(null, countArguments) is nint result ? result : 0;
            int count = countArguments[0] is int value ? value : 0;
            int mapped = 0;

            for (int index = 0; index < count; index++)
            {
                uint instanceId = unchecked((uint)Marshal.ReadInt32(joystickIds, index * sizeof(uint)));
                object? gamepadState = isGamepad.Invoke(null, new object[] { instanceId });
                if (gamepadState?.Equals(true) == true)
                    continue;

                object? guid = getGuid.Invoke(null, new object[] { instanceId });
                string? guidText = guid is null ? null : FormatSdlGuid(guid);
                if (string.IsNullOrEmpty(guidText) || !GenericJoystickMappings.Add(guidText))
                    continue;

                string name = (getName.Invoke(null, new object[] { instanceId }) as string)?.Trim()
                    ?? "Joystick";
                string mapping =
                    $"{guidText},TF8 Generic {SanitizeMappingName(name)}," +
                    "a:b0,b:b1,x:b2,y:b3," +
                    "back:b4,guide:b5,start:b6," +
                    "leftshoulder:b7,rightshoulder:b8," +
                    "leftstick:b9,rightstick:b10," +
                    "leftx:a0,lefty:a1,rightx:a2,righty:a3," +
                    "lefttrigger:a4,righttrigger:a5," +
                    "dpup:h0.1,dpdown:h0.4,dpleft:h0.8,dpright:h0.2," +
                    "platform:Windows";

                int mappingResult = addMapping.Invoke(null, new object[] { mapping }) is int mappingValue
                    ? mappingValue
                    : -1;
                mapped++;
                Logger?.LogInformation(
                    "TF8PlayerFortRise mapped raw SDL joystick {id} '{name}' as a gamepad (result {result}).",
                    instanceId, name, mappingResult);
            }

            if (count > 0)
            {
                Logger?.LogInformation(
                    "TF8PlayerFortRise found {count} SDL joysticks; added generic mappings for {mapped} unmapped devices.",
                    count, mapped);
            }
        }
        finally
        {
            if (joystickIds != 0)
                free.Invoke(null, new object[] { joystickIds });
        }
    }

    private static string SanitizeMappingName(string name) =>
        name.Replace(',', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string FormatSdlGuid(object guid)
    {
        int size = Marshal.SizeOf(guid);
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(guid, buffer, fDeleteOld: false);
            byte[] bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static void RegisterClassicMaps(IModContent content, IModuleContext context)
    {
        string[] towers =
        [
            "00 - Sacred Ground", "01 - Twilight Spire", "02 - Backfire", "03 - Flight",
            "04 - Mirage", "05 - Thornwood", "06 - Frostfang Keep", "07 - Kings Court",
            "08 - Sunken City", "09 - Moonstone", "10 - TowerForge", "11 - Ascension"
        ];

        int registeredTowers = 0;
        int registeredLevels = 0;

        foreach (string tower in towers)
        {
            string root = $"Content/Levels/Versus/{tower}";
            if (!TryReadTowerConfiguration(content, $"{root}/tower.xml", out string? theme,
                    out Treasure[]? treasure, out float specialArrowRate))
            {
                Logger?.LogWarning("TF8PlayerFortRise could not read classic tower metadata: {tower}.", tower);
                continue;
            }

            List<IResourceInfo> levels = [];
            for (int index = 0; index < 20; index++)
            {
                string path = $"{root}/{index:D2}.oel";
                if (content.TryGetResource(path, out IResourceInfo resource))
                    levels.Add(resource);
            }

            if (levels.Count == 0)
            {
                Logger?.LogWarning("TF8PlayerFortRise found no classic level files in {root}.", root);
                continue;
            }

            string id = "Classic_" + tower[..2];
            context.Registry.Towers.RegisterVersusTower(id, "8 Players Classic", new VersusTowerConfiguration
            {
                Theme = theme!,
                Levels = levels.ToArray(),
                Treasure = treasure,
                Author = "JONESY13 / TF8 FORTRISE",
                ArrowShuffle = false,
                Procedural = false,
                SpecialArrowRate = specialArrowRate
            });

            registeredTowers++;
            registeredLevels += levels.Count;
            Logger?.LogInformation(
                "TF8PlayerFortRise registered classic tower {id} with {levels} levels.",
                id, levels.Count);
        }

        Logger?.LogInformation(
            "TF8PlayerFortRise registered {towers} classic towers and {levels} classic levels.",
            registeredTowers, registeredLevels);
    }

    internal static void RegisterDarkWorldMaps(IModContent content, IModuleContext context)
    {
        string[] towers =
        [
            "12 - The Amaranth", "13 - Dreadwood", "14 - Darkfang", "15 - Cataclysm"
        ];

        int registeredTowers = 0;
        int registeredLevels = 0;

        foreach (string tower in towers)
        {
            string root = $"Content/Levels/Versus/{tower}";
            if (!TryReadTowerConfiguration(content, $"{root}/tower.xml", out string? theme,
                    out Treasure[]? treasure, out float specialArrowRate))
            {
                Logger?.LogWarning("TF8PlayerFortRise could not read Dark World tower metadata: {tower}.", tower);
                continue;
            }

            List<IResourceInfo> levels = [];
            for (int index = 0; index < 20; index++)
            {
                string path = $"{root}/{index:D2}.oel";
                if (content.TryGetResource(path, out IResourceInfo resource))
                    levels.Add(resource);
            }

            if (levels.Count == 0)
            {
                Logger?.LogWarning("TF8PlayerFortRise found no Dark World level files in {root}.", root);
                continue;
            }

            string id = "DarkWorld_" + tower[..2];
            context.Registry.Towers.RegisterVersusTower(id, "8 Players DarkWorld", new VersusTowerConfiguration
            {
                Theme = theme!,
                Levels = levels.ToArray(),
                Treasure = treasure,
                Author = "JONESY13 / TF8 FORTRISE DARK WORLD",
                ArrowShuffle = false,
                Procedural = false,
                SpecialArrowRate = specialArrowRate
            });

            registeredTowers++;
            registeredLevels += levels.Count;
            Logger?.LogInformation(
                "TF8PlayerFortRise registered Dark World tower {id} with {levels} levels.", id, levels.Count);
        }

        Logger?.LogInformation(
            "TF8PlayerFortRise registered {towers} Dark World towers and {levels} Dark World levels.",
            registeredTowers, registeredLevels);
    }

    internal static bool TryReadTowerConfiguration(
        IModContent content,
        string towerPath,
        out string? theme,
        out Treasure[]? treasure,
        out float specialArrowRate)
    {
        theme = null;
        treasure = null;
        specialArrowRate = 0.6f;
        try
        {
            if (!content.TryGetResource(towerPath, out _))
                return false;

            using Stream stream = content.OpenStream(towerPath);
            XElement? root = XDocument.Load(stream).Root;
            theme = root?.Element("theme")?.Value.Trim();

            XElement? treasureNode = root?.Element("treasure");
            if (treasureNode is not null)
            {
                string items = treasureNode.Value;
                List<Treasure> parsedTreasure = [];
                foreach (string item in items.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse(item, ignoreCase: true, out Pickups pickup))
                        parsedTreasure.Add(new Treasure { Pickup = pickup });
                    else
                        Logger?.LogWarning("TF8PlayerFortRise ignored unknown treasure '{pickup}' in {path}.", item, towerPath);
                }

                treasure = parsedTreasure.ToArray();
                string? arrows = treasureNode.Attribute("arrows")?.Value;
                if (arrows is not null && float.TryParse(arrows, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedRate))
                    specialArrowRate = parsedRate;
            }

            return !string.IsNullOrEmpty(theme);
        }
        catch (Exception exception)
        {
            Logger?.LogWarning(exception, "Could not read TF8 tower data: {path}", towerPath);
            return false;
        }
    }

    internal static T[] CopyToCapacity<T>(T[] source)
    {
        if (source.Length >= MaxPlayers)
            return source;

        T[] expanded = new T[MaxPlayers];
        Array.Copy(source, expanded, source.Length);
        return expanded;
    }

    internal static void EnsureVariantPlayerValues(Variant variant)
    {
        FieldInfo? field = AccessTools.Field(typeof(Variant), "playerValues");
        if (field?.GetValue(variant) is not bool[] values || values.Length >= MaxPlayers)
            return;

        Array.Resize(ref values, MaxPlayers);
        field.SetValue(variant, values);
    }

    internal static IEnumerable<CodeInstruction> ReplaceFourWithEight(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_4)
                instruction.opcode = OpCodes.Ldc_I4_8;
            yield return instruction;
        }
    }

    internal static IEnumerable<CodeInstruction> ReplaceFourWithAvailableGamepadSlots(
        IEnumerable<CodeInstruction> instructions)
    {
        int availableSlots = Math.Clamp(GetFnaGamepadSlots(), VanillaPlayers, MaxPlayers);
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_4 && availableSlots != VanillaPlayers)
            {
                instruction.opcode = availableSlots switch
                {
                    5 => OpCodes.Ldc_I4_5,
                    6 => OpCodes.Ldc_I4_6,
                    7 => OpCodes.Ldc_I4_7,
                    _ => OpCodes.Ldc_I4_8
                };
            }

            yield return instruction;
        }
    }

    internal static IEnumerable<CodeInstruction> ExpandLevelControllerAttachedFlags(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> rewritten = instructions.ToList();
        for (int index = 0; index + 1 < rewritten.Count; index++)
        {
            // Level's constructor has several unrelated literal 4 values: the
            // pause layer itself is layer 4. Only expand `new bool[4]`, which
            // is the controllerAttachedFlags array.
            if (rewritten[index].opcode != OpCodes.Ldc_I4_4 ||
                rewritten[index + 1].opcode != OpCodes.Newarr ||
                rewritten[index + 1].operand is not Type elementType ||
                elementType != typeof(bool))
            {
                continue;
            }

            rewritten[index].opcode = OpCodes.Ldc_I4_8;
            rewritten[index].operand = null;

            // Initialize all eight flags to true in the constructor as well.
            for (int initializer = index + 2; initializer + 1 < rewritten.Count; initializer++)
            {
                if (rewritten[initializer].opcode == OpCodes.Ldc_I4_4 &&
                    rewritten[initializer + 1].opcode.FlowControl == FlowControl.Cond_Branch)
                {
                    rewritten[initializer].opcode = OpCodes.Ldc_I4_8;
                    rewritten[initializer].operand = null;
                    break;
                }
            }

            break;
        }

        return rewritten;
    }

    internal static IEnumerable<CodeInstruction> ReplaceConstants(
        IEnumerable<CodeInstruction> instructions,
        IReadOnlyDictionary<int, int> replacements)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (TryReadInt32(instruction, out int value) && replacements.TryGetValue(value, out int replacement))
                WriteInt32(instruction, replacement);
            yield return instruction;
        }
    }

    private static bool TryReadInt32(CodeInstruction instruction, out int value)
    {
        if (instruction.opcode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_0) { value = 0; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_1) { value = 1; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_2) { value = 2; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_3) { value = 3; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_4) { value = 4; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_5) { value = 5; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_6) { value = 6; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_7) { value = 7; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_8) { value = 8; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte signed) { value = signed; return true; }
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int integer) { value = integer; return true; }
        value = default;
        return false;
    }

    private static void WriteInt32(CodeInstruction instruction, int value)
    {
        instruction.operand = null;
        instruction.opcode = value switch
        {
            0 => OpCodes.Ldc_I4_0,
            1 => OpCodes.Ldc_I4_1,
            2 => OpCodes.Ldc_I4_2,
            3 => OpCodes.Ldc_I4_3,
            4 => OpCodes.Ldc_I4_4,
            5 => OpCodes.Ldc_I4_5,
            6 => OpCodes.Ldc_I4_6,
            7 => OpCodes.Ldc_I4_7,
            8 => OpCodes.Ldc_I4_8,
            _ => OpCodes.Ldc_I4
        };

        if (instruction.opcode == OpCodes.Ldc_I4)
            instruction.operand = value;
    }
}

[HarmonyPatch(typeof(TFGame), nameof(TFGame.CharacterTaken))]
internal static class CharacterTakenPatch
{
    private static bool Prefix(int characterIndex, ref bool __result)
    {
        if (!TF8Runtime.Enabled)
            return true;

        for (int i = 0; i < TFGame.Players.Length; i++)
        {
            if (TFGame.Players[i] && TFGame.Characters[i] == characterIndex)
            {
                __result = true;
                return false;
            }
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(TFGame), "get_FirstPlayer")]
internal static class FirstPlayerPatch
{
    private static bool Prefix(ref int __result)
    {
        if (!TF8Runtime.Enabled)
            return true;

        for (int i = 0; i < TFGame.Players.Length; i++)
        {
            if (TFGame.Players[i])
            {
                __result = i;
                return false;
            }
        }

        __result = -1;
        return false;
    }
}

[HarmonyPatch(typeof(MInput), "UpdateJoysticks")]
internal static class MInputPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithAvailableGamepadSlots(instructions);
}

[HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.AssignInputs))]
internal static class PlayerInputPatch
{
    private static void Prefix() => TF8Runtime.EnsureGamepadConfigurations();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceConstants(instructions, new Dictionary<int, int> { [3] = 7, [4] = 8 });
}

[HarmonyPatch(typeof(SaveData), nameof(SaveData.Verify))]
internal static class SaveDataGamepadConfigurationPatch
{
    private static void Postfix() => TF8Runtime.EnsureGamepadConfigurations();
}

[HarmonyPatch(typeof(MenuInput), nameof(MenuInput.UpdateInputs))]
internal static class MenuInputPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceConstants(instructions, new Dictionary<int, int> { [4] = 8, [5] = 9 });
}

[HarmonyPatch(typeof(MenuInput), nameof(MenuInput.RumbleAll))]
internal static class MenuInputRumbleAllPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(MenuInput), nameof(MenuInput.RumblePlayers))]
internal static class MenuInputRumblePlayersPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(MenuButtons), nameof(MenuButtons.Update))]
internal static class MenuButtonsPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(ReadyBanner), MethodType.Constructor)]
internal static class ReadyBannerConstructorPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(ReadyBanner), "Update")]
internal static class ReadyBannerUpdatePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}

[HarmonyPatch(typeof(ReadyBanner), "GetButtons")]
internal static class ReadyBannerButtonsPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        TF8Runtime.ReplaceFourWithEight(instructions);
}
