using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wizard;

namespace Shadowbus
{
    [Flags]
    internal enum CustomFormatContextKind
    {
        None = 0,
        DeckList = 1,
        Story = 2,
        Practice = 4,
        Room = 8,
        AllDeckSelections = Story | Practice | Room
    }

    internal sealed class CustomFormatDefinition
    {
        internal CustomFormatDefinition(
            string id,
            string displayName,
            Format baseGameFormat,
            string deckDirectory,
            int sortOrder,
            CustomFormatContextKind supportedContexts)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A custom format ID is required.", nameof(id));
            }

            Id = id.Trim().ToLowerInvariant();
            DisplayName = displayName ?? Id;
            BaseGameFormat = baseGameFormat;
            DeckDirectory = deckDirectory ?? throw new ArgumentNullException(nameof(deckDirectory));
            SortOrder = sortOrder;
            SupportedContexts = supportedContexts;
        }

        internal string Id { get; }
        internal string DisplayName { get; }
        internal Format BaseGameFormat { get; }
        internal string DeckDirectory { get; }
        internal int SortOrder { get; }
        internal CustomFormatContextKind SupportedContexts { get; }

        internal bool Supports(CustomFormatContextKind context)
        {
            return (SupportedContexts & context) == context;
        }
    }

    internal static class CustomFormats
    {
        internal const string UnlimitedId = "unlimited";
        internal const string ModernId = "modern";

        private static readonly Dictionary<string, CustomFormatDefinition> Definitions =
            new Dictionary<string, CustomFormatDefinition>(StringComparer.OrdinalIgnoreCase);

        static CustomFormats()
        {
            Register(new CustomFormatDefinition(
                UnlimitedId,
                "\u65e0\u9650\u6a21\u5f0f",
                Format.Unlimited,
                PathHelper.UnlimitedDeckPath,
                0,
                CustomFormatContextKind.DeckList | CustomFormatContextKind.AllDeckSelections));
            Register(new CustomFormatDefinition(
                ModernId,
                "\u6469\u767b\u6a21\u5f0f",
                Format.Unlimited,
                Path.Combine(PathHelper.CustomFormatPath, ModernId, "Decks"),
                100,
                CustomFormatContextKind.DeckList | CustomFormatContextKind.AllDeckSelections));
        }

        internal static IReadOnlyList<CustomFormatDefinition> All => Definitions.Values
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        internal static CustomFormatDefinition Unlimited => Get(UnlimitedId);

        internal static void Initialize()
        {
            foreach (CustomFormatDefinition definition in Definitions.Values)
            {
                Directory.CreateDirectory(definition.DeckDirectory);
            }
        }

        internal static void Register(CustomFormatDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (Definitions.ContainsKey(definition.Id))
            {
                throw new InvalidOperationException(
                    $"A custom format with ID '{definition.Id}' is already registered.");
            }
            Definitions.Add(definition.Id, definition);
        }

        internal static CustomFormatDefinition Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !Definitions.TryGetValue(id, out var definition))
            {
                return Definitions[UnlimitedId];
            }
            return definition;
        }

        internal static bool TryGet(string id, out CustomFormatDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                definition = null;
                return false;
            }
            return Definitions.TryGetValue(id, out definition);
        }
    }

    internal static class CustomFormatContext
    {
        private static string deckListFormatId = CustomFormats.UnlimitedId;
        private static string selectionFormatId = CustomFormats.UnlimitedId;
        private static string roomFormatId = CustomFormats.UnlimitedId;

        internal static string DeckListFormatId
        {
            get => deckListFormatId;
            set => deckListFormatId = Normalize(value, CustomFormatContextKind.DeckList);
        }

        internal static string SelectionFormatId
        {
            get => selectionFormatId;
            set => selectionFormatId = CustomFormats.Get(value).Id;
        }

        internal static string RoomFormatId
        {
            get => roomFormatId;
            set => roomFormatId = Normalize(value, CustomFormatContextKind.Room);
        }

        internal static CustomFormatDefinition DeckListFormat =>
            CustomFormats.Get(DeckListFormatId);

        internal static CustomFormatDefinition SelectionFormat =>
            CustomFormats.Get(SelectionFormatId);

        internal static CustomFormatDefinition RoomFormat =>
            CustomFormats.Get(RoomFormatId);

        internal static void OpenDeckList(string formatId)
        {
            DeckListFormatId = formatId;
            DeckListUI.ChangeSceneToDeckList(DeckListFormat.BaseGameFormat, null, null);
        }

        private static string Normalize(string id, CustomFormatContextKind context)
        {
            CustomFormatDefinition definition = CustomFormats.Get(id);
            return definition.Supports(context) ? definition.Id : CustomFormats.UnlimitedId;
        }
    }
}
