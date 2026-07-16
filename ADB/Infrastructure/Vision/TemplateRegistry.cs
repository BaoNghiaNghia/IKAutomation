using ADB_Tool_Automation_Post_FB.Core.Vision;
using System;
using System.Collections.Generic;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.Vision
{
    public sealed class TemplateRegistry : ITemplateRegistry
    {
        private const double DefaultThreshold = 0.80;

        private static readonly IReadOnlyDictionary<TemplateId, TemplateDefinition> Definitions =
            new Dictionary<TemplateId, TemplateDefinition>
            {
                { TemplateId.WorldMapAnchor, Define(TemplateId.WorldMapAnchor, "Global/world_map_anchor.png") },
                { TemplateId.ContinentMapTitle, Define(TemplateId.ContinentMapTitle, "Global/continent_map_title.png") },
                { TemplateId.ResourceSearchPanelAnchor, Define(TemplateId.ResourceSearchPanelAnchor, "Search/resource_search_panel_anchor.png") },
                { TemplateId.SearchButtonEnabled, Define(TemplateId.SearchButtonEnabled, "Search/search_button_enabled.png") },
                { TemplateId.ResourceTabSelected, Define(TemplateId.ResourceTabSelected, "Search/resource_tab_selected.png") },
                { TemplateId.ResourceTabUnselected, Define(TemplateId.ResourceTabUnselected, "Search/resource_tab_unselected.png") },
                { TemplateId.ResourceIronSelected, Define(TemplateId.ResourceIronSelected, "Search/resource_iron_selected.png") },
                { TemplateId.ResourceIronUnselected, Define(TemplateId.ResourceIronUnselected, "Search/resource_iron_unselected.png") },
                { TemplateId.LevelMinusButton, Define(TemplateId.LevelMinusButton, "Search/level_minus_button.png") },
                { TemplateId.LevelPlusButton, Define(TemplateId.LevelPlusButton, "Search/level_plus_button.png") },
                { TemplateId.LevelValue7, Define(TemplateId.LevelValue7, "Search/level_value_7.png") },
                { TemplateId.UnoccupiedFilterChecked, Define(TemplateId.UnoccupiedFilterChecked, "Search/unoccupied_filter_checked.png") },
                { TemplateId.UnoccupiedFilterUnchecked, Define(TemplateId.UnoccupiedFilterUnchecked, "Search/unoccupied_filter_unchecked.png") },
                { TemplateId.CheckboxChecked, Define(TemplateId.CheckboxChecked, "Search/checkbox_checked.png") },
                { TemplateId.CheckboxUnchecked, Define(TemplateId.CheckboxUnchecked, "Search/checkbox_unchecked.png") },
                { TemplateId.ResourceNotFoundToast, Define(TemplateId.ResourceNotFoundToast, "Errors/resource_not_found_toast.png") },
                { TemplateId.ResourcePopup, Define(TemplateId.ResourcePopup, "Resources/resource_popup.png") },
                { TemplateId.GatherButton, Define(TemplateId.GatherButton, "Resources/gather_button.png") },
                { TemplateId.TeamPanel, Define(TemplateId.TeamPanel, "Teams/team_panel.png") },
                { TemplateId.TeamFree, Define(TemplateId.TeamFree, "Teams/team_free.png") },
                { TemplateId.TeamBusy, Define(TemplateId.TeamBusy, "Teams/team_busy.png") },
                { TemplateId.TeamSelected, Define(TemplateId.TeamSelected, "Teams/team_selected.png") },
                { TemplateId.NetworkError, Define(TemplateId.NetworkError, "Errors/network_error.png") },
                { TemplateId.ReconnectButton, Define(TemplateId.ReconnectButton, "Errors/reconnect_button.png") }
            };

        private readonly string rootDirectory;

        public TemplateRegistry()
            : this(Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "InfinityKingdom",
                "1280x720",
                "vi"))
        {
        }

        public TemplateRegistry(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Template root directory is required.", nameof(rootDirectory));

            this.rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public TemplateDefinition GetDefinition(TemplateId id)
        {
            if (!Definitions.TryGetValue(id, out TemplateDefinition definition))
                throw new KeyNotFoundException($"TemplateId '{id}' is not registered.");

            return definition;
        }

        public string GetPath(TemplateId id)
        {
            TemplateDefinition definition = GetDefinition(id);
            string platformPath = definition.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(rootDirectory, platformPath);
        }

        public byte[] LoadBytes(TemplateId id)
        {
            string path = GetPath(id);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Template file for '{id}' was not found at '{path}'.", path);

            return File.ReadAllBytes(path);
        }

        public bool Exists(TemplateId id)
        {
            return File.Exists(GetPath(id));
        }

        private static TemplateDefinition Define(TemplateId id, string relativePath)
        {
            return new TemplateDefinition(id, relativePath, DefaultThreshold);
        }
    }
}
